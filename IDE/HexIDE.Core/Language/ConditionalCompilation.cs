// SPDX-License-Identifier: MIT
// Copyright (C) 2026 The HexIDE Authors
// Compiled into HexIDE.Core and, by link, into the language server, so the interpreter and the server read a module's
// directives with one scanner (#1112). It depends on nothing but the BCL; each side brings its own evaluator.

using System.Text;
using System.Text.RegularExpressions;

namespace HexIDE.Language;

/// <summary>A module's text with its conditional compilation applied (#1112).</summary>
/// <param name="Text">
/// The text a parser reads: every directive line, and every line of an inactive branch, made empty, so the lines keep their
/// numbers and their endings. It exists only in memory: nothing writes it back, and the code window shows the module as written.
/// </param>
/// <param name="InactiveLines">The inactive branches' lines, 1-based and inclusive, which the code window can grey (#1113).</param>
/// <param name="UndeterminedLines">
/// Whole <c>#If</c> blocks, from <c>#If</c> to <c>#End If</c>, whose branch the evaluator could not choose. Their first
/// branch is the one kept, so a parser has something to read, but nothing there can be said to be compiled or not.
/// The interpreter's evaluator always chooses, so it never reports one.
/// </param>
public sealed record PreprocessedSource(string Text, IReadOnlyList<LineRange> InactiveLines, IReadOnlyList<LineRange> UndeterminedLines);

/// <summary>Lines <see cref="First"/> to <see cref="Last"/>, 1-based and inclusive.</summary>
public readonly record struct LineRange(int First, int Last)
{
    public bool Contains(int line) => line >= First && line <= Last;
}

/// <summary>A directive's error, at the 1-based line it is on: a compile error to vb6.exe.</summary>
public sealed class ConditionalCompilationException(string message, int line) : Exception(message)
{
    public int Line { get; } = line;
}

/// <summary>An evaluator's error with no line yet: <see cref="ConditionalCompilation"/> gives it the directive's line.</summary>
public sealed class DirectiveEvaluationException(string message) : Exception(message);

/// <summary>
/// How a directive's expression is read and valued. The interpreter's reads it with its own parser and values it with its own
/// operators; the language server's reads with its grammar and answers what it can.
/// </summary>
public interface IConstantEvaluator<TExpression, TValue> where TExpression : class
{
    /// <summary>The expression <paramref name="text"/> holds, or null where it holds none, so the line is not a directive.</summary>
    TExpression? Parse(string text);

    /// <summary>The expression's value. An error is a <see cref="DirectiveEvaluationException"/>.</summary>
    TValue Evaluate(TExpression expression, Func<string, TValue> lookup);

    /// <summary>Whether a condition holds, or null where the evaluator cannot tell.</summary>
    bool? Holds(TValue condition);

    /// <summary>A value as a <c>#Const</c> keeps it: a Variant, in VB6.</summary>
    TValue Constant(TValue value);

    /// <summary>A name no <c>#Const</c> above the line defines: a project argument, a built-in, or neither.</summary>
    TValue Name(string name);
}

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
public static partial class ConditionalCompilation
{
    /// <summary>vb6.exe's messages for a directive out of place (measured).</summary>
    public const string NoIf = "An # ElseIf, # Else, or # EndIf must be preceded by an # If clause";
    public const string ElseIfAfterElse = "An # ElseIf must be preceded by an # If or # ElseIf and followed by an # Else or # EndIf";
    public const string ElseAfterElse = "An # Else clause must be preceded by an # If";
    public const string Unterminated = "You must terminate the # If block with an # EndIf";
    public const string DuplicateDefinition = "Duplicate definition";

