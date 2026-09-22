using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace HexIDE.Tests.Infrastructure;

/// <summary>
/// The MCP tools as their source declares them: name, description, parameters, reply type and the enums they
/// promise to describe. Read from <c>HexIdeTools.cs</c> as text because no test project references
/// <c>HexIDE.Desktop</c>, the approach <c>CommandLineDocumentationTests</c> takes with <c>ServerOptions.cs</c>.
/// </summary>
/// <remarks>
/// <para>
/// A text parser that meets a shape it does not expect must fail, not skip. Every guard built on this one
/// reads its output, so a declaration it silently drops drops out of all of them at once (hexide-io/HexIDE#549).
/// So it reports what it could not read in <see cref="Unparsed"/>, and <c>ToolSourceTests</c> fails on
/// anything there and on any count that disagrees with a plain count of the attribute in the source.
/// </para>
/// </remarks>
internal static class ToolSource
{
    internal sealed record Parameter(string Name, bool Optional, string Type = "");

    internal sealed record Tool(
        string Name,
        string Description,
        IReadOnlyList<Parameter> Parameters,
        IReadOnlyList<(string Enum, string[] NotRendered)> Enums,
        string ReplyType);

    /// <summary>A positional property of a reply record: the name a caller sees on the wire, and its C# type.</summary>
    internal sealed record Field(string Name, string Type);

    // An attribute list may name the tool attribute bare, qualified, or with its Attribute suffix, and carry
    // any arguments in any order; the name is taken from the arguments afterwards.
    private static readonly Regex ToolStart = new(
        @"(?<=[\[,]\s*)(?:global::)?(?:[\w.]+\.)?McpServerTool(?:Attribute)?\s*\((?<args>(?:[^()""]|""(?:[^""\\]|\\.)*"")*)\)");
    private static readonly Regex NameArgument = new(@"\bName\s*=\s*""(?<name>[^""]+)""");
    private static readonly Regex Literal = new(@"""(?<text>(?:[^""\\]|\\.)*)""");
    private static readonly Regex Describes = new(
        @"(?:global::)?(?:[\w.]+\.)?DescribesEnum(?:Attribute)?\s*\(\s*typeof\s*\(\s*(?:global::)?(?:[\w.]+\.)?(?<type>\w+)\s*\)(?<rest>(?:[^()""]|""(?:[^""\\]|\\.)*"")*)\)");
    private static readonly Regex DescriptionStart = new(@"(?<=[\[,]\s*)(?:[\w.]+\.)?Description(?:Attribute)?\s*\(");
    private static readonly Regex Signature = new(
        @"^[ \t]*public\s+(?:static\s+)?(?:async\s+)?(?:Task<(?<reply>\w+)>|(?<reply>\w+))\s+\w+\s*\(", RegexOptions.Multiline);

    private static readonly Regex RecordStart = new(@"\brecord\s+(?:class\s+|struct\s+)?(?<name>\w+)\s*\(");
    private static readonly Regex PropertyName = new(@"JsonPropertyName\s*\(\s*""(?<name>[^""]+)""");

    private static readonly Lazy<(IReadOnlyList<Tool> Tools, IReadOnlyList<string> Unparsed)> Parsed = new(Read);
    private static readonly Lazy<IReadOnlyDictionary<string, IReadOnlyList<Field>>> ParsedRecords = new(ReadRecords);

    public static IReadOnlyList<Tool> Tools => Parsed.Value.Tools;

    /// <summary>What the parser met and could not read, one line each. Empty is the only acceptable answer.</summary>
    public static IReadOnlyList<string> Unparsed => Parsed.Value.Unparsed;

    /// <summary>The raw source, for counts taken independently of the parser.</summary>
    public static string Source => File.ReadAllText(Path.Combine(RepoTree.Root(), "IDE", "HexIDE.Desktop", "Server", "HexIdeTools.cs"));

    /// <summary>
    /// The records in the server folder and in the automation driver whose nodes the tree tools return, by C#
    /// name, each with its fields as a caller sees them.
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<Field>> Records => ParsedRecords.Value;

