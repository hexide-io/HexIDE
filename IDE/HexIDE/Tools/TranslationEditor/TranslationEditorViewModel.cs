using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text.Json;
using Avalonia.Collections;
using Avalonia.Platform;
using Dock.Model.Mvvm.Controls;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Utils;
using PropertyChanged.SourceGenerator;
using R3;
using Serilog;

namespace HexIDE.Tools.TranslationEditor;

/// <summary>
/// The local translation editor — a Document tab (the Object Browser pattern) that lets a user override any
/// shipped <c>Str.*</c> string for any culture, or author a brand-new local translation for a culture HexIDE
/// does not ship. Rows are <c>[Key, From, To]</c>: From is a selectable read-only reference (default
/// <c>en</c>); editing a To cell writes a per-locale user override (bold), and emptying a To cell reverts it
/// to the inherited value. The user tier merges last in <see cref="ILocalizationService"/> resolution.
/// </summary>
public partial class TranslationEditorViewModel : Document
{
    private readonly ILocalizationService _localization;
    private readonly IUserTranslationsService _userTranslations;
    private readonly IWindowManager _windowManager;

    // Re-entrancy guard: when the VM writes ToValue programmatically (revert / refresh), the row's
    // PropertyChanged must NOT route back into the commit path.
    private bool _suppressCommit;

    // The canonical (en) keys, in file order — one row per key.
    private readonly IReadOnlyList<string> _canonicalKeys;

    public ObservableCollection<TranslationRowViewModel> Rows { get; } = new();

    /// <summary>The grouped + filtered view bound by the grid (group headers per Area, Search filter).</summary>
    public DataGridCollectionView RowsView { get; }

    // ── From cascade ─────────────────────────────────────────────
    public IReadOnlyList<LanguageInfo> FromLanguages { get; } = CultureCatalog.Languages;
    [Notify] private LanguageInfo fromLanguage;
    [Notify] private IReadOnlyList<RegionInfo> fromRegions = [];
    [Notify] private RegionInfo? fromRegion;
    [Notify] private bool fromIsRegionEnabled;

    // ── To cascade ───────────────────────────────────────────────
    public IReadOnlyList<LanguageInfo> ToLanguages { get; } = CultureCatalog.Languages;
    [Notify] private LanguageInfo toLanguage;
    [Notify] private IReadOnlyList<RegionInfo> toRegions = [];
    [Notify] private RegionInfo? toRegion;
    [Notify] private bool toIsRegionEnabled;

    // ── Search + status ──────────────────────────────────────────
    [Notify] private string searchText = string.Empty;
    [Notify] private string statusMessage = string.Empty;

    /// <summary>The effective From locale id (region id when a region is picked, else the neutral).</summary>
    public string FromId => FromRegion?.Id ?? FromLanguage.Id;

    /// <summary>The effective To locale id (region id when a region is picked, else the neutral).</summary>
    public string ToId => ToRegion?.Id ?? ToLanguage.Id;

    public DelegateCommand ResetAllCommand { get; }
    public DelegateCommand ClearSearchCommand { get; }
    public DelegateCommand CloseCommand { get; }

