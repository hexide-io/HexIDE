using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Text.Json.Serialization;
using Antlr4.Runtime;
using Antlr4.Runtime.Misc;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// Writes each program a test runs, with HexIDE's result, as one JSON line: a corpus for running the same programs on
/// another VB6 engine, out of process, and comparing what each gives (#1368). Off unless <c>HEXIDE_DUMP_PROGRAMS</c> names a dump, which then goes to
/// <c>artifacts/program-dump/&lt;name&gt;.jsonl</c> under the repository root, a gitignored folder, so nothing lands beside
/// tracked files.
/// </summary>
internal sealed partial class ProgramDump
{
    public const string Variable = "HEXIDE_DUMP_PROGRAMS";

    /// <summary>The procedure the startup module's bare statements are moved into. HexIDE runs a module's bare statements,
    /// and an engine that starts only at a named, parameterless procedure can call this one.</summary>
    public const string Entry = "HexDumpMain";

    private static readonly object Gate = new();
    private static readonly JsonSerializerOptions Json = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
    private static readonly Lazy<ProgramDump?> Configured = new(() => ForName(Environment.GetEnvironmentVariable(Variable)));

    public ProgramDump(string path) => FilePath = path;

    public string FilePath { get; }

    /// <summary>The dump <c>HEXIDE_DUMP_PROGRAMS</c> names, read once, or null when it is unset or empty.</summary>
    public static ProgramDump? FromEnvironment => Configured.Value;

    /// <summary>The dump a name stands for. The name reaches a path, so it is held to the characters a session name may
    /// use, and anything else is refused rather than written somewhere it was not meant to go.</summary>
    public static ProgramDump? ForName(string? name)
    {
        if (string.IsNullOrEmpty(name))
            return null;
        if (!IsValidName(name))
            throw new InvalidOperationException(
                $"{Variable} names a dump file under artifacts/program-dump: lower case letters, digits, '.', '_' and '-', " +
                $"not starting with '.'. '{name}' is refused.");
        return new ProgramDump(Path.Combine(RepoRoot(), "artifacts", "program-dump", name + ".jsonl"));
    }

