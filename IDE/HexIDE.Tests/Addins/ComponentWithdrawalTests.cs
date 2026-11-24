using HexIDE.Addins;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Runtime.Components;

namespace HexIDE.Tests.Addins;

/// <summary>
/// A disabled add-in's components and toolbox items go, as its other contributions do, and the Basic provider's stay
/// (#5601). Each test reads what the component registry then holds.
/// </summary>
public sealed class ComponentWithdrawalTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "hexide-component-withdrawal-" + Guid.NewGuid().ToString("N"));
    private readonly string packages;
    private readonly IPackageVerifier verifier = Substitute.For<IPackageVerifier>();
    private readonly LocalizationService localization = new();
    private readonly IHexIdeHost loader = Substitute.For<IHexIdeHost>();

    private static readonly AddinOrigin Provider = new(RegistrationGrants.BasicProvider, "HexIDE.Basic") { IsComposed = true };
    private static readonly AddinOrigin Own = new("com.example.probe", "Probe own");

    public ComponentWithdrawalTests()
    {
        packages = Path.Combine(root, "addins");
        Directory.CreateDirectory(packages);
        loader.ToolWindows.Returns(new AddinToolWindowService(localization));
        loader.Menus.Returns(new AddinMenuService(localization));
    }

    public void Dispose()
    {
        try { Directory.Delete(root, true); } catch { /* best effort: Windows may still hold the loaded probe */ }
    }

    private sealed class Probe(string typeName) : IComponentClass
    {
        public string Name => typeName;
        public string VBTypeName => typeName;
        public string ClassName => typeName[(typeName.LastIndexOf('.') + 1)..];
        public bool IsVisual => true;
        public IReadOnlyList<PropertyClass> Properties => [];
        public IReadOnlyDictionary<string, PropertyClass> PropertiesByName => new Dictionary<string, PropertyClass>();
        public IReadOnlyList<EventClass> Events => [];
        public IReadOnlyList<PropertyChange> ApplyDesignerConsequences(ComponentInstance instance, PropertyClass property) => [];
    }

    // A loaded third-party package, com.example.probe, consented to.
    private async Task<(AddinRegistry Addins, string Package)> Loaded()
    {
        var package = Path.Combine(packages, "own");
        Directory.CreateDirectory(package);
        DuplicateAddinTests.WriteProbe(Path.Combine(package, "DuplicateProbe.dll"), "own");
        verifier.Verify(package).Returns(new PackageVerificationResult(PackageVerdict.Verified,
            new AddinManifest { Id = Own.Id, Title = "Probe own", Entry = "DuplicateProbe.dll" }, null, null, null)
            { ManifestHash = "hash-own" });
        var windows = Substitute.For<IWindowManager>();
        windows.ShowDialog(Arg.Any<IDialog>()).Returns(Task.FromResult(true));
        var addins = new AddinRegistry(verifier, Substitute.For<ISettingsService>(), Substitute.For<IDeveloperModeService>(), windows,
            localization, new ConsentStore(Path.Combine(root, "consent.json")),
            new RevocationStore(null, Path.Combine(root, "revocations")), Path.Combine(root, "state.json"), packages);
        addins.LoadAll(loader);
        await addins.PromptPendingConsentAsync();
        addins.GetStatus(package).Should().Be(AddinStatus.Loaded);
        return (addins, package);
    }

    private static HexIdeHost Ide(IAddinRegistry addins, ComponentRegistry components) =>
        new(null!, null!, null!, addins, null!, null!, null!, null!, null!, null!, components, null!, null!, null!, null!,
            new AddinResourceService(), new AddinLanguagePackService());

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Disabling_or_revoking_an_add_in_takes_out_its_components_and_toolbox_items_and_leaves_the_provider_s(bool revoke)
    {
        var (addins, package) = await Loaded();
        using var _ = addins;
        var components = new ComponentRegistry();
        var ide = Ide(addins, components);
        var gauge = new Probe("Probe.Gauge");
        var own = new AddinOwnedHost(ide, null!, Own);
        own.RegisterComponent(gauge);
        own.RegisterToolboxItem(gauge);
        var provider = new AddinOwnedHost(ide, null!, Provider);
        provider.RegisterComponent(CommandButtonComponentClass.Instance);
        provider.RegisterToolboxItem(CommandButtonComponentClass.Instance, "Geo.Button");
        components.Components.Should().HaveCount(2);

        if (revoke) addins.RevokeConsent("hash-own");
        else addins.SetEnabled(package, false);

        components.Components.Should().Equal(CommandButtonComponentClass.Instance);
        components.Toolbox.Should().Equal(new ToolboxEntry(CommandButtonComponentClass.Instance, "Geo.Button"));
    }

    [AvaloniaFact]
    public async Task A_disabled_add_in_s_earlier_component_goes_and_a_later_one_is_taken_out_at_once()
    {
        var (addins, package) = await Loaded();
        using var _ = addins;
        var components = new ComponentRegistry();
        var own = new AddinOwnedHost(Ide(addins, components), null!, Own);
        var gauge = new Probe("Probe.Gauge");
        var dial = new Probe("Probe.Dial");
        own.RegisterComponent(dial);
        addins.SetEnabled(package, false);

        own.RegisterComponent(gauge);

        components.Components.Should().BeEmpty();
        components.Toolbox.Should().BeEmpty();
    }
}
