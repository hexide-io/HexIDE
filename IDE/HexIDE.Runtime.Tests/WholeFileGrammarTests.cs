using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using HexIDE.Runtime.Interpreter;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// Whether the interpreter's grammar accepts a WHOLE VB6 file — header and designer block included — and
/// not only the body the code window shows today.
///
/// <para>
/// This is the corpus conformance check <see cref="GrammarParityTests"/> names as its stronger follow-up,
/// narrowed to the one question hexide-io/HexIDE#273 has to answer before it can put the whole file in the
/// code window: does prepending what load splits off introduce a syntax error that the body alone did not
/// have? A grammar that rejects a header is a reason to fix the grammar, not a reason to keep hiding the
/// header — so what this asserts is deliberately narrow.
/// </para>
///
/// <para>
/// Three outcomes, kept apart on purpose. A file whose BODY already fails is a pre-existing grammar gap:
/// recorded, not fatal here, because the composition did not cause it and #273 does not make it worse. A
/// file whose whole-file parse fails inside the prefix, or fails in the body only when the prefix is in
/// front of it (a context leak), is what blocks the change. Collapsing the three into "no diagnostics"
/// would let a body that already fails read as a composition defect, and would hide a leak behind a
/// pre-existing failure.
/// </para>
/// </summary>
public class WholeFileGrammarTests
{
    private static readonly string ReportPath =
        Path.Join(Path.GetTempPath(), "hexide-whole-file-grammar-interpreter.txt");

    /// <summary>
    /// The same roots as <c>SerializationCorpusTests</c>, PLUS the repository's own demo folder always.
    /// That differs deliberately: there, an explicit HEXIDE_ROUNDTRIP_CORPUS replaces the defaults, and a
    /// developer who points it at a VB6 install would silently stop covering the demos. Here the demos are
    /// the files hexide-io/HexIDE#273 re-composes, so they are never dropped.
    /// </summary>
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

        // The only .ctl and .pag this repository owns. Everything else with those extensions lives in a
        // VB6 install, which CI does not have — see corpus/designer/README.md.
        var designer = FindUpwards(Path.Join("corpus", "designer"));
        if (designer is not null) yield return designer;
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

    private static readonly string[] Extensions = [".frm", ".cls", ".bas", ".ctl", ".pag"];

    /// <summary>
    /// Bodies that already fail, and why. Only the corpus this repository owns is held to this: the VB6
    /// template tree is on some machines and not others, so a list naming its files would pass or fail by
    /// accident of who ran it. What is in the tree is everywhere, CI included, which is what makes it
    /// assertable.
    /// </summary>
    private static readonly Dictionary<string, string> KnownBodyFailures = new(StringComparer.OrdinalIgnoreCase)
    {
        ["TideTable.bas"] = "spring-tide carries a deliberate syntax error — the demo exists to show a "
                          + "foreign server's diagnostics in HexIDE's editor (demo/README.md).",
    };

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
    public void WholeFilesParseThroughTheInterpreterGrammar()
    {
        var files = CorpusFiles();
        files.Should().NotBeEmpty("the corpus is the whole proof — an empty one passes vacuously");

        var report = new StringBuilder();
        var blocking = new List<string>();
        var preExisting = new List<string>();
        var newRepoFailures = new List<string>();
        var unsplittable = new List<string>();
        var counts = new SortedDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var ownedRoots = new[] { FindUpwards("demo"), FindUpwards(Path.Join("corpus", "designer")) }
            .Where(r => r is not null).Select(r => r!).ToList();

        foreach (var path in files)
        {
            var ext = Path.GetExtension(path);
            counts[ext] = counts.TryGetValue(ext, out var n) ? n + 1 : 1;

            var whole = File.ReadAllText(path);
            var (prefix, body, split) = SplitAsLoadDoes(path, whole);
            if (!split)
                unsplittable.Add(path);

            var wholeError = Parse(whole);
            if (wholeError is null)
            {
                report.AppendLine("ok         " + path);
                continue;
            }

            var bodyError = Parse(body);
            if (bodyError is not null)
            {
                var note = "body line " + bodyError.Value.Line + ": " + First(bodyError.Value.Message);
                preExisting.Add(path + "\n    " + note);
                report.AppendLine("pre-exist  " + path + " — " + note);
                if (ownedRoots.Any(r => path.StartsWith(r, StringComparison.OrdinalIgnoreCase))
                    && !KnownBodyFailures.ContainsKey(Path.GetFileName(path)))
                    newRepoFailures.Add(path + "\n    " + note);
                continue;
            }

            var prefixLines = CountLines(prefix);
            var where = wholeError.Value.Line <= prefixLines ? "in the prefix" : "leaked into the body";
            blocking.Add(path + "\n    whole line " + wholeError.Value.Line + " (" + where + ", prefix is "
                         + prefixLines + " lines): " + First(wholeError.Value.Message));
            report.AppendLine("BLOCKING   " + path + " — whole line " + wholeError.Value.Line + " " + where
                              + ": " + First(wholeError.Value.Message));
        }

        var counted = string.Join(", ", counts.Select(c => c.Value + " " + c.Key));
        var header = files.Count + " files: " + counted + "\n"
                     + blocking.Count + " blocking, " + preExisting.Count + " pre-existing body failures, "
                     + unsplittable.Count + " with nothing split off\n\n";
        File.WriteAllText(ReportPath, header + report);

        blocking.Should().BeEmpty(
            "putting a file's own header in front of its body must not introduce a syntax error the body "
            + "alone did not have (report: " + ReportPath + ").\n" + string.Join("\n", blocking));

        newRepoFailures.Should().BeEmpty(
            "a file in this repository whose body the interpreter's grammar cannot parse is a grammar gap "
            + "— add it to " + nameof(KnownBodyFailures) + " with an issue number once it is filed, or fix "
            + "it.\n" + string.Join("\n", newRepoFailures));
    }

