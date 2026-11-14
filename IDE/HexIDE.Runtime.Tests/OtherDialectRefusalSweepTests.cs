using System.IO;
using System.Text;
using System.Text.Json;
using Antlr4.Runtime;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

// #3434's refusal checks tokens before parsing, so a rule that reaches too far refuses a whole classic module, which
// is worse than any syntax error. Every VB6 source this repository holds goes through it here, and none may be
// refused: the conformance cases vb6.exe accepted, and every module file in the tree (the corpus, the demos and the
// test fixtures), both whole and from its Attribute VB_Name line on. Each check runs both ways, before parsing and
// with the word operators the interpreter classifies only once the parser has failed.
public class OtherDialectRefusalSweepTests
{
    private static readonly string[] ModuleExtensions = [".bas", ".cls", ".frm", ".ctl", ".pag", ".dob", ".dsr"];
    private static readonly string[] SkippedFolders = ["bin", "obj", ".git", "node_modules", "TestResults"];

    [Fact]
    public void NoConformanceCaseVb6AcceptsIsRefused()
    {
        var dir = Path.Combine(RepoRoot(), "corpus", "conformance");
        var accepted = JsonSerializer.Deserialize<List<Verdict>>(File.ReadAllText(Path.Combine(dir, "results.json")),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!
            .Where(v => v.Actual == "legal").Select(v => v.Key).ToHashSet(StringComparer.Ordinal);
        var refused = new List<string>();
        var checkedCases = 0;
        foreach (var file in Directory.GetFiles(dir, "*.json").OrderBy(f => f))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            if (name is "results" or "audit") continue;
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            foreach (var el in doc.RootElement.GetProperty("cases").EnumerateArray())
            {
                var key = $"{name}/{el.GetProperty("id").GetString()}";
                var legal = el.TryGetProperty("measured", out var measured)
                    ? measured.GetProperty("actual").GetString() == "legal"
                    : accepted.Contains(key);
                if (!legal) continue;
                var code = el.GetProperty("code").EnumerateArray().Select(x => x.GetString()!).ToArray();
                var scope = el.TryGetProperty("scope", out var s) ? s.GetString() : "statement";
                var module = scope == "module"
                    ? string.Join("\r\n", code)
                    : "Sub Main()\r\n" + string.Join("\r\n", code) + "\r\nEnd Sub";
                checkedCases++;
                if (Refusal(module) is { } why) refused.Add($"{key}: {why}");
            }
        }

        checkedCases.Should().BeGreaterThan(200, "the conformance corpus should be present");
        refused.Should().BeEmpty();
    }

    [Fact]
    public void NoModuleFileInTheTreeIsRefused()
    {
        var root = RepoRoot();
        var files = ModuleFiles(new DirectoryInfo(root)).ToList();
        var refused = new List<string>();
        foreach (var file in files)
        {
            var text = File.ReadAllText(file.FullName, Encoding.Latin1);
            var at = text.IndexOf("Attribute VB_Name", StringComparison.OrdinalIgnoreCase);
            foreach (var source in at > 0 ? new[] { text, text[at..] } : new[] { text })
                if (Refusal(source) is { } why)
                    refused.Add($"{Path.GetRelativePath(root, file.FullName)}: {why}");
        }

        files.Count.Should().BeGreaterThan(150, "the corpus and the demos should be present");
        refused.Should().BeEmpty();
    }

    private static string? Refusal(string source)
    {
        foreach (var wordOperators in new[] { false, true })
        {
            var lexer = new VB6Lexer(new AntlrInputStream(new StringReader(source)));
            lexer.RemoveErrorListeners();
            try
            {
                OtherDialectRefusal.Check(NextCounterList.Tokens(lexer), source, wordOperators);
            }
            catch (VBCompileErrorException e)
            {
                return $"line {e.Line}{(wordOperators ? " (word operators)" : "")}: {e.Message.ReplaceLineEndings(" ")}";
            }
        }
        return null;
    }

    private static IEnumerable<FileInfo> ModuleFiles(DirectoryInfo dir)
    {
        foreach (var file in dir.EnumerateFiles())
            if (ModuleExtensions.Contains(file.Extension, StringComparer.OrdinalIgnoreCase))
                yield return file;
        foreach (var sub in dir.EnumerateDirectories())
            if (!SkippedFolders.Contains(sub.Name, StringComparer.OrdinalIgnoreCase))
                foreach (var file in ModuleFiles(sub))
                    yield return file;
    }

    private sealed record Verdict(string Key, string Area, string Actual);

    // The monorepo root, which holds both IDE and LspServer.
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !(Directory.Exists(Path.Combine(dir.FullName, "IDE"))
                             && Directory.Exists(Path.Combine(dir.FullName, "LspServer"))))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("the repository root");
    }
}