    public TranslationEditorViewModel(
        ILocalizationService localization,
        IUserTranslationsService userTranslations,
        IWindowManager windowManager)
    {
        localization.BindTitle(this, "Str.TranslationEditor.Title");
        CanClose = true;
        CanFloat = false;

        _localization = localization;
        _userTranslations = userTranslations;
        _windowManager = windowManager;

        _canonicalKeys = LoadCanonicalKeys();

        // From defaults to English (the canonical reference).
        fromLanguage = FromLanguages.FirstOrDefault(l => l.Id == "en") ?? FromLanguages[0];

        // To defaults to the neutral of the active language if that neutral is a CultureInfo culture, else en.
        var activeNeutral = CultureCatalog.NeutralOf(localization.ActiveLanguage);
        toLanguage = ToLanguages.FirstOrDefault(l => l.Id == activeNeutral)
                     ?? ToLanguages.FirstOrDefault(l => l.Id == "en")
                     ?? ToLanguages[0];

        ResetAllCommand = new DelegateCommand(() => ResetAllAsync().ListenErrors());
        ClearSearchCommand = new DelegateCommand(() => SearchText = string.Empty);
        CloseCommand = new DelegateCommand(Close);

        // Build the rows once; their values are (re)populated by the cascade refreshers below.
        foreach (var key in _canonicalKeys)
        {
            var row = new TranslationRowViewModel(key);
            row.AreaLabel = LocalizeArea(row.Area);
            row.ResetCommand = new DelegateCommand(() => ResetKey(row));
            row.PropertyChanged += OnRowPropertyChanged;
            Rows.Add(row);
        }

        RowsView = new DataGridCollectionView(Rows);
        RowsView.GroupDescriptions.Add(new DataGridPathGroupDescription(nameof(TranslationRowViewModel.AreaLabel)));
        RowsView.Filter = FilterRow;

        // Group-header labels are IDE-language strings (not the From/To locales), so refresh them when the
        // chrome language changes — the editor VM is a singleton, so it outlives any single open.
        localization.LanguageChanged += OnIdeLanguageChanged;

        // React to cascade + search changes. The region rebuilds run under the suppress guard so the
        // programmatic SelectedRegion reset doesn't double-refresh.
        this.ObservePropertyChanged(x => x.FromLanguage).Subscribe(_ => OnFromLanguageChanged());
        this.ObservePropertyChanged(x => x.FromRegion).Subscribe(_ => RefreshFromValues());
        this.ObservePropertyChanged(x => x.ToLanguage).Subscribe(_ => OnToLanguageChanged());
        this.ObservePropertyChanged(x => x.ToRegion).Subscribe(_ => RefreshToValues());
        this.ObservePropertyChanged(x => x.SearchText).Subscribe(_ => RowsView.Refresh());

        // Initial population.
        RebuildFromRegions();
        RebuildToRegions();
        RefreshFromValues();
        RefreshToValues();
    }

    // ── Cascade plumbing ─────────────────────────────────────────

    private void OnFromLanguageChanged()
    {
        RebuildFromRegions();   // resets FromRegion → "(Standard)"; RefreshFromValues runs via FromRegion change
        RefreshFromValues();
    }

    private void OnToLanguageChanged()
    {
        RebuildToRegions();
        RefreshToValues();
    }

    private void RebuildFromRegions()
    {
        FromRegions = BuildRegionList(FromLanguage.Id);
        FromIsRegionEnabled = FromRegions.Count > 1;
        // Select the synthetic "(Standard)" entry (always index 0) so the combo shows it rather than a
        // blank box; FromId stays == neutral because the standard entry's Id is the bare neutral id.
        FromRegion = FromRegions[0];
    }

    private void RebuildToRegions()
    {
        ToRegions = BuildRegionList(ToLanguage.Id);
        ToIsRegionEnabled = ToRegions.Count > 1;
        ToRegion = ToRegions[0];
    }

    /// <summary>The Region combo for a language: a synthetic "(Standard)" (= bare neutral id) pinned first,
    /// then the CultureInfo regions A–Z. Mirrors <c>LanguagePageViewModel.RebuildRegions</c>.</summary>
    private IReadOnlyList<RegionInfo> BuildRegionList(string languageId)
    {
        var standard = new RegionInfo(languageId, _localization.GetString("Str.Options.Language.RegionStandard"));
        var regions = CultureCatalog.RegionsFor(languageId);
        return [standard, .. regions];
    }

    // ── Row population ───────────────────────────────────────────

