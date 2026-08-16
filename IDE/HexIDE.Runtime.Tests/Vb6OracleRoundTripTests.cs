using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// The only gate that can catch outcome 3 — "works in HexIDE, fails in VB6" (docs/serialization-outcomes.md).
///
/// Every other serialization check is HexIDE marking its own homework: we decide what valid means, then
/// confirm we produced it. This lane asks the toolchain the user will actually go back to. It is what found
/// the menu-shortcut defect (#22), where the round-tripped form still opened perfectly in HexIDE and VB6
/// refused it outright.
///
/// Self-calibrating: each form is compiled BEFORE the round-trip as a control. A form VB6 cannot build in
/// the first place — an unregistered OCX, a missing dependency — is skipped rather than blamed on HexIDE.
/// Only "VB6 built the original but not our output" is a failure.
///
/// Opt-in, because it spawns a vb6.exe per form and is Windows-only:
///   HEXIDE_ORACLE=1 dotnet test HexIDE.Runtime.Tests/ --filter "FullyQualifiedName~Vb6OracleRoundTrip"
/// </summary>
public class Vb6OracleRoundTripTests : IDisposable
{
    private readonly string scratch =
        Path.Join(Path.GetTempPath(), "hexide-oracle-" + Guid.NewGuid().ToString("N"));

    public Vb6OracleRoundTripTests() => Directory.CreateDirectory(scratch);

    public void Dispose()
    {
        try { Directory.Delete(scratch, recursive: true); } catch { /* best effort */ }
        GC.SuppressFinalize(this);
    }

    private static string? FindVb6() =>
        new[]
        {
            Environment.GetEnvironmentVariable("VB6_EXE"),
            @"C:\Program Files (x86)\Microsoft Visual Studio\VB98\VB6.EXE",
            @"C:\Program Files\Microsoft Visual Studio\VB98\VB6.EXE",
        }.FirstOrDefault(p => !string.IsNullOrWhiteSpace(p) && File.Exists(p));

    private static IReadOnlyList<string> CorpusForms()
    {
        var root = Environment.GetEnvironmentVariable("VB6_TEMPLATES")
                   ?? @"C:\Program Files (x86)\Microsoft Visual Studio\VB98\Template";
        if (!Directory.Exists(root)) return [];
        return Directory.EnumerateFiles(root, "*.frm", SearchOption.AllDirectories)
                        .OrderBy(f => f).ToList();
    }

    private sealed class Sink : IDeserializeErrorSink
    {
        public List<string> Errors { get; } = new();
        public void LogError(string error) => Errors.Add(error);
    }

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

    /// <summary>Runs vb6.exe /make. Returns null on success, or VB6's reason for refusing.</summary>
    private static string? Compile(string vb6, string vbpPath)
    {
        var dir = Path.GetDirectoryName(vbpPath)!;
        var errLog = Path.Join(dir, "make.log");
        var psi = new ProcessStartInfo
        {
            FileName = vb6,
            Arguments = $"/make \"{vbpPath}\" /out \"{errLog}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var p = Process.Start(psi)!;
        if (!p.WaitForExit(90_000))
        {
            try { p.Kill(true); } catch { /* ignore */ }
            return "vb6.exe did not exit — it probably popped a GUI (check the .vbp path is absolute)";
        }

        var exe = Path.Join(dir, "Probe.exe");
        if (File.Exists(exe)) return null;

        var detail = File.Exists(errLog) ? File.ReadAllText(errLog).Trim() : "(no /out log produced)";
        // VB6 writes the per-form parse failure to a sibling log named after the form.
        foreach (var formLog in Directory.EnumerateFiles(dir, "*.log").Where(f => !f.EndsWith("make.log")))
            detail += " | " + Path.GetFileName(formLog) + ": " + File.ReadAllText(formLog).Trim();
        return detail.Replace("\r\n", " | ");
    }

    /// <summary>Stages a form + companion + minimal .vbp in its own directory, ready to compile.</summary>
    private string StageProject(string label, string formName, string formText, byte[]? companion)
    {
        var dir = Path.Join(scratch, label);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Join(dir, formName + ".frm"), formText);
        if (companion is { Length: > 0 })
            File.WriteAllBytes(Path.Join(dir, formName + ".frx"), companion);

        var vbp = Path.Join(dir, "Probe.vbp");
        File.WriteAllText(vbp,
            $"Type=Exe\r\nForm={formName}.frm\r\nStartup=\"{formName}\"\r\n"
          + "ExeName32=\"Probe.exe\"\r\nName=\"Probe\"\r\n");
        return vbp;
    }