    /// <summary>Every field any reply can carry. Looser than a tool's own reply; see <see cref="FieldsOf"/>.</summary>
    public static IReadOnlyList<string> ReplyFields =>
        Records.Values.SelectMany(f => f).Select(f => f.Name).Distinct().ToList();

    /// <summary>
    /// The records a field's type refers to: every identifier in it that names a reply record, so a list, an
    /// array or a nullable of one is followed as the record itself.
    /// </summary>
    public static IEnumerable<string> RecordsIn(string type) =>
        Regex.Matches(type, @"\w+").Select(m => m.Value).Where(Records.ContainsKey);

    /// <summary>The fields of one record, or null when no reply record has that name.</summary>
    public static IReadOnlyList<Field>? FieldsOf(string record) => Records.TryGetValue(record, out var fields) ? fields : null;

    private static readonly string[] ReplyFolders =
    [
        Path.Combine("IDE", "HexIDE.Desktop", "Server"),
        Path.Combine("IDE", "HexIDE", "Automation"),
    ];

    private static IReadOnlyDictionary<string, IReadOnlyList<Field>> ReadRecords()
    {
        var records = new Dictionary<string, IReadOnlyList<Field>>(StringComparer.Ordinal);
        foreach (var file in ReplyFolders.SelectMany(f => Directory.EnumerateFiles(Path.Combine(RepoTree.Root(), f), "*.cs")))
        {
            // Comments inside a parameter list are not parameters: before this, the words of a comment's last
            // line leaked into the field set as if a reply could carry them.
            var text = WithoutComments(File.ReadAllText(file));
            foreach (Match record in RecordStart.Matches(text))
                records[record.Groups["name"].Value] = PartsOf(text[(record.Index + record.Length - 1)..])
                    .Select(FieldOf)
                    .OfType<Field>()
                    .ToList();
        }
        return records;
    }

    /// <summary>
    /// A field as the wire spells it: its <c>JsonPropertyName</c> if it has one, otherwise the name as
    /// System.Text.Json's camel case writes it, which lower-cases a whole leading acronym (<c>VBTypeName</c> is
    /// <c>vbTypeName</c>, not <c>vBTypeName</c>). A <c>JsonIgnore</c>d property is not on the wire at all.
    /// </summary>
    internal static Field? FieldOf(string part)
    {
        var attributes = Regex.Match(part, @"^\s*(?:\[[^\]]*\]\s*)*").Value;
        if (attributes.Contains("JsonIgnore", StringComparison.Ordinal))
            return null;
        var declaration = part[attributes.Length..].Split('=', 2)[0].Trim();
        var at = declaration.LastIndexOfAny([' ', '\t', '\n', '\r']);
        var name = declaration[(at + 1)..];
        var type = declaration[..Math.Max(at, 0)].Trim();
        var wire = PropertyName.Match(attributes) is { Success: true } renamed
            ? renamed.Groups["name"].Value
            : JsonNamingPolicy.CamelCase.ConvertName(name);
        return new Field(wire, type);
    }

    private static (IReadOnlyList<Tool>, IReadOnlyList<string>) Read()
    {
        var source = WithoutComments(Source);
        var starts = ToolStart.Matches(source).ToList();
        var tools = new List<Tool>();
        var unparsed = new List<string>();
        for (var i = 0; i < starts.Count; i++)
        {
            var end = i + 1 < starts.Count ? starts[i + 1].Index : source.Length;
            var block = source[starts[i].Index..end];
            var line = source[..starts[i].Index].Count(c => c == '\n') + 1;

            if (NameArgument.Match(starts[i].Groups["args"].Value) is not { Success: true } name)
            {
                unparsed.Add($"HexIdeTools.cs:{line}: a tool attribute with no Name = \"...\"");
                continue;
            }
            if (Signature.Match(block) is not { Success: true } signature)
            {
                unparsed.Add($"HexIdeTools.cs:{line}: {name.Groups["name"].Value} has no public method signature this can read");
                continue;
            }

            // Only what sits between the attribute and the method belongs to the tool. An attribute placed
            // anywhere else is reported by the count check rather than credited to a neighbour.
            var header = block[..signature.Index];
            var enums = Describes.Matches(header)
                .Select(m => (m.Groups["type"].Value,
                    Literal.Matches(m.Groups["rest"].Value).Select(l => l.Groups["text"].Value).ToArray()))
                .ToList();
            var description = DescriptionOf(header);
            if (description.Length == 0)
                unparsed.Add($"HexIdeTools.cs:{line}: {name.Groups["name"].Value} has no [Description] this can read");

            tools.Add(new Tool(
                name.Groups["name"].Value,
                description,
                PartsOf(block[(signature.Index + signature.Length - 1)..]).Select(ParameterOf).OfType<Parameter>().ToList(),
                enums,
                signature.Groups["reply"].Value));
        }
        return (tools, unparsed);
    }

