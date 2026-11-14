namespace HexIDE.Addins;

/// <summary>Inert chrome text: a literal, an owned IDE key, or text with per-language translations.</summary>
public sealed class AddinTitle
{
    public string Text { get; }
    public string? Key { get; private init; }
    public IReadOnlyDictionary<string, string>? Translations { get; }

    public AddinTitle(string text, IReadOnlyDictionary<string, string>? translations = null)
    {
        Text = text;
        Translations = translations;
    }

    private AddinTitle() { Text = ""; }

    public static AddinTitle FromKey(string key) => new() { Key = key ?? throw new ArgumentException("A localization key is required.", nameof(key)) };
}
