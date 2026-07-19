using System;
using System.Collections.Generic;
using Avalonia.Media;

namespace HexIDE.Localization;

/// <summary>
/// Applies and tracks the active IDE-chrome language pack.
/// <para>
/// Strings live in JSON packs (<c>avares://HexIDE/Localization/Packs/{id}.json</c>). AXAML binds to
/// them via <c>{DynamicResource Str.Key}</c>; a merged <see cref="Avalonia.Controls.ResourceDictionary"/>
/// is hot-swapped on <see cref="Apply"/>, exactly as <c>ThemeService</c> swaps theme colours, so the UI
/// re-resolves live with no restart. C# call sites read the same map via <see cref="GetString"/>.
/// </para>
/// <para>
/// The language is a pack-selector string only: this service never calls <c>ResourceManager</c>, reads
/// satellite assemblies, or mutates thread culture — so the deliberate invariant-culture lock that keeps
/// VB6 number parsing stable is preserved.
/// </para>
/// </summary>
public interface ILocalizationService
{
    /// <summary>Id of the active pack (e.g. <c>"en"</c>).</summary>
    string ActiveLanguage { get; }

    /// <summary>Selectable languages for the Options Language combo (one per translation + synthetic entries).</summary>
    IReadOnlyList<LanguageInfo> AvailableLanguages { get; }

    /// <summary>
    /// Regions under a language for the dependent Region combo (empty for languages with none, or for
    /// the <c>system</c>/<c>pseudo</c> entries). Does not include the synthetic "(Standard)" item — the
    /// caller prepends that. Region ids are mostly fileless and resolve through their neutral.
    /// </summary>
    IReadOnlyList<RegionInfo> RegionsFor(string languageId);

    /// <summary>Reading direction of the active pack. Drives app-wide layout mirroring (RTL).</summary>
    FlowDirection FlowDirection { get; }

    /// <summary>Raised after <see cref="Apply"/> changes the active language.</summary>
    event Action? LanguageChanged;

    /// <summary>Switch the IDE chrome to the given pack at runtime. No restart required.</summary>
    void Apply(string id);

    /// <summary>
    /// Returns the localized string for <paramref name="key"/> (active pack → English fallback →
    /// the key itself, so a miss is visible and never throws).
    /// </summary>
    string GetString(string key);

    /// <summary>Terse alias for <see cref="GetString"/>.</summary>
    string this[string key] { get; }

    /// <summary>
    /// Returns the string for <paramref name="key"/> from a specific pack without making it active.
    /// Used by the bilingual revert gate to label its buttons in two languages at once.
    /// </summary>
    string GetStringFrom(string packId, string key);

    /// <summary>
    /// Localized property-grid description for a VB6 property (key <c>Str.PropDesc.{name}</c>), or
    /// <c>null</c> if no pack supplies it — callers then fall back to the English text embedded in
    /// <c>HexIDE.Core</c> (whose property <i>names</i> stay English as VB6 API identifiers).
    /// </summary>
    string? GetPropertyDescription(string propertyName);

    /// <summary>
    /// The keys defined in a culture's <b>own</b> shipped chain — the union of its neutral pack's keys and
    /// its region pack's keys, excluding the canonical <c>en</c>. Empty when <paramref name="id"/> is
    /// <c>"en"</c>. Drives the translation editor's red en-fallback highlight: when the reference (From)
    /// culture is not <c>en</c>, a key absent from this set is one the reference only inherits from English
    /// (so its "translation" is secretly the canonical text).
    /// </summary>
    IReadOnlySet<string> ShippedOwnChainKeys(string id);
}
