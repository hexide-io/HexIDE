using System.IO;
using System.Text.RegularExpressions;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// ANTLR evaluates a predicate at the left edge of a lexer rule's alternative at the start of every token, and it caches
/// no DFA edge computed through a predicate. One such predicate, in IDENTIFIER_CONTINUATION, made the interpreter's lexer
/// about 350 times slower and the language server's about 200 times. The interpreter's took 385 ms over a 489-line form,
/// which the editor re-lexes on every edit for bracket colouring (#2676). A predicate at the right edge runs only once
/// its alternative has matched, so it costs nothing on ordinary tokens. This reads the grammars rather than timing them,
/// so it cannot be flaky.
/// </summary>
public class LexerPredicatePlacementTests
{
    [Theory]
    [InlineData("IDE/HexIDE.Runtime/Interpreter/Grammar/VB6.g4")]
    [InlineData("LspServer/HexIDE.VbLspServer/Grammar/VisualBasic6Lexer.g4")]
    public void No_lexer_rule_starts_an_alternative_with_a_predicate(string grammar)
    {
        var path = Path.Combine(RepoRoot(), grammar);
        File.Exists(path).Should().BeTrue("the grammar should be at {0}", path);
        var text = WithoutMembersOrComments(File.ReadAllText(path));

        var predicates = Regex.Matches(text, @"\{[^{}]*\}\?");
        var leftEdge = new List<string>();
        foreach (Match predicate in predicates)
        {
            var before = text[..predicate.Index].TrimEnd();
            if (before.Length == 0 || before[^1] is not (':' or '|' or '(')) continue;
            var rule = Regex.Matches(before, @"(?m)^(?:fragment\s+)?([A-Za-z_]\w*)").LastOrDefault()?.Groups[1].Value;
            if (rule is { Length: > 0 } && char.IsUpper(rule[0])) leftEdge.Add(rule);
        }

        // Both grammars have predicates today, so a scan that found none would be reading the wrong thing.
        predicates.Count.Should().BePositive("{0} has predicates; finding none means the scan is broken", grammar);
        leftEdge.Should().BeEmpty(
            "a predicate at the left edge of a lexer alternative is evaluated at the start of every token and stops ANTLR "
          + "caching the lexer's DFA; move it to the alternative's right edge (#2676). Left-edge predicates in: {0}",
            string.Join(", ", leftEdge));
    }

    // The C# in @members and @lexer::members blocks has braces of its own, and comments can mention a predicate.
    private static string WithoutMembersOrComments(string text)
    {
        text = Regex.Replace(text, @"/\*.*?\*/", "", RegexOptions.Singleline);
        text = Regex.Replace(text, @"//[^\r\n]*", "");
        for (var at = Regex.Match(text, @"@(?:\w+::)?members\s*\{"); at.Success; at = Regex.Match(text, @"@(?:\w+::)?members\s*\{"))
        {
            var depth = 0;
            var end = at.Index + at.Length - 1;
            for (; end < text.Length; end++)
            {
                if (text[end] == '{') depth++;
                else if (text[end] == '}' && --depth == 0) break;
            }
            text = text[..at.Index] + text[Math.Min(end + 1, text.Length)..];
        }
        return text;
    }

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
