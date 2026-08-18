using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using HexIDE.IDE;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// Corpus round-trip harness — the one specced in docs/TEST_PROJECTS.md and ROADMAP that was never built.
///
/// Loads real VB6-authored files, re-serializes them, and compares bytes. Any difference in a file HexIDE
/// did not author is a violation of the round-trip guarantee ("open any VB6 project, save it, lose nothing")
/// by definition — no oracle round-trip is needed to call it a defect, because VB6 wrote the input.
///
/// Corpus roots, in order of preference:
///   1. HEXIDE_ROUNDTRIP_CORPUS — ';'-separated absolute paths.
///   2. The VB6 install's Template tree (Windows dev machines only) — genuinely Microsoft-authored.
///   3. demo/ in this repo — always present, so the harness is never vacuous on CI.
///
/// CI is Linux and has no VB6 install, so absent roots are skipped rather than failed.
/// </summary>
public class SerializationCorpusTests
{
    private static readonly string ReportPath =
        Path.Join(Path.GetTempPath(), "hexide-roundtrip-report.txt");

    private static IEnumerable<string> CorpusRoots()
    {
        var env = Environment.GetEnvironmentVariable("HEXIDE_ROUNDTRIP_CORPUS");
        if (!string.IsNullOrWhiteSpace(env))
        {
            foreach (var p in env.Split(';', StringSplitOptions.RemoveEmptyEntries))
                if (Directory.Exists(p)) yield return p;
            yield break;
        }

        var vb98 = Environment.GetEnvironmentVariable("VB6_TEMPLATES")
                   ?? @"C:\Program Files (x86)\Microsoft Visual Studio\VB98\Template";
        if (Directory.Exists(vb98)) yield return vb98;

        var repoDemo = FindUpwards("demo");
        if (repoDemo is not null) yield return repoDemo;
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

    private static IReadOnlyList<string> CorpusFiles(params string[] extensions)
    {
        var files = new List<string>();
        foreach (var root in CorpusRoots())
            foreach (var ext in extensions)
                files.AddRange(Directory.EnumerateFiles(root, "*" + ext, SearchOption.AllDirectories));
        return files.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(f => f).ToList();
    }

    private sealed class Sink : IDeserializeErrorSink
    {
        public List<string> Errors { get; } = new();
        public void LogError(string error) => Errors.Add(error);
    }

    /// <summary>The module's real name, as VB6 records it — not the file name.</summary>
    private static string? ReadVbName(string source)
    {
        foreach (var line in source.Split('\n'))
        {
            var t = line.Trim();
            if (!t.StartsWith("Attribute VB_Name", StringComparison.OrdinalIgnoreCase)) continue;
            var open = t.IndexOf('"');
            var close = t.LastIndexOf('"');
            if (open >= 0 && close > open) return t.Substring(open + 1, close - open - 1);
        }
        return null;
    }

    /// <summary>
    /// A file HexIDE itself wrote proves nothing about VB6 fidelity — it round-trips because both ends
    /// share the same defects. Scoring those separately stops them flattering the headline number.
    /// </summary>
    private static bool IsHexIdeAuthored(string path) =>
        path.Replace('\\', '/').Contains("/demo/", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Every differing line, capped. Reporting only the *first* is actively misleading here: one
    /// systematic defect early in the file (a trailing space on the Begin line, say) masks every other
    /// difference in the corpus and makes many distinct bugs look like one.
    /// </summary>
    private static string? Differences(string expected, string actual, int max = 8)
    {
        if (expected == actual) return null;

        var e = expected.Replace("\r\n", "\n").Split('\n');
        var a = actual.Replace("\r\n", "\n").Split('\n');
        var lines = new List<string>();
        var shown = 0;
        var total = 0;

        for (var i = 0; i < Math.Max(e.Length, a.Length); i++)
        {
            var el = i < e.Length ? e[i] : "<missing>";
            var al = i < a.Length ? a[i] : "<missing>";
            if (el == al) continue;

            total++;
            if (shown++ < max)
                lines.Add($"      line {i + 1,-4} VB6: {Show(el)}\n               HexIDE: {Show(al)}");
        }

        if (total == 0) return "identical by line — differs only in line endings or trailing bytes";
        if (total > shown) lines.Add($"      … and {total - max} more differing line(s)");
        return $"{total} differing line(s)\n" + string.Join("\n", lines);

        static string Show(string s) => "«" + s.Replace("\t", "\\t") + "»";
    }

    [Fact]
    public void Forms_and_controls_round_trip_byte_for_byte()
    {
        var files = CorpusFiles(".frm", ".ctl");
        if (files.Count == 0) return; // no VB6 install and no demo/ — nothing to check (CI)

        var report = new StringBuilder();
        report.AppendLine($"Round-trip corpus report — {files.Count} form/control files");
        report.AppendLine(new string('=', 78));

        var failures = new List<string>();

        foreach (var path in files)
        {
            var name = Path.GetFileName(path);
            string original;
            try { original = Vb6TextFile.ReadAllText(path); }
            catch (Exception ex) { report.AppendLine($"READ-FAIL {name}: {ex.Message}"); continue; }

            try
            {
                IReadOnlyDictionary<int, byte[]>? blobs = null;
                var frxPath = Path.ChangeExtension(path,
                    Path.GetExtension(path).Equals(".ctl", StringComparison.OrdinalIgnoreCase) ? ".ctx" : ".frx");
                if (File.Exists(frxPath))
                    blobs = FrxDeserializer.Read(File.ReadAllBytes(frxPath));

                var owner = new ProjectDefinition(VBProjectType.EXE, "Corpus");
                var sink = new Sink();
                var form = new FormDeserializer().Deserialize(owner, original, sink, blobs);

                if (form is null)
                {
                    failures.Add(name);
                    report.AppendLine($"PARSE-FAIL {name}: deserializer returned null");
                    foreach (var e in sink.Errors.Take(3)) report.AppendLine($"      {e}");
                    continue;
                }

                var (rendered, binary) = new FormSerializer().Serialize(form, name);

                // The binary companion is the half that gets DESTROYED rather than merely rewritten:
                // a null here is read by the save path as "delete the .frx". Never discard it again.
                if (File.Exists(frxPath))
                {
                    var originalBinary = File.ReadAllBytes(frxPath);
                    // This lane measures the SERIALIZER, so it reports what could not be reproduced. It
                    // does not mean the file is destroyed: ProjectService refuses to write or delete a
                    // companion it cannot reproduce (issue #17), and CompanionBinaryPreservationTests
                    // guards that at the write layer. These stay reported because the underlying gap —
                    // unmodelled blob-backed properties — is real and still open.
                    if (binary is null || binary.Length == 0)
                    {
                        failures.Add(name);
                        report.AppendLine(
                            $"BLOB-LOSS {name}: companion {Path.GetFileName(frxPath)} holds "
                          + $"{originalBinary.Length} bytes ({blobs?.Count ?? 0} blob(s)) and the serializer "
                          + $"reproduces none — original preserved on disk by the save-path guard");
                    }
                    else if (!binary.SequenceEqual(originalBinary))
                    {
                        failures.Add(name);
                        report.AppendLine(
                            $"BLOB-DIFF {name}: companion {originalBinary.Length} bytes in, "
                          + $"{binary.Length} bytes out — original preserved by the save-path guard");
                    }
                }

                var diff = Differences(original, rendered);
                if (diff is null)
                {
                    if (!failures.Contains(name)) report.AppendLine($"OK        {name}");
                }
                else
                {
                    if (!failures.Contains(name)) failures.Add(name);
                    report.AppendLine($"DIFF      {name}: {diff}");
                    if (sink.Errors.Count > 0)
                        report.AppendLine($"      (deserializer logged {sink.Errors.Count} error(s), first: {sink.Errors[0]})");
                }
            }
            catch (Exception ex)
            {
                failures.Add(name);
                report.AppendLine($"THREW     {name}: {ex.GetType().Name}: {ex.Message}");
            }
        }

        var vb6Authored = files.Where(f => !IsHexIdeAuthored(f)).Select(Path.GetFileName).ToList();
        var vb6Failures = failures.Where(f => vb6Authored.Contains(f)).Count();

        report.AppendLine(new string('=', 78));
        report.AppendLine($"VB6-authored:    {vb6Authored.Count - vb6Failures}/{vb6Authored.Count} round-tripped");
        report.AppendLine($"HexIDE-authored: {files.Count - vb6Authored.Count - (failures.Count - vb6Failures)}"
                        + $"/{files.Count - vb6Authored.Count} round-tripped (proves nothing — same code both ends)");
        // Diagnostics only, and the path is shared — a concurrent run must never fail a test over it.

        try { File.WriteAllText(ReportPath, report.ToString()); } catch { /* best effort */ }

        // A baseline, not zero. The backlog is tracked in issues; this gate exists to stop it growing
        // and to make progress visible. RAISE this number as fixes land — never lower it.
        const int KnownVb6Failures = 22;
        vb6Failures.Should().BeLessThanOrEqualTo(KnownVb6Failures,
            $"VB6-authored round-trip regressed past the known baseline. Full report: {ReportPath}\n"
            + report.ToString());
    }

    [Fact]
    public void No_more_corpus_forms_are_held_read_only_than_the_baseline()
    {
        // The refusal gate is the honest response to a defect, but every form it holds is a form the
        // developer cannot edit — so the count is a burndown, not a steady state. It was 12 of 22 until
        // menu hierarchies round-tripped (#83). The six that remain are container nesting (#84), and
        // none of them contains a menu at all.
        //
        // LOWER this as fixes land. Raising it means a form that used to be editable no longer is,
        // which is a regression however good the reason sounds.
        const int KnownReadOnly = 6;

        var files = CorpusFiles(".frm", ".ctl");
        if (files.Count == 0) return;

        var gated = new List<string>();
        foreach (var path in files)
        {
            var frxPath = Path.ChangeExtension(path,
                Path.GetExtension(path).Equals(".ctl", StringComparison.OrdinalIgnoreCase) ? ".ctx" : ".frx");
            IReadOnlyDictionary<int, byte[]>? blobs = null;
            if (File.Exists(frxPath)) blobs = FrxDeserializer.Read(File.ReadAllBytes(frxPath));

            var owner = new ProjectDefinition(VBProjectType.EXE, "Corpus");
            var form = new FormDeserializer().Deserialize(owner, Vb6TextFile.ReadAllText(path), new Sink(), blobs);
            if (form is not null && !form.CanSaveFaithfully)
                gated.Add($"{Path.GetFileName(path)} — {form.UnfaithfulSaveReason}");
        }

        gated.Count.Should().BeLessThanOrEqualTo(KnownReadOnly,
            "the refusal gate must narrow as reproduction improves, never widen\n"
            + string.Join("\n", gated));
    }

    [Fact]
    public void Every_corpus_form_can_be_opened()
    {
        // Outcome 0 — refusing to open — is safe but it is not free: a project HexIDE cannot open is a
        // project it cannot demo. This is a separate, stricter property than byte-identity, and unlike
        // that one it is fully achieved, so it gates at zero.
        var files = CorpusFiles(".frm", ".ctl");
        if (files.Count == 0) return;

        var unopenable = new List<string>();
        var detail = new StringBuilder();

        foreach (var path in files)
        {
            var frxPath = Path.ChangeExtension(path,
                Path.GetExtension(path).Equals(".ctl", StringComparison.OrdinalIgnoreCase) ? ".ctx" : ".frx");
            IReadOnlyDictionary<int, byte[]>? blobs = null;
            if (File.Exists(frxPath)) blobs = FrxDeserializer.Read(File.ReadAllBytes(frxPath));

            var sink = new Sink();
            try
            {
                var owner = new ProjectDefinition(VBProjectType.EXE, "Corpus");
                if (new FormDeserializer().Deserialize(owner, Vb6TextFile.ReadAllText(path), sink, blobs) is not null)
                    continue;
                unopenable.Add(Path.GetFileName(path));
                detail.AppendLine($"{Path.GetFileName(path)}: {sink.Errors.FirstOrDefault() ?? "returned null"}");
            }
            catch (Exception ex)
            {
                unopenable.Add(Path.GetFileName(path));
                detail.AppendLine($"{Path.GetFileName(path)}: {ex.GetType().Name}: {ex.Message}");
            }
        }

        unopenable.Should().BeEmpty("every VB6-authored form must at least open\n" + detail);
    }

    [Fact]
    public void HexIDEs_own_output_is_a_fixed_point()
    {
        // Byte-identity with VB6 is the goal; reaching a FIXED POINT is the floor. If saving HexIDE's own
        // output changes it again, the file never settles and source control never quiets down — every
        // save is a diff even when the user changed nothing.
        //
        // This is what decides whether interop churn is bounded. Round-tripping a VB6-authored form
        // through HexIDE produces one large diff; if HexIDE is then idempotent, that is the end of it
        // until VB6 touches the file again. If it is not, the file churns forever on its own.
        var files = CorpusFiles(".frm", ".ctl");
        if (files.Count == 0) return;

        var report = new StringBuilder();
        var drifting = new List<string>();

        foreach (var path in files)
        {
            var name = Path.GetFileName(path);
            var frxPath = Path.ChangeExtension(path,
                Path.GetExtension(path).Equals(".ctl", StringComparison.OrdinalIgnoreCase) ? ".ctx" : ".frx");
            IReadOnlyDictionary<int, byte[]>? blobs = null;
            if (File.Exists(frxPath)) blobs = FrxDeserializer.Read(File.ReadAllBytes(frxPath));

            string? Pass(string text)
            {
                var owner = new ProjectDefinition(VBProjectType.EXE, "Corpus");
                var form = new FormDeserializer().Deserialize(owner, text, new Sink(), blobs);
                return form is null ? null : new FormSerializer().Serialize(form, name).Item1;
            }

            string? first, second;
            try { first = Pass(Vb6TextFile.ReadAllText(path)); if (first is null) continue; second = Pass(first); }
            catch { continue; } // outcome 0 — cannot open. Covered by the round-trip lane.

            if (second is null)
            {
                drifting.Add(name);
                report.AppendLine($"RE-READ-FAIL {name}: HexIDE cannot re-open its own output");
                continue;
            }
            if (first == second) continue;

            drifting.Add(name);
            var a = first.Replace("\r\n", "\n").Split('\n');
            var b = second.Replace("\r\n", "\n").Split('\n');
            var firstDiff = "";
            for (var i = 0; i < Math.Max(a.Length, b.Length); i++)
            {
                var al = i < a.Length ? a[i] : "<missing>";
                var bl = i < b.Length ? b[i] : "<missing>";
                if (al == bl) continue;
                firstDiff = $"line {i + 1}: «{al}» -> «{bl}»";
                break;
            }
            report.AppendLine($"DRIFT      {name}: {firstDiff}");
        }

        // Zero, and it must stay zero. The four drifters were fractional twips losing one ULP per save
        // (6683.999999999999 -> ...998); rounding at the write boundary in FormSerializer.ToTwips fixed
        // them. This is a real gate now: any new drift means the file would churn forever on its own.
        const int KnownDrifters = 0;
        drifting.Count.Should().BeLessThanOrEqualTo(KnownDrifters,
            $"HexIDE's output must be a fixed point ({drifting.Count} drifting).\n" + report);
    }

    [Fact]
    public void Every_companion_offset_cited_by_a_form_resolves_to_a_blob()
    {
        // A .frx is not self-describing: it is a bag of records at byte offsets, and each record's layout
        // is decided by the .frm property that cites it. So the invariant that matters is not "the file
        // parses" but "every offset the form cites came back as a blob, and nothing else did".
        //
        // This is what separates a real gap from a cosmetic one. Image blobs — Picture, Icon, DragIcon,
        // MouseIcon — are 42 of 46 citations in the corpus and all resolve exactly. The failures are
        // entirely ComboBox/ListBox List and ItemData, which use a different record layout (a 2-byte
        // count, not a 4-byte length). Fixing that needs one oracle experiment, not a rewrite.
        var files = CorpusFiles(".frm", ".ctl");
        if (files.Count == 0) return;

        var cite = new Regex(@"^\s*(\w+)\s*=\s*""[^""]+\.(?:frx|ctx|pgx)"":([0-9A-Fa-f]+)",
                             RegexOptions.IgnoreCase | RegexOptions.Multiline);
        var report = new StringBuilder();
        var broken = new List<string>();

        foreach (var path in files)
        {
            var companion = Path.ChangeExtension(path,
                Path.GetExtension(path).Equals(".ctl", StringComparison.OrdinalIgnoreCase) ? ".ctx" : ".frx");
            if (!File.Exists(companion)) continue;

            var cited = cite.Matches(Vb6TextFile.ReadAllText(path))
                .Select(m => Convert.ToInt32(m.Groups[2].Value, 16))
                .Distinct().OrderBy(x => x).ToList();

            Dictionary<int, byte[]> parsed;
            try { parsed = FrxDeserializer.Read(File.ReadAllBytes(companion)); }
            catch (Exception ex)
            {
                broken.Add(Path.GetFileName(companion));
                report.AppendLine($"THREW      {Path.GetFileName(companion)}: {ex.GetType().Name}");
                continue;
            }

            var unresolved = cited.Where(o => !parsed.ContainsKey(o)).ToList();
            var phantom = parsed.Keys.Where(k => !cited.Contains(k)).ToList();
            if (unresolved.Count == 0 && phantom.Count == 0) continue;

            broken.Add(Path.GetFileName(companion));
            report.AppendLine(
                $"MISREAD    {Path.GetFileName(companion)}: cited {cited.Count}, parsed {parsed.Count}"
              + (unresolved.Count > 0 ? $", unresolved [{string.Join(",", unresolved.Select(o => "0x" + o.ToString("X4")))}]" : "")
              + (phantom.Count > 0 ? $", phantom [{string.Join(",", phantom.Select(o => "0x" + o.ToString("X4")))}]" : ""));
        }

        // Baseline, not zero: the three known failures all carry List/ItemData. Raise as fixes land.
        const int KnownMisreads = 3;
        broken.Count.Should().BeLessThanOrEqualTo(KnownMisreads,
            $"the .frx reader regressed past the known baseline ({broken.Count} misread: "
          + $"{string.Join(", ", broken)}).\n" + report);
    }

    [Fact]
    public void Standard_and_class_modules_round_trip_byte_for_byte()
    {
        var files = CorpusFiles(".bas", ".cls");
        if (files.Count == 0) return;

        var failures = new List<string>();
        var report = new StringBuilder();

        foreach (var path in files)
        {
            var name = Path.GetFileName(path);
            var original = Vb6TextFile.ReadAllText(path);
            var kind = Path.GetExtension(path).Equals(".cls", StringComparison.OrdinalIgnoreCase)
                ? ModuleKind.ClassModule
                : ModuleKind.StandardModule;

            // Mirror the product exactly: capture the header on load, re-emit it on save.
            var (preservedHeader, body) = ModuleFileFormat.SplitHeader(original, kind);
            // Take the name from VB_Name, as the product does. Using the file name instead produces
            // illegal identifiers for corpus files with spaces ("Load Resources") and reports 20 harness
            // artifacts as product defects, masking the real .cls header corruption underneath.
            var moduleName = ReadVbName(original) ?? Path.GetFileNameWithoutExtension(path);
            var rendered = ModuleFileFormat.ToFileContent(body, moduleName, kind, preservedHeader);

            var diff = Differences(original, rendered);
            if (diff is null) { report.AppendLine($"OK        {name}"); continue; }

            failures.Add(name);
            report.AppendLine($"DIFF      {name}: {diff}");
        }

        // Zero, and it must stay zero. The .cls failures here were the hardcoded class header
        // (ModuleFileFormat.Header) overwriting Instancing and data-binding settings; #18 replaced that
        // with verbatim preservation, so modules are the first format to reach outcome 1 across the
        // whole corpus. This is a real gate now, not a burndown counter.
        const int KnownModuleFailures = 0;
        failures.Count.Should().BeLessThanOrEqualTo(KnownModuleFailures,
            $"module round-trip regressed past the known baseline ({failures.Count} failing: "
          + $"{string.Join(", ", failures)}).\n" + report);
    }
}
