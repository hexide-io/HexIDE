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
    /// <param name="Description">The parameter's own <c>[Description]</c>, which the SDK sends in the input schema;
    /// empty when it has none.</param>
    internal sealed record Parameter(string Name, bool Optional, string Type = "", string Description = "");

    /// <param name="Body">
    /// The method's body as code: comments removed and the text of every string literal blanked, so a word
    /// inside a message is not mistaken for a use of it. An interpolation hole is code and is kept. Indices
    /// match nothing outside this string; the body is for searching, not for quoting.
    /// </param>
    internal sealed record Tool(
        string Name,
        string Description,
        IReadOnlyList<Parameter> Parameters,
        IReadOnlyList<(string Enum, string[] NotRendered)> Enums,
        string ReplyType,
        string Body = "")
    {
        /// <summary>Everything a caller reads about this tool: its description and each parameter's.</summary>
        public string AllText =>
            string.Join(" ", new[] { Description }.Concat(Parameters.Select(p => p.Description)).Where(t => t.Length > 0));
    }

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
        var code = CodeOnly(source);
        var constants = ConstantStrings(source);
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
            var description = DescriptionOf(header, constants, out var cut);
            if (description.Length == 0)
                unparsed.Add($"HexIdeTools.cs:{line}: {name.Groups["name"].Value} has no [Description] this can read");
            else if (cut is not null)
                unparsed.Add($"HexIdeTools.cs:{line}: {name.Groups["name"].Value}'s [Description] goes on past what this can read: {cut}");

            var body = BodyAt(code, starts[i].Index + signature.Index + signature.Length - 1);
            if (body is null)
                unparsed.Add($"HexIdeTools.cs:{line}: {name.Groups["name"].Value} has no method body this can read");

            tools.Add(new Tool(
                name.Groups["name"].Value,
                description,
                PartsOf(block[(signature.Index + signature.Length - 1)..])
                    .Select(part => ParameterOf(part, constants)).OfType<Parameter>().ToList(),
                enums,
                signature.Groups["reply"].Value,
                body ?? ""));
        }
        return (tools, unparsed);
    }

    /// <summary>
    /// The bodies of every method in the tool source with this name, overloads included, as code (see
    /// <see cref="Tool.Body"/>), for a guard on a helper the tools share.
    /// </summary>
    public static IReadOnlyList<string> MethodBodies(string method)
    {
        var code = CodeOnly(WithoutComments(Source));
        var declaration = new Regex(
            $@"^[ \t]*(?:(?:private|internal|public|protected|static|async)\s+)+[\w<>\[\]?.,]+(?:\s*\?)?\s+{Regex.Escape(method)}\s*\(",
            RegexOptions.Multiline);
        return declaration.Matches(code)
            .Select(m => BodyAt(code, m.Index + m.Length - 1))
            .OfType<string>()
            .ToList();
    }

    /// <summary>
    /// The body of the method whose parameter list opens at <paramref name="open"/>, in <paramref name="code"/>
    /// (see <see cref="CodeOnly"/>): what an expression body says up to its semicolon, or what lies between a block
    /// body's braces. Null when what follows the parameter list is neither.
    /// </summary>
    internal static string? BodyAt(string code, int open)
    {
        var close = Closing(code, open);
        if (close < 0) return null;
        var start = close + 1;
        while (start < code.Length && char.IsWhiteSpace(code[start])) start++;

        if (start + 1 < code.Length && code[start] == '=' && code[start + 1] == '>')
        {
            var depth = 0;
            for (var i = start + 2; i < code.Length; i++)
            {
                if (code[i] is '(' or '[' or '{') depth++;
                else if (code[i] is ')' or ']' or '}') depth--;
                else if (code[i] == ';' && depth == 0) return code[(start + 2)..i].Trim();
            }
            return null;
        }
        if (start < code.Length && code[start] == '{')
        {
            var end = Closing(code, start);
            return end < 0 ? null : code[(start + 1)..end];
        }
        return null;
    }

    /// <summary>
    /// Where the bracket at <paramref name="open"/> closes in <paramref name="code"/>, counting every kind of
    /// bracket, or -1 when it never does. Only meaningful in a <see cref="CodeOnly"/> text, where no literal can
    /// hold a bracket.
    /// </summary>
    internal static int Closing(string code, int open)
    {
        var depth = 0;
        for (var i = open; i < code.Length; i++)
        {
            if (code[i] is '(' or '[' or '{') depth++;
            else if (code[i] is ')' or ']' or '}' && --depth == 0) return i;
        }
        return -1;
    }

    /// <summary>
    /// <paramref name="text"/> with the contents of its string and character literals blanked, the quotes kept,
    /// and the length unchanged. An interpolation hole is code, so it is kept, and its own literals are blanked in
    /// turn. Expects comments to be gone already (see <see cref="WithoutComments"/>).
    /// </summary>
    internal static string CodeOnly(string text)
    {
        var result = text.ToCharArray();
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '"') i = BlankString(text, result, i);
            else if (text[i] == '\'') i = BlankChar(text, result, i);
        }
        return new string(result);
    }

    /// <summary>Blanks the string literal whose opening quote is at <paramref name="quote"/>; returns its closing quote.</summary>
    private static int BlankString(string text, char[] result, int quote)
    {
        var verbatim = false;
        var interpolated = false;
        for (var k = quote - 1; k >= 0 && k >= quote - 2 && text[k] is '@' or '$'; k--)
            if (text[k] == '@') verbatim = true; else interpolated = true;

        for (var i = quote + 1; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '"')
            {
                if (verbatim && i + 1 < text.Length && text[i + 1] == '"') { result[i] = result[++i] = ' '; continue; }
                return i;
            }
            if (!verbatim && c == '\\' && i + 1 < text.Length) { result[i] = result[++i] = ' '; continue; }
            if (interpolated && c == '{')
            {
                if (i + 1 < text.Length && text[i + 1] == '{') { result[i] = result[++i] = ' '; continue; }
                i = EndOfHole(text, result, i);
                continue;
            }
            if (c is not ('\n' or '\r')) result[i] = ' ';
        }
        return text.Length - 1;
    }

    /// <summary>Walks the interpolation hole opening at <paramref name="open"/>, blanking its literals; returns its closing brace.</summary>
    private static int EndOfHole(string text, char[] result, int open)
    {
        var depth = 0;
        for (var i = open; i < text.Length; i++)
        {
            if (text[i] == '"') i = BlankString(text, result, i);
            else if (text[i] == '\'') i = BlankChar(text, result, i);
            else if (text[i] == '{') depth++;
            else if (text[i] == '}' && --depth == 0) return i;
        }
        return text.Length - 1;
    }

    /// <summary>Blanks the character literal opening at <paramref name="quote"/>; returns its closing quote.</summary>
    private static int BlankChar(string text, char[] result, int quote)
    {
        for (var i = quote + 1; i < text.Length; i++)
        {
            if (text[i] == '\\' && i + 1 < text.Length) { result[i] = result[++i] = ' '; continue; }
            if (text[i] == '\'') return i;
            result[i] = ' ';
        }
        return text.Length - 1;
    }

    /// <summary>The description's text: every literal in the attribute, joined as the compiler joins a `+` chain.</summary>
    private static string DescriptionOf(string header, IReadOnlyDictionary<string, string> constants, out string? cut)
    {
        cut = null;
        if (DescriptionStart.Match(header) is not { Success: true } start) return "";
        return LiteralChainAt(header, start.Index + start.Length, constants, out cut);
    }

    private static readonly Regex Identifier = new(@"\G[A-Za-z_]\w*\b(?!\s*[.(])");

    /// <summary>
    /// The string literals and <c>const string</c>s starting at <paramref name="position"/>, joined across <c>+</c>.
    /// </summary>
    /// <remarks>
    /// A chain that goes on past something else, such as an interpolated string or a call, sets <paramref name="cut"/>
    /// to it, so the text is not taken for the whole: three tools end with <c>+ ProgramWaitNote</c>, and reading only
    /// up to it left 361 characters unmeasured and unchecked (#1258 review).
    /// </remarks>
    private static string LiteralChainAt(
        string text, int position, IReadOnlyDictionary<string, string> constants, out string? cut)
    {
        cut = null;
        var joined = new StringBuilder();
        while (true)
        {
            var at = text.Length - text[position..].TrimStart().Length;
            if (Literal.Match(text, at) is { Success: true } literal && literal.Index == at)
            {
                joined.Append(Regex.Unescape(literal.Groups["text"].Value));
                position = literal.Index + literal.Length;
            }
            else if (Identifier.Match(text, at) is { Success: true } name && constants.TryGetValue(name.Value, out var value))
            {
                joined.Append(value);
                position = at + name.Length;
            }
            else
            {
                if (joined.Length > 0)
                    cut = new string(text[at..].TakeWhile(c => c is not ('\r' or '\n')).Take(40).ToArray());
                break;
            }
            var rest = text[position..].TrimStart();
            if (!rest.StartsWith('+')) break;
            position = text.Length - rest.Length + 1;
        }
        return joined.ToString();
    }

    private static readonly Regex ConstantString = new(@"\bconst\s+string\s+(?<name>\w+)\s*=");
    private static readonly Regex DescriptionConstant = new(
        @"(?<=[\[,]\s*)(?:[\w.]+\.)?Description(?:Attribute)?\s*\(\s*(?<name>[A-Za-z_]\w*)\s*\)");

    /// <summary>Every <c>const string</c> in the file, so a description that names one can be read.</summary>
    private static IReadOnlyDictionary<string, string> ConstantStrings(string source) =>
        ConstantString.Matches(source).ToDictionary(
            m => m.Groups["name"].Value,
            m => LiteralChainAt(source, m.Index + m.Length, new Dictionary<string, string>(), out _),
            StringComparer.Ordinal);

    /// <summary>
    /// A method parameter as a caller sees it: the C# name, which the MCP SDK sends unchanged. The
    /// <see cref="CancellationToken"/> the SDK supplies itself is not one.
    /// </summary>
    private static Parameter? ParameterOf(string part, IReadOnlyDictionary<string, string> constants)
    {
        var description = DescriptionOf(part, constants, out _);
        if (description.Length == 0 && DescriptionConstant.Match(part) is { Success: true } named)
            description = constants.GetValueOrDefault(named.Groups["name"].Value, "");
        // Attributes are skipped with string literals respected, since a description may contain a bracket.
        var declaration = Regex.Replace(part, @"^\s*(?:\[(?:[^\]""]|""(?:[^""\\]|\\.)*"")*\]\s*)*", "").Trim();
        if (declaration.StartsWith("CancellationToken", StringComparison.Ordinal))
            return null;
        var beforeDefault = declaration.Split('=', 2);
        var words = beforeDefault[0].Trim().Split([' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries);
        return new Parameter(words[^1], beforeDefault.Length == 2, string.Join(" ", words[..^1]), description);
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
