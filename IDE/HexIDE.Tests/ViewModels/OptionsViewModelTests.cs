using System.Collections.Generic;
using System.Linq;
using HexIDE.Addins;
using HexIDE.Forms.ViewModels;
using HexIDE.Forms.ViewModels.Options;
using HexIDE.IDE;
using HexIDE.Keymaps;
using HexIDE.Localization;
using HexIDE.Themes;

namespace HexIDE.Tests.ViewModels;

public class OptionsViewModelTests
{
    private readonly ISettingsService _settings = Substitute.For<ISettingsService>();
    private readonly IThemeService _themeService = Substitute.For<IThemeService>();
    private readonly IKeymapService _keymapService = Substitute.For<IKeymapService>();
    private readonly ILanguageSwitchService _languageSwitch = Substitute.For<ILanguageSwitchService>();
    private readonly IAddinRegistry _addinRegistry = Substitute.For<IAddinRegistry>();
    private readonly AddinOptionsService _addinOptions = new();
    private readonly IDeveloperModeService _devMode = Substitute.For<IDeveloperModeService>();
    private readonly IEventBus _eventBus = Substitute.For<IEventBus>();
    private readonly ILocalizationService _localization;

    private static readonly AddinInfo _addinA = new("/addins/A.dll", "Add-in A", "First add-in", "1.0", "Me");
    private static readonly AddinInfo _addinB = new("/addins/B.dll", "Add-in B", "Second add-in", "2.0", "You");

    public OptionsViewModelTests()
    {
        // Use a real LocalizationService (canonical en) so node labels resolve to real English.
        AvaloniaTestSetup.EnsureInitialized();
        _localization = new LocalizationService();

        // Default snapshot returned by the mock settings service.
        _settings.RequireVariableDeclaration.Returns(false);
        _settings.AutoListMembers.Returns(true);
        _settings.AutoQuickInfo.Returns(true);
        _settings.AutoIndent.Returns(true);
        _settings.TabWidth.Returns(4);
        _settings.FormatOnSave.Returns(true);
        _settings.ShowGrid.Returns(true);
        _settings.GridWidth.Returns(8);
        _settings.GridHeight.Returns(8);
        _settings.AlignToGrid.Returns(true);
        _settings.PromptForProjectOnStartup.Returns(true);
        _settings.ActiveTheme.Returns("Classic");
        _settings.ActiveKeymap.Returns("Default");
        _settings.IsMinimapVisible.Returns(true);
        _settings.IsStandardToolbarVisible.Returns(true);
        _themeService.ActiveTheme.Returns("Classic");
        _themeService.AvailableThemes.Returns(["Classic", "Dark"]);
        _keymapService.ActiveKeymap.Returns("Default");
        _keymapService.AvailableKeymaps.Returns(["Default", "VB6"]);
        _settings.ActiveLanguage.Returns("en");
        _languageSwitch.ActiveLanguage.Returns("en");
        _languageSwitch.AvailableLanguages.Returns([new LanguageInfo("en", "English", "ltr")]);
        _languageSwitch.RegionsFor(Arg.Any<string>()).Returns([]);
        _addinRegistry.GetAll().Returns([_addinA, _addinB]);
        _addinRegistry.GetStatus(_addinA.AssemblyPath).Returns(AddinStatus.Loaded);
        _addinRegistry.GetStatus(_addinB.AssemblyPath).Returns(AddinStatus.Disabled);
    }

    private OptionsViewModel CreateSut() => new(_settings, _themeService, _keymapService, _languageSwitch, _addinRegistry, _addinOptions, _devMode, _localization, _eventBus);

    private static IEnumerable<OptionsNodeViewModel> Flatten(IEnumerable<OptionsNodeViewModel> nodes)
    {
        foreach (var n in nodes)
        {
            yield return n;
            foreach (var c in Flatten(n.Children)) yield return c;
        }
    }

    private static T Page<T>(OptionsViewModel sut) where T : class
        => Flatten(sut.RootNodes).Select(n => n.Page).OfType<T>().First();

    private static AddinOptionsPageViewModel Addin(OptionsViewModel sut, string title)
        => Flatten(sut.RootNodes).Select(n => n.Page).OfType<AddinOptionsPageViewModel>().First(p => p.Title == title);

    // ── Tree / navigation ───────────────────────────────────────

    [Fact]
    public void Constructor_BuildsExpectedGroups()
    {
        var sut = CreateSut();

        // "Advanced" held exactly one page — the LSP WebSocket URL — and went with it when a language
        // server's transport became a property of its entry in the configuration file (#255). An empty
        // group is worse than no group.
        sut.RootNodes.Select(n => n.Title)
            .Should().Equal("Environment", "Editor", "Form Designer", "Add-Ins");
    }