    /// <summary>The description's text: every literal in the attribute, joined as the compiler joins a `+` chain.</summary>
    private static string DescriptionOf(string header)
    {
        if (DescriptionStart.Match(header) is not { Success: true } start) return "";
        var text = new StringBuilder();
        var position = start.Index + start.Length;
        while (Literal.Match(header, position) is { Success: true } literal)
        {
            text.Append(Regex.Unescape(literal.Groups["text"].Value));
            position = literal.Index + literal.Length;
            if (!header[position..].TrimStart().StartsWith('+')) break;
        }
        return text.ToString();
    }

    /// <summary>
    /// A method parameter as a caller sees it: the C# name, which the MCP SDK sends unchanged. The
    /// <see cref="CancellationToken"/> the SDK supplies itself is not one.
    /// </summary>
    private static Parameter? ParameterOf(string part)
    {
        var declaration = Regex.Replace(part, @"^\s*(?:\[[^\]]*\]\s*)*", "").Trim();
        if (declaration.StartsWith("CancellationToken", StringComparison.Ordinal))
            return null;
        var beforeDefault = declaration.Split('=', 2);
        var words = beforeDefault[0].Trim().Split([' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries);
        return new Parameter(words[^1], beforeDefault.Length == 2, string.Join(" ", words[..^1]));
    }

    /// <summary>
    /// The comma-separated parts of the parenthesised list that <paramref name="text"/> opens with. Brackets and
    /// commas inside a string literal, such as a parameter's own <c>[Description]</c>, are not structure.
    /// </summary>
    private static List<string> PartsOf(string text)
    {
        var open = text.IndexOf('(');
        var parts = new List<string>();
        var start = open + 1;
        var depth = 0;
        for (var i = open; i < text.Length; i++)
        {
            if (text[i] == '"')
            {
                for (i++; i < text.Length && text[i] != '"'; i++)
                    if (text[i] == '\\') i++;
                continue;
            }
            if (text[i] is '(' or '<' or '[') depth++;
            else if (text[i] is ')' or '>' or ']') depth--;
            else if (text[i] == ',' && depth == 1) { parts.Add(text[start..i]); start = i + 1; }
            if (depth == 0)
            {
                if (text[start..i].Trim().Length > 0) parts.Add(text[start..i]);
                break;
            }
        }
        return parts;
    }

    /// <summary>The text with its <c>//</c> and <c>/* */</c> comments blanked, string literals left alone.</summary>
    internal static string WithoutComments(string text)
    {
        var result = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '"')
            {
                var verbatim = i > 0 && text[i - 1] == '@';
                result.Append(text[i]);
                for (i++; i < text.Length; i++)
                {
                    result.Append(text[i]);
                    if (!verbatim && text[i] == '\\' && i + 1 < text.Length) { result.Append(text[++i]); continue; }
                    if (text[i] != '"') continue;
                    if (verbatim && i + 1 < text.Length && text[i + 1] == '"') { result.Append(text[++i]); continue; }
                    break;
                }
            }
            else if (text[i] == '/' && i + 1 < text.Length && text[i + 1] == '/')
            {
                while (i < text.Length && text[i] != '\n') i++;
                if (i < text.Length) result.Append('\n');
            }
            else if (text[i] == '/' && i + 1 < text.Length && text[i + 1] == '*')
            {
                var close = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
                var end = close < 0 ? text.Length : close + 2;
                result.Append(new string(text[i..end].Where(c => c == '\n').ToArray()));
                i = end - 1;
            }
            else
                result.Append(text[i]);
        }
        return result.ToString();
    }
}
