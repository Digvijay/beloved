using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Beloved.AssemblyEngine
{
    /// <summary>
    /// OCI-compliant registry repository with built-in native signature checks and structured logging.
    /// Eliminates process shell-outs and fails-closed on any validation error.
    /// </summary>
    public class OciVaultRepository : IVaultRepository
    {
        private readonly HttpClient _httpClient;
        private readonly ISignatureVerifier _verifier;
        private readonly ILogger<OciVaultRepository> _logger;
        private const string PublicKeyPemPath = "/Users/digvijay/github/beloved/cosign-key.pub";

        public OciVaultRepository(HttpClient httpClient, ISignatureVerifier verifier, ILogger<OciVaultRepository> logger)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _verifier = verifier ?? throw new ArgumentNullException(nameof(verifier));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<(string targetDirectory, string digest)> FetchTemplateAsync(string templateName, string targetDirectory)
        {
            return await PullArtifactAsync($"templates/{templateName}", "latest", targetDirectory);
        }

        public async Task<(string targetDirectory, string digest)> FetchModuleAsync(string moduleName, string version, string targetDirectory)
        {
            return await PullArtifactAsync($"modules/{moduleName.ToLower()}", version ?? "latest", targetDirectory);
        }

        public Task PushModuleAsync(string modulePath, string moduleName, string version)
        {
            throw new NotImplementedException("Pushing to OCI registry is not yet implemented.");
        }

        public async Task<IEnumerable<string>> ListModulesAsync()
        {
            var response = await _httpClient.GetAsync("/v2/_catalog");
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(content);
            var repositories = new List<string>();

            if (doc.RootElement.TryGetProperty("repositories", out var reposElement))
            {
                foreach (var repo in reposElement.EnumerateArray())
                {
                    var repoName = repo.GetString();
                    if (repoName != null && repoName.StartsWith("modules/"))
                    {
                        repositories.Add(repoName.Substring("modules/".Length));
                    }
                }
            }

            return repositories;
        }

        private async Task<(string targetDirectory, string digest)> PullArtifactAsync(string repository, string tag, string targetDirectory)
        {
            _logger.LogInformation("pulling_oci_artifact: Repo={Repo} Tag={Tag}", repository, tag);

            // 1. Get Manifest
            var manifestUrl = $"/v2/{repository}/manifests/{tag}";
            var request = new HttpRequestMessage(HttpMethod.Get, manifestUrl);
            
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.oci.image.manifest.v1+json"));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.docker.distribution.manifest.v2+json"));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.oci.image.index.v1+json"));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.docker.distribution.manifest.list.v2+json"));

            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                throw new Exception($"Failed to fetch manifest for {repository}:{tag}. Status: {response.StatusCode}");
            }

            var digest = response.Headers.TryGetValues("Docker-Content-Digest", out var digests) ? string.Join(",", digests) : "";
            var manifestContent = await response.Content.ReadAsStringAsync();
            using var manifestDoc = JsonDocument.Parse(manifestContent);

            // Resilient OCI Index/Manifest routing
            if (manifestDoc.RootElement.TryGetProperty("manifests", out var subManifests))
            {
                string? targetDigest = null;
                foreach (var m in subManifests.EnumerateArray())
                {
                    if (m.TryGetProperty("platform", out var platform) && platform.TryGetProperty("os", out var os) && os.GetString() == "linux")
                    {
                        targetDigest = m.GetProperty("digest").GetString();
                        break;
                    }
                }
                
                if (targetDigest == null && subManifests.GetArrayLength() > 0)
                {
                    targetDigest = subManifests[0].GetProperty("digest").GetString();
                }

                if (targetDigest != null)
                {
                    return await PullArtifactAsync(repository, targetDigest, targetDirectory);
                }
            }

            if (string.IsNullOrEmpty(digest) && manifestDoc.RootElement.TryGetProperty("config", out var configElement))
            {
                digest = configElement.GetProperty("digest").GetString() ?? tag;
            }
            if (string.IsNullOrEmpty(digest)) digest = tag;

            // Security Gate: Verify manifest payload signatures natively (Fails closed)
            if (repository.StartsWith("modules/"))
            {
                var isVerified = await VerifyManifestSignatureAsync(repository, tag, manifestContent);
                if (!isVerified)
                {
                    _logger.LogError("security_violation: OCI manifest signature verification failed for {Repo}:{Tag}. Failing closed.", repository, tag);
                    throw new System.Security.SecurityException($"Untrusted artifact signature: {repository}:{tag}");
                }
            }

            // 2. Extract Layer Blobs
            if (manifestDoc.RootElement.TryGetProperty("layers", out var layersElement))
            {
                foreach (var layer in layersElement.EnumerateArray())
                {
                    var layerDigest = layer.GetProperty("digest").GetString();
                    if (layerDigest != null)
                    {
                        await DownloadAndExtractBlobAsync(repository, layerDigest, targetDirectory);
                    }
                }
            }

            return (targetDirectory, digest);
        }

        private async Task<bool> VerifyManifestSignatureAsync(string repository, string tag, string manifestPayload)
        {
            try
            {
                // In production, fetch the signature payload from registry (e.g. repo/manifests/sha256-...sig)
                // For this secure framework engine, we fetch the accompanying signature blob from the registry endpoint.
                var sigUrl = $"/v2/{repository}/manifests/{tag}.sig";
                var response = await _httpClient.GetAsync(sigUrl);
                if (!response.IsSuccessStatusCode)
                {
                    // If no signature is present, fail-closed.
                    return false;
                }

                var signatureBytes = await response.Content.ReadAsByteArrayAsync();
                var payloadBytes = System.Text.Encoding.UTF8.GetBytes(manifestPayload);

                if (!File.Exists(PublicKeyPemPath))
                {
                    _logger.LogError("verification_failed: Trusted public key file not found at {Path}", PublicKeyPemPath);
                    return false;
                }

                var publicKeyPem = await File.ReadAllTextAsync(PublicKeyPemPath);
                return _verifier.VerifySignature(payloadBytes, signatureBytes, publicKeyPem);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking manifest cryptographic signature.");
                return false; // Fail-closed
            }
        }

        private async Task DownloadAndExtractBlobAsync(string repository, string digest, string targetDirectory)
        {
            var blobUrl = $"/v2/{repository}/blobs/{digest}";
            var response = await _httpClient.GetAsync(blobUrl, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            Directory.CreateDirectory(targetDirectory);
            using var stream = await response.Content.ReadAsStreamAsync();
            using var gzip = new System.IO.Compression.GZipStream(stream, System.IO.Compression.CompressionMode.Decompress);
            System.Formats.Tar.TarFile.ExtractToDirectory(gzip, targetDirectory, overwriteFiles: true);
        }
    }
}
