using System.Text;
using System.Text.RegularExpressions;

namespace HexIDE.Runtime.Interpreter.ConditionalCompilation;

/// <summary>A module's text with its conditional compilation applied (#1112).</summary>
/// <param name="Text">
/// The text a parser reads: every directive line, and every line of an inactive branch, made empty, so the lines keep their
/// numbers and their endings. It exists only in memory: nothing writes it back, and the code window shows the module as written.
/// </param>
/// <param name="InactiveLines">The inactive branches' lines, 1-based and inclusive, which the code window can grey (#1113).</param>
public sealed record PreprocessedSource(string Text, IReadOnlyList<LineRange> InactiveLines);

/// <summary>Lines <see cref="First"/> to <see cref="Last"/>, 1-based and inclusive.</summary>
public readonly record struct LineRange(int First, int Last);

/// <summary>
/// VB6's conditional compilation, <c>#If</c>, <c>#ElseIf</c>, <c>#Else</c>, <c>#End If</c> and <c>#Const</c>, applied to a
/// module's text before it is parsed (#1112). Selecting a branch is collection, not binding: it reads constants and never
/// relates one symbol to another. Measured with <c>vb6.exe /make</c>, native and p-code alike (the oracle's "Conditional
/// compilation"):
/// <list type="bullet">
/// <item>A line is a directive only when it is well formed, indented or not, with an optional comment after: <c>#If e Then</c>,
/// <c>#ElseIf e Then</c>, <c>#Else</c>, <c>#End If</c> or <c>#EndIf</c>, or <c>#Const name = e</c>. Without its <c>Then</c>,
/// with a statement after <c>Then</c>, behind a line label, as <c>#Else whatever</c>, or with an expression that does not parse,
/// it is code: a syntax error in an active branch, skipped in an inactive one. A comment that ends in <c> _</c> swallows the
/// next line, a directive with it.</item>
/// <item>An inactive branch is never parsed, so it may hold anything, even after <c>End Sub</c>.</item>
/// <item>Every directive is evaluated where it stands, in an inactive branch too: a nested <c>#If Chr(65) = "A"</c> inside
/// <c>#If 0</c> is "Variable not defined".</item>
/// <item>A <c>#Const</c> takes no notice of <c>#If</c>: one in an inactive branch defines its constant all the same. It holds
/// from its line to the end of the module, across procedures, and is the module's alone. A second for the same name anywhere
/// in the module is "Duplicate definition". It may shadow a project argument or a built-in, and reads them first:
/// <c>#Const ARG = ARG + 1</c> with <c>ARG = 5</c> is 6.</item>
/// </list>
/// </summary>
public static partial class ConditionalCompiler
{
    /// <summary>vb6.exe's messages for a directive out of place (measured).</summary>
    internal const string NoIf = "An # ElseIf, # Else, or # EndIf must be preceded by an # If clause";
    internal const string ElseIfAfterElse = "An # ElseIf must be preceded by an # If or # ElseIf and followed by an # Else or # EndIf";
    internal const string ElseAfterElse = "An # Else clause must be preceded by an # If";
    internal const string Unterminated = "You must terminate the # If block with an # EndIf";

