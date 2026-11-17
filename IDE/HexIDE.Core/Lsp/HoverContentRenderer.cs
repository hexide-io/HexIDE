using HexIDE.Lsp.Messages;

namespace HexIDE.Lsp;

/// <summary>The hover renderer's supported wire formats and block model, shared with initialization.</summary>
public static class HoverContentRenderer
{
    public static IReadOnlyList<string> Formats { get; } = Array.AsReadOnly(new[] { "markdown", "plaintext" });

    public sealed record Block(string Text, string? Language = null, string? ListMarker = null,
        bool IsHeading = false, bool IsThematicBreak = false);

    public static IEnumerable<Block> Render(MarkupContent content)
    {
        if (content.Kind != "markdown")
        {
            yield return new(content.Value);
            yield break;
        }
        var lines = content.Value.ReplaceLineEndings("\n").Split('\n');
        var paragraph = new List<string>();
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var trimmed = line.TrimStart();
            var fence = trimmed.StartsWith("```") ? '`' : trimmed.StartsWith("~~~") ? '~' : '\0';
            var fenceLength = fence == '\0' ? 0 : trimmed.TakeWhile(c => c == fence).Count();
            var list = System.Text.RegularExpressions.Regex.Match(line, @"^\s*([-+*]|\d+\.)\s+(.*)$");
            var heading = System.Text.RegularExpressions.Regex.Match(line, @"^\s*#{1,6}(?:[ \t]+(.*)|$)");
            var thematicBreak = System.Text.RegularExpressions.Regex.IsMatch(line, @"^ *(?:-(?: *-){2,}|\*(?: *\*){2,}|_(?: *_){2,}) *$");
            if (fenceLength >= 3 || heading.Success || thematicBreak || list.Success || string.IsNullOrWhiteSpace(line))
            {
                if (paragraph.Count > 0)
                {
                    yield return new(string.Join(" ", paragraph));
                    paragraph.Clear();
                }
                if (fenceLength >= 3)
                {
                    var language = trimmed[fenceLength..].Trim();
                    var code = new List<string>();
                    while (++i < lines.Length)
                    {
                        var end = lines[i].Trim();
                        if (end.Length >= fenceLength && end.All(c => c == fence)) break;
                        code.Add(lines[i]);
                    }
                    yield return new(string.Join("\n", code), language);
                }
                else if (heading.Success)
                    yield return new(System.Text.RegularExpressions.Regex.Replace(heading.Groups[1].Value, @"[ \t]+#+[ \t]*$", "").Trim(), IsHeading: true);
                else if (thematicBreak)
                    yield return new("", IsThematicBreak: true);
                else if (list.Success)
                    yield return new(list.Groups[2].Value, ListMarker: list.Groups[1].Value);
            }
            else paragraph.Add(line);
        }
        if (paragraph.Count > 0) yield return new(string.Join(" ", paragraph));
    }
}
