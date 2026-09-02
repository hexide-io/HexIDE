using System.IO;
using System.Text.RegularExpressions;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// HexIDE carries TWO VB6 grammars — the interpreter's combined <c>VB6.g4</c> and the LSP server's split
/// <c>VisualBasic6Lexer.g4</c> + <c>VisualBasic6Parser.g4</c> — and they are not copies of each other.
///
/// <para>
/// They stay separate deliberately. The LSP grammar is kept close to proleap/grammars-v4 so its clean-room
/// fixes remain candidates to upstream (see <c>docs/vb6-grammar-fixes.md</c>); merging the two would
/// forfeit that. The interpreter's is shaped for execution instead.
/// </para>
///
/// <para>
/// What is NOT acceptable is drifting apart without noticing, because the two halves then disagree about
/// what VB6 is — and that reaches users. A real example: the LSP grammar had a <c>lineNumber</c> rule and
/// the interpreter's did not, so the editor accepted numeric line labels with no syntax error and the
/// module then failed to load on F5. This test exists so the next such gap is a build failure rather than
/// a bug report.
/// </para>
///
/// <para>
/// It compares rule INVENTORIES, not rule bodies. That is a deliberately weak check: it cannot tell
/// whether two same-named rules accept the same language. It is cheap, it has no false positives, and it
/// turns "did I need to mirror that change?" from a judgement call into a failing test. A corpus-based
/// conformance check — parse the same sources with both and compare accept/reject — is the stronger
/// follow-up.
/// </para>
/// </summary>
public class GrammarParityTests
{
    // Rules that legitimately exist on one side only. Every entry needs a reason, and a new one should be
    // added only after deciding the divergence is intended rather than to silence this test.
    private static readonly Dictionary<string, string> InterpreterOnly = new()
    {
        ["continueStmt"] = "A HexIDE grammar extension. VB6 has no Continue statement at all, so there is "
                         + "nothing for the LSP grammar to carry and nothing to upstream.",
    };

    private static readonly Dictionary<string, string> LspOnly = new()
    {
        ["integerLiteral"] = "The LSP grammar promotes literals to parser rules; the interpreter matches "
                           + "them as lexer tokens and reads the text directly.",
        ["doubleLiteral"]  = "As integerLiteral.",
        ["octalLiteral"]   = "As integerLiteral.",
    };

    [Fact]
    public void TheTwoGrammarsDeclareTheSameParserRules()
    {
        var root = RepoRoot();
        var interpreter = ParserRules(Path.Combine(root, "IDE", "HexIDE.Runtime", "Interpreter", "Grammar", "VB6.g4"));
        var lsp = ParserRules(Path.Combine(root, "LspServer", "HexIDE.VbLspServer", "Grammar", "VisualBasic6Parser.g4"));

        interpreter.Should().NotBeEmpty("the interpreter grammar should contain parser rules");
        lsp.Should().NotBeEmpty("the LSP grammar should contain parser rules");

        var onlyInterpreter = interpreter.Except(lsp).Except(InterpreterOnly.Keys).OrderBy(r => r).ToList();
        var onlyLsp = lsp.Except(interpreter).Except(LspOnly.Keys).OrderBy(r => r).ToList();

        onlyInterpreter.Should().BeEmpty(
            "a rule in the interpreter grammar but not the LSP one means the editor will report a syntax "
          + "error on code the interpreter can run. Either mirror it into "
          + "LspServer/HexIDE.VbLspServer/Grammar/VisualBasic6Parser.g4, or add it to InterpreterOnly here "
          + "with the reason it is deliberate. Unmirrored: {0}", string.Join(", ", onlyInterpreter));

        onlyLsp.Should().BeEmpty(
            "a rule in the LSP grammar but not the interpreter one means the editor accepts code that then "
          + "fails to PARSE at run time — the whole module, not one statement. That is how numeric line "
          + "labels went unnoticed. Either mirror it into IDE/HexIDE.Runtime/Interpreter/Grammar/VB6.g4, or "
          + "add it to LspOnly here with the reason. Unmirrored: {0}", string.Join(", ", onlyLsp));
    }

    [Fact]
    public void TheDocumentedExceptionsAreStillReal()
    {
        // A stale exception is as bad as a missing one: it silently permits a future divergence under a
        // reason that no longer applies. If a rule has since been mirrored, delete its entry.
        var root = RepoRoot();
        var interpreter = ParserRules(Path.Combine(root, "IDE", "HexIDE.Runtime", "Interpreter", "Grammar", "VB6.g4"));
        var lsp = ParserRules(Path.Combine(root, "LspServer", "HexIDE.VbLspServer", "Grammar", "VisualBasic6Parser.g4"));

        foreach (var (rule, why) in InterpreterOnly)
            (interpreter.Contains(rule) && !lsp.Contains(rule)).Should().BeTrue(
                "InterpreterOnly lists '{0}' ({1}), but it is no longer interpreter-only — remove the entry", rule, why);

        foreach (var (rule, why) in LspOnly)
            (lsp.Contains(rule) && !interpreter.Contains(rule)).Should().BeTrue(
                "LspOnly lists '{0}' ({1}), but it is no longer LSP-only — remove the entry", rule, why);
    }

    /// <summary>
    /// Parser-rule names declared by a .g4, with grammars-v4's keyword-escaping suffix normalised away —
    /// the LSP grammar spells them <c>type_</c> and <c>subscript_</c> where the interpreter's says
    /// <c>type</c> and <c>subscript</c>, which is a naming convention rather than a divergence.
    /// </summary>
    private static HashSet<string> ParserRules(string path)
    {
        File.Exists(path).Should().BeTrue("the grammar should be at {0}", path);
        var text = File.ReadAllText(path);
        text = Regex.Replace(text, @"/\*.*?\*/", "", RegexOptions.Singleline);
        text = Regex.Replace(text, @"//[^\r\n]*", "");

        // A parser rule is a lowercase-initial name at the start of a line whose next non-whitespace
        // character is the colon. Lexer rules start uppercase and are excluded by the pattern; alternatives
        // are excluded because they begin with '|'.
        //
        // The whitespace before the colon must be allowed to span several lines: the LSP grammar documents
        // some rules with comments BETWEEN the name and the colon, and those become blank lines once the
        // comments are stripped above. An earlier version permitted a single newline and silently missed
        // ifThenElseStmt, which this test then reported as a divergence that did not exist.
        var names = Regex.Matches(text, @"^[ \t]*([a-z][A-Za-z0-9_]*)\s*:", RegexOptions.Multiline)
                         .Select(m => m.Groups[1].Value);
        return names.Select(n => n.EndsWith('_') ? n[..^1] : n).ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>
    /// Walk up to the monorepo root, so this works from either CI job's working directory.
    ///
    /// Identified by holding BOTH halves rather than by a solution file: there is a <c>HexIDE.slnx</c> in
    /// the root AND in <c>IDE/</c>, so searching for that name stops one level too early and every
    /// subsequent path is wrong.
    /// </summary>
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !(Directory.Exists(Path.Combine(dir.FullName, "IDE"))
                             && Directory.Exists(Path.Combine(dir.FullName, "LspServer"))))
            dir = dir.Parent;
        dir.Should().NotBeNull("the repository root (the directory holding both IDE/ and LspServer/) should "
                             + "be findable from {0}", AppContext.BaseDirectory);
        return dir!.FullName;
    }
}
