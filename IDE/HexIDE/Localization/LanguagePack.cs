using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace HexIDE.Localization;

/// <summary>
/// A language pack as stored on disk (<c>avares://HexIDE/Localization/Packs/{id}.json</c>).
/// Real packs are merged over the English canonical pack at load, so they need only list their
/// overrides; any missing key inherits English.
/// </summary>
public record LanguagePackRecord(
    string Name,
    string? Direction,
    Dictionary<string, string>? Strings
);

/// <summary>A selectable language for the Options Language combo (one per distinct translation).</summary>
/// <param name="Id">Neutral pack id (filename stem, e.g. <c>"en"</c>, <c>"fr"</c>, <c>"zh-Hans"</c>; or a
/// synthetic id like <c>"system"</c>/<c>"pseudo"</c>).</param>
/// <param name="DisplayName">Language name shown in the combo (e.g. "French").</param>
/// <param name="Direction"><c>"ltr"</c> or <c>"rtl"</c>.</param>
public record LanguageInfo(string Id, string DisplayName, string Direction);

/// <summary>A region under a language, shown in the dependent Region combo.</summary>
/// <param name="Id">The region pack id (e.g. <c>"fr-FR"</c>, <c>"en-GB"</c>). Usually fileless — it
/// resolves through its neutral via the 3-tier fallback; only a few (e.g. <c>en-GB</c>) ship a real pack.</param>
/// <param name="DisplayName">Region name shown in the combo (e.g. "France").</param>
public record RegionInfo(string Id, string DisplayName);

