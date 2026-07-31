using System;
using System.Collections.Generic;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Beloved.AssemblyEngine
{
    public record OciDescriptor(string MediaType, string Digest, long Size);
    public record OciManifest(int SchemaVersion, string MediaType, OciDescriptor Config, List<OciDescriptor> Layers);

    /// <summary>
    /// Native C# OCI Layer and Image Manifest Builder.
    /// Eliminates external Docker CLI process invocation by generating OCI-compliant image layers
    /// directly in memory using System.Formats.Tar and System.IO.Compression.
    /// Written to core .NET architectural standards.
    /// </summary>
    public static class OciLayerBuilder
    {
        public const string OciLayerMediaType = "application/vnd.oci.image.layer.v1.tar+gzip";
        public const string OciManifestMediaType = "application/vnd.oci.image.manifest.v1+json";
        public const string OciConfigMediaType = "application/vnd.oci.image.config.v1+json";

        public static async Task<(byte[] layerGzBytes, string layerDigest, long layerSize)> BuildLayerTarGzAsync(
            Dictionary<string, byte[]> files, CancellationToken cancellationToken = default)
        {
            using var memoryStream = new MemoryStream();
            
            {
                using var gzipStream = new GZipStream(memoryStream, CompressionMode.Compress, leaveOpen: true);
                using var tarWriter = new TarWriter(gzipStream, TarEntryFormat.Pax, leaveOpen: true);

                foreach (var (relativePath, content) in files)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var normalizedPath = relativePath.Replace('\\', '/').TrimStart('/');
                    var entry = new PaxTarEntry(TarEntryType.RegularFile, normalizedPath)
                    {
                        DataStream = new MemoryStream(content),
                        Mode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead
                    };

                    await tarWriter.WriteEntryAsync(entry, cancellationToken);
                }
            }

            memoryStream.Position = 0;
            var compressedBytes = memoryStream.ToArray();

            using var sha256 = SHA256.Create();
            var hash = sha256.ComputeHash(compressedBytes);
            var digest = "sha256:" + BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();

            return (compressedBytes, digest, compressedBytes.Length);
        }

        public static (byte[] manifestBytes, string manifestDigest) BuildManifest(
            OciDescriptor layerDescriptor, OciDescriptor configDescriptor)
        {
            var manifest = new OciManifest(
                SchemaVersion: 2,
                MediaType: OciManifestMediaType,
                Config: configDescriptor,
                Layers: new List<OciDescriptor> { layerDescriptor }
            );

            var options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            };

            var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(manifest, options);

            using var sha256 = SHA256.Create();
            var hash = sha256.ComputeHash(jsonBytes);
            var digest = "sha256:" + BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();

            return (jsonBytes, digest);
        }
    }
}