    /// <summary>
    /// <paramref name="source"/> with its conditional compilation applied under <paramref name="constants"/>. A directive's
    /// error is a compile error, raised here, as the module loads and before anything runs, naming
    /// <paramref name="moduleName"/> and the line.
    /// </summary>
    public static PreprocessedSource Preprocess(string source, CompilationConstants constants, string? moduleName = null)
    {
        if (!source.Contains('#'))
            return new PreprocessedSource(source, []);
        var lines = PhysicalLines(source);
        var blank = new bool[lines.Count];
        var inactive = new List<LineRange>();
        var defined = new Dictionary<string, Vb6Value>(StringComparer.OrdinalIgnoreCase);
        var open = new Stack<Branch>();
        var any = false;

        Vb6Value Lookup(string name) =>
            defined.TryGetValue(name, out var value) || constants.TryGet(name, out value) ? value : new Vb6Value(0).AsVariant();

        Vb6Value Evaluate(VB6Parser.ValueStmtContext expression, int line) =>
            Located(() => ConstantExpression.Evaluate(expression, Lookup), line, moduleName);

        bool Holds(VB6Parser.ValueStmtContext expression, int line) =>
            Located(() => ConstantExpression.Holds(ConstantExpression.Evaluate(expression, Lookup)), line, moduleName);

        for (var first = 0; first < lines.Count;)
        {
            var last = first;
            while (last + 1 < lines.Count && Continues(lines[last].Text))
                last++;
            var active = open.Count == 0 || open.Peek().Active;
            var line = first + 1;
            var directive = lines[first].Text.TrimStart().StartsWith('#') ? Directive.Read(Joined(lines, first, last)) : null;
            switch (directive)
            {
                case { Kind: DirectiveKind.If } d:
                {
                    var holds = Holds(d.Expression!, line);
                    open.Push(new Branch(active, line) { Active = active && holds, Taken = holds });
                    break;
                }
                case { Kind: DirectiveKind.ElseIf } d:
                {
                    if (open.Count == 0)
                        throw At(NoIf, line, moduleName);
                    var branch = open.Peek();
                    if (branch.SeenElse)
                        throw At(ElseIfAfterElse, line, moduleName);
                    var holds = Holds(d.Expression!, line);
                    branch.Active = branch.ParentActive && !branch.Taken && holds;
                    branch.Taken |= holds;
                    break;
                }
                case { Kind: DirectiveKind.Else }:
                {
                    if (open.Count == 0)
                        throw At(NoIf, line, moduleName);
                    var branch = open.Peek();
                    if (branch.SeenElse)
                        throw At(ElseAfterElse, line, moduleName);
                    branch.Active = branch.ParentActive && !branch.Taken;
                    branch.Taken = branch.SeenElse = true;
                    break;
                }
                case { Kind: DirectiveKind.EndIf }:
                    if (open.Count == 0)
                        throw At(NoIf, line, moduleName);
                    open.Pop();
                    break;
                case { Kind: DirectiveKind.Const } d:
                {
                    var value = Evaluate(d.Expression!, line);
                    if (!defined.TryAdd(d.Name!, value.AsVariant()))
                        throw At("Duplicate definition", line, moduleName);
                    break;
                }
            }
            if (directive is not null || !active)
            {
                any = true;
                for (var i = first; i <= last; i++)
                    blank[i] = true;
                if (directive is null)
                    Extend(inactive, first + 1, last + 1);
            }
            first = last + 1;
        }
        if (open.Count > 0)
            throw At(Unterminated, open.Peek().Line, moduleName);
        if (!any)
            return new PreprocessedSource(source, []);

        var text = new StringBuilder(source.Length);
        for (var i = 0; i < lines.Count; i++)
            text.Append(blank[i] ? "" : lines[i].Text).Append(lines[i].Ending);
        return new PreprocessedSource(text.ToString(), inactive);
    }

    // One #If's state: whether the branch reading now is the one selected, and whether an earlier branch already was.
    private sealed class Branch(bool parentActive, int line)
    {
        public bool ParentActive { get; } = parentActive;
        public int Line { get; } = line;
        public bool Active { get; set; }
        public bool Taken { get; set; }
        public bool SeenElse { get; set; }
    }

    private enum DirectiveKind { If, ElseIf, Else, EndIf, Const }

    private sealed partial record Directive(DirectiveKind Kind, VB6Parser.ValueStmtContext? Expression = null, string? Name = null)
    {
        // A logical line's directive, or null where vb6.exe reads the line as code.
        public static Directive? Read(string line)
        {
            var text = WithoutComment(line).Trim();
            if (IfLine().Match(text) is { Success: true } ifLine)
                return Conditional(DirectiveKind.If, ifLine);
            if (ElseIfLine().Match(text) is { Success: true } elseIfLine)
                return Conditional(DirectiveKind.ElseIf, elseIfLine);
            if (ElseLine().IsMatch(text))
                return new Directive(DirectiveKind.Else);
            if (EndIfLine().IsMatch(text))
                return new Directive(DirectiveKind.EndIf);
            if (ConstLine().Match(text) is { Success: true } constLine
                && ConstantExpression.TryParse(constLine.Groups["e"].Value.Trim()) is { } value)
                return new Directive(DirectiveKind.Const, value, constLine.Groups["name"].Value);
            return null;
        }

