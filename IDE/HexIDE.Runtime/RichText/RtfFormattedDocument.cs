using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace HexIDE.Runtime.RichText;

// Measured RTF formatting subset. Unmodelled destinations and words are refused.
internal sealed class RtfFormattedDocument
{
    private sealed record Font(int Id, string Name, int? Charset, bool CanWrite = true);
    private static readonly Encoding Ansi = CodePagesEncodingProvider.Instance.GetEncoding(1252)!;
    private readonly List<Font> fonts = [];
    private readonly List<int> colors = [0];
    private readonly List<RtfRun> runs = [];
    public IReadOnlyList<RtfRun> Runs => runs.AsReadOnly();
    public string Text => string.Concat(runs.Select(r => r.Text));

    public static RtfFormattedDocument Read(string source)
    {
        var document = new RtfFormattedDocument();
        var fontTable = Regex.Match(source, @"\{\\fonttbl(?<fonts>(?:\{[^{}]*\})+)\}");
        if (!fontTable.Success) throw Unsupported("missing or unsupported font table");
        foreach (Match entry in Regex.Matches(fontTable.Groups["fonts"].Value, @"\{\\f(?<id>\d+)(?:\\fnil)?(?:\\fcharset(?<charset>\d+))? (?<name>[^{}\\;]+);\}"))
        {
            var charset = entry.Groups["charset"];
            document.fonts.Add(new Font(int.Parse(entry.Groups["id"].Value, CultureInfo.InvariantCulture),
                entry.Groups["name"].Value, charset.Success ? int.Parse(charset.Value, CultureInfo.InvariantCulture) : null));
        }
        var consumed = string.Concat(Regex.Matches(fontTable.Groups["fonts"].Value, @"\{\\f\d+(?:\\fnil)?(?:\\fcharset\d+)? [^{}\\;]+;\}").Select(m => m.Value));
        if (consumed != fontTable.Groups["fonts"].Value || document.fonts.Count == 0 ||
            document.fonts.Select(f => f.Id).Distinct().Count() != document.fonts.Count ||
            document.fonts.Select(f => f.Name).Distinct().Count() != document.fonts.Count ||
            document.fonts[0].Charset == 2 ||
            document.fonts.Any(f => f.Charset is not (null or 0) && (f.Charset != 2 || f.Name != "Symbol")))
            throw Unsupported("font table entries");
        source = source.Remove(fontTable.Index, fontTable.Length);
        var colorTable = Regex.Match(source, @"\{\\colortbl ?;(?<colors>(?:\\red\d+\\green\d+\\blue\d+;)*)\}");
        if (colorTable.Success)
        {
            foreach (Match entry in Regex.Matches(colorTable.Groups["colors"].Value, @"\\red(\d+)\\green(\d+)\\blue(\d+);"))
            {
                var red = int.Parse(entry.Groups[1].Value, CultureInfo.InvariantCulture);
                var green = int.Parse(entry.Groups[2].Value, CultureInfo.InvariantCulture);
                var blue = int.Parse(entry.Groups[3].Value, CultureInfo.InvariantCulture);
                if (red > 255 || green > 255 || blue > 255) throw Unsupported("colour components");
                document.colors.Add(red | green << 8 | blue << 16);
            }
            source = source.Remove(colorTable.Index, colorTable.Length);
        }

        var format = new RtfCharacterFormat(FontName: document.fonts[0].Name);
        var paragraph = new RtfParagraphFormat();
        var hasBulletGroups = false;
        var stack = new Stack<(RtfCharacterFormat Character, RtfParagraphFormat Paragraph)>();
        var text = new StringBuilder();
        void Flush()
        {
            if (text.Length == 0) return;
            var value = Ansi.GetString(Ansi.GetBytes(text.ToString()));
            if (document.runs.Count > 0 && document.runs[^1].Format == format && document.runs[^1].Paragraph.Controls == paragraph.Controls)
                document.runs[^1] = document.runs[^1] with { Text = document.runs[^1].Text + value };
            else document.runs.Add(new RtfRun(value, format) { Paragraph = paragraph });
            text.Clear();
        }
        for (var index = 0; index < source.Length; index++)
        {
            var c = source[index];
            if (c is '\r' or '\n') continue;
            if (c == '{' && source.AsSpan(index).StartsWith(@"{\pntext", StringComparison.Ordinal))
            {
                if (hasBulletGroups) throw Unsupported("multiple bullet paragraphs");
                hasBulletGroups = true;
                var bullet = Regex.Match(source[index..], @"^\{\\pntext\\f(?<font>\d+)\\'B7\\tab\}\{\\\*\\pn\\pnlvlblt\\pnf\k<font>\\pnindent0\{\\pntxtb\\'B7\}\}");
                if (!bullet.Success) throw Unsupported("bullet groups");
                var fontId = int.Parse(bullet.Groups["font"].Value, CultureInfo.InvariantCulture);
                if (!document.fonts.Any(f => f.Id == fontId && f.Name == "Symbol" && f.Charset == 2))
                    throw Unsupported("bullet font");
                Flush();
                paragraph = paragraph with { BulletFont = fontId, Controls = paragraph.Controls + bullet.Value };
                index += bullet.Length - 1;
                continue;
            }
            if (c == '{') { Flush(); stack.Push((format, paragraph)); continue; }
            if (c == '}') { Flush(); (format, paragraph) = stack.Pop(); continue; }
            if (c != '\\') { text.Append(c); continue; }
            c = source[++index];
            if (c is '\\' or '{' or '}') { text.Append(c); continue; }
            if (c == '\'')
            {
                if (index + 2 >= source.Length || !byte.TryParse(source.AsSpan(index + 1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
                    throw Unsupported("hex escape");
                text.Append(Ansi.GetString([value])); index += 2; continue;
            }
            var start = index;
            while (index < source.Length && char.IsAsciiLetter(source[index])) index++;
            var word = source[start..index];
            start = index;
            if (index < source.Length && source[index] == '-') index++;
            while (index < source.Length && char.IsAsciiDigit(source[index])) index++;
            int? number = index > start ? int.Parse(source[start..index], CultureInfo.InvariantCulture) : null;
            if (index >= source.Length || source[index] != ' ') index--;
            Flush();
            if (word == "pard") paragraph = new RtfParagraphFormat();
            if (word is "ql" or "qc" or "qr" or "li" or "ri" or "fi" or "tx")
            {
                paragraph = paragraph.WithControl(word, number);
                continue;
            }
            var enabled = number != 0;
            format = word switch
            {
                "b" => format with { Bold = enabled },
                "i" => format with { Italic = enabled },
                "ul" => format with { Underline = enabled },
                "ulnone" => format with { Underline = false },
                "strike" => format with { StrikeThrough = enabled },
                "protect" => format with { Protected = enabled },
                "fs" when number > 0 => format with { HalfPoints = number.Value, PointSize = number.Value / 2f },
                "f" when document.fonts.Any(f => f.Id == number && f.Charset is null or 0) => format with { FontName = document.fonts.Single(f => f.Id == number).Name },
                "cf" when number >= 0 && number < document.colors.Count => format with { Color = document.colors[number.Value] },
                "rtf" when number == 1 => format,
                "ansi" => format,
                "deff" when number == 0 => format,
                "viewkind" when number == 4 => format,
                "uc" when number == 1 => format,
                "lang" when number == 1033 => format,
                "pard" => format,
                "tab" => format,
                "par" => format,
                _ => throw Unsupported($"control word '{word}'")
            };
            if (word == "tab") text.Append('\t');
            if (word == "par") text.Append("\r\n");
        }
        Flush();
        // The final rich-edit paragraph mark belongs to the document, not Text.
        if (document.runs.Count > 0 && document.runs[^1].Text.EndsWith("\r\n", StringComparison.Ordinal))
        {
            var last = document.runs[^1];
            document.runs[^1] = last with { Text = last.Text[..^2] };
            if (document.runs[^1].Text.Length == 0) document.runs.RemoveAt(document.runs.Count - 1);
        }
        return document;
    }

    internal static RtfFormattedDocument FromRuns(IEnumerable<RtfRun> source, RtfFormattedDocument? basis = null)
    {
        var document = new RtfFormattedDocument();
        if (basis != null)
        {
            document.fonts.AddRange(basis.fonts);
            document.colors.Clear();
            document.colors.AddRange(basis.colors);
        }
        foreach (var run in source)
        {
            if (!document.fonts.Any(f => f.Name == run.Format.FontName))
            {
                var id = 0;
                while (document.fonts.Any(f => f.Id == id)) id++;
                // Only these new-face table entries are recorded; incoming entries retain their own metadata.
                document.fonts.Add(new Font(id, run.Format.FontName, 0,
                    run.Format.FontName is "MS Sans Serif" or "Courier New"));
            }
            if (!document.colors.Contains(run.Format.Color)) document.colors.Add(run.Format.Color);
            if (document.runs.Count > 0 && document.runs[^1].Format == run.Format
                && document.runs[^1].Paragraph == run.Paragraph)
                document.runs[^1] = run with { Text = document.runs[^1].Text + run.Text };
            else document.runs.Add(run);
        }
        return document;
    }

    public string WriteSelection(int start, int length)
    {
        var selected = new List<RtfRun>();
        var offset = 0;
        foreach (var run in runs)
        {
            var from = Math.Max(start - offset, 0);
            var to = Math.Min(start + length - offset, run.Text.Length);
            if (to > from) selected.Add(run with { Text = run.Text[from..to] });
            offset += run.Text.Length;
        }
        if (selected.Count != 1 || selected[0].Paragraph.Controls.Length != 0 ||
            selected[0].Text.Contains('\r') || selected[0].Text.Contains('\n'))
            throw Unsupported("mixed-format or paragraph selection output");
        var selection = new RtfFormattedDocument();
        var font = fonts.Single(f => f.Name == selected[0].Format.FontName);
        if (font.Charset != 0) throw Unsupported("selection font charset normalisation");
        selection.fonts.Add(font with { Id = 0 });
        if (selected[0].Format.Color != 0) selection.colors.Add(selected[0].Format.Color);
        selection.runs.Add(selected[0]);
        return selection.Write(true);
    }

    public string Write(bool selection = false)
    {
        var output = new StringBuilder(@"{\rtf1\ansi\deff0{\fonttbl");
        foreach (var font in fonts)
        {
            if (!font.CanWrite || font.Name.Any(c => c < 32 || c > 126 || c is '\\' or '{' or '}' or ';'))
                throw Unsupported("font name encoding or new-face table metadata");
            output.Append(@"{\f").Append(font.Id).Append(@"\fnil");
            if (font.Charset is { } charset) output.Append(@"\fcharset").Append(charset);
            output.Append(' ').Append(font.Name).Append(";}");
        }
        output.Append("}\r\n");
        if (colors.Count > 1)
        {
            output.Append(@"{\colortbl ;");
            foreach (var color in colors.Skip(1))
                output.Append(@"\red").Append(color & 255).Append(@"\green").Append(color >> 8 & 255).Append(@"\blue").Append(color >> 16 & 255).Append(';');
            output.Append("}\r\n");
        }
        output.Append(selection ? @"\uc1\pard" : @"\viewkind4\uc1\pard");
        var previous = new RtfCharacterFormat(FontName: "", HalfPoints: 0);
        var previousParagraph = "";
        var first = true;
        foreach (var run in runs)
        {
            var current = run.Format;
            if (current.Color > 0xffffff || current.PointSize == 1638.35f)
                throw Unsupported("selection values without a measured RTF conversion");
            var changed = false;
            void Control(string word) { output.Append('\\').Append(word); changed = true; }
            if (run.Paragraph.Controls != previousParagraph)
            {
                if (previousParagraph.Length > 0) Control("pard");
                output.Append(run.Paragraph.Controls);
                changed = true;
                previousParagraph = run.Paragraph.Controls;
            }
            if (first && !selection) Control("lang1033");
            if (current.Color != previous.Color) Control("cf" + colors.IndexOf(current.Color).ToString(CultureInfo.InvariantCulture));
            if (first && selection) Control("lang1033");
            first = false;
            if (current.Underline != previous.Underline) Control(current.Underline ? "ul" : "ulnone");
            if (current.Bold != previous.Bold) Control(current.Bold ? "b" : "b0");
            if (current.Italic != previous.Italic) Control(current.Italic ? "i" : "i0");
            if (current.StrikeThrough != previous.StrikeThrough) Control(current.StrikeThrough ? "strike" : "strike0");
            if (current.Protected != previous.Protected) Control(current.Protected ? "protect" : "protect0");
            if (current.FontName != previous.FontName) Control("f" + fonts.Single(f => f.Name == current.FontName).Id.ToString(CultureInfo.InvariantCulture));
            if (current.HalfPoints != previous.HalfPoints) Control("fs" + current.HalfPoints.ToString(CultureInfo.InvariantCulture));
            if (changed) output.Append(' ');
            foreach (var value in Ansi.GetBytes(run.Text))
            {
                switch (value)
                {
                    case 13: break;
                    case 10: output.Append("\r\n\\par "); break;
                    case 9: output.Append(@"\tab "); break;
                    case (byte)'\\': case (byte)'{': case (byte)'}': output.Append('\\').Append((char)value); break;
                    case >= 128: output.Append(@"\'").Append(value.ToString("x2", CultureInfo.InvariantCulture)); break;
                    case < 32: throw Unsupported("control character");
                    default: output.Append((char)value); break;
                }
            }
            previous = current;
        }
        return output.Append(selection ? "}\r\n" : "\r\n\\par }\r\n").ToString();
    }

    private static NotImplementedException Unsupported(string detail) => new($"RichTextBox RTF: {detail} is not implemented (#1100)");
}
