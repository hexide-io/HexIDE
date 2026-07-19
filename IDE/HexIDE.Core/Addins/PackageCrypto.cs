using System;
using System.Security.Cryptography;
using System.Text;

namespace HexIDE.Addins;

/// <summary>
/// Low-level ECDSA P-256 / SHA-256 primitives shared by package <i>verification</i> (the IDE) and
/// package <i>signing</i> (the SDK / build tooling). Public keys are exchanged as base64
/// SubjectPublicKeyInfo (SPKI); private keys as base64 PKCS#8. Signatures and file hashes are base64
/// and lower-case hex respectively. No third-party crypto dependency — all from the BCL.
/// </summary>
public static class PackageCrypto
{
    public static ECDsa ImportPublicKey(string base64Spki)
    {
        var ec = ECDsa.Create();
        ec.ImportSubjectPublicKeyInfo(Convert.FromBase64String(base64Spki), out _);
        return ec;
    }

    public static ECDsa ImportPrivateKey(string base64Pkcs8)
    {
        var ec = ECDsa.Create();
        ec.ImportPkcs8PrivateKey(Convert.FromBase64String(base64Pkcs8), out _);
        return ec;
    }

    public static string ExportPublicKey(ECDsa key) =>
        Convert.ToBase64String(key.ExportSubjectPublicKeyInfo());

    public static string ExportPrivateKey(ECDsa key) =>
        Convert.ToBase64String(key.ExportPkcs8PrivateKey());

    public static string Sign(ECDsa privateKey, byte[] data) =>
        Convert.ToBase64String(privateKey.SignData(data, HashAlgorithmName.SHA256));

    /// <summary>Verifies <paramref name="base64Signature"/> over <paramref name="data"/>. Returns
    /// false (never throws) for malformed signatures.</summary>
    public static bool Verify(ECDsa publicKey, byte[] data, string base64Signature)
    {
        try
        {
            return publicKey.VerifyData(data, Convert.FromBase64String(base64Signature), HashAlgorithmName.SHA256);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public static string Sha256Hex(byte[] data) =>
        Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    /// <summary>A human-comparable fingerprint of a public key: SHA-256 of the DER SubjectPublicKeyInfo
    /// bytes, upper-case hex in space-separated 4-character groups (cert-thumbprint style). This is the
    /// identity that doesn't lie — a power user compares it against an out-of-band published value.</summary>
    public static string KeyFingerprint(string base64Spki)
    {
        var hex = Convert.ToHexString(SHA256.HashData(Convert.FromBase64String(base64Spki)));
        var sb = new StringBuilder(hex.Length + hex.Length / 4);
        for (var i = 0; i < hex.Length; i++)
        {
            if (i > 0 && i % 4 == 0) sb.Append(' ');
            sb.Append(hex[i]);
        }
        return sb.ToString();
    }
}
