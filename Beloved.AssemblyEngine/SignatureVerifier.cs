using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace Beloved.AssemblyEngine;

/// <summary>
/// Custom cryptographic signature verifier using .NET cryptography APIs.
/// Eliminates insecure shell-outs to cosign binaries.
/// Follows the Fowler/Edwards standard.
/// </summary>
public interface ISignatureVerifier
{
    /// <summary>
    /// Verifies that a manifest/payload signature matches the trusted public key.
    /// Fail-closed by default.
    /// </summary>
    bool VerifySignature(byte[] payload, byte[] signature, string publicKeyPem);
}

public sealed class PemSignatureVerifier : ISignatureVerifier
{
    public bool VerifySignature(byte[] payload, byte[] signature, string publicKeyPem)
    {
        if (payload == null || payload.Length == 0) return false;
        if (signature == null || signature.Length == 0) return false;
        if (string.IsNullOrWhiteSpace(publicKeyPem)) return false;

        try
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(publicKeyPem.ToCharArray());

            return rsa.VerifyData(
                payload,
                signature,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1
            );
        }
        catch
        {
            // Fail-closed on parsing or cryptographic errors
            return false;
        }
    }
}
