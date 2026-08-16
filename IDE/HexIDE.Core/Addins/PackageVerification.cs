using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace HexIDE.Addins;

/// <summary>
/// Pure verification of a signed add-in package directory against a supplied root public key.
/// Independent of the host and of where the root key comes from, so it is fully unit-testable with an
/// ephemeral chain. The IDE wraps this with its baked-in root (see <c>HexIDE.Addins.PackageVerifier</c>).
///
/// Signatures are checked over the <b>raw bytes on disk</b> (never a re-serialization), so there is no
/// canonical-form coupling between signer and verifier. Order: parse manifest → verify intermediate
/// (by root) → verify publisher (by intermediate) → verify manifest (by publisher) → hash every file.
/// </summary>
public static class PackageVerification
{
    /// <summary>The signing envelope (manifest + chain + signatures) — present in every package and
    /// therefore not part of the hashed payload inventory.</summary>
    private static readonly HashSet<string> s_envelopeFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "addin.json", "addin.sig",
        "publisher.json", "publisher.sig",
        "intermediate.json", "intermediate.sig",
    };

    public static PackageVerificationResult Verify(string packageDirectory, string? rootPublicKeyBase64,
                                                   string? firstPartyPublicKeyBase64 = null)
    {
        var manifestPath = Path.Combine(packageDirectory, "addin.json");
        if (!File.Exists(manifestPath))
            return new(PackageVerdict.Untrusted, null, null, null, "addin.json not found");

        byte[] manifestBytes;
        AddinManifest? manifest;
        try
        {
            manifestBytes = File.ReadAllBytes(manifestPath);
            manifest = JsonSerializer.Deserialize(manifestBytes, AddinJsonContext.Default.AddinManifest);
        }
        catch (Exception ex)
        {
            return new(PackageVerdict.Untrusted, null, null, null, $"addin.json unreadable: {ex.Message}");
        }

        // Content identity of the package (the consent key) — computed as soon as the bytes are read,
        // so EVERY manifest-bearing result carries a non-null hash and the consent gate can never key
        // on an empty string.
        var manifestHash = PackageCrypto.Sha256Hex(manifestBytes);

        if (manifest is null || string.IsNullOrEmpty(manifest.Entry))
            return new(PackageVerdict.Untrusted, manifest, null, null, "addin.json invalid (no entry)") { ManifestHash = manifestHash };

        // Carry the manifest + hash on every result so developer mode can still resolve and identify
        // the entry even when verification fails.
        PackageVerificationResult Untrusted(string error) =>
            new(PackageVerdict.Untrusted, manifest, null, null, error) { ManifestHash = manifestHash };

        // Defense in depth: a signed manifest must never be able to steer a load OUTSIDE its package.
        // The entry and every listed file are combined with the package dir at hash time (below) and at
        // load time (AddinRegistry.LoadEntry / AddinLoadContext), where a rooted, absolute, or "..".bearing
        // path would escape via Path.Combine. Reject such an entry up-front so no downstream Path.Combine
        // ever sees an unsafe value — even from an otherwise-valid signature.
        if (!IsSafeRelative(manifest.Entry.Replace('\\', '/')))
            return Untrusted("entry path is not a safe package-relative path");

        if (string.IsNullOrEmpty(rootPublicKeyBase64))
            return Untrusted("no root public key available");

        if (!TryReadBytes(packageDirectory, "intermediate.json", out var interBytes)) return Untrusted("intermediate.json missing");
        if (!TryReadText(packageDirectory, "intermediate.sig", out var interSig))     return Untrusted("intermediate.sig missing");
        if (!TryReadBytes(packageDirectory, "publisher.json", out var pubBytes))       return Untrusted("publisher.json missing");
        if (!TryReadText(packageDirectory, "publisher.sig", out var pubSig))           return Untrusted("publisher.sig missing");
        if (!TryReadText(packageDirectory, "addin.sig", out var addinSig))             return Untrusted("addin.sig missing");

        // 1. intermediate record signed by the root.
        try
        {
            using var root = PackageCrypto.ImportPublicKey(rootPublicKeyBase64);
            if (!PackageCrypto.Verify(root, interBytes, interSig)) return Untrusted("intermediate signature invalid");
        }
        catch (Exception ex) { return Untrusted($"root key error: {ex.Message}"); }

        IntermediateRecord? inter;
        try { inter = JsonSerializer.Deserialize(interBytes, AddinJsonContext.Default.IntermediateRecord); }
        catch (Exception ex) { return Untrusted($"intermediate.json unreadable: {ex.Message}"); }
        if (inter is null || string.IsNullOrEmpty(inter.PublicKey)) return Untrusted("intermediate.json invalid");

        // 2. publisher record signed by the intermediate.
        try
        {
            using var interKey = PackageCrypto.ImportPublicKey(inter.PublicKey);
            if (!PackageCrypto.Verify(interKey, pubBytes, pubSig)) return Untrusted("publisher signature invalid");
        }
        catch (Exception ex) { return Untrusted($"intermediate key error: {ex.Message}"); }

        PublisherRecord? pub;
        try { pub = JsonSerializer.Deserialize(pubBytes, AddinJsonContext.Default.PublisherRecord); }
        catch (Exception ex) { return Untrusted($"publisher.json unreadable: {ex.Message}"); }
        if (pub is null || string.IsNullOrEmpty(pub.PublicKey)) return Untrusted("publisher.json invalid");

        if (!string.Equals(pub.IntermediateId, inter.IntermediateId, StringComparison.Ordinal))
            return Untrusted("publisher record does not reference this intermediate");
        if (!string.Equals(manifest.PublisherId, pub.PublisherId, StringComparison.Ordinal))
            return Untrusted("manifest publisherId does not match the publisher record");

        // 3. manifest signed by the publisher.
        try
        {
            using var pubKey = PackageCrypto.ImportPublicKey(pub.PublicKey);
            if (!PackageCrypto.Verify(pubKey, manifestBytes, addinSig)) return Untrusted("addin signature invalid");
        }
        catch (Exception ex) { return Untrusted($"publisher key error: {ex.Message}"); }

        // 4. integrity: every listed file exists and matches its hash; the entry must be listed.
        var entryListed = false;
        var listed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in manifest.Files)
        {
            var rel = f.Path.Replace('\\', '/');

            // Same traversal guard as the entry: a listed payload path must stay inside the package, or
            // the hash below (and any later native/managed probe of that path) would reach outside it.
            if (!IsSafeRelative(rel))
                return Untrusted($"unsafe file path in manifest: {f.Path}");

            listed.Add(rel);

            var filePath = Path.Combine(packageDirectory, f.Path);
            if (!File.Exists(filePath)) return Untrusted($"listed file missing: {f.Path}");

            string actual;
            try { actual = PackageCrypto.Sha256Hex(File.ReadAllBytes(filePath)); }
            catch (Exception ex) { return Untrusted($"cannot hash {f.Path}: {ex.Message}"); }

            if (!string.Equals(actual, f.Sha256, StringComparison.OrdinalIgnoreCase))
                return Untrusted($"hash mismatch: {f.Path}");

            if (string.Equals(rel, manifest.Entry.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase))
                entryListed = true;
        }
        if (!entryListed) return Untrusted("entry assembly is not in the manifest file list");

        // 5. completeness: the manifest must be a FULL inventory of the package. Any payload file that
        // is not listed (and therefore not hash-verified) is rejected, so nothing unverified — e.g. a
        // dependency smuggled in for deps.json/native probing to pick up — can exist to be loaded.
        IEnumerable<string> packageFiles;
        try
        {
            // Materialize the enumeration inside the guard: EnumerateFiles is lazy, and an inaccessible subdirectory
            // or a MAX_PATH-exceeding nested path throws DURING iteration — which must become Untrusted, not a crash.
            packageFiles = Directory.EnumerateFiles(packageDirectory, "*", SearchOption.AllDirectories).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return Untrusted($"package not fully enumerable: {ex.Message}");
        }
        foreach (var file in packageFiles)
        {
            var rel = Path.GetRelativePath(packageDirectory, file).Replace('\\', '/');
            if (s_envelopeFiles.Contains(rel) || listed.Contains(rel)) continue;
            return Untrusted($"unlisted file in package: {rel}");
        }

        // First-party is pinned to the publisher's KEY, not the publisherId string: only a package
        // signed by the first-party private key (which only HexIDE holds) matches.
        var isFirstParty = !string.IsNullOrEmpty(firstPartyPublicKeyBase64)
            && string.Equals(pub.PublicKey, firstPartyPublicKeyBase64, StringComparison.Ordinal);

        // Logo: honour the manifest's logoPath ONLY when it names a listed (hence hash-verified),
        // in-package, traversal-free file. A logoPath that is rooted, contains "..", or is not in the
        // verified inventory is silently dropped — the add-in still loads, just without a logo. This is
        // what lets the UI decode <package>/<LogoPath> safely: it can never point outside the package or
        // at an unverified file.
        string? logoPath = null;
        if (!string.IsNullOrEmpty(manifest.LogoPath))
        {
            var logoRel = manifest.LogoPath.Replace('\\', '/');
            if (IsSafeRelative(logoRel) && listed.Contains(logoRel))
                logoPath = logoRel;
        }

        // The verified signing chain, for the trust-chain inspector. Built only here (a Verified result),
        // from records we just cryptographically checked: every value is an IDE-computed fact.
        var chain = new TrustChain
        {
            BuildVersion = manifest.Version,
            BuildHash = manifestHash,
            IsFirstParty = isFirstParty,
            Publisher = new TrustLink(pub.DisplayName, pub.PublisherId, PackageCrypto.KeyFingerprint(pub.PublicKey)),
            Intermediate = new TrustLink(inter.DisplayName, inter.IntermediateId, PackageCrypto.KeyFingerprint(inter.PublicKey)),
            Root = new TrustLink("HexIDE root", "", PackageCrypto.KeyFingerprint(rootPublicKeyBase64!)),
        };

        return new(PackageVerdict.Verified, manifest, pub.DisplayName, pub.PublisherId, null)
        {
            ManifestHash = manifestHash,
            IsFirstParty = isFirstParty,
            IntermediateId = inter.IntermediateId,
            LogoPath = logoPath,
            Chain = chain,
        };
    }

    /// <summary>A safe package-relative path: not rooted, not absolute, and with no <c>..</c> segment that
    /// could escape the package directory.</summary>
    private static bool IsSafeRelative(string rel)
    {
        if (string.IsNullOrEmpty(rel) || rel.StartsWith('/') || Path.IsPathRooted(rel))
            return false;
        foreach (var segment in rel.Split('/'))
            if (segment == "..")
                return false;
        return true;
    }

    private static bool TryReadBytes(string dir, string name, out byte[] bytes)
    {
        var path = Path.Combine(dir, name);
        try
        {
            if (File.Exists(path)) { bytes = File.ReadAllBytes(path); return true; }
        }
        // A locked/unreadable envelope file (e.g. AV holding a scan lock on first launch) is treated as missing →
        // Untrusted, never an exception that unwinds through IDE startup.
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
        bytes = [];
        return false;
    }

    private static bool TryReadText(string dir, string name, out string text)
    {
        var path = Path.Combine(dir, name);
        try
        {
            if (File.Exists(path)) { text = File.ReadAllText(path).Trim(); return true; }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
        text = "";
        return false;
    }
}