    /// <summary>
    /// <paramref name="source"/> with its conditional compilation applied, its expressions valued by <paramref name="evaluator"/>.
    /// A directive's error is a <see cref="ConditionalCompilationException"/> at its line.
    /// </summary>
    public static PreprocessedSource Preprocess<TExpression, TValue>(string source, IConstantEvaluator<TExpression, TValue> evaluator)
        where TExpression : class
    {
        if (!source.Contains('#'))
            return new PreprocessedSource(source, [], []);
        var lines = PhysicalLines(source);
        var blank = new bool[lines.Count];
        var inactive = new List<LineRange>();
        var undetermined = new List<LineRange>();
        var defined = new Dictionary<string, TValue>(StringComparer.OrdinalIgnoreCase);
        var open = new Stack<Branch>();
        var any = false;

        TValue Lookup(string name) => defined.TryGetValue(name, out var value) ? value : evaluator.Name(name);

        TValue Evaluate(TExpression expression, int line)
        {
            try
            {
                return evaluator.Evaluate(expression, Lookup);
            }
            catch (DirectiveEvaluationException e)
            {
                throw new ConditionalCompilationException(e.Message, line);
            }
        }

        bool? Holds(TExpression expression, int line)
        {
            var value = Evaluate(expression, line);
            try
            {
                return evaluator.Holds(value);
            }
            catch (DirectiveEvaluationException e)
            {
                throw new ConditionalCompilationException(e.Message, line);
            }
        }

        for (var first = 0; first < lines.Count;)
        {
            var last = first;
            while (last + 1 < lines.Count && Continues(lines[last].Text))
                last++;
            var active = open.Count == 0 || open.Peek().Active;
            var line = first + 1;
            var directive = lines[first].Text.TrimStart().StartsWith('#')
                ? Directive<TExpression>.Read(Joined(lines, first, last), evaluator)
                : null;
            switch (directive)
            {
                case { Kind: DirectiveKind.If } d:
                {
                    var holds = Holds(d.Expression!, line);
                    // A condition nobody can value keeps its branch, so a parser has something to read, and marks the block.
                    open.Push(new Branch(active, line)
                        { Active = active && holds != false, Taken = holds != false, Undetermined = holds is null });
                    break;
                }
                case { Kind: DirectiveKind.ElseIf } d:
                {
                    if (open.Count == 0)
                        throw new ConditionalCompilationException(NoIf, line);
                    var branch = open.Peek();
                    if (branch.SeenElse)
                        throw new ConditionalCompilationException(ElseIfAfterElse, line);
                    var holds = Holds(d.Expression!, line);
                    branch.Active = branch.ParentActive && !branch.Taken && holds != false;
                    branch.Taken |= holds != false;
                    branch.Undetermined |= holds is null;
                    break;
                }
                case { Kind: DirectiveKind.Else }:
                {
                    if (open.Count == 0)
                        throw new ConditionalCompilationException(NoIf, line);
                    var branch = open.Peek();
                    if (branch.SeenElse)
                        throw new ConditionalCompilationException(ElseAfterElse, line);
                    branch.Active = branch.ParentActive && !branch.Taken;
                    branch.Taken = branch.SeenElse = true;
                    break;
                }
                case { Kind: DirectiveKind.EndIf }:
                {
                    if (open.Count == 0)
                        throw new ConditionalCompilationException(NoIf, line);
                    var branch = open.Pop();
                    if (branch.Undetermined)
                        undetermined.Add(new LineRange(branch.Line, last + 1));
                    break;
                }
                case { Kind: DirectiveKind.Const } d:
                {
                    var value = evaluator.Constant(Evaluate(d.Expression!, line));
                    if (!defined.TryAdd(d.Name!, value))
                        throw new ConditionalCompilationException(DuplicateDefinition, line);
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
            throw new ConditionalCompilationException(Unterminated, open.Peek().Line);
        if (!any)
            return new PreprocessedSource(source, [], []);

        var text = new StringBuilder(source.Length);
        for (var i = 0; i < lines.Count; i++)
            text.Append(blank[i] ? "" : lines[i].Text).Append(lines[i].Ending);
        return new PreprocessedSource(text.ToString(), inactive, undetermined);
    }

    // One #If's state: whether the branch reading now is the one selected, and whether an earlier branch already was.
    private sealed class Branch(bool parentActive, int line)
    {
        public bool ParentActive { get; } = parentActive;
        public int Line { get; } = line;
        public bool Active { get; set; }
        public bool Taken { get; set; }
        public bool SeenElse { get; set; }
        public bool Undetermined { get; set; }
    }

    private enum DirectiveKind { If, ElseIf, Else, EndIf, Const }

    private sealed record Directive<TExpression>(DirectiveKind Kind, TExpression? Expression = null, string? Name = null)
        where TExpression : class
    {
        // A logical line's directive, or null where vb6.exe reads the line as code.
        public static Directive<TExpression>? Read<TValue>(string line, IConstantEvaluator<TExpression, TValue> evaluator)
        {
            var text = WithoutComment(line).Trim();
            if (IfLine().Match(text) is { Success: true } ifLine)
                return Conditional(DirectiveKind.If, ifLine, evaluator);
            if (ElseIfLine().Match(text) is { Success: true } elseIfLine)
                return Conditional(DirectiveKind.ElseIf, elseIfLine, evaluator);
            if (ElseLine().IsMatch(text))
                return new Directive<TExpression>(DirectiveKind.Else);
            if (EndIfLine().IsMatch(text))
                return new Directive<TExpression>(DirectiveKind.EndIf);
            if (ConstLine().Match(text) is { Success: true } constLine
                && evaluator.Parse(constLine.Groups["e"].Value.Trim()) is { } value)
                return new Directive<TExpression>(DirectiveKind.Const, value, constLine.Groups["name"].Value);
            return null;
        }

        private static Directive<TExpression>? Conditional<TValue>(DirectiveKind kind, Match line,
            IConstantEvaluator<TExpression, TValue> evaluator) =>
            evaluator.Parse(line.Groups["e"].Value.Trim()) is { } condition ? new Directive<TExpression>(kind, condition) : null;
    }

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
}
