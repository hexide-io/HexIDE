using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HexIDE.IDE;
using HexIDE.Localization;

namespace HexIDE.Forms.ViewModels.Options;

/// <summary>
/// Environment &#8594; Language: two cascading combos — a <b>Language</b> combo that drives a dependent
/// <b>Region</b> combo. Picking a <i>language</i> applies it live and shows the countdown-revert gate (via
/// <see cref="ILanguageSwitchService"/>); picking a <i>region</i> applies live <b>without</b> a gate (a
/// region only tweaks wording within an already-readable language, so it cannot lock the user out). The two
/// combos compose into the single persisted <c>ActiveLanguage</c> id (<c>(Standard)</c>→neutral, a region→
/// its id). "System default" and the dev-only pseudo entries have no regions, so the Region combo disables.
/// OK persists the kept selection; Cancel reverts (shell). "Restore Defaults" applies the default silently.
/// </summary>
public partial class LanguagePageViewModel : ObservableObject, IOptionsPage
{
    private readonly ISettingsService _settings;
    private readonly ILanguageSwitchService _languageSwitch;
    private readonly ILocalizationService _localization;
    private readonly Action _onCustomize;
    private bool _loading;

    public LanguagePageViewModel(ISettingsService settings, ILanguageSwitchService languageSwitch,
                                 ILocalizationService localization, Action onCustomize)
    {
        _settings = settings;
        _languageSwitch = languageSwitch;
        _localization = localization;
        _onCustomize = onCustomize;
        LoadFromSettings();
    }

    /// <summary>Hands off to the editor: the shell commits-and-closes the dialog, then opens the tab.</summary>
    [RelayCommand]
    private void Customize() => _onCustomize();

    public string Title => "Language";

    public IReadOnlyList<LanguageInfo> AvailableLanguages => _languageSwitch.AvailableLanguages;

    [ObservableProperty] private LanguageInfo? _selectedLanguage;
    [ObservableProperty] private IReadOnlyList<RegionInfo> _availableRegions = [];
    [ObservableProperty] private RegionInfo? _selectedRegion;
    [ObservableProperty] private bool _isRegionEnabled;

    partial void OnSelectedLanguageChanged(LanguageInfo? value)
    {
        if (value is null) return;

        // Repopulate the Region combo for the new language (resets to "(Standard)"). Done under the guard
        // so the programmatic SelectedRegion change doesn't trigger a region-apply.
        var wasLoading = _loading;
        _loading = true;
        RebuildRegions(value, value.Id);
        _loading = wasLoading;

        // Programmatic sets (load / restore / revert-snapback) leave the apply to the caller.
        if (_loading) return;

        // A genuine language change: apply the language's (Standard) id live + gate it.
        _ = ApplyLanguageWithGateAsync(EffectiveId());
    }

    partial void OnSelectedRegionChanged(RegionInfo? value)
    {
        // A genuine region change applies live but never gates — it cannot strand a reader.
        if (_loading || value is null) return;
        _languageSwitch.Apply(EffectiveId());
    }

    private async Task ApplyLanguageWithGateAsync(string newId)
    {
        var kept = await _languageSwitch.SwitchWithGateAsync(newId);
        if (kept) return;

        // Reverted (or timed out): snap both combos back to the still-active language without re-applying.
        _loading = true;
        var (lang, regionId) = ResolveSelection(_languageSwitch.ActiveLanguage);
        SelectedLanguage = lang;
        RebuildRegions(lang, regionId);
        _loading = false;
    }

    public void LoadFromSettings()
    {
        _loading = true;
        var (lang, regionId) = ResolveSelection(_settings.ActiveLanguage);
        SelectedLanguage = lang;
        RebuildRegions(lang, regionId);
        _loading = false;
    }

    public void SaveToSettings()
    {
        if (SelectedLanguage is not null)
            _settings.ActiveLanguage = EffectiveId();
    }

    public void RestoreDefaults()
    {
        // Preview the default live but skip the gate (the default is the safe/readable fallback, and a bulk
        // "Reset All" must not interrupt with a confirmation dialog).
        _loading = true;
        var (lang, regionId) = ResolveSelection(SettingsDefaults.ActiveLanguage);
        SelectedLanguage = lang;
        RebuildRegions(lang, regionId);
        _loading = false;
        _languageSwitch.Apply(SettingsDefaults.ActiveLanguage);
    }

    /// <summary>The id composed from the two combos: a chosen region, else the language's own id.</summary>
    private string EffectiveId() => SelectedRegion?.Id ?? SelectedLanguage?.Id ?? "system";

    /// <summary>
    /// Populates the Region combo for <paramref name="language"/> with a synthetic "(Standard)" entry (the
    /// bare neutral) plus its regions, and selects <paramref name="regionIdToSelect"/>. System/pseudo have
    /// no regions, so the combo is emptied and disabled.
    /// </summary>
    private void RebuildRegions(LanguageInfo? language, string regionIdToSelect)
    {
        if (language is null || IsMetaLanguage(language.Id))
        {
            AvailableRegions = [];
            SelectedRegion = null;
            IsRegionEnabled = false;
            return;
        }

        var standard = new RegionInfo(language.Id, _localization.GetString("Str.Options.Language.RegionStandard"));
        var regions = _languageSwitch.RegionsFor(language.Id)
            .OrderBy(r => r.DisplayName, StringComparer.OrdinalIgnoreCase);
        AvailableRegions = [standard, .. regions];   // "(Standard)" pinned first, then regions A–Z
        IsRegionEnabled = true;
        SelectedRegion = AvailableRegions.FirstOrDefault(r => r.Id == regionIdToSelect) ?? standard;
    }

    /// <summary>Maps a persisted/active id to its (Language, region-id) pair. A language id selects
    /// "(Standard)"; a region id selects that region; an unknown id falls back to the first entry.</summary>
    private (LanguageInfo lang, string regionId) ResolveSelection(string id)
    {
        var direct = AvailableLanguages.FirstOrDefault(l => l.Id == id);
        if (direct is not null) return (direct, id);   // (Standard): its id == the language id

        foreach (var lang in AvailableLanguages)
            foreach (var region in _languageSwitch.RegionsFor(lang.Id))
                if (region.Id == id) return (lang, id);

        return (AvailableLanguages[0], AvailableLanguages[0].Id);
    }

    private static bool IsMetaLanguage(string id) => id is "system" or "pseudo" or "pseudo-rtl";
}