    [Fact]
    public void TreeNodes_Relocalize_WhenLanguageChangesMidDialog()
    {
        var sut = CreateSut();
        sut.RootNodes[0].Title.Should().Be("Environment");

        _localization.Apply("fr");   // e.g. the in-dialog Language page previews French live

        sut.RootNodes[0].Title.Should().Be("Environnement");
    }

    [Fact]
    public void DeveloperNode_Hidden_WhenNotInDeveloperMode()
    {
        _devMode.IsEnabled.Returns(false);

        var sut = CreateSut();

        sut.RootNodes.Select(n => n.Title).Should().NotContain("Developer");
    }

    [Fact]
    public void DeveloperNode_ShownLast_WhenInDeveloperMode()
    {
        _devMode.IsEnabled.Returns(true);

        var sut = CreateSut();

        sut.RootNodes.Last().Title.Should().Be("Developer");
        sut.RootNodes.Last().Page.Should().BeOfType<DeveloperPageViewModel>();
    }

    [Fact]
    public void Constructor_SelectsFirstLeafPage()
    {
        var sut = CreateSut();

        sut.SelectedNode.Should().NotBeNull();
        sut.SelectedNode!.Page.Should().BeOfType<EnvironmentGeneralPageViewModel>();
        sut.CurrentPage.Should().BeSameAs(sut.SelectedNode.Page);
    }

    [Fact]
    public void SelectedNode_DrivesCurrentPage()
    {
        var sut = CreateSut();
        var gridNode = Flatten(sut.RootNodes).First(n => n.Page is FormDesignerGridPageViewModel);

        sut.SelectedNode = gridNode;

        sut.CurrentPage.Should().BeOfType<FormDesignerGridPageViewModel>();
    }

    [Fact]
    public void Title_IsOptions()
    {
        CreateSut().Title.Should().Be("Options");
    }

    [Fact]
    public void CanResize_IsTrue()
    {
        CreateSut().CanResize.Should().BeTrue();
    }

    // ── Page load ───────────────────────────────────────────────

    [Fact]
    public void Pages_LoadFromSettings()
    {
        _settings.TabWidth.Returns(8);
        _settings.AutoQuickInfo.Returns(false);
        _settings.GridWidth.Returns(12);

        var sut = CreateSut();

        Page<EditorFormattingPageViewModel>(sut).TabWidth.Should().Be(8);
        Page<EditorGeneralPageViewModel>(sut).AutoQuickInfo.Should().BeFalse();
        Page<FormDesignerGridPageViewModel>(sut).GridWidth.Should().Be(12);
    }

    // ── OK commits ──────────────────────────────────────────────

    [Fact]
    public void OkCommand_WritesAllPagesBackToSettings()
    {
        var sut = CreateSut();
        Page<EditorFormattingPageViewModel>(sut).TabWidth = 2;
        Page<EditorGeneralPageViewModel>(sut).AutoListMembers = false;
        Page<FormDesignerGridPageViewModel>(sut).ShowGrid = false;
        Page<EnvironmentGeneralPageViewModel>(sut).PromptForProjectOnStartup = false;

        sut.OkCommand.Execute(null);

        _settings.Received().TabWidth = 2;
        _settings.Received().AutoListMembers = false;
        _settings.Received().ShowGrid = false;
        _settings.Received().PromptForProjectOnStartup = false;
    }

    [Fact]
    public void OkCommand_CallsSaveOnce()
    {
        var sut = CreateSut();

        sut.OkCommand.Execute(null);

        _settings.Received(1).Save();
    }

    [Fact]
    public void OkCommand_RaisesCloseRequested_WithTrue()
    {
        var sut = CreateSut();
        bool? result = null;
        sut.CloseRequested += r => result = r;

        sut.OkCommand.Execute(null);

        result.Should().BeTrue();
    }

    // ── Cancel discards ─────────────────────────────────────────

    [Fact]
    public void CancelCommand_DoesNotCallSave()
    {
        var sut = CreateSut();
        Page<EditorFormattingPageViewModel>(sut).TabWidth = 16;

        sut.CancelCommand.Execute(null);

        _settings.DidNotReceive().Save();
    }

    [Fact]
    public void CancelCommand_DoesNotWriteBackToSettings()
    {
        var sut = CreateSut();
        Page<EditorFormattingPageViewModel>(sut).TabWidth = 16;

        sut.CancelCommand.Execute(null);

        _settings.DidNotReceive().TabWidth = 16;
    }

    [Fact]
    public void CancelCommand_RaisesCloseRequested_WithFalse()
    {
        var sut = CreateSut();
        bool? result = null;
        sut.CloseRequested += r => result = r;

        sut.CancelCommand.Execute(null);

        result.Should().BeFalse();
    }

