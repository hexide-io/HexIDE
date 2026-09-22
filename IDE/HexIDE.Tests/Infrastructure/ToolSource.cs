using System.Text;
using System.Text.RegularExpressions;

namespace HexIDE.Tests.Infrastructure;

/// <summary>
/// The MCP tools as their source declares them: name, description, parameters and the enums they promise
/// to describe. Read from <c>HexIdeTools.cs</c> as text because no test project references
/// <c>HexIDE.Desktop</c>, the approach <c>CommandLineDocumentationTests</c> takes with <c>ServerOptions.cs</c>.
/// </summary>
internal static class ToolSource
{
    internal sealed record Parameter(string Name, bool Optional);

    internal sealed record Tool(
        string Name,
        string Description,
        IReadOnlyList<Parameter> Parameters,
        IReadOnlyList<(string Enum, string[] NotRendered)> Enums);

    private static readonly Regex ToolStart = new(@"\[McpServerTool\(Name = ""(?<name>[a-z_]+)""\)\]");
    private static readonly Regex Literal = new(@"""(?<text>(?:[^""\\]|\\.)*)""");
    private static readonly Regex Describes = new(@"\[DescribesEnum\(typeof\((?:[\w.]+\.)?(?<type>\w+)\)(?<rest>[^\]]*)\)\]");

    private static readonly Regex RecordStart = new(@"\brecord\s+\w+\s*\(");

    private static IReadOnlyList<Tool>? _tools;
    private static IReadOnlyList<string>? _replyFields;

    public static IReadOnlyList<Tool> Tools => _tools ??= Read();

    /// <summary>
    /// Every field a reply can carry, as a caller sees it: the positional properties of the records in the
    /// server folder and in the automation driver whose nodes the tree tools return, camel-cased, because the
    /// SDK serializes with the web defaults and nothing here overrides a name.
    /// </summary>
    public static IReadOnlyList<string> ReplyFields => _replyFields ??= ReadReplyFields();

    private static readonly string[] ReplyFolders =
    [
        Path.Combine("IDE", "HexIDE.Desktop", "Server"),
        Path.Combine("IDE", "HexIDE", "Automation"),
    ];

    private static IReadOnlyList<string> ReadReplyFields()
    {
        var fields = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in ReplyFolders.SelectMany(f => Directory.EnumerateFiles(Path.Combine(RepoTree.Root(), f), "*.cs")))
        {
            var text = File.ReadAllText(file);
            foreach (Match record in RecordStart.Matches(text))
                foreach (var parameter in ParametersOf(text[(record.Index + record.Length - 1)..]))
                    fields.Add(char.ToLowerInvariant(parameter.Name[0]) + parameter.Name[1..]);
        }
        return fields.ToList();
    }

    private static IReadOnlyList<Tool> Read()
    {
        var source = File.ReadAllText(Path.Combine(RepoTree.Root(), "IDE", "HexIDE.Desktop", "Server", "HexIdeTools.cs"));
        var starts = ToolStart.Matches(source).ToList();
        var tools = new List<Tool>();
        for (var i = 0; i < starts.Count; i++)
        {
            var end = i + 1 < starts.Count ? starts[i + 1].Index : source.Length;
            var block = source[starts[i].Index..end];
            var signatureAt = block.IndexOf("\n    public ", StringComparison.Ordinal);
            var header = block[..signatureAt];
            var enums = Describes.Matches(header)
                .Select(m => (m.Groups["type"].Value,
                    Literal.Matches(m.Groups["rest"].Value).Select(l => l.Groups["text"].Value).ToArray()))
                .ToList();
            tools.Add(new Tool(starts[i].Groups["name"].Value, DescriptionOf(header), ParametersOf(block[signatureAt..]), enums));
        }
        return tools;
    }

    /// <summary>The description's text: every literal in the attribute, joined as the compiler joins a `+` chain.</summary>
    private static string DescriptionOf(string header)
    {
        var at = header.IndexOf("[Description(", StringComparison.Ordinal);
        if (at < 0) return "";
        var text = new StringBuilder();
        var position = at;
        while (Literal.Match(header, position) is { Success: true } literal)
        {
            text.Append(Regex.Unescape(literal.Groups["text"].Value));
            position = literal.Index + literal.Length;
            if (!header[position..].TrimStart().StartsWith('+')) break;
        }
        return text.ToString();
    }

    /// <summary>
    /// The method's parameters as a caller sees them: the C# names, which the MCP SDK sends unchanged, less the
    /// <see cref="CancellationToken"/> the SDK supplies itself.
    /// </summary>
    private static IReadOnlyList<Parameter> ParametersOf(string signature)
    {
        var open = signature.IndexOf('(');
        var depth = 0;
        var close = open;
        for (var i = open; i < signature.Length; i++)
        {
            if (signature[i] is '(' or '<') depth++;
            else if (signature[i] is ')' or '>') depth--;
            if (depth == 0) { close = i; break; }
        }

        var list = signature[(open + 1)..close];
        var parts = new List<string>();
        var start = 0;
        depth = 0;
        for (var i = 0; i < list.Length; i++)
        {
            if (list[i] is '<' or '(') depth++;
            else if (list[i] is '>' or ')') depth--;
            else if (list[i] == ',' && depth == 0) { parts.Add(list[start..i]); start = i + 1; }
        }
        if (list[start..].Trim().Length > 0) parts.Add(list[start..]);

        return parts
            .Select(p => p.Trim())
            .Where(p => !p.StartsWith("CancellationToken", StringComparison.Ordinal))
            .Select(p =>
            {
                var beforeDefault = p.Split('=', 2);
                var name = beforeDefault[0].Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)[^1];
                return new Parameter(name, beforeDefault.Length == 2);
            })
            .ToList();
    }
}
