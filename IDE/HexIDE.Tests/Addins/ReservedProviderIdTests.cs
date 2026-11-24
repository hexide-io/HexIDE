using HexIDE.Addins;
using HexIDE.IDE;
using HexIDE.Localization;

namespace HexIDE.Tests.Addins;

/// <summary>
/// A package can't take HexIDE's own ids, or reach what the composed Basic provider registered by naming its id (#5597).
/// </summary>
public sealed class ReservedProviderIdTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "hexide-reserved-id-" + Guid.NewGuid().ToString("N"));
    private readonly string packages;
    private readonly IPackageVerifier verifier = Substitute.For<IPackageVerifier>();
    private readonly LocalizationService localization = new();
    private readonly IHexIdeHost host = Substitute.For<IHexIdeHost>();

    // The provider's identity, as HexIDE.Desktop composes it.
    private static readonly AddinOrigin Provider = new(RegistrationGrants.BasicProvider, "HexIDE.Basic") { IsComposed = true };

    public ReservedProviderIdTests()
    {
        packages = Path.Combine(root, "addins");
        Directory.CreateDirectory(packages);
        host.ToolWindows.Returns(new AddinToolWindowService(localization));
        host.Menus.Returns(new AddinMenuService(localization));
    }

    public void Dispose()
    {
        try { Directory.Delete(root, true); } catch { /* best effort: Windows may still hold the loaded probe */ }
    }

    private string Package(string folder, string id, bool firstParty)
    {
        var directory = Path.Combine(packages, folder);
        Directory.CreateDirectory(directory);
        DuplicateAddinTests.WriteProbe(Path.Combine(directory, "DuplicateProbe.dll"), folder);
        verifier.Verify(directory).Returns(new PackageVerificationResult(PackageVerdict.Verified,
            new AddinManifest { Id = id, Title = "Probe " + folder, Entry = "DuplicateProbe.dll" }, null, null, null)
            { ManifestHash = "hash-" + folder, IsFirstParty = firstParty });
        return directory;
    }

    private AddinRegistry Registry()
    {
        var windows = Substitute.For<IWindowManager>();
        windows.ShowDialog(Arg.Any<IDialog>()).Returns(Task.FromResult(true));
        return new AddinRegistry(verifier, Substitute.For<ISettingsService>(), Substitute.For<IDeveloperModeService>(), windows,
            localization, new ConsentStore(Path.Combine(root, "consent.json")),
            new RevocationStore(null, Path.Combine(root, "revocations")), Path.Combine(root, "state.json"), packages);
    }

    private sealed class Counted : IDisposable
    {
        public int Disposed { get; private set; }
        public void Dispose() => Disposed++;
    }

    [AvaloniaTheory]
    [InlineData("hexide.basic")]
    [InlineData("HexIDE.Basic")]
    [InlineData("hexide.other")]
    public async Task A_signed_third_party_package_with_a_reserved_id_is_refused_at_load(string id)
    {
        var package = Package("impostor", id, firstParty: false);
        using var registry = Registry();

        registry.LoadAll(host);
        await registry.PromptPendingConsentAsync();

        registry.GetStatus(package).Should().Be(AddinStatus.Error);
        var info = registry.GetAll().Single(addin => addin.AssemblyPath == package);
        info.LoadError.Should().BeEquivalentTo(new AddinMessage("Str.Addins.ReservedId", id));
        localization.GetString("Str.Addins.ReservedId").Should().Contain("{0}");
        host.DidNotReceive().RegisterCommand("initialized", Arg.Any<string>(), Arg.Any<Action>(), null, null);
    }

    [AvaloniaFact]
    public async Task A_first_party_package_may_hold_a_reserved_id()
    {
        var package = Package("bundled", "hexide.basic", firstParty: true);
        using var registry = Registry();

        registry.LoadAll(host);
        await registry.PromptPendingConsentAsync();

        registry.GetStatus(package).Should().Be(AddinStatus.Loaded);
        host.Received(1).RegisterCommand("initialized", "bundled", Arg.Any<Action>(), null, null);
    }

    [AvaloniaFact]
    public async Task Disabling_or_revoking_a_package_with_the_provider_s_id_leaves_the_provider_s_registrations()
    {
        var impostor = Package("impostor", "HexIDE.Basic", firstParty: false);
        using var registry = Registry();
        var themes = new Counted();
        registry.TrackContribution(Provider, themes);
        registry.LoadAll(host);
        await registry.PromptPendingConsentAsync();

        registry.SetEnabled(impostor, false);
        registry.RevokeConsent("hash-impostor");
        var drawer = new Counted();
        registry.TrackContribution(Provider, drawer);

        themes.Disposed.Should().Be(0);
        drawer.Disposed.Should().Be(0);
    }

    [AvaloniaFact]
    public async Task Disabling_a_package_still_takes_out_its_own_registrations()
    {
        var package = Package("own", "com.example.probe", firstParty: false);
        using var registry = Registry();
        registry.LoadAll(host);
        await registry.PromptPendingConsentAsync();
        var contribution = new Counted();
        var provider = new Counted();
        registry.TrackContribution(new AddinOrigin("Com.Example.Probe", "Probe own"), contribution);
        registry.TrackContribution(Provider with { Id = "COM.EXAMPLE.PROBE" }, provider);

        registry.SetEnabled(package, false);

        contribution.Disposed.Should().Be(1);
        provider.Disposed.Should().Be(0, "a composed owner is never the package, whatever its id");
        var later = new Counted();
        registry.TrackContribution(new AddinOrigin("COM.example.probe", "Probe own"), later);
        later.Disposed.Should().Be(1, "a disabled package's later registration is taken out at once, its id in any case");
    }
}
