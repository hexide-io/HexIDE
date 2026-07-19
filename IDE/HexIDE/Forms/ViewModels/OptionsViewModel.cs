using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HexIDE.Addins;
using HexIDE.Events;
using HexIDE.Forms.ViewModels.Options;
using HexIDE.IDE;
using HexIDE.Keymaps;
using HexIDE.Localization;
using HexIDE.Themes;
using Serilog;

namespace HexIDE.Forms.ViewModels;

/// <summary>
/// Shell for the 2-pane Tools&#8594;Options dialog: a hierarchical category tree on the
/// left (<see cref="RootNodes"/>) and the selected page on the right (<see cref="CurrentPage"/>,
/// resolved to a View by the <see cref="ViewLocator"/>). Each page owns its own settings copy;
/// the shell commits them all on OK and reverts the live theme preview on Cancel.
/// </summary>
public partial class OptionsViewModel : ObservableObject, IDialog
{
    private readonly ISettingsService _settings;
    private readonly IThemeService _themeService;
    private readonly IKeymapService _keymapService;
    private readonly ILanguageSwitchService _languageSwitch;
    private readonly IAddinRegistry _addinRegistry;
    private readonly AddinOptionsService _addinOptions;
    private readonly IDeveloperModeService _devMode;
    private readonly ILocalizationService _localization;
    private readonly IEventBus _eventBus;
    private readonly string _originalTheme;
    private readonly string _originalLanguage;
    private readonly List<IOptionsPage> _pages = [];
    private readonly List<OptionsNodeViewModel> _keyedNodes = [];

    public string Title => _localization.GetString("Str.Options.Title");
    public bool CanResize => true;
    public event Action<bool>? CloseRequested;

    /// <summary>Top-level category nodes shown in the left-hand tree.</summary>
    public ObservableCollection<OptionsNodeViewModel> RootNodes { get; } = [];

    [ObservableProperty]
    private OptionsNodeViewModel? _selectedNode;

    /// <summary>The page ViewModel for the right-hand pane; null when a group node is selected.</summary>
    public object? CurrentPage => SelectedNode?.Page;

    /// <summary>True when a resettable first-party settings page is selected — gates "Restore Defaults".</summary>
    public bool CanRestorePageDefaults => SelectedNode?.Page is IOptionsPage;

    partial void OnSelectedNodeChanged(OptionsNodeViewModel? value)
    {
        OnPropertyChanged(nameof(CurrentPage));
        OnPropertyChanged(nameof(CanRestorePageDefaults));
    }

    public OptionsViewModel(ISettingsService settings, IThemeService themeService, IKeymapService keymapService,
                            ILanguageSwitchService languageSwitch, IAddinRegistry addinRegistry,
                            AddinOptionsService addinOptions, IDeveloperModeService devMode,
                            ILocalizationService localization, IEventBus eventBus)
    {
        _settings = settings;
        _themeService = themeService;
        _keymapService = keymapService;
        _languageSwitch = languageSwitch;
        _addinRegistry = addinRegistry;
        _addinOptions = addinOptions;
        _devMode = devMode;
        _localization = localization;
        _eventBus = eventBus;
        _originalTheme = themeService.ActiveTheme;
        _originalLanguage = languageSwitch.ActiveLanguage;

        BuildTree();
        SelectedNode = FirstLeaf(RootNodes);

        // The Language page lives in this dialog and previews live, so re-localize the tree (and the
        // dialog title) when the language changes mid-dialog. Unsubscribed on close (Ok/Cancel).
        _localization.LanguageChanged += OnLanguageChanged;
    }

    private void OnLanguageChanged()
    {
        foreach (var node in _keyedNodes)
            node.Title = _localization.GetString(node.Key!);
        OnPropertyChanged(nameof(Title));
    }