    private void RefreshFromValues()
    {
        var fromId = FromId;
        var ownChain = _localization.ShippedOwnChainKeys(fromId) ?? (IReadOnlySet<string>)new HashSet<string>();
        var fromIsEn = fromId == "en";
        // The From column shows the EFFECTIVE reference (GetStringFrom merges the user tier), so a key the user
        // has authored for the From culture is no longer "secretly English". Keep the red rule consistent by
        // also excluding the user's own overrides for the From id and its neutral.
        var fromNeutral = CultureCatalog.NeutralOf(fromId);
        var userFrom = Overrides(fromId);
        var userFromNeutral = fromNeutral != fromId ? Overrides(fromNeutral) : EmptyOverrides;
        _suppressCommit = true;
        try
        {
            foreach (var row in Rows)
            {
                row.FromValue = _localization.GetStringFrom(fromId, row.Key);
                row.IsRedFallback = !fromIsEn
                    && !ownChain.Contains(row.Key)
                    && !userFrom.ContainsKey(row.Key)
                    && !userFromNeutral.ContainsKey(row.Key);
                row.ChainTooltip = BuildChainTooltip(row.Key);
            }
        }
        finally { _suppressCommit = false; }
        OnPropertyChanged(nameof(FromId));
    }

    /// <summary>Null-safe accessor for a locale's overrides (a substituted service may return null in tests).</summary>
    private IReadOnlyDictionary<string, string> Overrides(string id) =>
        _userTranslations.GetOverrides(id) ?? EmptyOverrides;

    private static readonly IReadOnlyDictionary<string, string> EmptyOverrides =
        new Dictionary<string, string>();

    private void RefreshToValues()
    {
        var toId = ToId;
        var overrides = Overrides(toId);
        _suppressCommit = true;
        try
        {
            foreach (var row in Rows)
            {
                row.ToValue = _localization.GetStringFrom(toId, row.Key);
                row.IsOverridden = overrides.ContainsKey(row.Key);
                row.HasValidationError = false;
                row.ChainTooltip = BuildChainTooltip(row.Key);
            }
        }
        finally { _suppressCommit = false; }
        OnPropertyChanged(nameof(ToId));
    }

    private void RefreshRow(TranslationRowViewModel row)
    {
        var toId = ToId;
        var overrides = Overrides(toId);
        _suppressCommit = true;
        try
        {
            row.ToValue = _localization.GetStringFrom(toId, row.Key);
            row.IsOverridden = overrides.ContainsKey(row.Key);
            row.HasValidationError = false;
            row.ChainTooltip = BuildChainTooltip(row.Key);
        }
        finally { _suppressCommit = false; }
    }

    /// <summary>The localized group-header label for a raw area segment (e.g. "Menu" → "Menus"); falls back
    /// to the raw segment if the key is somehow absent.</summary>
    private string LocalizeArea(string area)
    {
        var key = $"Str.TranslationEditor.Area.{area}";
        var label = _localization.GetString(key);
        return label == key ? area : label;
    }

    /// <summary>Re-localize the group headers when the IDE chrome language changes, then re-group.</summary>
    private void OnIdeLanguageChanged()
    {
        foreach (var row in Rows)
            row.AreaLabel = LocalizeArea(row.Area);
        RowsView.Refresh();
    }

    /// <summary>A pragmatic multi-line view of the inheritance chain for a key:
    /// canonical en, the To neutral, the To region, then any user override.</summary>
    private string BuildChainTooltip(string key)
    {
        var lines = new List<string>();
        var toId = ToId;
        var neutralId = CultureCatalog.NeutralOf(toId);

        lines.Add($"en: {_localization.GetStringFrom("en", key)}");

        var neutralLang = ToLanguages.FirstOrDefault(l => l.Id == neutralId);
        var neutralName = neutralLang?.DisplayName ?? neutralId;
        lines.Add($"{neutralName}: {_localization.GetStringFrom(neutralId, key)}");

        if (toId != neutralId)
        {
            var regionName = ToRegion?.DisplayName ?? toId;
            lines.Add($"{regionName}: {_localization.GetStringFrom(toId, key)}");
        }

        var overrides = Overrides(toId);
        var overrideValue = overrides.TryGetValue(key, out var ov)
            ? ov
            : _localization.GetString("Str.TranslationEditor.Tooltip.None");
        lines.Add(string.Format(
            _localization.GetString("Str.TranslationEditor.Tooltip.Override"), overrideValue));

        return string.Join(Environment.NewLine, lines);
    }