        private static Directive? Conditional(DirectiveKind kind, Match line) =>
            ConstantExpression.TryParse(line.Groups["e"].Value.Trim()) is { } condition ? new Directive(kind, condition) : null;

        [GeneratedRegex(@"^#If\b(?<e>.*?)\bThen$", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
        private static partial Regex IfLine();

        [GeneratedRegex(@"^#ElseIf\b(?<e>.*?)\bThen$", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
        private static partial Regex ElseIfLine();

        [GeneratedRegex(@"^#Else$", RegexOptions.IgnoreCase)]
        private static partial Regex ElseLine();

        [GeneratedRegex(@"^#End\s*If$", RegexOptions.IgnoreCase)]
        private static partial Regex EndIfLine();

        [GeneratedRegex(@"^#Const\s+(?<name>[A-Za-z][A-Za-z0-9_]*)\s*=(?<e>.*)$", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
        private static partial Regex ConstLine();
    }

    private readonly record struct PhysicalLine(string Text, string Ending);

    private static List<PhysicalLine> PhysicalLines(string source)
    {
        var lines = new List<PhysicalLine>();
        var start = 0;
        for (var i = 0; i < source.Length; i++)
        {
            if (source[i] != '\n')
                continue;
            var end = i > start && source[i - 1] == '\r' ? i - 1 : i;
            lines.Add(new PhysicalLine(source[start..end], source[end..(i + 1)]));
            start = i + 1;
        }
        if (start < source.Length)
            lines.Add(new PhysicalLine(source[start..], ""));
        return lines;
    }

    // Whether a line goes on to the next: it ends in a space and an underscore outside a string, a comment's included.
    private static bool Continues(string line)
    {
        var inString = false;
        foreach (var c in line)
        {
            if (c == '"')
                inString = !inString;
            else if (c == '\'' && !inString)
                break;
        }
        var trimmed = line.TrimEnd(' ', '\t');
        return !inString && trimmed.Length >= 2 && trimmed[^1] == '_' && trimmed[^2] is ' ' or '\t';
    }

    // A logical line's text, its continuations joined with a space where each underscore was.
    private static string Joined(List<PhysicalLine> lines, int first, int last)
    {
        if (first == last)
            return lines[first].Text;
        var joined = new StringBuilder();
        for (var i = first; i <= last; i++)
        {
            var text = lines[i].Text;
            joined.Append(i < last ? text.TrimEnd(' ', '\t')[..^1] : text).Append(i < last ? " " : "");
        }
        return joined.ToString();
    }

    // The line up to its comment: the first apostrophe outside a string.
    private static string WithoutComment(string line)
    {
        var inString = false;
        for (var i = 0; i < line.Length; i++)
        {
            if (line[i] == '"')
                inString = !inString;
            else if (line[i] == '\'' && !inString)
                return line[..i];
        }
        return line;
    }

    private static void Extend(List<LineRange> ranges, int first, int last)
    {
        if (ranges.Count > 0 && ranges[^1].Last == first - 1)
            ranges[^1] = ranges[^1] with { Last = last };
        else
            ranges.Add(new LineRange(first, last));
    }

    private static T Located<T>(Func<T> evaluate, int line, string? moduleName)
    {
        try
        {
            return evaluate();
        }
        catch (VBCompileErrorException e) when (e.Line is null)
        {
            const string prefix = "Compile error:\n\n";
            throw At(e.Message.StartsWith(prefix, StringComparison.Ordinal) ? e.Message[prefix.Length..] : e.Message, line, moduleName);
        }
    }

    private static VBCompileErrorException At(string message, int line, string? moduleName) =>
        new($"{message} in {(moduleName is null ? "" : moduleName + ", ")}line {line}") { Line = line };
}
