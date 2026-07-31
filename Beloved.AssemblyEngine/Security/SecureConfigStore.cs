using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Beloved.AssemblyEngine.Security
{
    /// <summary>
    /// Enterprise cross-platform encrypted credential store for Beloved CLI tokens.
    /// Uses AES-256 GCM binary envelope format ("BELOVED1") backed by native OpenSSL/CommonCrypto/CNG engines.
    /// Enforces strict POSIX file permissions (0600 User-Only Read/Write) on macOS/Linux and atomic file operations.
    /// </summary>
    public static class SecureConfigStore
    {
        private static readonly byte[] MagicHeader = Encoding.UTF8.GetBytes("BELOVED1"); // 8-byte format header
        private const int NonceSize = 12;
        private const int TagSize = 16;
        private const int HeaderSize = 8 + NonceSize + TagSize;

        public static void SaveApiKey(string configPath, string apiKey)
        {
            if (string.IsNullOrWhiteSpace(configPath)) throw new ArgumentNullException(nameof(configPath));
            if (string.IsNullOrWhiteSpace(apiKey)) throw new ArgumentNullException(nameof(apiKey));

            var dir = Path.GetDirectoryName(configPath)!;
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var plainText = JsonSerializer.Serialize(new { ApiKey = apiKey });
            var plainBytes = Encoding.UTF8.GetBytes(plainText);

            var key = DeriveMachineUserKey(configPath);
            var nonce = new byte[NonceSize];
            RandomNumberGenerator.Fill(nonce);

            using var aes = new AesGcm(key, TagSize);
            var cipherText = new byte[plainBytes.Length];
            var tag = new byte[TagSize];

            aes.Encrypt(nonce, plainBytes, cipherText, tag);

            // Construct binary envelope: [MAGIC(8)] + [NONCE(12)] + [TAG(16)] + [CIPHERTEXT(N)]
            var envelope = new byte[HeaderSize + cipherText.Length];
            Buffer.BlockCopy(MagicHeader, 0, envelope, 0, MagicHeader.Length);
            Buffer.BlockCopy(nonce, 0, envelope, MagicHeader.Length, NonceSize);
            Buffer.BlockCopy(tag, 0, envelope, MagicHeader.Length + NonceSize, TagSize);
            Buffer.BlockCopy(cipherText, 0, envelope, HeaderSize, cipherText.Length);

            var tempPath = configPath + ".tmp";
            File.WriteAllBytes(tempPath, envelope);

            // Enforce POSIX 0600 permissions (User Read/Write Only) on Linux/macOS
            if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            {
                try
                {
                    File.SetUnixFileMode(tempPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                }
                catch
                {
                    // Ignore if file system does not support Unix modes
                }
            }

            File.Move(tempPath, configPath, overwrite: true);
        }

        public static string ReadApiKey(string configPath)
        {
            if (!File.Exists(configPath))
            {
                throw new InvalidOperationException("You must run 'beloved login <api-key>' first.");
            }

            var envelope = File.ReadAllBytes(configPath);
            if (envelope.Length < HeaderSize)
            {
                throw new InvalidDataException("Invalid or corrupted Beloved credential file.");
            }

            // Verify Magic Header
            for (int i = 0; i < MagicHeader.Length; i++)
            {
                if (envelope[i] != MagicHeader[i])
                {
                    throw new InvalidDataException("Unrecognized Beloved credential header envelope.");
                }
            }

            var nonce = new byte[NonceSize];
            var tag = new byte[TagSize];
            var cipherText = new byte[envelope.Length - HeaderSize];

            Buffer.BlockCopy(envelope, MagicHeader.Length, nonce, 0, NonceSize);
            Buffer.BlockCopy(envelope, MagicHeader.Length + NonceSize, tag, 0, TagSize);
            Buffer.BlockCopy(envelope, HeaderSize, cipherText, 0, cipherText.Length);

            var key = DeriveMachineUserKey(configPath);
            using var aes = new AesGcm(key, TagSize);
            var plainBytes = new byte[cipherText.Length];

            aes.Decrypt(nonce, cipherText, tag, plainBytes);

            var json = Encoding.UTF8.GetString(plainBytes);
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.GetProperty("ApiKey").GetString()!;
        }

        public static void ClearConfig(string configPath)
        {
            if (File.Exists(configPath))
            {
                try
                {
                    var len = new FileInfo(configPath).Length;
                    var noise = new byte[len];
                    RandomNumberGenerator.Fill(noise);
                    File.WriteAllBytes(configPath, noise); // Cryptographic shredding
                }
                catch { }

                File.Delete(configPath);
            }
        }

        private static byte[] DeriveMachineUserKey(string configPath)
        {
            var userHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var userName = Environment.UserName;
            var pathDir = Path.GetDirectoryName(configPath) ?? string.Empty;

            var rawInfo = $"{userHome}:{userName}:{pathDir}:BelovedVaultSecretKey-v2";
            return HMACSHA256.HashData(Encoding.UTF8.GetBytes("BelovedMasterSalt"), Encoding.UTF8.GetBytes(rawInfo));
        }
    }
}
