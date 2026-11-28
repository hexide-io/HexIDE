using HexIDE.Addins;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;

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

    private static HexIdeHost Ide(IAddinRegistry addins, ComponentRegistry components, AddinLanguageEditingService? editing = null) =>
        new(null!, null!, null!, addins, null!, null!, null!, null!, null!, null!, components, null!, null!, null!, null!,
            new AddinResourceService(), new AddinLanguagePackService(), editing ?? new AddinLanguageEditingService());

    // No real grant lets a package register a language's editing helpers yet, so the probe stands in for phase 3's first-party
    // provider package, signed first-party, with a test grant (#5403). The add-in registry withdraws them under its lock, from
    // the thread that disabled the add-in, so this runs the disable off the UI thread too.
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Disabling_or_revoking_an_add_in_takes_out_its_language_editing_and_leaves_the_provider_s(bool revoke)
    {
        var (addins, package) = await Loaded();
        using var _ = addins;
        var editing = new AddinLanguageEditingService
        {
            Grants = [.. RegistrationGrants.All, new(Own.Id, Composed: false) { LanguageEditing = true }],
        };
        var ide = Ide(addins, new ComponentRegistry(), editing);
        var brackets = Substitute.For<HexIDE.Language.IBracketReader>();
        brackets.Brackets(default!).ReturnsForAnyArgs([new HexIDE.Language.BracketMark(0, 0)]);
        new AddinOwnedHost(ide, null!, Own with { IsFirstParty = true })
            .RegisterLanguageEditing(new HexIDE.Language.LanguageEditing(["acme"]) { Brackets = brackets });
        new AddinOwnedHost(ide, null!, Provider).RegisterLanguageEditing(HexIDE.Runtime.BasicProvider.LanguageEditing);
        var changes = 0;
        editing.Changed += () => Interlocked.Increment(ref changes);

        await Task.Run(() =>
        {
            if (revoke) addins.RevokeConsent("hash-own");
            else addins.SetEnabled(package, false);
        }, TestContext.Current.CancellationToken);

        editing.BracketsFor("acme").Brackets("()").Should().BeEmpty();
        editing.BracketsFor("vb6").Brackets("()").Should().HaveCount(2);
        changes.Should().Be(1);
    }

    // No real grant lets a package make a project type's programs yet, so the probe stands in for phase 3's first-party
    // provider package, signed first-party, with a test grant for a type of its own (#5621). The registry withdraws the maker under its lock, from
    // the thread that disabled the add-in, so this runs the disable off the UI thread, which is held in Wait meanwhile.
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Disabling_or_revoking_an_add_in_takes_out_its_project_maker_and_leaves_the_provider_s(bool revoke)
    {
        var (addins, package) = await Loaded();
        using var _ = addins;
        var makers = new HexIDE.Projects.ProjectMakers
        {
            Grants = [.. RegistrationGrants.All, new(Own.Id, Composed: false) { MakesProjectTypes = ["acme.project"] }],
        };
        var ide = new HexIdeHost(null!, null!, null!, addins, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!,
            new HexIDE.Projects.ContributedProjects { Makers = makers }, new AddinResourceService(), new AddinLanguagePackService(),
            new AddinLanguageEditingService());
        new AddinOwnedHost(ide, null!, Own with { IsFirstParty = true }).RegisterProjectMaker(new AcmeMaker(new HexIDE.Runtime.Making.Vb6ProjectMaker()));
        new AddinOwnedHost(ide, null!, Provider).RegisterProjectMaker(new HexIDE.Runtime.Making.Vb6ProjectMaker());
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var acme = new ProjectDefinition(VBProjectType.EXE, "A") { ContributedBy = "acme.project" };
        var vb6Project = new ProjectDefinition(VBProjectType.EXE, "V");
        makers.Makes(acme).Should().BeTrue();
        var changes = 0;
        makers.Changed += () => Interlocked.Increment(ref changes);

        Task.Run(() =>
        {
            if (revoke) addins.RevokeConsent("hash-own");
            else addins.SetEnabled(package, false);
        }).Wait(TimeSpan.FromSeconds(10)).Should().BeTrue("the registry disposes under its lock, and the withdrawal only posts");

        makers.Makes(acme).Should().BeFalse("a withdrawn maker is asked nothing from the moment it's disposed");
        changes.Should().Be(0, "what's offered is asked again on the UI thread, not under the registry's lock");
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        changes.Should().Be(1);
        makers.Makes(vb6Project).Should().BeTrue("the provider's maker stays");
    }

    // A maker for a type of the probe's own, which the test grant names.
    private sealed class AcmeMaker(HexIDE.IDE.IProjectMaker inner) : HexIDE.IDE.IProjectMaker
    {
        public string ProjectTypeId => "acme.project";
        public (string Field, string Value)? RefusedProgramName(ProjectDefinition project) => inner.RefusedProgramName(project);
        public string ProgramPath(ProjectDefinition project) => inner.ProgramPath(project);
        public HexIDE.IDE.MakeRefusal? Unavailable(string program) => inner.Unavailable(program);
        public IReadOnlyList<string> WrittenBeside(string program) => inner.WrittenBeside(program);
        public void Make(ProjectDefinition project, string sources, string projectFile, string program) =>
            inner.Make(project, sources, projectFile, program);
        public HexIDE.IDE.IProjectToolchain? Toolchain => null;
    }

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

    // No real grant lets a package keep a library's namespace yet. Phase 3's first-party provider package will, so the probe
    // stands in for one here, signed first-party, with a test grant keeping Acme.
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Disabling_or_revoking_an_add_in_takes_out_its_control_libraries_and_leaves_the_provider_s(bool revoke)
    {
        var (addins, package) = await Loaded();
        using var _ = addins;
        var components = new ComponentRegistry
        {
            Grants = [.. RegistrationGrants.All, new(Own.Id, Composed: false) { TypeNamespaces = ["Acme"] }],
        };
        var ide = Ide(addins, components);
        var acme = Substitute.For<IControlLibrary>();
        acme.Name.Returns("Acme");
        acme.Guid.Returns("{00000000-0000-0000-0000-00000000ac3e}");
        new AddinOwnedHost(ide, null!, Own with { IsFirstParty = true }).RegisterControlLibrary(acme, () => [new Probe("Acme.Gauge")]);
        var common = HexIDE.Runtime.BasicProvider.Libraries[0];
        new AddinOwnedHost(ide, null!, Provider).RegisterControlLibrary(common, () => HexIDE.Runtime.BasicProvider.ControlsOf(common));
        components.Libraries.Should().HaveCount(2);

        if (revoke) addins.RevokeConsent("hash-own");
        else addins.SetEnabled(package, false);

        components.Libraries.Should().Equal(common);
        components.LibraryControls.Should().NotBeEmpty().And.OnlyContain(control => ReferenceEquals(control.Library, common));
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