    /// <summary>
    /// What load keeps out of the code window today, and what it puts in. Designer files go through the
    /// production splitter rather than a re-derived one, so this measures the real boundary.
    /// </summary>
    private static (string Prefix, string Body, bool Split) SplitAsLoadDoes(string path, string whole)
    {
        var ext = Path.GetExtension(path);
        if (ext.Equals(".bas", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".cls", StringComparison.OrdinalIgnoreCase))
        {
            var kind = ext.Equals(".cls", StringComparison.OrdinalIgnoreCase)
                ? ModuleKind.ClassModule
                : ModuleKind.StandardModule;
            var (h, b) = ModuleFileFormat.SplitHeader(whole, kind);
            return (h, b, h.Length > 0);
        }

        try
        {
            var (_, code) = new VbFrmFormatDeserializer().Deserialize(whole);
            var prefix = whole.Length >= code.Length ? whole[..(whole.Length - code.Length)] : "";
            return (prefix, code, code.Length != whole.Length);
        }
        catch (Exception)
        {
            // A designer file the loader itself cannot read is a serialization question, not a grammar
            // one. Measure it whole against whole, which is what the composed buffer would be anyway.
            return ("", whole, false);
        }
    }

    /// <summary>
    /// The interpreter's own syntax check — the production path, error listeners and depth guard included,
    /// rather than a parser built here with different settings.
    /// </summary>
    private static (int Line, string Message)? Parse(string source)
    {
        try
        {
            new SyntaxChecker().Run(source);
            return null;
        }
        catch (VBCompileErrorException e)
        {
            return (e.Line ?? 0, e.Message);
        }
        catch (Exception e)
        {
            return (0, e.GetType().Name + ": " + e.Message);
        }
    }

    private static int CountLines(string s)
    {
        if (s.Length == 0) return 0;
        var n = 0;
        foreach (var c in s)
            if (c == '\n') n++;
        return s.EndsWith('\n') ? n : n + 1;
    }

    /// <summary>
    /// The message without its "Compile error:" banner. <c>VBCompileErrorException</c> puts that on its own
    /// line and the substance two lines below, so taking the first line reports the same six characters for
    /// every failure — which is exactly as useful as no message at all.
    /// </summary>
    private static string First(string message)
    {
        var parts = message.Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var body = parts.Where(p => !p.StartsWith("Compile error", StringComparison.OrdinalIgnoreCase)).ToList();
        return body.Count > 0 ? string.Join(" | ", body) : message;
    }
}
