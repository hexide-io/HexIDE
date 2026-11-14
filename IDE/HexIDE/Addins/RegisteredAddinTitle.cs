using System.Globalization;
using HexIDE.IDE;
using HexIDE.Localization;

namespace HexIDE.Addins;

// Only host-owned data survives registration. Language changes never read the add-in's dictionary.
internal sealed class RegisteredAddinTitle
{
    private readonly string? _key;
    private readonly DistributionText _text;
    private static readonly Lazy<LocalizationService> Canonical = new(() => new LocalizationService());

    private static readonly IReadOnlyDictionary<string, string> KeyOwners =
        new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(new Dictionary<string, string>
        {
            ["Str.AIChat."] = "com.hexide.aichat",
        });

    internal RegisteredAddinTitle(AddinTitle title, AddinOrigin origin = default)
    {
        ArgumentNullException.ThrowIfNull(title);
        _key = title.Key;
        if (_key is not null)
        {
            if (!Canonical.Value.HasCanonicalKey(_key))
                throw new ArgumentException($"Unknown canonical localization key '{_key}'.", nameof(title));
            if (!((origin.IsFirstParty || origin.IsDeveloperUnsigned) && KeyOwners.Any(owner =>
                origin.Id == owner.Value && _key.StartsWith(owner.Key, StringComparison.Ordinal))))
                throw new ArgumentException($"Localization key '{_key}' is outside this add-in's owned area.", nameof(title));
        }
        var translations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (title.Translations is { } supplied)
            foreach (var (language, text) in supplied)
            {
                if (string.IsNullOrEmpty(language) || !IsCulture(language))
                    throw new ArgumentException($"Malformed translation language '{language}'.", nameof(title));
                if (string.IsNullOrWhiteSpace(text))
                    throw new ArgumentException($"Empty translation for '{language}'.", nameof(title));
                translations.Add(language, PrepareText(text));
            }
        _text = new DistributionText(PrepareText(title.Text), translations);
    }

    private static bool IsCulture(string language)
    {
        // GetCultureInfo accepts underscores and arbitrary ICU locale names; the public API takes culture tags.
        if (!System.Text.RegularExpressions.Regex.IsMatch(language, @"^[A-Za-z]{2,8}(-[A-Za-z0-9]{1,8})*$")) return false;
        try { return CultureInfo.GetCultureInfo(language).Name.Length > 0; }
        catch (CultureNotFoundException) { return false; }
    }

    // Plain text and translations share this display boundary (#3925).
    private static string PrepareText(string text) => text;

    internal string Resolve(ILocalizationService localization) => _key is null
        ? _text.Resolve(BrandText.LanguageChain(localization)) : localization.GetString(_key);
}
