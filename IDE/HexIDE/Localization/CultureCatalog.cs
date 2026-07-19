using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace HexIDE.Localization;

/// <summary>
/// The canonical source of truth for the From/To language and region pickers in the local translation
/// editor: a cascade built entirely from .NET <see cref="CultureInfo"/>, never from the shipped-pack
/// <c>LanguageManifest</c>. The editor can therefore target <b>any</b> culture .NET detects (authoring a
/// brand-new local translation), not just the ones HexIDE ships.
/// <para>
/// The IDE locks the thread culture to invariant, so every display string here uses the deterministic
/// <see cref="CultureInfo.EnglishName"/> (and <see cref="RegionInfo.EnglishName"/>) rather than the
/// OS-locale-dependent <c>DisplayName</c>. The neutral/region lists and the parent→regions lookup are
/// computed once in a static constructor.
/// </para>
/// </summary>
public static class CultureCatalog
{
    private static readonly IReadOnlyList<LanguageInfo> s_languages;
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<RegionInfo>> s_regionsByNeutral;

    static CultureCatalog()
    {
        // Every neutral culture (incl. the Chinese script neutrals zh-Hans / zh-Hant), minus the
        // invariant culture (empty Name). Direction comes from TextInfo.IsRightToLeft.
        s_languages = CultureInfo.GetCultures(CultureTypes.NeutralCultures)
            .Where(c => !string.IsNullOrEmpty(c.Name))
            .Select(c => new LanguageInfo(
                c.Name,
                c.EnglishName,
                c.TextInfo.IsRightToLeft ? "rtl" : "ltr"))
            .OrderBy(l => l.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Specific cultures grouped by their neutral parent. Grouping by Parent.Name auto-reconciles the
        // Chinese script split (zh-CN.Parent == zh-Hans, zh-TW.Parent == zh-Hant) for free.
        var lookup = new Dictionary<string, IReadOnlyList<RegionInfo>>(StringComparer.Ordinal);
        foreach (var group in CultureInfo.GetCultures(CultureTypes.SpecificCultures)
                     .Where(c => !string.IsNullOrEmpty(c.Name) && !string.IsNullOrEmpty(c.Parent.Name))
                     .GroupBy(c => c.Parent.Name, StringComparer.Ordinal))
        {
            var regions = group
                .Select(c => new RegionInfo(c.Name, CountryDisplayName(c)))
                .OrderBy(r => r.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();
            lookup[group.Key] = regions;
        }

        s_regionsByNeutral = lookup;
    }

    /// <summary>All neutral cultures (incl. <c>zh-Hans</c>/<c>zh-Hant</c>), sorted by English name.</summary>
    public static IReadOnlyList<LanguageInfo> Languages => s_languages;

    /// <summary>
    /// The specific cultures whose <c>Parent.Name == neutralId</c> (e.g. <c>de</c> → de-DE, de-AT, …),
    /// each carrying its country display name, sorted by that display. Empty when the neutral has no
    /// specific cultures.
    /// </summary>
    public static IReadOnlyList<RegionInfo> RegionsFor(string neutralId) =>
        s_regionsByNeutral.TryGetValue(neutralId, out var regions) ? regions : [];

    /// <summary>
    /// The neutral id for a culture id: <c>CultureInfo.GetCultureInfo(id).Parent.Name</c> when non-empty,
    /// else the id itself — so <c>de-AT</c> → <c>de</c>, <c>zh-CN</c> → <c>zh-Hans</c>, <c>de</c> → <c>de</c>.
    /// Robust to unknown ids (returns the id on any failure).
    /// </summary>
    public static string NeutralOf(string id)
    {
        try
        {
            var parent = CultureInfo.GetCultureInfo(id).Parent.Name;
            return string.IsNullOrEmpty(parent) ? id : parent;
        }
        catch (CultureNotFoundException)
        {
            return id;
        }
    }

    /// <summary>
    /// A country/region display name for a specific culture. Prefers <see cref="RegionInfo.EnglishName"/>
    /// (deterministic, country-only). On failure falls back to the parenthetical of the culture's English
    /// name (e.g. "German (Austria)" → "Austria"), else the full English name.
    /// </summary>
    private static string CountryDisplayName(CultureInfo c)
    {
        try
        {
            return new System.Globalization.RegionInfo(c.Name).EnglishName;
        }
        catch (ArgumentException)
        {
            var open = c.EnglishName.IndexOf('(');
            var close = c.EnglishName.IndexOf(')');
            if (open >= 0 && close > open + 1)
                return c.EnglishName[(open + 1)..close];
            return c.EnglishName;
        }
    }
}
