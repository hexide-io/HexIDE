using System.Text;

namespace HexIDE.Runtime.RichText;

// Measured RICHTX32 document shapes. Unmodelled RTF is refused, never flattened.
internal sealed class RtfDocument
{
    private const string FontTable = "{\\fonttbl{\\f0\\fnil\\fcharset0 MS Sans Serif;}}\r\n";
    private const string DefaultHeader = "{\\rtf1\\ansi\\ansicpg1252\\deff0\\deflang1033" + FontTable + "\\viewkind4\\uc1\\pard\\f0\\fs17 ";
    private const string TextHeader = "{\\rtf1\\ansi\\deff0" + FontTable + "\\viewkind4\\uc1\\pard\\lang1033\\f0\\fs17 ";
    private const string Footer = "\r\n\\par }\r\n";
    private static readonly Encoding Ansi = CodePagesEncodingProvider.Instance.GetEncoding(1252)!;
    private readonly bool isDefault;
    private RtfFormattedDocument? formatted;
    public IReadOnlyList<RtfRun> Runs => formatted?.Runs ?? [new RtfRun(Text, new RtfCharacterFormat())];

    private RtfDocument(string text, bool isDefault = false)
    {
        Text = text;
        this.isDefault = isDefault;
    }

    public string Text { get; }
    public static RtfDocument Empty => new("", true);

    public static RtfDocument FromText(string text) => new(Ansi.GetString(Ansi.GetBytes(text)));

    internal RtfDocument WithRuns(IEnumerable<RtfRun> runs)
    {
        var next = RtfFormattedDocument.FromRuns(runs, formatted);
        return new RtfDocument(next.Text) { formatted = next };
    }

    internal IEnumerable<RtfRun> Slice(int start, int length)
    {
        var offset = 0;
        foreach (var run in Runs)
        {
            var from = Math.Max(start - offset, 0);
            var to = Math.Min(start + length - offset, run.Text.Length);
            if (to > from) yield return run with { Text = run.Text[from..to] };
            offset += run.Text.Length;
        }
    }

    public string WriteSelection(int start, int length)
    {
        if (start < 0 || length < 0 || start > Text.Length - length)
            throw new ArgumentOutOfRangeException(nameof(start));
        if (length == 0) return "{\\rtf1\\ansi\\ansicpg1252\\deff0\\deflang1033\\uc1 }\r\n";
        return (formatted ?? RtfFormattedDocument.Read(Write())).WriteSelection(start, length);
    }

    public string Write()
    {
        if (formatted != null) return formatted.Write();
        var body = new StringBuilder();
        var bytes = Ansi.GetBytes(Text);
        for (var index = 0; index < bytes.Length; index++)
        {
            var value = bytes[index];
            switch (value)
            {
                case 13 when index + 1 < bytes.Length && bytes[index + 1] == 10:
                    body.Append("\r\n\\par "); index++; break;
                case 9: body.Append("\\tab "); break;
                case (byte)'\\': case (byte)'{': case (byte)'}':
                    body.Append('\\').Append((char)value);
                    break;
                case >= 128:
                    body.Append("\\'").Append(value.ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
                    break;
                case < 32:
                    throw Unsupported("writing control characters and paragraph breaks");
                default: body.Append((char)value); break;
            }
        }
        return (isDefault ? DefaultHeader : TextHeader) + body + Footer;
    }

    public static RtfDocument Read(string source)
    {
        if (!source.StartsWith("{\\rtf", StringComparison.Ordinal) || !HasBalancedGroups(source))
            return FromText(source);

        var isDefault = source.StartsWith(DefaultHeader, StringComparison.Ordinal);
        var header = isDefault ? DefaultHeader : TextHeader;
        if (!source.StartsWith(header, StringComparison.Ordinal) || !source.EndsWith(Footer, StringComparison.Ordinal))
            return ReadFormatted(source);

        var body = source.AsSpan(header.Length, source.Length - header.Length - Footer.Length);
        var text = new StringBuilder();
        for (var index = 0; index < body.Length; index++)
        {
            var character = body[index];
            if (character == '\\')
            {
                if (++index >= body.Length)
                    throw Unsupported("a trailing escape");
                character = body[index];
                if (character is '\\' or '{' or '}')
                    text.Append(character);
                else if (character == '\'' && index + 2 < body.Length &&
                         byte.TryParse(body.Slice(index + 1, 2), System.Globalization.NumberStyles.HexNumber,
                             System.Globalization.CultureInfo.InvariantCulture, out var value))
                {
                    text.Append(Ansi.GetString([value]));
                    index += 2;
                }
                else if (body[index..].StartsWith("tab ", StringComparison.Ordinal))
                {
                    text.Append('\t');
                    index += 3;
                }
                else
                    return ReadFormatted(source);
            }
            else if (character is '{' or '}' || character < 32)
                return ReadFormatted(source);
            else
                text.Append(character);
        }
        return new RtfDocument(Ansi.GetString(Ansi.GetBytes(text.ToString())), isDefault);
    }

    private static RtfDocument ReadFormatted(string source)
    {
        var parsed = RtfFormattedDocument.Read(source);
        return new RtfDocument(parsed.Text) { formatted = parsed };
    }

    private static bool HasBalancedGroups(string source)
    {
        var depth = 0;
        for (var index = 0; index < source.Length; index++)
        {
            if (source[index] == '\\')
            {
                if (source.AsSpan(index + 1).StartsWith("bin", StringComparison.Ordinal))
                    throw Unsupported("binary RTF");
                index++;
                continue;
            }
            if (source[index] == '{') depth++;
            if (source[index] == '}' && --depth < 0) return false;
        }
        return depth == 0;
    }

    private static NotImplementedException Unsupported(string detail) =>
        new($"RichTextBox RTF: {detail} is not implemented (#1100)");
}
