using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace HexIDE.TwinBasic;

/// <summary>
/// The language data a compiler's initialization carries: the strings of one of the IDE's <c>locale\*.lang</c> packs, as
/// a JSON object of key to text. The IDE sends its active pack, <c>en-gb</c> by default; an empty object does not complete
/// initialization (#1200). A pack is a list of <c>Key: "text";</c> entries, where an entry ends at a semicolon followed
/// by a space, a tab or a line break, block comments are ignored, adjacent quoted runs join, and <c>""</c> inside a run is
/// one quote. Read as the IDE's front end reads it on BETA 1024, including a carriage return between runs being kept.
/// </summary>
public static partial class CompilerLanguagePack
{
    public const string DefaultName = "en-gb";

    public static string PathIn(string installFolder, string name = DefaultName) =>
        Path.Combine(installFolder, "locale", name + ".lang");

    public static IReadOnlyDictionary<string, string> Read(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var entries = new Dictionary<string, string>(StringComparer.Ordinal);
        var body = Comment().Replace(text + "\r\n", "");
        foreach (var entry in Terminator().Split(body))
        {
            var colon = entry.IndexOf(':');
            if (colon < 0) continue;
            entries[entry[..colon].Trim()] = Unquote(entry.AsSpan(colon + 1));
        }
        return entries;
    }

    public static CompilerLanguageData ToLanguageData(IReadOnlyDictionary<string, string> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        using var document = JsonDocument.Parse(JsonSerializer.SerializeToUtf8Bytes(entries, LanguagePackJsonContext.Default.IReadOnlyDictionaryStringString));
        return CompilerWireCodec.ReadLanguageData(document.RootElement);
    }

    private static string Unquote(ReadOnlySpan<char> value)
    {
        var text = new StringBuilder();
        bool quoted = false, justClosed = false;
        foreach (var c in value)
        {
            if (quoted)
            {
                if (c == '"') { quoted = false; justClosed = true; }
                else text.Append(c);
                continue;
            }
            if (c == '"')
            {
                quoted = true;
                if (justClosed) text.Append('"');
            }
            else if (c == '\r') text.Append(c);
            justClosed = false;
        }
        return text.ToString();
    }

    [GeneratedRegex(@"/\*[\s\S]*?\*/")]
    private static partial Regex Comment();

    [GeneratedRegex(@";(?: |\t|\r?\n)")]
    private static partial Regex Terminator();
}

[System.Text.Json.Serialization.JsonSerializable(typeof(IReadOnlyDictionary<string, string>))]
internal sealed partial class LanguagePackJsonContext : System.Text.Json.Serialization.JsonSerializerContext;
