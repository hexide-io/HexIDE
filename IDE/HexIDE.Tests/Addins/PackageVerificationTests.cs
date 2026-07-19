using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using HexIDE.AddinPacker;
using HexIDE.Addins;

namespace HexIDE.Tests.Addins;

/// <summary>
/// Exercises the pure package-verification pipeline against freshly-signed fixtures. Uses an ephemeral
/// trust chain per test class (via <see cref="PackageSigner"/>) so the tests are independent of the
/// committed dev chain and of the IDE's embedded root.
/// </summary>
public sealed class PackageVerificationTests : IDisposable
{
    private readonly string _root;
    private readonly string _trust;
    private readonly string _pkg;
    private readonly string _rootPub;
    private readonly string _firstPartyPub;

    public PackageVerificationTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "hexide_pkgtest_" + Guid.NewGuid().ToString("N"));
        _trust = Path.Combine(_root, "trust");
        var src = Path.Combine(_root, "src");
        _pkg = Path.Combine(_root, "pkg");
        Directory.CreateDirectory(src);

        PackageSigner.GenerateDevTrust(_trust);
        _rootPub = File.ReadAllText(Path.Combine(_trust, "hexide-root.pub")).Trim();
        _firstPartyPub = File.ReadAllText(Path.Combine(_trust, "hexide-firstparty.pub")).Trim();

        File.WriteAllBytes(Path.Combine(src, "MyAddin.dll"), [1, 2, 3, 4, 5]);
        PackageSigner.Pack(new PackRequest
        {
            SrcDir = src,
            OutDir = _pkg,
            Id = "com.test.addin",
            Title = "Test Addin",
            Version = "1.0.0",
            Author = "Tester",
            Entry = "MyAddin.dll",
            TrustDir = _trust,
        });
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch { /* best-effort cleanup */ }
    }

    [Fact]
    public void Verifies_a_correctly_signed_package()
    {
        var r = PackageVerification.Verify(_pkg, _rootPub);

        r.IsVerified.Should().BeTrue(r.Error ?? "");
        r.Manifest!.Id.Should().Be("com.test.addin");
        r.PublisherDisplayName.Should().Be("HexIDE (First-Party)");
        r.PublisherId.Should().Be("com.hexide.firstparty");
    }

    [Fact]
    public void Rejects_a_tampered_payload_file()
    {
        File.WriteAllBytes(Path.Combine(_pkg, "MyAddin.dll"), [9, 9, 9]);

        var r = PackageVerification.Verify(_pkg, _rootPub);

        r.IsVerified.Should().BeFalse();
        r.Error.Should().Contain("hash mismatch");
    }

    [Fact]
    public void Rejects_a_tampered_manifest()
    {
        var manifestPath = Path.Combine(_pkg, "addin.json");
        File.WriteAllText(manifestPath, File.ReadAllText(manifestPath) + " ");

        var r = PackageVerification.Verify(_pkg, _rootPub);

        r.IsVerified.Should().BeFalse();
        r.Error.Should().Contain("addin signature invalid");
    }

    [Fact]
    public void Rejects_a_missing_signature()
    {
        File.Delete(Path.Combine(_pkg, "addin.sig"));

        var r = PackageVerification.Verify(_pkg, _rootPub);

        r.IsVerified.Should().BeFalse();
        r.Error.Should().Contain("addin.sig missing");
    }

    [Fact]
    public void Rejects_a_chain_that_does_not_lead_to_the_trusted_root()
    {
        // A package signed by chain A, verified against chain B's root, must fail at the first link.
        var otherTrust = Path.Combine(_root, "other");
        PackageSigner.GenerateDevTrust(otherTrust);
        var otherRootPub = File.ReadAllText(Path.Combine(otherTrust, "hexide-root.pub")).Trim();

        var r = PackageVerification.Verify(_pkg, otherRootPub);

        r.IsVerified.Should().BeFalse();
        r.Error.Should().Contain("intermediate signature invalid");
    }

    [Fact]
    public void Rejects_an_unlisted_file_in_the_package()
    {
        // A signed package that gains an extra (unlisted, unverified) DLL — e.g. a dependency smuggled
        // in for deps.json/native probing to pick up — must be rejected even though every LISTED file
        // still verifies. The manifest must be a complete inventory.
        File.WriteAllBytes(Path.Combine(_pkg, "Sneaky.dll"), [7, 7, 7]);

        var r = PackageVerification.Verify(_pkg, _rootPub);

        r.IsVerified.Should().BeFalse();
        r.Error.Should().Contain("unlisted file");
    }

    [Fact]
    public void Reports_no_manifest_for_a_directory_without_addin_json()
    {
        var empty = Path.Combine(_root, "empty");
        Directory.CreateDirectory(empty);

        var r = PackageVerification.Verify(empty, _rootPub);

        r.IsVerified.Should().BeFalse();
        r.Manifest.Should().BeNull();
        r.Error.Should().Be("addin.json not found");
    }

    // ── First-party is pinned to a KEY, not the publisherId string ──────────────

    [Fact]
    public void IsFirstParty_TrueWhenPublisherKeyMatchesThePinnedKey()
    {
        // The package is signed by the chain's first-party publisher key; pin that key.
        var r = PackageVerification.Verify(_pkg, _rootPub, _firstPartyPub);

        r.IsVerified.Should().BeTrue();
        r.IsFirstParty.Should().BeTrue();
    }

    [Fact]
    public void IsFirstParty_FalseWhenNoKeyIsPinned()
    {
        // No first-party key supplied (the 2-arg overload) — nothing is first-party.
        PackageVerification.Verify(_pkg, _rootPub).IsFirstParty.Should().BeFalse();
    }

    [Fact]
    public void Masquerade_RejectedEvenWhenPublisherIdStringMatches()
    {
        // The package's publisherId IS "com.hexide.firstparty" (a string check would pass), but it is
        // signed by THIS chain's key. Pin a DIFFERENT chain's first-party key: key mismatch -> NOT
        // first-party. This is the whole point of pinning the key instead of the id string.
        var otherTrust = Path.Combine(_root, "other");
        PackageSigner.GenerateDevTrust(otherTrust);
        var otherFirstPartyPub = File.ReadAllText(Path.Combine(otherTrust, "hexide-firstparty.pub")).Trim();

        var r = PackageVerification.Verify(_pkg, _rootPub, otherFirstPartyPub);

        r.Manifest!.PublisherId.Should().Be("com.hexide.firstparty");   // the id string matches…
        r.IsFirstParty.Should().BeFalse();                              // …but the key does not, so: not first-party
    }

    // ── Publisher logo (Phase 13): hash-covered, and surfaced only when it names a listed file ──

    [Fact]
    public void Verifies_a_package_with_a_logo_and_surfaces_its_path()
    {
        var pkg = PackWithLogo("logo.png");

        var r = PackageVerification.Verify(pkg, _rootPub);

        r.IsVerified.Should().BeTrue(r.Error ?? "");
        r.LogoPath.Should().Be("logo.png");
    }

    [Fact]
    public void A_tampered_logo_file_is_rejected()
    {
        // The logo is listed in files[] like any payload, so altering it breaks the hash — proof the
        // logo is signature-covered and cannot be swapped after signing.
        var pkg = PackWithLogo("logo.png");
        File.WriteAllBytes(Path.Combine(pkg, "logo.png"), [0xFF, 0xFF, 0xFF]);

        var r = PackageVerification.Verify(pkg, _rootPub);

        r.IsVerified.Should().BeFalse();
        r.Error.Should().Contain("hash mismatch");
    }

    [Fact]
    public void A_logoPath_that_is_not_a_listed_file_is_dropped()
    {
        // Re-sign the manifest so logoPath names a file that isn't in files[]. The signature is valid
        // (we re-sign with the chain key), but the verifier must not surface an unverified logo path.
        var pkg = PackWithLogo("logo.png");
        ReSignManifestWithLogoPath(pkg, "not-listed.png");

        var r = PackageVerification.Verify(pkg, _rootPub);

        r.IsVerified.Should().BeTrue(r.Error ?? "");
        r.LogoPath.Should().BeNull();
    }

    [Fact]
    public void A_traversal_logoPath_is_dropped()
    {
        // A logoPath that escapes the package directory must be refused even though the signature is
        // valid — otherwise the UI could be steered into decoding an arbitrary local file.
        var pkg = PackWithLogo("logo.png");
        ReSignManifestWithLogoPath(pkg, "../escape.png");

        var r = PackageVerification.Verify(pkg, _rootPub);

        r.IsVerified.Should().BeTrue(r.Error ?? "");
        r.LogoPath.Should().BeNull();
    }

    // ── Path-traversal defense in depth: a signed manifest can't steer a load outside the package ──

    [Fact]
    public void Rejects_a_traversal_entry_path()
    {
        // A validly-signed manifest whose entry escapes the package must be refused before any load —
        // otherwise a signed add-in could point the loader (Path.Combine) at an arbitrary local assembly.
        ReSignManifest(_pkg, m => m with { Entry = "../evil.dll" });

        var r = PackageVerification.Verify(_pkg, _rootPub);

        r.IsVerified.Should().BeFalse();
        r.Error.Should().Contain("entry path is not a safe");
    }

    [Fact]
    public void Rejects_a_rooted_entry_path()
    {
        ReSignManifest(_pkg, m => m with { Entry = OperatingSystem.IsWindows() ? @"C:\evil.dll" : "/evil.dll" });

        var r = PackageVerification.Verify(_pkg, _rootPub);

        r.IsVerified.Should().BeFalse();
        r.Error.Should().Contain("entry path is not a safe");
    }

    [Fact]
    public void Rejects_a_traversal_file_path()
    {
        // A listed payload path that escapes the package is refused even with a valid signature, so the
        // hash step (and any later native/managed probe of that path) never reaches outside the package.
        ReSignManifest(_pkg, m => m with
        {
            Files = [.. m.Files, new AddinFileHash { Path = "../escape.dll", Sha256 = "00" }]
        });

        var r = PackageVerification.Verify(_pkg, _rootPub);

        r.IsVerified.Should().BeFalse();
        r.Error.Should().Contain("unsafe file path");
    }

    // ── Trust chain (Phase 14): fingerprints + the chain surfaced on a verified result ──

    [Fact]
    public void KeyFingerprint_is_grouped_uppercase_hex_and_deterministic()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var spki = PackageCrypto.ExportPublicKey(key);

        var fp = PackageCrypto.KeyFingerprint(spki);

        fp.Replace(" ", "").Should().MatchRegex("^[0-9A-F]{64}$");   // SHA-256 = 64 upper-case hex chars
        fp.Should().Contain(" ");                                    // space-grouped
        PackageCrypto.KeyFingerprint(spki).Should().Be(fp);          // deterministic
    }

    [Fact]
    public void Verified_result_carries_the_trust_chain_with_matching_fingerprints()
    {
        var r = PackageVerification.Verify(_pkg, _rootPub, _firstPartyPub);

        r.IsVerified.Should().BeTrue(r.Error ?? "");
        r.Chain.Should().NotBeNull();
        var chain = r.Chain!;

        chain.BuildHash.Should().Be(r.ManifestHash);
        chain.BuildVersion.Should().Be("1.0.0");
        chain.IsFirstParty.Should().BeTrue();

        // The publisher fingerprint is exactly SHA-256 of the publisher's public key in the trust dir.
        var pub = JsonSerializer.Deserialize(File.ReadAllBytes(Path.Combine(_trust, "publisher.json")),
            AddinJsonContext.Default.PublisherRecord)!;
        chain.Publisher.KeyFingerprint.Should().Be(PackageCrypto.KeyFingerprint(pub.PublicKey));
        chain.Publisher.Id.Should().Be("com.hexide.firstparty");
        chain.Root.KeyFingerprint.Should().Be(PackageCrypto.KeyFingerprint(_rootPub));
        chain.Intermediate.KeyFingerprint.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void An_untrusted_result_carries_no_chain()
    {
        File.WriteAllBytes(Path.Combine(_pkg, "MyAddin.dll"), [9, 9, 9]);   // breaks the hash

        var r = PackageVerification.Verify(_pkg, _rootPub);

        r.IsVerified.Should().BeFalse();
        r.Chain.Should().BeNull();
    }

    // Packs a fresh single-DLL package that also bundles a logo file (any bytes — the verifier hashes,
    // it does not decode).
    private string PackWithLogo(string logoName)
    {
        var src = Path.Combine(_root, "src_logo_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(src);
        File.WriteAllBytes(Path.Combine(src, "MyAddin.dll"), [1, 2, 3, 4, 5]);
        File.WriteAllBytes(Path.Combine(src, logoName), [0x89, 0x50, 0x4E, 0x47, 1, 2, 3, 4]);
        var outDir = Path.Combine(_root, "pkg_logo_" + Guid.NewGuid().ToString("N"));
        PackageSigner.Pack(new PackRequest
        {
            SrcDir = src,
            OutDir = outDir,
            Id = "com.test.addin",
            Title = "Test Addin",
            Version = "1.0.0",
            Author = "Tester",
            Entry = "MyAddin.dll",
            TrustDir = _trust,
            Logo = logoName,
        });
        return outDir;
    }

    // Rewrites addin.json's logoPath and re-signs with the chain's first-party key, so the signature
    // stays valid and only the logoPath sanitization is under test.
    private void ReSignManifestWithLogoPath(string pkg, string logoPath) =>
        ReSignManifest(pkg, m => m with { LogoPath = logoPath });

    // Applies an arbitrary mutation to addin.json and re-signs with the chain's first-party key, so the
    // signature stays valid and only the mutated field is under test.
    private void ReSignManifest(string pkg, Func<AddinManifest, AddinManifest> mutate)
    {
        var manifestPath = Path.Combine(pkg, "addin.json");
        var manifest = JsonSerializer.Deserialize(File.ReadAllBytes(manifestPath), AddinJsonContext.Default.AddinManifest)!;
        var newBytes = JsonSerializer.SerializeToUtf8Bytes(mutate(manifest), AddinJsonContext.Default.AddinManifest);
        File.WriteAllBytes(manifestPath, newBytes);
        var keyB64 = File.ReadAllText(Path.Combine(_trust, "firstparty.key")).Trim();
        using var key = PackageCrypto.ImportPrivateKey(keyB64);
        File.WriteAllText(Path.Combine(pkg, "addin.sig"), PackageCrypto.Sign(key, newBytes));
    }
}
