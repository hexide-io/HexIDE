using System.Reflection;
using HexIDE.Addins;
using HexIDE.Desktop;
using HexIDE.IDE;
using HexIDE.Keymaps;
using HexIDE.Localization;
using HexIDE.Sidecar;

namespace HexIDE.Tests.Profiles;

public sealed class ProfileRecoveryTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "hexide-profile-recovery-" + Guid.NewGuid());
    private readonly bool previousRecovery = Static.IsRecoveryMode;
    private readonly Func<IProjectService, IProjectManager, Task>? previousHook = Static.StartupProjectHook;
    private readonly Action<DISetup, Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime>? previousDesktopHook = Static.DesktopStartupHook;

    public ProfileRecoveryTests()
    {
        Directory.CreateDirectory(root);
        Static.IsRecoveryMode = true;
    }

    [Fact]
    public void SettingsIgnoreSavedValuesAndUseRecoveryBaseline()
    {
        var path = Path.Combine(root, "settings.json");
        File.WriteAllText(path, """{"tabWidth":19,"activeTheme":"Dark","activeKeymap":"VB6","activeLanguage":"fr","window":{"state":"Normal","width":400}}""");
        var settings = new SettingsService(path);
        settings.TabWidth.Should().Be(SettingsDefaults.TabWidth);
        settings.ActiveTheme.Should().Be("Classic");
        settings.ActiveKeymap.Should().Be("Default");
        settings.ActiveLanguage.Should().Be("en");
        settings.Window.Should().Be(WindowData.Default);
        settings.HasExplicitThemeChoice.Should().BeFalse();
        settings.HasExplicitKeymapChoice.Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SettingsEditsStayInMemoryIncludingAutomaticSaves(bool existing)
    {
        var path = Path.Combine(root, "settings.json");
        const string original = "{}";
        if (existing) File.WriteAllText(path, original);
        var settings = new SettingsService(path);
        settings.TabWidth = 17;
        settings.IsStandardToolbarVisible = !SettingsDefaults.IsStandardToolbarVisible;
        settings.Window = WindowData.Default with { Width = 555 };
        settings.Save();
        settings.Load();
        settings.TabWidth.Should().Be(17);
        settings.Window.Width.Should().Be(555);
        if (existing) File.ReadAllText(path).Should().Be(original);
        else File.Exists(path).Should().BeFalse();
        Directory.GetFiles(root, "*.tmp").Should().BeEmpty();
    }

    private WindowStateService LayoutService(string path)
    {
        var service = new WindowStateService(new SettingsService(Path.Combine(root, "settings.json")));
        typeof(WindowStateService).GetField("_dockLayoutPath", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(service, path);
        return service;
    }

    [Fact]
    public void LayoutIgnoresDiskAndResetNeverDeletesSavedLayout()
    {
        var path = Path.Combine(root, "layout.json");
        var original = LayoutManifestJson.Serialize(LayoutManifest.Default);
        File.WriteAllText(path, original);
        var service = LayoutService(path);
        service.LoadLayoutManifest().Should().BeNull();
        service.SaveLayoutManifest(LayoutManifest.Default);
        service.LoadLayoutManifest().Should().NotBeNull();
        service.ResetLayoutManifest();
        service.LoadLayoutManifest().Should().BeNull();
        File.ReadAllText(path).Should().Be(original);
    }

    [Fact]
    public void LayoutChangesWithoutExistingFileStayInMemory()
    {
        var path = Path.Combine(root, "layout.json");
        var service = LayoutService(path);
        service.SaveLayoutManifestJson(LayoutManifestJson.Serialize(LayoutManifest.Default));
        service.LoadLayoutManifest().Should().NotBeNull();
        File.Exists(path).Should().BeFalse();
        service.ResetLayoutManifest();
        service.LoadLayoutManifest().Should().BeNull();
    }

    [AvaloniaFact]
    public void KeymapDiscoveryIgnoresUserPacks()
    {
        File.WriteAllText(Path.Combine(root, "custom.json"), """{"name":"custom","bindings":{}}""");
        var keymaps = new KeymapService(root);
        keymaps.AvailableKeymaps.Should().Equal("Default", "VB6");
        keymaps.ActiveKeymap.Should().Be("Default");
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void TranslationOverridesAreNeitherLoadedNorWritten(bool existing)
    {
        var directory = Path.Combine(root, "translations");
        var path = Path.Combine(directory, "en.json");
        const string original = """{"id":"en","strings":{"Str.Menu.File":"Saved override"}}""";
        if (existing)
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(path, original);
        }
        var translations = new UserTranslationsService(directory);
        translations.GetOverrides("en").Should().BeEmpty();
        translations.SetOverride("en", "Str.Menu.File", "Session file").Should().BeTrue();
        translations.GetOverrides("en")["Str.Menu.File"].Should().Be("Session file");
        translations.LoadAll();
        translations.GetOverrides("en")["Str.Menu.File"].Should().Be("Session file");
        translations.ResetAll("en");
        translations.GetOverrides("en").Should().BeEmpty();
        if (existing) File.ReadAllText(path).Should().Be(original);
        else Directory.Exists(directory).Should().BeFalse();
    }

    [Fact]
    public void AddinLoadingDoesNotEvenVerifyPackagesOrPersistEnablement()
    {
        var directory = Path.Combine(root, "addins");
        Directory.CreateDirectory(Path.Combine(directory, "example"));
        var verifier = Substitute.For<IPackageVerifier>();
        var state = Path.Combine(root, "addins.json");
        using var registry = new AddinRegistry(verifier, Substitute.For<ISettingsService>(),
            Substitute.For<IDeveloperModeService>(), Substitute.For<IWindowManager>(),
            Substitute.For<ILocalizationService>(), new ConsentStore(Path.Combine(root, "consent.json")),
            new RevocationStore(null, root), state, directory);
        registry.LoadAll(Substitute.For<IHexIdeHost>());
        verifier.DidNotReceiveWithAnyArgs().Verify(default!);
        registry.GetAll().Should().BeEmpty();
        registry.SetEnabled(directory, false);
        File.Exists(state).Should().BeFalse();
    }

    [Fact]
    public async Task StartupSelectsRecoveryAndStillOpensExplicitProject()
    {
        var properties = typeof(ServerOptions).GetProperties(BindingFlags.Public | BindingFlags.Static)
            .Where(p => p.SetMethod != null).ToDictionary(p => p, p => p.GetValue(null));
        var capture = Static.CaptureLsp;
        var developer = Static.DeveloperMode;
        try
        {
            foreach (var property in properties.Keys)
                property.SetValue(null, property.PropertyType.IsValueType ? Activator.CreateInstance(property.PropertyType) : null);
            Static.IsRecoveryMode = false;
            var path = Path.Combine(root, "Explicit.vbp");
            ServerOptions.ParseArgs(["--profile", "none", path], root);
            DesktopStartup.Register();
            Static.IsRecoveryMode.Should().BeTrue();
            File.WriteAllText(Path.Combine(root, "Module1.bas"), "Attribute VB_Name = \"Module1\"\r\nPublic Sub Main()\r\nEnd Sub\r\n");
            File.WriteAllText(path, "Type=Exe\r\nModule=Module1; Module1.bas\r\nName=\"Explicit\"\r\n");
            var manager = Substitute.For<IProjectManager>();
            var projects = new HexIDE.Projects.ProjectService(
                () => throw new InvalidOperationException("Unexpected project dialog"),
                Substitute.For<IWindowManager>(), new EventBus(), manager,
                Substitute.For<IRecentProjectsService>(), Substitute.For<IReferenceLibraryService>(),
                Substitute.For<IUserSidecarService>(), new FileBaselineStore(),
                Substitute.For<ILocalizationService>(), Substitute.For<IHeaderRefresher>(), new Vb6ProjectFileFormat());
            await Static.StartupProjectHook!(projects, manager);
            manager.Received(1).AddProject(Arg.Is<HexIDE.Runtime.ProjectElements.ProjectDefinition>(
                p => p.Name == "Explicit" && p.Modules.Count == 1));
            typeof(ServerOptions).GetProperty(nameof(ServerOptions.Profile))!.SetValue(null, "vb6");
            DesktopStartup.Register();
            Static.IsRecoveryMode.Should().BeFalse();
        }
        finally
        {
            foreach (var (property, value) in properties) property.SetValue(null, value);
            Static.CaptureLsp = capture;
            Static.DeveloperMode = developer;
        }
    }

    public void Dispose()
    {
        Static.IsRecoveryMode = previousRecovery;
        Static.StartupProjectHook = previousHook;
        Static.DesktopStartupHook = previousDesktopHook;
        Directory.Delete(root, true);
    }
}
