using HexIDE.Lsp.Messages;

namespace HexIDE.Lsp;

/// <summary>The hover renderer's supported wire formats and block model, shared with initialization.</summary>
public static class HoverContentRenderer
{
    public static IReadOnlyList<string> Formats { get; } = Array.AsReadOnly(new[] { "markdown", "plaintext" });

    public sealed record Block(string Text, string? Language = null, string? ListMarker = null);

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
            if (fenceLength >= 3 || list.Success || string.IsNullOrWhiteSpace(line))
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
                else if (list.Success)
                    yield return new(list.Groups[2].Value, ListMarker: list.Groups[1].Value);
            }
            else paragraph.Add(line);
        }
        if (paragraph.Count > 0) yield return new(string.Join(" ", paragraph));
    }
}
