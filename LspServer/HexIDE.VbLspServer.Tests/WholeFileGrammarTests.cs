using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using HexIDE.VbLspServer;

namespace HexIDE.VbLspServer.Tests;

/// <summary>
/// Whether the bundled server's grammar accepts a WHOLE VB6 file — header and designer block included —
/// and not only the body the IDE's code window shows today.
///
/// <para>
/// The other half of this measurement lives in <c>HexIDE.Runtime.Tests/WholeFileGrammarTests.cs</c>, and
/// the duplication is deliberate: the two halves carry two grammars and do not reference each other, so a
/// shared harness would either merge them or make one half depend on the other. Both ask the same question
/// for hexide-io/HexIDE#273 — does prepending what load splits off introduce an error the body alone did
/// not have — and both classify the same three ways: an error in the prefix, an error that leaks into the
/// body only when the prefix is in front of it, and a body that already fails on its own.
/// </para>
/// </summary>
public class WholeFileGrammarTests
{
    private static readonly string ReportPath =
        Path.Join(Path.GetTempPath(), "hexide-whole-file-grammar-lsp.txt");

    private static readonly string[] Extensions = [".frm", ".cls", ".bas", ".ctl", ".pag"];

    /// <summary>
    /// Bodies that already fail, and why. Only the repository's own demos are held to this: the VB6
    /// template tree is on some machines and not others, so a list naming its files would pass or fail by
    /// accident of who ran it. The demos are everywhere, CI included, which is what makes them assertable.
    /// </summary>
    private static readonly Dictionary<string, string> KnownDemoBodyFailures = new(StringComparer.OrdinalIgnoreCase)
    {
        ["TideTable.bas"] = "spring-tide carries a deliberate syntax error — the demo exists to show a "
                          + "foreign server's diagnostics in HexIDE's editor (demo/README.md).",
        ["Class4.cls"]    = "hexide-io/HexIDE#482 — this grammar rejects a signed exponent with no decimal "
                          + "point, so `1E+30` is a syntax error here and is not one to the interpreter.",
    };

    private static IEnumerable<string> CorpusRoots()
    {
        var env = Environment.GetEnvironmentVariable("HEXIDE_ROUNDTRIP_CORPUS");
        if (!string.IsNullOrWhiteSpace(env))
            foreach (var p in env.Split(';', StringSplitOptions.RemoveEmptyEntries))
                if (Directory.Exists(p)) yield return p;

        var vb98 = Environment.GetEnvironmentVariable("VB6_TEMPLATES")
                   ?? @"C:\Program Files (x86)\Microsoft Visual Studio\VB98\Template";
        if (Directory.Exists(vb98)) yield return vb98;

        var demo = FindUpwards("demo");
        if (demo is not null) yield return demo;
    }

    private static string? FindUpwards(string folderName)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Join(dir.FullName, folderName);
            if (Directory.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        return null;
    }

    private static IReadOnlyList<string> CorpusFiles()
    {
        var files = new List<string>();
        foreach (var root in CorpusRoots())
            foreach (var ext in Extensions)
                files.AddRange(Directory.EnumerateFiles(root, "*" + ext, SearchOption.AllDirectories));
        return files.Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
    }

