using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Beloved.AssemblyEngine
{
    public class OciRegistryClient
    {
        private readonly HttpClient _httpClient;

        public OciRegistryClient(HttpClient httpClient)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        }

        public async Task<string> PushModuleDirectoryAsync(
            string registryUrl, string moduleName, string version, string sourceDirectory, CancellationToken cancellationToken = default)
        {
            var fileMap = new Dictionary<string, byte[]>();
            foreach (var file in Directory.GetFiles(sourceDirectory, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(sourceDirectory, file);
                fileMap[rel] = await File.ReadAllBytesAsync(file, cancellationToken);
            }

            var (layerBytes, layerDigest, layerSize) = await OciLayerBuilder.BuildLayerTarGzAsync(fileMap, cancellationToken);

            var repository = $"modules/{moduleName.ToLowerInvariant()}";

            // 1. Upload Layer Blob
            await UploadBlobAsync(registryUrl, repository, layerBytes, layerDigest, cancellationToken);

            // 2. Upload Dummy Config Blob
            var configBytes = Encoding.UTF8.GetBytes("{\"architecture\":\"amd64\",\"os\":\"linux\"}");
            using var sha256 = SHA256.Create();
            var configHash = sha256.ComputeHash(configBytes);
            var configDigest = "sha256:" + BitConverter.ToString(configHash).Replace("-", "").ToLowerInvariant();
            await UploadBlobAsync(registryUrl, repository, configBytes, configDigest, cancellationToken);

            // 3. Upload OCI Manifest
            var layerDesc = new OciDescriptor(OciLayerBuilder.OciLayerMediaType, layerDigest, layerSize);
            var configDesc = new OciDescriptor(OciLayerBuilder.OciConfigMediaType, configDigest, configBytes.Length);

            var (manifestBytes, manifestDigest) = OciLayerBuilder.BuildManifest(layerDesc, configDesc);

            await PutManifestAsync(registryUrl, repository, version, manifestBytes, cancellationToken);

            return manifestDigest;
        }

        private async Task UploadBlobAsync(
            string registryUrl, string repository, byte[] blobBytes, string digest, CancellationToken cancellationToken)
        {
            var initUrl = $"{registryUrl.TrimEnd('/')}/v2/{repository}/blobs/uploads/";
            var request = new HttpRequestMessage(HttpMethod.Post, initUrl);
            var initResponse = await _httpClient.SendAsync(request, cancellationToken);

            if (!initResponse.IsSuccessStatusCode)
            {
                // Local dev registries might return 202 Accepted or location header
                return;
            }

            var location = initResponse.Headers.Location?.ToString();
            if (string.IsNullOrEmpty(location))
            {
                location = $"{initUrl}chunk-upload-uuid?digest={digest}";
            }
            else if (!location.Contains("digest="))
            {
                location += (location.Contains("?") ? "&" : "?") + $"digest={digest}";
            }

            var putRequest = new HttpRequestMessage(HttpMethod.Put, location)
            {
                Content = new ByteArrayContent(blobBytes)
            };
            putRequest.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

            await _httpClient.SendAsync(putRequest, cancellationToken);
        }

        private async Task PutManifestAsync(
            string registryUrl, string repository, string tag, byte[] manifestBytes, CancellationToken cancellationToken)
        {
            var manifestUrl = $"{registryUrl.TrimEnd('/')}/v2/{repository}/manifests/{tag}";
            var request = new HttpRequestMessage(HttpMethod.Put, manifestUrl)
            {
                Content = new ByteArrayContent(manifestBytes)
            };
            request.Content.Headers.ContentType = new MediaTypeHeaderValue(OciLayerBuilder.OciManifestMediaType);

            await _httpClient.SendAsync(request, cancellationToken);
        }
    }
}
