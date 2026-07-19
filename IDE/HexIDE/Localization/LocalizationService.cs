using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Platform;
using Serilog;

namespace HexIDE.Localization;

/// <summary>
/// Loads JSON language packs and hot-swaps a merged <see cref="ResourceDictionary"/> of
/// <c>Str.*</c> keys into <see cref="Application"/>.Resources, mirroring <c>ThemeService</c>.
/// Decoupled from thread culture: it never touches <see cref="CultureInfo"/> except a single
/// read-only read in <c>ResolveOsLanguage</c>.
/// </summary>
public sealed class LocalizationService : ILocalizationService
{
    // The English canonical pack — the universal fallback. Loaded once, never mutated.
    private readonly Dictionary<string, string> _englishStrings;

    // The per-user override tier, merged LAST in resolution. Optional so existing `new LocalizationService()`
    // tests still construct without it; when present, an override change live-re-applies the active language.
    private readonly IUserTranslationsService? _userTranslations;

    // The active pack's effective map (English merged with the active pack's overrides).
    private Dictionary<string, string> _currentStrings;

    // The merged dictionary currently injected into Application.Resources (the "language slot").
    private ResourceDictionary? _currentLanguageDict;

    public string ActiveLanguage { get; private set; } = "system";

    // "System default" (follows the OS each launch) + the file-backed packs. The two synthetic
    // pseudo-localization locales are DEVELOPER-ONLY (appended in the ctor) so end users never see them.
    public IReadOnlyList<LanguageInfo> AvailableLanguages { get; }

    public FlowDirection FlowDirection { get; private set; } = FlowDirection.LeftToRight;

    public event Action? LanguageChanged;