    [Fact]
    public void HexIDE_output_still_builds_in_vb6()
    {
        if (Environment.GetEnvironmentVariable("HEXIDE_ORACLE") != "1")
            return; // opt-in: spawns a vb6.exe per form

        var vb6 = FindVb6();
        if (vb6 is null) return; // no VB6 install (CI is Linux)

        var forms = CorpusForms();
        if (forms.Count == 0) return;

        var report = new StringBuilder();
        var regressions = new List<string>();
        int controlBuilt = 0, skipped = 0, index = 0;

        foreach (var path in forms)
        {
            index++;
            var original = File.ReadAllText(path);
            var name = ReadVbName(original);
            if (name is null) { skipped++; continue; }

            var frxPath = Path.ChangeExtension(path, ".frx");
            var companion = File.Exists(frxPath) ? File.ReadAllBytes(frxPath) : null;

            // The .frm cites its companion by the original file name; staging renames it, so retarget.
            var staged = companion is null
                ? original
                : original.Replace(Path.GetFileName(frxPath), name + ".frx", StringComparison.OrdinalIgnoreCase);

            // ── Control: can VB6 build this form as it shipped? ──────────────────────────────────
            var controlFailure = Compile(vb6, StageProject($"{index:D2}-control", name, staged, companion));
            if (controlFailure is not null)
            {
                // Unregistered OCX, missing dependency — not attributable to HexIDE.
                skipped++;
                report.AppendLine($"SKIP    {Path.GetFileName(path)}: VB6 cannot build the original");
                continue;
            }
            controlBuilt++;

            // ── Subject: round-trip through HexIDE, then ask VB6 again ──────────────────────────
            string roundTripped;
            byte[]? roundTrippedCompanion;
            try
            {
                var owner = new ProjectDefinition(VBProjectType.EXE, "Probe");
                var blobs = companion is null ? null : FrxDeserializer.Read(companion);
                var form = new FormDeserializer().Deserialize(owner, staged, new Sink(), blobs);
                if (form is null)
                {
                    // Outcome 0 — we refuse to open it. Safe, and not a category-3 regression.
                    report.AppendLine($"NO-LOAD {Path.GetFileName(path)}: HexIDE cannot open it (outcome 0)");
                    continue;
                }

                if (!form.CanSaveFaithfully)
                {
                    // Outcome 0 — the product refuses to write this file, so the bytes below would never
                    // reach disk. Compiling them would measure a serializer path no user can trigger.
                    report.AppendLine($"REFUSED {Path.GetFileName(path)}: {form.UnfaithfulSaveReason} (outcome 0)");
                    continue;
                }
                var (text, binary) = new FormSerializer().Serialize(form, name + ".frm");
                roundTripped = text;
                // Pass-through: a companion we cannot reproduce is preserved, never regenerated.
                roundTrippedCompanion = binary is { Length: > 0 } ? binary : companion;
            }
            catch (Exception ex)
            {
                report.AppendLine($"NO-LOAD {Path.GetFileName(path)}: {ex.GetType().Name} (outcome 0)");
                continue;
            }

            var subjectFailure = Compile(vb6,
                StageProject($"{index:D2}-subject", name, roundTripped, roundTrippedCompanion));

            if (subjectFailure is null)
            {
                report.AppendLine($"OK      {Path.GetFileName(path)}");
            }
            else
            {
                regressions.Add(Path.GetFileName(path));
                report.AppendLine($"BROKE   {Path.GetFileName(path)}: {subjectFailure}");
            }
        }

        report.AppendLine(new string('-', 78));
        report.AppendLine($"{controlBuilt} form(s) built as shipped, {skipped} skipped, "
                        + $"{regressions.Count} broken by the round-trip");

        // Zero, and it must stay zero. The five breakages were all menu forms, failing on the flattening of
        // nested Begin blocks (#22) for two independent reasons: a Shortcut on what became a top-level
        // menu, and a separator that became one. Rather than being fixed, they were moved to outcome 0 —
        // HexIDE now refuses to save a form it cannot reproduce, so those bytes never reach disk.
        //
        // That is a real resolution, not a suppression: the failure mode this gate exists to catch is a
        // file the developer cannot open in VB6, and there is no longer any way to produce one. When the
        // parenting model lands and the refusal is lifted, this number must stay at zero on its own merits.
        const int KnownCategory3Regressions = 0;
        regressions.Count.Should().BeLessThanOrEqualTo(KnownCategory3Regressions,
            "a form VB6 could build before HexIDE touched it must still build afterwards — this is "
          + "outcome 3, the worst in docs/serialization-outcomes.md, because it is silent\n" + report);
    }
}