    // ── Theme live preview ──────────────────────────────────────

    [Fact]
    public void ThemePage_AppliesThemeLiveOnChange()
    {
        var sut = CreateSut();

        Page<ThemePageViewModel>(sut).SelectedTheme = "Dark";

        _themeService.Received().Apply("Dark");
    }

    [Fact]
    public void CancelCommand_RevertsThemeToOriginal()
    {
        var sut = CreateSut();
        Page<ThemePageViewModel>(sut).SelectedTheme = "Dark";

        sut.CancelCommand.Execute(null);

        _themeService.Received().Apply("Classic");
    }

    [Fact]
    public void OkCommand_PersistsSelectedTheme()
    {
        var sut = CreateSut();
        Page<ThemePageViewModel>(sut).SelectedTheme = "Dark";

        sut.OkCommand.Execute(null);

        _settings.Received().ActiveTheme = "Dark";
    }

    // ── New first-party pages (Phase 2) ─────────────────────────

    [Fact]
    public void Environment_HasKeymapAndToolbarsPages()
    {
        var sut = CreateSut();

        Flatten(sut.RootNodes).Select(n => n.Page).OfType<KeymapPageViewModel>().Should().ContainSingle();
        Flatten(sut.RootNodes).Select(n => n.Page).OfType<ToolbarsPageViewModel>().Should().ContainSingle();
    }

    [Fact]
    public void KeymapPage_DoesNotApplyDuringEdit()
    {
        var sut = CreateSut();

        Page<KeymapPageViewModel>(sut).SelectedKeymap = "VB6";

        _keymapService.DidNotReceive().Apply(Arg.Any<string>());
    }

    [Fact]
    public void OkCommand_PersistsAndAppliesKeymap()
    {
        var sut = CreateSut();
        Page<KeymapPageViewModel>(sut).SelectedKeymap = "VB6";

        sut.OkCommand.Execute(null);

        _settings.Received().ActiveKeymap = "VB6";
        _keymapService.Received().Apply("VB6");
    }

    [Fact]
    public void OkCommand_PersistsToolbarVisibility()
    {
        var sut = CreateSut();
        Page<ToolbarsPageViewModel>(sut).StandardToolbarVisible = false;
        Page<ToolbarsPageViewModel>(sut).DebugToolbarVisible = true;

        sut.OkCommand.Execute(null);

        _settings.Received().IsStandardToolbarVisible = false;
        _settings.Received().IsDebugToolbarVisible = true;
    }

    [Fact]
    public void OkCommand_PersistsMinimapVisibility()
    {
        var sut = CreateSut();
        Page<EditorGeneralPageViewModel>(sut).IsMinimapVisible = false;

        sut.OkCommand.Execute(null);

        _settings.Received().IsMinimapVisible = false;
    }

    // ── Reset to defaults (Phase 3) ─────────────────────────────

    [Fact]
    public void CanRestorePageDefaults_TrueForSettingsPage_FalseForGroup()
    {
        var sut = CreateSut();
        sut.CanRestorePageDefaults.Should().BeTrue();   // default selection is a settings page

        sut.SelectedNode = sut.RootNodes[0];    // "Environment" group node (no page)
        sut.CanRestorePageDefaults.Should().BeFalse();
        sut.CurrentPage.Should().BeNull();
    }

    [Fact]
    public void RestorePageDefaultsCommand_ResetsOnlyCurrentPage()
    {
        var sut = CreateSut();
        var formatting = Page<EditorFormattingPageViewModel>(sut);
        var grid = Page<FormDesignerGridPageViewModel>(sut);
        formatting.TabWidth = 99;
        grid.GridWidth = 99;

        sut.SelectedNode = Flatten(sut.RootNodes).First(n => ReferenceEquals(n.Page, formatting));
        sut.RestorePageDefaultsCommand.Execute(null);

        formatting.TabWidth.Should().Be(SettingsDefaults.TabWidth);   // reset
        grid.GridWidth.Should().Be(99);                               // untouched
    }

    [Fact]
    public void ResetAllCommand_ResetsEveryPage()
    {
        var sut = CreateSut();
        Page<EditorFormattingPageViewModel>(sut).TabWidth = 99;
        Page<FormDesignerGridPageViewModel>(sut).GridWidth = 99;
        Page<EditorGeneralPageViewModel>(sut).AutoListMembers = false;

        sut.ResetAllCommand.Execute(null);

        Page<EditorFormattingPageViewModel>(sut).TabWidth.Should().Be(SettingsDefaults.TabWidth);
        Page<FormDesignerGridPageViewModel>(sut).GridWidth.Should().Be(SettingsDefaults.GridWidth);
        Page<EditorGeneralPageViewModel>(sut).AutoListMembers.Should().Be(SettingsDefaults.AutoListMembers);
    }