    public LocalizationService(IUserTranslationsService? userTranslations = null, IDeveloperModeService? developerMode = null)
    {
        _englishStrings = LoadEnglish();
        _currentStrings = new Dictionary<string, string>(_englishStrings);

        _userTranslations = userTranslations;
        if (_userTranslations != null)
            _userTranslations.OverridesChanged += OnUserOverridesChanged;

        // Pseudo (LTR/RTL) are coverage / RTL-layout dev oracles, not real languages — list them in the
        // Language dropdown ONLY in developer mode (DEBUG + --developer-mode; always false in Release), so
        // users never see them. Apply("pseudo"/"pseudo-rtl") still works for the gate/MCP automation in dev.
        List<LanguageInfo> packs = [.. LanguageManifest.Packs];
        if (developerMode?.IsEnabled == true)
        {
            packs.Add(new LanguageInfo("pseudo", "Pseudo (LTR)", "ltr"));
            packs.Add(new LanguageInfo("pseudo-rtl", "Pseudo (RTL)", "rtl"));
        }

        // Alpha-sort everything by display name so the dropdown is easy to scan; the "System default"
        // sentinel always pins to the top regardless of sort.
        packs.Sort((a, b) => string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase));
        AvailableLanguages =
        [
            new LanguageInfo("system", "System default", "ltr"),
            .. packs,
        ];
    }

    public void Apply(string id)
    {
        if (!TryResolveMap(id, out var map, out var direction))
        {
            // Pack failed to load — fall back to the canonical pack (en).
            id = "en";
            direction = "ltr";
        }

        ActiveLanguage = id;
        _currentStrings = map;
        FlowDirection = direction == "rtl"
            ? Avalonia.Media.FlowDirection.RightToLeft
            : Avalonia.Media.FlowDirection.LeftToRight;

        // Swap the language slot in Application.Resources so every {DynamicResource Str.*} re-resolves.
        // Guarded so the service stays usable headless (tests read GetString off the in-memory map).
        var app = Application.Current;
        if (app != null)
        {
            if (_currentLanguageDict != null)
                app.Resources.MergedDictionaries.Remove(_currentLanguageDict);

            _currentLanguageDict = BuildResourceDictionary(map);
            app.Resources.MergedDictionaries.Add(_currentLanguageDict);
        }

        ApplyFlowDirection();
        LanguageChanged?.Invoke();
        Log.Debug("LocalizationService: Applied language '{Id}'", id);
    }

    public string GetString(string key)
    {
        if (_currentStrings.TryGetValue(key, out var value)) return value;
        if (_englishStrings.TryGetValue(key, out var english)) return english;
        return key;
    }

    public string this[string key] => GetString(key);

    public string GetStringFrom(string packId, string key)
    {
        // TryResolveMap returns the English map on failure, so map is always usable.
        TryResolveMap(packId, out var map, out _);
        if (map.TryGetValue(key, out var value)) return value;
        if (_englishStrings.TryGetValue(key, out var english)) return english;
        return key;
    }

    public string? GetPropertyDescription(string propertyName)
    {
        var key = "Str.PropDesc." + Sanitize(propertyName);
        if (_currentStrings.TryGetValue(key, out var value)) return value;
        if (_englishStrings.TryGetValue(key, out var english)) return english;
        return null; // caller falls back to the English description embedded in HexIDE.Core
    }

    public IReadOnlyList<RegionInfo> RegionsFor(string languageId) =>
        LanguageManifest.Regions.TryGetValue(languageId, out var regions) ? regions : [];

    // ── Internals ────────────────────────────────────────────────

    /// <summary>
    /// Maps the OS UI culture to a pack id. Tries (1) an exact neutral-language match (<c>fr</c> → <c>fr</c>),
    /// then (2) an exact region match against <see cref="LanguageManifest.Regions"/> (<c>en-GB</c> → <c>en-GB</c>,
    /// <c>zh-CN</c> → <c>zh-CN</c>), then (3) a neutral-language match on the two-letter code (<c>fr-CD</c> →
    /// <c>fr</c>), else (4) canonical <c>en</c>. Read-only — does NOT mutate thread culture (the invariant
    /// lock set in App.Initialize stands). Backs the "system" sentinel.
    /// </summary>
    private static string ResolveOsLanguage()
    {
        var cultureName = CultureInfo.CurrentUICulture.Name;                       // e.g. "en-GB", "fr-CD", "fr"

        // (1) Exact language (neutral) match — e.g. "fr" → "fr".
        foreach (var lang in LanguageManifest.Packs)
            if (string.Equals(lang.Id, cultureName, StringComparison.OrdinalIgnoreCase))
                return lang.Id;

        // (2) Exact region match — e.g. "en-GB" → "en-GB" (so a British machine gets Colour/Customise, not
        // the `en` canonical), "zh-CN" → "zh-CN". Regions are fileless except en-GB but all resolve correctly.
        foreach (var regions in LanguageManifest.Regions.Values)
            foreach (var r in regions)
                if (string.Equals(r.Id, cultureName, StringComparison.OrdinalIgnoreCase))
                    return r.Id;

        // (3) Neutral-language match — e.g. "fr-CD" → "fr" (a francophone on an unshipped region still
        // gets French).
        var twoLetter = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;     // e.g. "fr"
        foreach (var lang in LanguageManifest.Packs)
            if (string.Equals(lang.Id, twoLetter, StringComparison.OrdinalIgnoreCase))
                return lang.Id;

        return "en";
    }

    /// <summary>
    /// Live-preview hook: when a user override changes, re-apply the active language only if the changed
    /// locale id is in the active chain (the resolved active id, or its neutral) — otherwise the edit was
    /// to a non-displayed culture and is saved silently. Falls out of the existing <see cref="Apply"/>
    /// hot-swap for free.
    /// </summary>
    private void OnUserOverridesChanged(string id)
    {
        if (IsIdInActiveChain(id))
            Apply(ActiveLanguage);
    }

    /// <summary>True if <paramref name="id"/> is the resolved active id or its neutral language.</summary>
    private bool IsIdInActiveChain(string id)
    {
        var activeId = ActiveLanguage == "system" ? ResolveOsLanguage() : ActiveLanguage;
        return id == activeId || id == NeutralLanguageOf(activeId);
    }

    /// <summary>
    /// Resolves a pack id to its effective string map and reading direction. <c>"system"</c> resolves to
    /// the OS-matched pack. The map is built **3-tier** — canonical <c>en</c>, then the neutral-language
    /// pack (e.g. <c>fr</c> for <c>fr-CA</c>) if one exists, then the pack itself — so a key absent from a
    /// region inherits its neutral pack, and one absent from both inherits canonical English. Returns false
    /// if a file pack fails to load (map is then the canonical map).
    /// </summary>
    private bool TryResolveMap(string id, out Dictionary<string, string> map, out string direction)
    {
        direction = "ltr";

        if (id == "system")
            id = ResolveOsLanguage();

        if (id == "en")
        {
            // The canonical, key-complete pack (US-spelled, the de-facto neutral English). Honor user
            // overrides on `en` itself (authoring the canonical), merged last.
            map = new Dictionary<string, string>(_englishStrings);
            MergeUserOverrides("en", map);
            return true;
        }

        if (id is "pseudo" or "pseudo-rtl")
        {
            var rtl = id == "pseudo-rtl";
            map = PseudoLocalizer.Transform(_englishStrings, rtl);
            direction = rtl ? "rtl" : "ltr";
            return true;
        }

        try
        {
            var merged = new Dictionary<string, string>(_englishStrings);
            string? direction2 = null;
            var found = false;

            // Optional neutral-language layer: for a region id "L-R", merge the neutral "L" pack first.
            var neutral = NeutralLanguageOf(id);
            if (neutral != id && PackExists(neutral))
            {
                direction2 = MergePack(neutral, merged) ?? direction2;
                found = true;
            }

            // The pack itself (region or neutral). Guarded with PackExists: most region ids are now
            // fileless (the empty regional packs were deleted) and resolve through their neutral above —
            // an absent file must NOT throw, it just contributes no overrides.
            if (PackExists(id))
            {
                direction2 = MergePack(id, merged) ?? direction2;
                found = true;
            }

            // The per-user override tier, merged LAST so it beats every shipped tier — neutral first, then
            // the region id, mirroring the shipped-pack order. An UNSHIPPED culture that has only user
            // overrides still counts as resolved (found), so it does not fall back to canonical en.
            var neutralId = NeutralLanguageOf(id);
            if (neutralId != id && MergeUserOverrides(neutralId, merged))
                found = true;
            if (MergeUserOverrides(id, merged))
                found = true;

            // Neither the id nor its neutral resolved to a real pack — a genuinely unknown id. Signal
            // failure so the caller falls back to the canonical `en` (a fileless region whose neutral
            // *does* exist counts as found, so it still resolves correctly).
            if (!found)
            {
                map = merged;
                return false;
            }

            map = merged;
            direction = direction2 ?? "ltr";
            return true;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "LocalizationService: Failed to load language pack '{Id}', falling back to English", id);
            map = new Dictionary<string, string>(_englishStrings);
            return false;
        }
    }

    /// <summary>Merges the user override tier for <paramref name="packId"/> into <paramref name="into"/>
    /// (last-writer-wins). Returns whether any override was applied — so the caller can mark an otherwise
    /// unshipped culture as resolved.</summary>
    private bool MergeUserOverrides(string packId, Dictionary<string, string> into)
    {
        var overrides = _userTranslations?.GetOverrides(packId);
        if (overrides is null || overrides.Count == 0)
            return false;
        foreach (var (k, v) in overrides)
            into[k] = v;
        return true;
    }

    /// <summary>Loads pack <paramref name="id"/> and merges its overrides into <paramref name="into"/>;
    /// returns the pack's declared direction (or null). An empty pack is valid — it merges nothing.</summary>
    private static string? MergePack(string id, Dictionary<string, string> into)
    {
        var pack = LoadPack(id);
        if (pack.Strings is not null)
            foreach (var (k, v) in pack.Strings)
                into[k] = v;
        return pack.Direction;
    }

    /// <summary>
    /// Region → neutral-pack mappings where the neutral is NOT just the language prefix. Chinese splits by
    /// <em>script</em>, not region: Simplified regions inherit <c>zh-Hans</c> and Traditional regions
    /// inherit <c>zh-Hant</c> (never the bare <c>zh</c>, which would mix the two scripts).
    /// </summary>
    private static readonly Dictionary<string, string> ScriptNeutrals = new(StringComparer.OrdinalIgnoreCase)
    {
        ["zh-CN"] = "zh-Hans", ["zh-SG"] = "zh-Hans",
        ["zh-TW"] = "zh-Hant", ["zh-HK"] = "zh-Hant", ["zh-MO"] = "zh-Hant",
    };

    /// <summary>The neutral pack a region inherits: a script neutral for Chinese (see
    /// <see cref="ScriptNeutrals"/>), else the language prefix (<c>fr</c> from <c>fr-CA</c>), else the id
    /// itself when it has no region part.</summary>
    private static string NeutralLanguageOf(string id)
    {
        if (ScriptNeutrals.TryGetValue(id, out var scriptNeutral))
            return scriptNeutral;
        var dash = id.IndexOf('-');
        return dash > 0 ? id[..dash] : id;
    }

    private static bool PackExists(string id) =>
        AssetLoader.Exists(new Uri($"avares://HexIDE/Localization/Packs/{id}.json"));

    private static LanguagePackRecord LoadPack(string id)
    {
        var uri = new Uri($"avares://HexIDE/Localization/Packs/{id}.json");
        using var stream = AssetLoader.Open(uri);
        return JsonSerializer.Deserialize(stream, LocalizationJsonContext.Default.LanguagePackRecord)
               ?? throw new InvalidOperationException($"Language pack '{id}' deserialized to null.");
    }

    private static Dictionary<string, string> LoadEnglish()
    {
        try
        {
            return LoadPack("en").Strings ?? new Dictionary<string, string>();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "LocalizationService: Failed to load canonical pack (en.json)");
            return new Dictionary<string, string>();
        }
    }

    /// <inheritdoc />
    public IReadOnlySet<string> ShippedOwnChainKeys(string id)
    {
        // en is the canonical itself — by definition nothing is an "en-only" fallback above it.
        if (id == "en")
            return new HashSet<string>();

        // The keys the From culture's OWN shipped chain (region ∪ neutral) actually defines — EXCLUDING the
        // canonical en. A key not in this set is "secretly English" (the red-fallback rule). When the neutral
        // IS en (e.g. en-GB), the neutral contributes nothing: en-GB is red wherever it doesn't override en.
        var neutral = NeutralLanguageOf(id);
        var keys = new HashSet<string>();
        if (neutral != "en")
            keys.UnionWith(LoadPackOwnStrings(neutral).Keys);
        if (id != neutral)
            keys.UnionWith(LoadPackOwnStrings(id).Keys);
        return keys;
    }

    /// <summary>The keys this pack file (id only — not its chain) declares, or empty if it has no file.</summary>
    private static Dictionary<string, string> LoadPackOwnStrings(string id)
    {
        if (!PackExists(id))
            return new Dictionary<string, string>();
        try
        {
            return LoadPack(id).Strings ?? new Dictionary<string, string>();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "LocalizationService: Failed to load pack '{Id}' for own-key scan", id);
            return new Dictionary<string, string>();
        }
    }

    /// <summary>
    /// Pushes the active reading direction onto the open top-level windows so the IDE chrome mirrors
    /// for RTL packs. The form-designer canvas is locked to LeftToRight in FormEditView.axaml (the form
    /// being designed has its own direction; it is not IDE chrome), mirroring the Light-theme lock there.
    /// </summary>
    private void ApplyFlowDirection()
    {
        switch (Application.Current?.ApplicationLifetime)
        {
            case IClassicDesktopStyleApplicationLifetime desktop:
                foreach (var window in desktop.Windows)
                    window.FlowDirection = FlowDirection;
                break;
            case ISingleViewApplicationLifetime { MainView: { } mainView }:
                mainView.FlowDirection = FlowDirection;
                break;
        }
    }

    private static ResourceDictionary BuildResourceDictionary(Dictionary<string, string> strings)
    {
        var dict = new ResourceDictionary();
        foreach (var (key, value) in strings)
            dict[key] = value;
        return dict;
    }

    /// <summary>Strips non-identifier characters so e.g. "(Name)" → "Name" for the PropDesc key.</summary>
    private static string Sanitize(string name)
    {
        var sb = new StringBuilder(name.Length);
        foreach (var c in name)
            if (char.IsLetterOrDigit(c) || c == '_')
                sb.Append(c);
        return sb.ToString();
    }
}