[JsonSerializable(typeof(LanguagePackRecord))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal partial class LocalizationJsonContext : JsonSerializerContext { }

/// <summary>
/// Static registry of the shipped translations. <c>avares://</c> has no trim/AOT-safe directory
/// enumeration, so packs are registered here — mirroring <c>ThemeService.AvailableThemes</c> and
/// <c>KeymapService.AvailableKeymaps</c>.
/// <para><see cref="Packs"/> is the <b>Language</b> combo (one entry per distinct translation; the neutral
/// id is the key-complete or merge-over base). <see cref="Regions"/> is the dependent <b>Region</b> combo,
/// a data-driven map keyed by neutral id — most region ids are <i>fileless</i> and resolve through their
/// neutral via the 3-tier fallback; only a few (e.g. <c>en-GB</c>) ship a real pack. The synthetic
/// pseudo-localization entries are appended by <see cref="LocalizationService"/>.</para>
/// Adding a language = drop <c>{id}.json</c> in <c>Packs/</c> + one line in <see cref="Packs"/> (+ its
/// regions in <see cref="Regions"/>). See docs/localization-regions.md for the curated catalog.
/// </summary>
internal static class LanguageManifest
{
    // One entry per distinct translation (the Language combo). `en` is the canonical, key-complete pack
    // (US-spelled, the de-facto neutral English); every other pack merges over it. Chinese ships two
    // *script* neutrals (Hans/Hant), not a bare `zh`.
    internal static readonly IReadOnlyList<LanguageInfo> Packs =
    [
        new LanguageInfo("en", "English", "ltr"),
        new LanguageInfo("fr", "French", "ltr"),
        new LanguageInfo("ar", "Arabic", "rtl"),
        new LanguageInfo("zh-Hans", "Chinese (Simplified)", "ltr"),
        new LanguageInfo("zh-Hant", "Chinese (Traditional)", "ltr"),
        new LanguageInfo("de", "German", "ltr"),
        new LanguageInfo("es", "Spanish", "ltr"),
        new LanguageInfo("ja", "Japanese", "ltr"),
        new LanguageInfo("he", "Hebrew", "rtl"),
        new LanguageInfo("cs", "Czech", "ltr"),
        new LanguageInfo("it", "Italian", "ltr"),
        new LanguageInfo("ko", "Korean", "ltr"),
        new LanguageInfo("ru", "Russian", "ltr"),
        new LanguageInfo("uk", "Ukrainian", "ltr"),
        new LanguageInfo("pl", "Polish", "ltr"),
        new LanguageInfo("pt", "Portuguese", "ltr"),
        new LanguageInfo("tr", "Turkish", "ltr"),
        // Batch 2 — ordered by developer-community size (see openspec/specs/language-packs/spec.md).
        new LanguageInfo("hi", "Hindi", "ltr"),
        new LanguageInfo("ur", "Urdu", "rtl"),
        new LanguageInfo("id", "Indonesian", "ltr"),
        new LanguageInfo("vi", "Vietnamese", "ltr"),
        new LanguageInfo("fa", "Persian", "rtl"),
        new LanguageInfo("nl", "Dutch", "ltr"),
        new LanguageInfo("sv", "Swedish", "ltr"),
        new LanguageInfo("el", "Greek", "ltr"),
        new LanguageInfo("nb", "Norwegian Bokmål", "ltr"),
        new LanguageInfo("da", "Danish", "ltr"),
        new LanguageInfo("fi", "Finnish", "ltr"),
        // Latin — a full-translation pack. Loaded purely by string id (no CultureInfo("la") is ever
        // constructed), so it works regardless of .NET's Latin culture support.
        new LanguageInfo("la", "Latin", "ltr"),
        // Esperanto — likewise a full-translation pack loaded by string id.
        new LanguageInfo("eo", "Esperanto", "ltr"),
    ];

    // Regions under each language (the dependent Region combo), keyed by neutral id. The VM prepends a
    // synthetic "(Standard)" entry (the bare neutral). All region ids here are fileless except `en-GB`,
    // which ships a real pack; the rest resolve through their neutral via 3-tier fallback (Chinese region
    // → script neutral). Listed for UI consistency + "we support this region" signal.
    internal static readonly IReadOnlyDictionary<string, IReadOnlyList<RegionInfo>> Regions =
        new Dictionary<string, IReadOnlyList<RegionInfo>>
        {
            ["en"] =
            [
                new("en-US", "United States"), new("en-GB", "United Kingdom"), new("en-CA", "Canada"),
                new("en-AU", "Australia"), new("en-IN", "India"), new("en-NG", "Nigeria"),
                new("en-PH", "Philippines"), new("en-ZA", "South Africa"), new("en-IE", "Ireland"),
                new("en-NZ", "New Zealand"), new("en-SG", "Singapore"),
            ],
            ["fr"] =
            [
                new("fr-FR", "France"), new("fr-CA", "Canada"), new("fr-BE", "Belgium"),
                new("fr-CH", "Switzerland"), new("fr-LU", "Luxembourg"), new("fr-MC", "Monaco"),
                new("fr-CD", "Congo [DRC]"), new("fr-CI", "Côte d’Ivoire"), new("fr-SN", "Senegal"),
                new("fr-CM", "Cameroon"), new("fr-MA", "Morocco"), new("fr-DZ", "Algeria"),
                new("fr-HT", "Haiti"),
            ],
            ["ar"] =
            [
                new("ar-SA", "Saudi Arabia"), new("ar-EG", "Egypt"), new("ar-AE", "U.A.E."),
                new("ar-MA", "Morocco"), new("ar-DZ", "Algeria"), new("ar-IQ", "Iraq"),
                new("ar-KW", "Kuwait"), new("ar-QA", "Qatar"), new("ar-BH", "Bahrain"),
                new("ar-OM", "Oman"), new("ar-JO", "Jordan"), new("ar-LB", "Lebanon"),
                new("ar-YE", "Yemen"), new("ar-SY", "Syria"), new("ar-TN", "Tunisia"),
                new("ar-LY", "Libya"), new("ar-SD", "Sudan"), new("ar-PS", "Palestinian Authority"),
            ],
            ["zh-Hans"] = [new("zh-CN", "China"), new("zh-SG", "Singapore")],
            ["zh-Hant"] = [new("zh-TW", "Taiwan"), new("zh-HK", "Hong Kong"), new("zh-MO", "Macau")],
            ["de"] =
            [
                new("de-DE", "Germany"), new("de-AT", "Austria"), new("de-CH", "Switzerland"),
                new("de-LU", "Luxembourg"), new("de-LI", "Liechtenstein"), new("de-BE", "Belgium"),
            ],
            ["es"] =
            [
                new("es-ES", "Spain"), new("es-MX", "Mexico"), new("es-AR", "Argentina"),
                new("es-CO", "Colombia"), new("es-CL", "Chile"), new("es-US", "United States"),
                new("es-PE", "Peru"), new("es-VE", "Venezuela"), new("es-EC", "Ecuador"),
                new("es-GT", "Guatemala"), new("es-DO", "Dominican Republic"), new("es-CR", "Costa Rica"),
                new("es-PR", "Puerto Rico"), new("es-BO", "Bolivia"), new("es-CU", "Cuba"),
                new("es-GQ", "Equatorial Guinea"), new("es-HN", "Honduras"), new("es-NI", "Nicaragua"),
                new("es-PA", "Panama"), new("es-PY", "Paraguay"), new("es-SV", "El Salvador"),
                new("es-UY", "Uruguay"),
            ],
            ["ja"] = [new("ja-JP", "Japan")],
            ["he"] = [new("he-IL", "Israel")],
            ["cs"] = [new("cs-CZ", "Czechia")],
            ["it"] = [new("it-IT", "Italy"), new("it-CH", "Switzerland"), new("it-SM", "San Marino")],
            ["ko"] = [new("ko-KR", "Korea"), new("ko-KP", "North Korea")],
            ["ru"] =
            [
                new("ru-RU", "Russia"), new("ru-UA", "Ukraine"), new("ru-KZ", "Kazakhstan"),
                new("ru-BY", "Belarus"), new("ru-KG", "Kyrgyzstan"),
            ],
            ["uk"] = [new("uk-UA", "Ukraine")],
            ["pl"] = [new("pl-PL", "Poland")],
            ["pt"] =
            [
                new("pt-BR", "Brazil"), new("pt-PT", "Portugal"), new("pt-AO", "Angola"),
                new("pt-MZ", "Mozambique"), new("pt-CV", "Cabo Verde"), new("pt-GW", "Guinea-Bissau"),
                new("pt-ST", "São Tomé & Príncipe"), new("pt-TL", "Timor-Leste"),
                new("pt-GQ", "Equatorial Guinea"),
            ],
            ["tr"] = [new("tr-TR", "Türkiye"), new("tr-CY", "Cyprus")],
            ["hi"] = [new("hi-IN", "India")],
            ["ur"] = [new("ur-PK", "Pakistan"), new("ur-IN", "India")],
            ["id"] = [new("id-ID", "Indonesia")],
            ["vi"] = [new("vi-VN", "Vietnam")],
            ["fa"] = [new("fa-IR", "Iran"), new("fa-AF", "Afghanistan")],
            ["nl"] = [new("nl-NL", "Netherlands"), new("nl-BE", "Belgium"), new("nl-SR", "Suriname")],
            ["sv"] = [new("sv-SE", "Sweden"), new("sv-FI", "Finland")],
            ["el"] = [new("el-GR", "Greece"), new("el-CY", "Cyprus")],
            ["nb"] = [new("nb-NO", "Norway")],
            ["da"] = [new("da-DK", "Denmark")],
            ["fi"] = [new("fi-FI", "Finland")],
            ["la"] = [new("la-VA", "Vatican City")],
            ["eo"] = [new("eo-001", "World")],
        };
}
