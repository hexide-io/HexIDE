using System.Security.Cryptography;
using System.Text.Json;
using HexIDE.Addins;

namespace HexIDE.AddinPacker;

public sealed class PackRequest
{
    public required string SrcDir { get; init; }
    public required string OutDir { get; init; }
    public required string Id { get; init; }
    public required string Title { get; init; }
    public string Description { get; init; } = "";
    public required string Version { get; init; }
    public string Author { get; init; } = "";
    public required string Entry { get; init; }
    /// <summary>Directory holding firstparty.key, publisher.json/.sig and intermediate.json/.sig.</summary>
    public required string TrustDir { get; init; }
    /// <summary>Package-relative names of private-dependency files to bundle alongside the entry.</summary>
    public List<string> Include { get; init; } = [];
    /// <summary>Optional package-relative path of a PNG logo. Copied + hashed like an <see cref="Include"/>
    /// file and recorded as the manifest's <c>logoPath</c> so it is signature-covered.</summary>
    public string? Logo { get; init; }
}

/// <summary>
/// Produces a signed add-in package folder. Mirror image of <c>HexIDE.Addins.PackageVerifier</c>:
/// it hashes every bundled file, writes <c>addin.json</c>, signs the exact manifest bytes with the
/// publisher key, and copies the publisher + intermediate chain artifacts so the package verifies
/// fully offline.
/// </summary>
public static class PackageSigner
{
    /// <summary>Generates a self-contained dev trust chain (root → intermediate → first-party
    /// publisher) into <paramref name="outDir"/>. One-time bootstrap; the outputs are committed.</summary>
    public static void GenerateDevTrust(string outDir)
    {
        Directory.CreateDirectory(outDir);

        using var root = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var intermediate = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var firstParty = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        var interRec = new IntermediateRecord
        {
            IntermediateId = "hexide-marketplace-dev",
            DisplayName = "HexIDE Marketplace (DEV)",
            PublicKey = PackageCrypto.ExportPublicKey(intermediate),
        };
        var interBytes = JsonSerializer.SerializeToUtf8Bytes(interRec, AddinJsonContext.Default.IntermediateRecord);
        File.WriteAllBytes(Path.Combine(outDir, "intermediate.json"), interBytes);
        File.WriteAllText(Path.Combine(outDir, "intermediate.sig"), PackageCrypto.Sign(root, interBytes));

        var pubRec = new PublisherRecord
        {
            PublisherId = "com.hexide.firstparty",
            DisplayName = "HexIDE (First-Party)",
            PublicKey = PackageCrypto.ExportPublicKey(firstParty),
            IntermediateId = "hexide-marketplace-dev",
        };
        var pubBytes = JsonSerializer.SerializeToUtf8Bytes(pubRec, AddinJsonContext.Default.PublisherRecord);
        File.WriteAllBytes(Path.Combine(outDir, "publisher.json"), pubBytes);
        File.WriteAllText(Path.Combine(outDir, "publisher.sig"), PackageCrypto.Sign(intermediate, pubBytes));

        File.WriteAllText(Path.Combine(outDir, "hexide-root.pub"), PackageCrypto.ExportPublicKey(root));
        // The first-party publisher PUBLIC key is pinned (baked into the IDE) so first-party status is
        // bound to possession of the first-party private key, not just the publisherId string.
        File.WriteAllText(Path.Combine(outDir, "hexide-firstparty.pub"), PackageCrypto.ExportPublicKey(firstParty));
        File.WriteAllText(Path.Combine(outDir, "firstparty.key"), PackageCrypto.ExportPrivateKey(firstParty));
        // Dev-only private keys retained so the chain can be regenerated/rotated. NEVER ship these.
        File.WriteAllText(Path.Combine(outDir, "root.key"), PackageCrypto.ExportPrivateKey(root));
        File.WriteAllText(Path.Combine(outDir, "intermediate.key"), PackageCrypto.ExportPrivateKey(intermediate));

        Console.WriteLine($"Generated dev trust chain in {outDir}");
    }