    // ── Commit (driven by a row's ToValue edit) ──────────────────

    private void OnRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_suppressCommit) return;
        if (e.PropertyName != nameof(TranslationRowViewModel.ToValue)) return;
        if (sender is TranslationRowViewModel row)
            CommitTo(row);
    }

    private void CommitTo(TranslationRowViewModel row)
    {
        var toId = ToId;
        var newValue = row.ToValue;

        if (string.IsNullOrWhiteSpace(newValue))
        {
            // Empty/whitespace ⇒ revert to the inherited (as-shipped) value, clear bold + error.
            _userTranslations.ClearOverride(toId, row.Key);
            _suppressCommit = true;
            try
            {
                row.ToValue = _localization.GetStringFrom(toId, row.Key);
                row.IsOverridden = false;
                row.HasValidationError = false;
                row.ChainTooltip = BuildChainTooltip(row.Key);
            }
            finally { _suppressCommit = false; }
            StatusMessage = string.Empty;
            return;
        }

        if (!_userTranslations.SetOverride(toId, row.Key, newValue))
        {
            // Placeholder-breaking edit — reject without persisting. Keep the typed value visible so the
            // user can fix it; surface the localized error.
            row.HasValidationError = true;
            StatusMessage = string.Format(
                _localization.GetString("Str.TranslationEditor.PlaceholderError"), "{0}", "{1}");
            return;
        }

        row.HasValidationError = false;
        row.IsOverridden = true;
        _suppressCommit = true;
        try { row.ChainTooltip = BuildChainTooltip(row.Key); }
        finally { _suppressCommit = false; }
        StatusMessage = string.Empty;
    }

    // ── Commands ─────────────────────────────────────────────────

    private void ResetKey(TranslationRowViewModel row)
    {
        _userTranslations.ClearOverride(ToId, row.Key);
        RefreshRow(row);
        StatusMessage = string.Empty;
    }

    private async System.Threading.Tasks.Task ResetAllAsync()
    {
        var result = await _windowManager.MessageBox(
            _localization.GetString("Str.TranslationEditor.ResetAllConfirm"),
            _localization.GetString("Str.TranslationEditor.Title"),
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);

        if (result != MessageBoxResult.Yes) return;

        _userTranslations.ResetAll(ToId);
        RefreshToValues();
        StatusMessage = string.Empty;
    }

    private bool FilterRow(object? item)
    {
        if (item is not TranslationRowViewModel row) return false;
        var term = SearchText?.Trim();
        if (string.IsNullOrEmpty(term)) return true;
        return row.Key.Contains(term, StringComparison.OrdinalIgnoreCase)
               || row.FromValue.Contains(term, StringComparison.OrdinalIgnoreCase)
               || row.ToValue.Contains(term, StringComparison.OrdinalIgnoreCase);
    }

    private void Close()
    {
        if (Factory is { } factory && Owner != null)
            factory.CloseDockable(this);
    }

    /// <summary>Loads the canonical en.json key list (file order) via the same asset path the localization
    /// engine uses. Failure yields an empty grid rather than throwing.</summary>
    private static IReadOnlyList<string> LoadCanonicalKeys()
    {
        try
        {
            var uri = new Uri("avares://HexIDE/Localization/Packs/en.json");
            using var stream = AssetLoader.Open(uri);
            var record = JsonSerializer.Deserialize(stream, LocalizationJsonContext.Default.LanguagePackRecord);
            return record?.Strings?.Keys.ToList() ?? [];
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "TranslationEditor: failed to load canonical key list (en.json)");
            return [];
        }
    }
}
