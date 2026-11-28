using HexIDE.Addins;
using HexIDE.IDE;
using HexIDE.Localization;
using System.Security.Cryptography;

namespace HexIDE.Tests.Addins;

public sealed class StartupRevocationTests
{
    // A first-party package fetches too: a build revocation applies to it, and a default install has nothing else, so
    // without the fetch a revoked AI Chat build would keep loading until HexIDE was updated (#5572).
    [Theory]
    [InlineData(true, "https://example.invalid/list", 1, true)]
    [InlineData(false, "https://example.invalid/list", 1, true)]
    [InlineData(false, "https://example.invalid/list", 0, false)]
    [InlineData(true, "https://example.invalid/list", 0, false)]
    [InlineData(false, "", 0, true)]
    [InlineData(true, "", 0, true)]
    public void StartupFetchDependsOnInstalledPackagesAndConfiguredUrl(bool firstParty, string url, int expectedFetches, bool hasManifest)
    {
        var dir = Path.Combine(Path.GetTempPath(), "hexide-startup-revocation-" + Guid.NewGuid().ToString("N"));
        var package = Path.Combine(dir, "addins", "package");
        Directory.CreateDirectory(package);
        try
        {
            using var root = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var fetches = 0;
            var store = new RevocationStore(PackageCrypto.ExportPublicKey(root), dir, (source, timeout) =>
            {
                source.Should().Be(url);
                timeout.Should().Be(TimeSpan.FromSeconds(3));
                fetches++;
                throw new IOException("Offline");
            });
            var verifier = Substitute.For<IPackageVerifier>();
            verifier.Verify(package).Returns(new PackageVerificationResult(PackageVerdict.Verified,
                hasManifest ? new AddinManifest { Id = "third.party", Title = "Package", Entry = "unused.dll" } : null,
                null, null, hasManifest ? null : "addin.json not found")
                { IsFirstParty = firstParty, ManifestHash = "hash" });
            var settings = Substitute.For<ISettingsService>();
            settings.RevocationListUrl.Returns(url);
            using var registry = new AddinRegistry(verifier, settings, Substitute.For<IDeveloperModeService>(),
                Substitute.For<IWindowManager>(), new LocalizationService(), new ConsentStore(Path.Combine(dir, "consent.json")),
                store, Path.Combine(dir, "state.json"), Path.Combine(dir, "addins"));

            registry.LoadAll(Substitute.For<IHexIdeHost>());

            fetches.Should().Be(expectedFetches);
            if (!firstParty && hasManifest)
                registry.GetStatus(package).Should().Be(AddinStatus.AwaitingConsent);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void A_first_party_build_revoked_on_the_published_list_is_refused_at_startup()
    {
        var dir = Path.Combine(Path.GetTempPath(), "hexide-startup-revocation-" + Guid.NewGuid().ToString("N"));
        var package = Path.Combine(dir, "addins", "package");
        Directory.CreateDirectory(package);
        try
        {
            using var root = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var list = System.Text.Encoding.UTF8.GetBytes(
                """{"schema":1,"issuedUtc":"2099-01-01T00:00:00Z","revoked":{"manifestHashes":["bad-build"],"publisherIds":[],"intermediateIds":[]}}""");
            var store = new RevocationStore(PackageCrypto.ExportPublicKey(root), dir,
                (_, _) => (list, PackageCrypto.Sign(root, list)));
            var verifier = Substitute.For<IPackageVerifier>();
            verifier.Verify(package).Returns(new PackageVerificationResult(PackageVerdict.Verified,
                new AddinManifest { Id = "com.hexide.aichat", Title = "AI Chat", Entry = "unused.dll" }, null, null, null)
                { IsFirstParty = true, ManifestHash = "bad-build" });
            var settings = Substitute.For<ISettingsService>();
            settings.RevocationListUrl.Returns("https://example.invalid/list");
            using var registry = new AddinRegistry(verifier, settings, Substitute.For<IDeveloperModeService>(),
                Substitute.For<IWindowManager>(), new LocalizationService(), new ConsentStore(Path.Combine(dir, "consent.json")),
                store, Path.Combine(dir, "state.json"), Path.Combine(dir, "addins"));

            registry.LoadAll(Substitute.For<IHexIdeHost>());

            registry.GetStatus(package).Should().Be(AddinStatus.Revoked,
                "the default install has only first-party add-ins, so the fetch must happen for them too");
        }
        finally { Directory.Delete(dir, true); }
    }

    // A key is checked at load by the chain the package verified with (#5315). The list names it as 64 lower-case hex digits,
    // the chain carries it grouped and upper-case, and they still match.
    [Theory]
    [InlineData(false, "publisher", AddinStatus.Revoked)]
    [InlineData(false, "intermediate", AddinStatus.Revoked)]
    [InlineData(true, "publisher", AddinStatus.Revoked)]
    [InlineData(true, "intermediate", AddinStatus.Error)]
    public void A_package_signed_with_a_revoked_key_is_refused_at_load(bool firstParty, string revokedKey, AddinStatus expected)
    {
        var dir = Path.Combine(Path.GetTempPath(), "hexide-startup-revocation-" + Guid.NewGuid().ToString("N"));
        var package = Path.Combine(dir, "addins", "package");
        Directory.CreateDirectory(package);
        try
        {
            using var root = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            using var publisher = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            using var intermediate = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var publisherKey = PackageCrypto.KeyFingerprint(PackageCrypto.ExportPublicKey(publisher));
            var intermediateKey = PackageCrypto.KeyFingerprint(PackageCrypto.ExportPublicKey(intermediate));
            var listed = (revokedKey == "publisher" ? publisherKey : intermediateKey).Replace(" ", "").ToLowerInvariant();
            var list = System.Text.Encoding.UTF8.GetBytes(
                """{"schema":1,"issuedUtc":"2099-01-01T00:00:00Z","revoked":{"manifestHashes":[],"publisherIds":[],"intermediateIds":[],"keyFingerprints":["""
                + "\"" + listed + "\"]}}");
            var store = new RevocationStore(PackageCrypto.ExportPublicKey(root), dir,
                (_, _) => (list, PackageCrypto.Sign(root, list)));
            var verifier = Substitute.For<IPackageVerifier>();
            verifier.Verify(package).Returns(new PackageVerificationResult(PackageVerdict.Verified,
                new AddinManifest { Id = "acme.probe", Title = "Probe", Entry = "unused.dll" }, "Acme", "acme", null)
            {
                IsFirstParty = firstParty, ManifestHash = "hash", IntermediateId = "marketplace",
                Chain = new TrustChain
                {
                    BuildVersion = "1", BuildHash = "hash", IsFirstParty = firstParty,
                    Publisher = new TrustLink("Acme", "acme", publisherKey),
                    Intermediate = new TrustLink("Marketplace", "marketplace", intermediateKey),
                    Root = new TrustLink("HexIDE root", "", PackageCrypto.KeyFingerprint(PackageCrypto.ExportPublicKey(root))),
                },
            });
            var settings = Substitute.For<ISettingsService>();
            settings.RevocationListUrl.Returns("https://example.invalid/list");
            using var registry = new AddinRegistry(verifier, settings, Substitute.For<IDeveloperModeService>(),
                Substitute.For<IWindowManager>(), new LocalizationService(), new ConsentStore(Path.Combine(dir, "consent.json")),
                store, Path.Combine(dir, "state.json"), Path.Combine(dir, "addins"));

            registry.LoadAll(Substitute.For<IHexIdeHost>());

            // A first-party package that isn't revoked goes on to load, and fails here only for want of unused.dll.
            registry.GetStatus(package).Should().Be(expected);
        }
        finally { Directory.Delete(dir, true); }
    }
}