    public static bool IsValidName(string name) =>
        name.Length is > 0 and <= 64 && name[0] != '.' && !name.Contains("..") &&
        name.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '.' or '_' or '-');

    /// <summary>Appends one program and what it did. <paramref name="thrown"/> is whatever the run threw, if anything.</summary>
    public void Record(string test, int sequence, IReadOnlyList<(string Name, bool IsClass, string Source)> modules,
        IEnumerable<Vb6Value> log, string output, Exception? thrown)
    {
        var record = new Program(
            test,
            sequence,
            modules.Select((m, i) =>
            {
                if (m.IsClass)
                    return new Module(m.Name, "class", m.Source, null, null, null);
                var (wrapped, bare, refused) = Wrap(m.Source);
                return new Module(m.Name, i == 0 ? "startup" : "module", m.Source, wrapped, refused is null ? bare : null, refused);
            }).ToList(),
            // TypeName's answer, which a comparison's type pass reads.
            log.Select(v => new Printed(VB6BuiltIns.DebugTypeName(v), VbPrint.ItemText(v))).ToList(),
            OutputLines(output),
            VbLocale.Current.Name,
            thrown switch
            {
                null => null,
                VBRunTimeException e => new Failure(e.Error.ErrNo, e.Error.Description, nameof(VBRunTimeException)),
                _ => new Failure(null, thrown.Message, thrown.GetType().Name),
            });
        var line = JsonSerializer.Serialize(record, Json);
        lock (Gate)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.AppendAllText(FilePath, line + "\n", new UTF8Encoding(false));
        }
    }

    /// <summary>
    /// A standard module as an engine that starts only at a named procedure can run it: its declarations, then its
    /// procedures, then its bare statements moved in order into <see cref="Entry"/>, each with the line label or number in
    /// front of it. <c>bare</c> says whether there were any. The moved statements are not indented, so a label stays in
    /// the first column, where the source had it and where an engine may require it. A module-level <c>Dim</c>, <c>Const</c>, <c>Def</c>type, <c>Implements</c> or <c>Attribute</c> stays a
    /// declaration, so a procedure that reads it still can; anything nested inside a statement goes with that
    /// statement. Purely syntactic: the parse tree is read, never bound.
    /// <para>
    /// The interpreter parses a module only after its conditional compilation is applied, so the grammar's own directive
    /// rules are narrower than VB6's: <c>#ElseIf</c> without <c>Then</c> does not parse. Directive lines are therefore
    /// masked as remarks for this parse, which keeps every offset, and the text is cut from the source itself, so a
    /// directive inside a procedure goes with it. The wrapped module must hold the source's tokens, and where the parse
    /// failed, which a program vb6.exe refuses may well do, in the source's order too: a recovered parse can leave code out
    /// or misplace it without saying so. Otherwise the module is refused with the reason rather than written out in part
    /// (#2211). A directive at module level is refused this way, since it can't be moved into the entry procedure apart
    /// from the statements it governs.
    /// </para>
    /// </summary>
    internal static (string? Wrapped, bool Bare, string? Refused) Wrap(string source)
    {
        var errors = new FirstError();
        var input = new AntlrInputStream(new StringReader(Directive().Replace(source, m => m.Groups[1].Value + "'")));
        var lexer = new VB6Lexer(input);
        var parser = new VB6Parser(new CommonTokenStream(lexer));
        lexer.RemoveErrorListeners();
        parser.RemoveErrorListeners();
        lexer.AddErrorListener(errors);
        parser.AddErrorListener(errors);
        var module = parser.startRule().module();

        string Text(ParserRuleContext c) => source[c.Start.StartIndex..(c.Stop.StopIndex + 1)];

        var head = new StringBuilder();
        var declarations = new StringBuilder();
        var procedures = new StringBuilder();
        var entry = new StringBuilder();
        var body = module.moduleBody();
        if (body is null)
            return Checked(source, source + "\n", "", errors.Message);
        if (body.Start.StartIndex > 0)
            head.Append(source[..body.Start.StartIndex].TrimEnd()).Append('\n');

        foreach (var element in body.moduleBodyElement())
        {
            if (element.moduleBlock()?.block() is not { } block)
            {
                var isProcedure = element.subStmt() is not null || element.functionStmt() is not null
                    || element.propertyGetStmt() is not null || element.propertyLetStmt() is not null
                    || element.propertySetStmt() is not null;
                (isProcedure ? procedures : declarations).Append(Text(element)).Append('\n');
                continue;
            }
            string? label = null;
            foreach (var child in block.children)
            {
                if (child is VB6Parser.LineHeadContext lineHead)
                    label = Text(lineHead).TrimEnd();
                // A line number on a line of its own, or with only a remark, is a jump target too.
                else if (child is VB6Parser.EmptyLineNumberContext empty)
                    entry.Append(Text(empty.lineNumber())).Append('\n');
                else if (child is VB6Parser.BlockStmtContext statement)
                {
                    if (IsDeclaration(statement) && label is null)
                        declarations.Append(Text(statement)).Append('\n');
                    else
                        entry.Append(label is null ? "" : label + " ").Append(Text(statement)).Append('\n');
                    label = null;
                }
            }
        }

        var front = new StringBuilder().Append(head).Append(declarations).Append(procedures).ToString();
        return Checked(source, front, entry.ToString(), errors.Message);
    }

    /// <summary>
    /// <paramref name="front"/>, then the entry procedure holding <paramref name="entry"/>, if together they hold exactly the
    /// source's tokens: in any order where the source parsed, and in the source's own order where it did not
    /// (<paramref name="unparsed"/> says why not).
    /// </summary>
    private static (string? Wrapped, bool Bare, string? Refused) Checked(string source, string front, string entry, string? unparsed)
    {
        var expected = Tokens(source).ToList();
        var actual = Tokens(front).Concat(Tokens(entry)).ToList();
        var kept = unparsed is null
            ? expected.Order(StringComparer.Ordinal).SequenceEqual(actual.Order(StringComparer.Ordinal), StringComparer.Ordinal)
            : expected.SequenceEqual(actual, StringComparer.Ordinal);
        if (kept)
            return (front + $"Public Sub {Entry}()\n" + entry + "End Sub\n", entry.Length > 0, null);
        if (unparsed is not null)
            return (null, false, $"the source does not parse here, and wrapping it would change it: {unparsed}");
        var lost = expected.Except(actual, StringComparer.Ordinal).Take(3).ToList();
        var gained = actual.Except(expected, StringComparer.Ordinal).Take(3).ToList();
        return (null, false, "the wrapped module would not hold the source's code" +
            (lost.Count > 0 ? "; it would lose " + string.Join(" ", lost) : "") +
            (gained.Count > 0 ? "; it would gain " + string.Join(" ", gained) : ""));
    }

    /// <summary>The text of each token a statement is made of. Line breaks, spaces, colons and remarks are left out, since
    /// the wrapper is free to change those.</summary>
    private static IEnumerable<string> Tokens(string text)
    {
        var lexer = new VB6Lexer(new AntlrInputStream(new StringReader(text)));
        lexer.RemoveErrorListeners();
        for (var token = lexer.NextToken(); token.Type != TokenConstants.EOF; token = lexer.NextToken())
            if (token.Channel == TokenConstants.DefaultChannel && token.Type is not (VB6Lexer.NEWLINE or VB6Lexer.WS or VB6Lexer.COLON))
                yield return token.Text;
    }

    /// <summary>The start of a conditional-compilation directive's line, up to its <c>#</c>.</summary>
    [GeneratedRegex(@"^([ \t]*)#(?=[ \t]*(?:If|ElseIf|Else|End[ \t]*If|Const)\b)", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex Directive();

    private sealed class FirstError : IAntlrErrorListener<IToken>, IAntlrErrorListener<int>
    {
        public string? Message { get; private set; }

        public void SyntaxError(TextWriter output, IRecognizer recognizer, IToken offendingSymbol,
            int line, int charPositionInLine, string msg, RecognitionException e) => Message ??= $"line {line}, {msg}";

        public void SyntaxError(TextWriter output, IRecognizer recognizer, int offendingSymbol,
            int line, int charPositionInLine, string msg, RecognitionException e) => Message ??= $"line {line}, {msg}";
    }

    private static IReadOnlyList<string> OutputLines(string output)
    {
        var lines = output.ReplaceLineEndings("\n").Split('\n').ToList();
        if (lines[^1].Length == 0)
            lines.RemoveAt(lines.Count - 1);
        return lines;
    }

    private static bool IsDeclaration(VB6Parser.BlockStmtContext statement) => statement.GetChild(0) switch
    {
        // Static is a local's keyword, so a bare Static goes into the entry procedure, where it is legal.
        VB6Parser.VariableStmtContext variable => variable.STATIC() is null,
        VB6Parser.ConstStmtContext or VB6Parser.DeftypeStmtContext or VB6Parser.ImplementsStmtContext
            or VB6Parser.AttributeStmtContext => true,
        _ => false,
    };

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !(Directory.Exists(Path.Combine(dir.FullName, "IDE"))
                             && Directory.Exists(Path.Combine(dir.FullName, "LspServer"))))
            dir = dir.Parent;
        return dir?.FullName
            ?? throw new InvalidOperationException($"No repository root above {AppContext.BaseDirectory}, so {Variable} has nowhere to write.");
    }

    private sealed record Program(
        [property: JsonPropertyName("test")] string Test,
        [property: JsonPropertyName("sequence")] int Sequence,
        [property: JsonPropertyName("modules")] IReadOnlyList<Module> Modules,
        [property: JsonPropertyName("printed")] IReadOnlyList<Printed> Printed,
        [property: JsonPropertyName("lines")] IReadOnlyList<string> Lines,
        [property: JsonPropertyName("locale")] string Locale,
        [property: JsonPropertyName("error")] Failure? Error);

    private sealed record Module(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("kind")] string Kind,
        [property: JsonPropertyName("source")] string Source,
        [property: JsonPropertyName("wrapped")] string? Wrapped,
        [property: JsonPropertyName("bare")] bool? Bare,
        [property: JsonPropertyName("notWrapped")] string? NotWrapped);

    private sealed record Printed(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("text")] string Text);

    private sealed record Failure(
        [property: JsonPropertyName("number")] int? Number,
        [property: JsonPropertyName("message")] string Message,
        [property: JsonPropertyName("kind")] string Kind);
}