    private void BuildTree()
    {
        var environment = Group("Str.Options.Node.Environment")
            .Add(Leaf(new EnvironmentGeneralPageViewModel(_settings), "Str.Options.Node.General"))
            .Add(Leaf(new ThemePageViewModel(_settings, _themeService), "Str.Options.Node.Theme"))
            .Add(Leaf(new LanguagePageViewModel(_settings, _languageSwitch, _localization, CustomizeLanguage), "Str.Options.Node.Language"))
            .Add(Leaf(new KeymapPageViewModel(_settings, _keymapService), "Str.Options.Node.Keymap"))
            .Add(Leaf(new ToolbarsPageViewModel(_settings), "Str.Options.Node.Toolbars"));

        var editor = Group("Str.Options.Node.Editor")
            .Add(Leaf(new EditorGeneralPageViewModel(_settings), "Str.Options.Node.General"))
            .Add(Leaf(new EditorFormattingPageViewModel(_settings), "Str.Options.Node.Formatting"));

        var designer = Group("Str.Options.Node.FormDesigner")
            .Add(Leaf(new FormDesignerGridPageViewModel(_settings), "Str.Options.Node.Grid"));

        var advanced = Group("Str.Options.Node.Advanced")
            .Add(Leaf(new AdvancedLspPageViewModel(_settings), "Str.Options.Node.Lsp"));

        RootNodes.Add(environment);
        RootNodes.Add(editor);
        RootNodes.Add(designer);
        RootNodes.Add(advanced);

        BuildAddinsGroup();

        // Developer node — last in the tree, and only when the session is in developer mode.
        if (_devMode.IsEnabled)
            RootNodes.Add(Leaf(new DeveloperPageViewModel(_settings), "Str.Options.Node.Developer"));
    }

    /// <summary>Creates a localized grouping node and records it for live re-localization.</summary>
    private OptionsNodeViewModel Group(string key)
    {
        var node = new OptionsNodeViewModel(_localization.GetString(key), page: null, key: key);
        _keyedNodes.Add(node);
        return node;
    }

    /// <summary>
    /// One node per discovered add-in (including disabled ones, so they can be re-activated).
    /// Add-in pages are not first-party <see cref="IOptionsPage"/>s — they manage their own
    /// enable/disable state via the registry and are not part of the OK/Cancel commit.
    /// </summary>
    private void BuildAddinsGroup()
    {
        var addins = _addinRegistry.GetAll();
        if (addins.Count == 0) return;

        var group = Group("Str.Options.Node.AddIns");
        foreach (var info in addins)
        {
            var page = new AddinOptionsPageViewModel(_addinRegistry, info, _addinOptions.GetFactoryFor(info.AssemblyPath));
            // Add-in node label is the add-in's own name — not localized (no key).
            group.Add(new OptionsNodeViewModel(page.Title, page));
        }
        RootNodes.Add(group);
    }

    /// <summary>Wraps a page in a localized leaf node, records it for the OK commit pass, and tracks it
    /// for live re-localization.</summary>
    private OptionsNodeViewModel Leaf(IOptionsPage page, string key)
    {
        _pages.Add(page);
        var node = new OptionsNodeViewModel(_localization.GetString(key), page, key);
        _keyedNodes.Add(node);
        return node;
    }

    private static OptionsNodeViewModel? FirstLeaf(IEnumerable<OptionsNodeViewModel> nodes)
    {
        foreach (var node in nodes)
        {
            if (node.Page is not null) return node;
            if (FirstLeaf(node.Children) is { } child) return child;
        }
        return null;
    }

    /// <summary>
    /// Commit-and-close the modal Options dialog (same path as OK), then publish the open-editor event so
    /// the Translation Editor tab lands on the now-unblocked dock. Publishing AFTER <see cref="Ok"/> ensures
    /// the dialog is gone before the dock receives the tab.
    /// </summary>
    private void CustomizeLanguage()
    {
        Ok();
        _eventBus.Publish(new OpenTranslationEditorEvent());
    }

    [RelayCommand]
    private void Ok()
    {
        foreach (var page in _pages) page.SaveToSettings();
        _settings.Save();
        Log.Debug("OptionsViewModel: OK — settings saved");
        _localization.LanguageChanged -= OnLanguageChanged;
        CloseRequested?.Invoke(true);
    }

    [RelayCommand]
    private void Cancel()
    {
        // Both Theme and Language preview live, so revert both to their pre-dialog values.
        // The language revert is silent (no gate) — Cancel is itself the confirmation.
        _localization.LanguageChanged -= OnLanguageChanged;
        _themeService.Apply(_originalTheme);
        _languageSwitch.Apply(_originalLanguage);
        Log.Debug("OptionsViewModel: Cancel — changes discarded");
        CloseRequested?.Invoke(false);
    }

    /// <summary>Reset only the currently-selected page to its defaults (preview-only, committed on OK).</summary>
    [RelayCommand]
    private void RestorePageDefaults() => (SelectedNode?.Page as IOptionsPage)?.RestoreDefaults();

    /// <summary>Reset every page to its defaults (preview-only, committed on OK).</summary>
    [RelayCommand]
    private void ResetAll()
    {
        foreach (var page in _pages) page.RestoreDefaults();
    }
}