    [Fact]
    public void ResetAll_RestoresThemeToDefaultLive()
    {
        var sut = CreateSut();
        Page<ThemePageViewModel>(sut).SelectedTheme = "Dark";

        sut.ResetAllCommand.Execute(null);

        Page<ThemePageViewModel>(sut).SelectedTheme.Should().Be(SettingsDefaults.ActiveTheme);
        _themeService.Received().Apply(SettingsDefaults.ActiveTheme);
    }

    [Fact]
    public void ResetAll_IsPreviewOnly_CommitsOnOk()
    {
        var sut = CreateSut();
        Page<EditorFormattingPageViewModel>(sut).TabWidth = 99;

        sut.ResetAllCommand.Execute(null);
        _settings.DidNotReceive().Save();          // nothing persisted yet

        sut.OkCommand.Execute(null);

        _settings.Received().TabWidth = SettingsDefaults.TabWidth;
        _settings.Received(1).Save();
    }

    [Fact]
    public void ResetAll_ThenCancel_DoesNotPersist()
    {
        var sut = CreateSut();
        Page<EditorFormattingPageViewModel>(sut).TabWidth = 99;

        sut.ResetAllCommand.Execute(null);
        sut.CancelCommand.Execute(null);

        _settings.DidNotReceive().Save();
        _settings.DidNotReceive().TabWidth = Arg.Any<int>();
    }

    // ── Add-Ins section (Phase 4) ───────────────────────────────

    [Fact]
    public void AddInsGroup_HasNodePerDiscoveredAddin_IncludingDisabled()
    {
        var sut = CreateSut();
        var group = sut.RootNodes.First(n => n.Title == "Add-Ins");
        group.Children.Select(c => c.Title).Should().Equal("Add-in A", "Add-in B");
    }

    [Fact]
    public void NoAddins_OmitsAddInsGroup()
    {
        _addinRegistry.GetAll().Returns([]);
        var sut = CreateSut();
        sut.RootNodes.Select(n => n.Title).Should().NotContain("Add-Ins");
    }

    [Fact]
    public void SelectingAddinNode_ShowsAddinPage()
    {
        var sut = CreateSut();
        var node = Flatten(sut.RootNodes).First(n => n.Page is AddinOptionsPageViewModel);

        sut.SelectedNode = node;

        sut.CurrentPage.Should().BeOfType<AddinOptionsPageViewModel>();
        sut.CanRestorePageDefaults.Should().BeFalse();   // add-ins aren't resettable settings pages
    }

    [Fact]
    public void AddinPage_Deactivate_PersistsViaRegistry()
    {
        var sut = CreateSut();
        var a = Addin(sut, "Add-in A");   // Loaded => enabled
        a.IsAddinEnabled.Should().BeTrue();

        a.ToggleEnabledCommand.Execute(null);

        _addinRegistry.Received().SetEnabled(_addinA.AssemblyPath, false);
        a.RestartNoteVisible.Should().BeTrue();
    }

    [Fact]
    public void AddinPage_DisabledAddin_ShowsActivate_AndReactivates()
    {
        var sut = CreateSut();
        var b = Addin(sut, "Add-in B");   // Disabled
        b.IsAddinEnabled.Should().BeFalse();
        b.ToggleLabel.Should().Be("Activate");

        b.ToggleEnabledCommand.Execute(null);

        _addinRegistry.Received().SetEnabled(_addinB.AssemblyPath, true);
    }

    [Fact]
    public void ResetAll_DoesNotToggleAddins()
    {
        var sut = CreateSut();
        sut.ResetAllCommand.Execute(null);
        _addinRegistry.DidNotReceive().SetEnabled(Arg.Any<string>(), Arg.Any<bool>());
    }

    // ── Add-in contributed options pages (Phase 5) ──────────────

    [Fact]
    public void AddinPage_ShowsContributedContent_WhenRegisteredForThatAddin()
    {
        var path = typeof(OptionsViewModelTests).Assembly.Location;   // capture key
        _addinOptions.RegisterOptionsPage(() => new object());        // captures this test assembly's path
        var info = new AddinInfo(path, "Contrib Add-in", "", "1.0", "");
        _addinRegistry.GetAll().Returns([info]);
        _addinRegistry.GetStatus(path).Returns(AddinStatus.Loaded);

        var page = Addin(CreateSut(), "Contrib Add-in");

        page.HasContributedContent.Should().BeTrue();
    }

    [Fact]
    public void AddinPage_NoContributedContent_WhenNoneRegistered()
    {
        // default add-ins A and B have no contributed pages registered
        Addin(CreateSut(), "Add-in A").HasContributedContent.Should().BeFalse();
    }
}
