using HexIDE.Addins;
using HexIDE.IDE;
using HexIDE.Localization;
using System.Security.Cryptography;

namespace HexIDE.Tests.Addins;

public sealed class StartupRevocationTests
{
    [Theory]
    [InlineData(true, "https://example.invalid/list", 0, true)]
    [InlineData(false, "https://example.invalid/list", 1, true)]
    [InlineData(false, "https://example.invalid/list", 0, false)]
    [InlineData(false, "", 0, true)]
    public void StartupFetchDependsOnPinnedFirstPartyAndConfiguredUrl(bool firstParty, string url, int expectedFetches, bool hasManifest)
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
}