    [Fact]
    public void WholeFilesParseThroughTheServerGrammar()
    {
        var files = CorpusFiles();
        files.Should().NotBeEmpty("the corpus is the whole proof — an empty one passes vacuously");

        var report = new StringBuilder();
        var blocking = new List<string>();
        var preExisting = new List<string>();
        var newDemoFailures = new List<string>();
        var counts = new SortedDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var demoRoot = FindUpwards("demo");

        foreach (var path in files)
        {
            var ext = Path.GetExtension(path);
            counts[ext] = counts.TryGetValue(ext, out var n) ? n + 1 : 1;

            var whole = File.ReadAllText(path);
            var prefixLines = PrefixLineCount(path, whole);

            var (wholeDiagnostics, wholeTree) = VbDiagnosticsProvider.GetDiagnosticsAndTree(whole);
            wholeTree.Should().NotBeNull("a null tree would make an empty diagnostic list meaningless: " + path);

            if (wholeDiagnostics.Count == 0)
            {
                report.AppendLine("ok         " + path);
                continue;
            }

            var body = string.Join("\r\n", SplitLines(whole).Skip(prefixLines));
            var (bodyDiagnostics, _) = VbDiagnosticsProvider.GetDiagnosticsAndTree(body);
            if (bodyDiagnostics.Count > 0)
            {
                var note = "body line " + (bodyDiagnostics[0].Range.Start.Line + 1) + ": " + bodyDiagnostics[0].Message;
                preExisting.Add(path + "\n    " + note);
                report.AppendLine("pre-exist  " + path + " — " + note);
                if (demoRoot is not null
                    && path.StartsWith(demoRoot, StringComparison.OrdinalIgnoreCase)
                    && !KnownDemoBodyFailures.ContainsKey(Path.GetFileName(path)))
                    newDemoFailures.Add(path + "\n    " + note);
                continue;
            }

            var first = wholeDiagnostics[0];
            var line = first.Range.Start.Line + 1;
            var where = line <= prefixLines ? "in the prefix" : "leaked into the body";
            blocking.Add(path + "\n    whole line " + line + " (" + where + ", prefix is " + prefixLines
                         + " lines): " + first.Message);
            report.AppendLine("BLOCKING   " + path + " — whole line " + line + " " + where + ": " + first.Message);
        }

        var counted = string.Join(", ", counts.Select(c => c.Value + " " + c.Key));
        var header = files.Count + " files: " + counted + "\n"
                     + blocking.Count + " blocking, " + preExisting.Count + " pre-existing body failures\n\n";
        File.WriteAllText(ReportPath, header + report);

        blocking.Should().BeEmpty(
            "putting a file's own header in front of its body must not introduce a diagnostic the body "
            + "alone did not have (report: " + ReportPath + ").\n" + string.Join("\n", blocking));

        newDemoFailures.Should().BeEmpty(
            "a demo whose body this grammar cannot parse is a grammar gap — add it to "
            + nameof(KnownDemoBodyFailures) + " with an issue number once it is filed, or fix it.\n"
            + string.Join("\n", newDemoFailures));
    }

    private static string[] SplitLines(string text) => text.Replace("\r\n", "\n").Split('\n');

    /// <summary>
    /// How many lines the IDE keeps out of the code window today.
    ///
    /// <para>
    /// Re-derived here rather than shared, because the IDE's splitters (<c>ModuleFileFormat.SplitHeader</c>
    /// and <c>VbFrmFormatDeserializer</c>) are in the other half of the monorepo and the server does not
    /// reference it. It is a boundary count only — nothing downstream depends on it beyond deciding which
    /// side of the file a diagnostic landed on — so an approximation that follows the same rules is enough,
    /// and a divergence between the two would show up as a misreported side rather than a missed error.
    /// </para>
    /// </summary>
    private static int PrefixLineCount(string path, string whole)
    {
        var lines = SplitLines(whole);
        var ext = Path.GetExtension(path);
        var i = 0;

        if (ext.Equals(".frm", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".ctl", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".pag", StringComparison.OrdinalIgnoreCase))
        {
            // Down to the root designer End. BeginProperty/EndProperty are their own tokens and do not
            // change the Begin depth.
            var depth = 0;
            var started = false;
            for (; i < lines.Length; i++)
            {
                var t = lines[i].Trim();
                if (t.StartsWith("Begin ", StringComparison.OrdinalIgnoreCase))
                {
                    depth++;
                    started = true;
                }
                else if (t.Equals("End", StringComparison.OrdinalIgnoreCase))
                {
                    depth--;
                    if (started && depth == 0) { i++; break; }
                }
            }
            return started ? i : 0;
        }

        if (ext.Equals(".cls", StringComparison.OrdinalIgnoreCase)
            && i < lines.Length
            && lines[i].TrimStart().StartsWith("VERSION", StringComparison.OrdinalIgnoreCase)
            && lines[i].Contains("CLASS", StringComparison.OrdinalIgnoreCase))
        {
            i++;
            if (i < lines.Length && lines[i].Trim().Equals("BEGIN", StringComparison.OrdinalIgnoreCase))
            {
                i++;
                while (i < lines.Length && !lines[i].Trim().Equals("END", StringComparison.OrdinalIgnoreCase)) i++;
                if (i < lines.Length) i++;
            }
        }

        while (i < lines.Length && lines[i].TrimStart().StartsWith("Attribute ", StringComparison.OrdinalIgnoreCase))
            i++;

        return i;
    }
}