    /// <summary>Signs a revocation list with the HexIDE root key: copies the input JSON verbatim to
    /// <c>revocations.json</c> and writes the detached root signature to <c>revocations.sig</c>.</summary>
    public static void SignRevocations(string inJsonPath, string rootKeyPath, string outDir)
    {
        Directory.CreateDirectory(outDir);
        var jsonBytes = File.ReadAllBytes(inJsonPath);
        var keyB64 = File.ReadAllText(rootKeyPath).Trim();
        using var key = PackageCrypto.ImportPrivateKey(keyB64);
        File.WriteAllBytes(Path.Combine(outDir, "revocations.json"), jsonBytes);
        File.WriteAllText(Path.Combine(outDir, "revocations.sig"), PackageCrypto.Sign(key, jsonBytes));
        Console.WriteLine($"Signed revocation list -> {Path.Combine(outDir, "revocations.json")}");
    }

    public static void Pack(PackRequest r)
    {
        // Start from a clean output dir so the package is an EXACT match for the manifest — a stale
        // file left from a previous pack would (correctly) fail the verifier's completeness check.
        if (Directory.Exists(r.OutDir))
            Directory.Delete(r.OutDir, recursive: true);
        Directory.CreateDirectory(r.OutDir);
        var files = new List<AddinFileHash>();

        void CopyAndHash(string relName)
        {
            var dst = Path.Combine(r.OutDir, relName);
            Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
            File.Copy(Path.Combine(r.SrcDir, relName), dst, overwrite: true);
            files.Add(new AddinFileHash
            {
                Path = relName.Replace('\\', '/'),
                Sha256 = PackageCrypto.Sha256Hex(File.ReadAllBytes(dst)),
            });
        }

        CopyAndHash(r.Entry);
        foreach (var inc in r.Include)
            CopyAndHash(inc);

        // The logo is just another hashed payload file. Copy it unless it was already included, so it is
        // listed in files[] exactly once and covered by the manifest signature + completeness check.
        var logoRel = string.IsNullOrEmpty(r.Logo) ? null : r.Logo.Replace('\\', '/');
        if (logoRel is not null && !files.Exists(f => string.Equals(f.Path, logoRel, StringComparison.OrdinalIgnoreCase)))
            CopyAndHash(r.Logo!);

        var pubBytes = File.ReadAllBytes(Path.Combine(r.TrustDir, "publisher.json"));
        var pub = JsonSerializer.Deserialize(pubBytes, AddinJsonContext.Default.PublisherRecord)
                  ?? throw new InvalidOperationException("publisher.json in trust dir is invalid");

        var manifest = new AddinManifest
        {
            Id = r.Id,
            Title = r.Title,
            Description = r.Description,
            Version = r.Version,
            Author = r.Author,
            PublisherId = pub.PublisherId,
            Entry = r.Entry.Replace('\\', '/'),
            LogoPath = logoRel,
            Files = files.ToArray(),
        };
        var manifestBytes = JsonSerializer.SerializeToUtf8Bytes(manifest, AddinJsonContext.Default.AddinManifest);
        File.WriteAllBytes(Path.Combine(r.OutDir, "addin.json"), manifestBytes);

        var keyB64 = File.ReadAllText(Path.Combine(r.TrustDir, "firstparty.key")).Trim();
        using var key = PackageCrypto.ImportPrivateKey(keyB64);
        File.WriteAllText(Path.Combine(r.OutDir, "addin.sig"), PackageCrypto.Sign(key, manifestBytes));

        foreach (var f in new[] { "publisher.json", "publisher.sig", "intermediate.json", "intermediate.sig" })
            File.Copy(Path.Combine(r.TrustDir, f), Path.Combine(r.OutDir, f), overwrite: true);

        Console.WriteLine($"Packed '{r.Id}' -> {r.OutDir} ({files.Count} file(s))");
    }
}
