using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using HexIDE.Events;
using HexIDE.Localization;
using HexIDE.Lsp;
using HexIDE.Lsp.Messages;
using HexIDE.Runtime.ProjectElements;
using Serilog;

namespace HexIDE.IDE;

public class Vb6ToolchainService : IVb6ToolchainService
{
    private static readonly string[] DefaultPaths =
    [
        @"C:\Program Files (x86)\Microsoft Visual Studio\VB98\VB6.EXE",
        @"C:\Program Files\Microsoft Visual Studio\VB98\VB6.EXE",
        @"C:\Program Files (x86)\Microsoft Visual Basic\VB6.EXE",
        @"C:\Program Files\Microsoft Visual Basic\VB6.EXE",
    ];

    // A build is bounded so a VB6 that stalls on a modal (missing reference, licence prompt, …) can't
    // hang the IDE indefinitely. Generous: a large real project can take a while to compile.
    private static readonly TimeSpan BuildTimeout = TimeSpan.FromSeconds(180);

    // Matches: path(line) : error|warning anything
    // e.g.  C:\MyProject\Form1.frm(10) : error C0001: Compile error
    private static readonly Regex ErrorRegex = new(
        @"^(.+?)\((\d+)\)\s*:\s*(error|warning)\s+(.+)$",
        RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // A .vbp may override the output name:  ExeName32="MyApp.exe"  (value may be unquoted).
    private static readonly Regex ExeName32Regex = new(
        @"^\s*ExeName32\s*=\s*""?([^""\r\n]+?)""?\s*$",
        RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly ILspClient lspClient;
    private readonly IEventBus eventBus;
    private readonly IWindowManager windowManager;
    private readonly ILocalizationService _localization;

    public string? Vb6ExePath { get; }
    public bool IsAvailable => Vb6ExePath != null;

    public Vb6ToolchainService(ILspClient lspClient, IEventBus eventBus, IWindowManager windowManager, ILocalizationService localization)
    {
        this.lspClient = lspClient;
        this.eventBus = eventBus;
        this.windowManager = windowManager;
        _localization = localization;

        var envPath = Environment.GetEnvironmentVariable("VB6_EXE");
        Vb6ExePath = envPath != null && File.Exists(envPath)
            ? envPath
            : DefaultPaths.FirstOrDefault(File.Exists);
    }

    public async Task<bool> MakeWithVb6Async(ProjectDefinition project)
    {
        if (!IsAvailable)
        {
            await windowManager.MessageBox(
                _localization.GetString("Str.Vb6Toolchain.Vb6NotFound"),
                _localization.GetString("Str.Vb6Toolchain.BuildFailedTitle"), MessageBoxButtons.Ok, MessageBoxIcon.Error);
            return false;
        }

        if (project.AbsolutePath is null)
        {
            await windowManager.MessageBox(
                _localization.GetString("Str.Vb6Toolchain.ProjectNotSaved"),
                _localization.GetString("Str.Vb6Toolchain.BuildFailedTitle"), MessageBoxButtons.Ok, MessageBoxIcon.Warning);
            return false;
        }

        // Flush any pending in-memory changes to disk.
        eventBus.Publish(new ApplyAllUnsavedChangesEvent());
        await Task.Delay(150);

        var outcome = await RunVb6Async(project.AbsolutePath!);

        // The previous build's errors expire here, whatever this one did — including a timeout, which used
        // to return before this point and so was the one path that left them on screen indefinitely.
        //
        // It clears what THIS source published and nothing else. It used to send an empty diagnostic set
        // for every form in the project, which the channel could not tell apart from "this document is
        // clean", so a build deleted whatever a language server had published for those forms and the
        // marks only returned on the next keystroke (#358). Driving it from what the compiler itself
        // published also reaches a form renamed since the last build, which a walk over the project's
        // current forms does not (the other half of #269).
        await lspClient.ClearDiagnosticsFromAsync(DiagnosticOwner.Vb6Compiler);

        if (outcome is null)
        {
            Log.Warning("Vb6ToolchainService: VB6 /make exceeded {Timeout}s and was terminated",
                BuildTimeout.TotalSeconds);
            await windowManager.MessageBox(
                _localization.GetString("Str.Vb6Toolchain.CompilationFailedNoOutput"),
                _localization.GetString("Str.Vb6Toolchain.BuildFailedTitle"), MessageBoxButtons.Ok, MessageBoxIcon.Error);
            return false;
        }

        if (outcome.ExitCode != 0)
        {
            var byUri = ParseVb6Errors(outcome.Output, project)
                .GroupBy(x => x.uri)
                .ToDictionary(g => g.Key, g => g.Select(x => x.diagnostic).ToArray());

            foreach (var (uri, diags) in byUri)
                await lspClient.InjectDiagnosticsAsync(uri, diags, DiagnosticOwner.Vb6Compiler);

            // If we couldn't parse any structured errors, surface the raw output.
            if (byUri.Count == 0)
            {
                var msg = string.IsNullOrWhiteSpace(outcome.Output)
                    ? _localization.GetString("Str.Vb6Toolchain.CompilationFailedNoOutput")
                    : string.Format(_localization.GetString("Str.Vb6Toolchain.CompilationFailed"), outcome.Output);
                await windowManager.MessageBox(msg, _localization.GetString("Str.Vb6Toolchain.BuildFailedTitle"), MessageBoxButtons.Ok, MessageBoxIcon.Error);
            }
        }

        return outcome.ExitCode == 0;
    }

    /// <summary>What one run of <c>VB6.EXE /make</c> reported.</summary>
    /// <param name="ExitCode">Zero when the compiler produced a binary.</param>
    /// <param name="Output">The <c>/out</c> log and both console streams, joined for parsing.</param>
    internal sealed record Vb6BuildOutcome(int ExitCode, string Output);

    /// <summary>
    /// Runs the compiler once, or returns null if it had to be killed for exceeding
    /// <see cref="BuildTimeout"/>.
    ///
    /// <para>
    /// Separated from the diagnostics handling around it so that handling can be exercised at all. VB6 is
    /// Windows-only and installed on developer machines rather than on CI, so a test that has to start the
    /// real compiler is a test that never runs — which is how this method's caller came to erase every
    /// form's diagnostics on every build with nothing to catch it. Overriding this is the seam that lets a
    /// test say "the compiler exited 0" without one being present.
    /// </para>
    /// </summary>
    internal virtual async Task<Vb6BuildOutcome?> RunVb6Async(string projectPath)
    {
        // VB6.exe is a GUI app: under /make it writes compile errors to the file named by /out, not
        // reliably to stdout/stderr. Capture the /out log (the authoritative error source) as well as
        // the console streams, then parse all of them together.
        var logPath = Path.Combine(Path.GetTempPath(), $"hexide-vb6make-{Guid.NewGuid():N}.log");

        var psi = new ProcessStartInfo(Vb6ExePath!)
        {
            Arguments = $"/make \"{projectPath}\" /out \"{logPath}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = Process.Start(psi)!;

        // Drain both pipes immediately (a chatty child could otherwise fill a pipe buffer and deadlock),
        // and bound the wait by BuildTimeout. On timeout, kill the tree — which also unblocks the reads.
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        using var cts = new CancellationTokenSource(BuildTimeout);

        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* already exited */ }
            TryDeleteLog(logPath);
            return null;
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        var logText = ReadAndDeleteLog(logPath);

        return new Vb6BuildOutcome(
            process.ExitCode,
            string.Join("\n", new[] { logText, stdout, stderr }.Where(s => !string.IsNullOrWhiteSpace(s))));
    }

    public async Task RunWithVb6Async(ProjectDefinition project)
    {
        var success = await MakeWithVb6Async(project);
        if (!success)
            return;

        // VB6 puts the EXE in the same directory as the .vbp file, named by ExeName32 if the project
        // sets it (otherwise <ProjectName>.exe) — so assuming project.Name would miss a renamed output.
        var projectDir = Path.GetDirectoryName(project.AbsolutePath!)!;
        var exePath = Path.Combine(projectDir, ResolveOutputExeName(project));

        if (!File.Exists(exePath))
        {
            await windowManager.MessageBox(
                string.Format(_localization.GetString("Str.Vb6Toolchain.ExeNotFound"), exePath),
                _localization.GetString("Str.Vb6Toolchain.RunFailedTitle"), MessageBoxButtons.Ok, MessageBoxIcon.Warning);
            return;
        }

        Process.Start(new ProcessStartInfo(exePath) { UseShellExecute = true });
    }

    private static string GetFormUri(FormDefinition form) => $"vb6://form/{form.Name}";

    // The compiled EXE's file name: a .vbp may specify ExeName32="Foo.exe"; otherwise VB6 names the
    // output after the project. ExeName32 is a top-level .vbp key, preserved verbatim among the unparsed
    // lines (and, defensively, scanned in the preserved section tail too).
    private static string ResolveOutputExeName(ProjectDefinition project)
    {
        foreach (var (_, raw) in project.UnknownPreSectionLines)
        {
            var m = ExeName32Regex.Match(raw);
            if (m.Success && m.Groups[1].Value.Trim() is { Length: > 0 } name)
                return name;
        }
        if (project.ExtensionTail is { } tail)
        {
            var m = ExeName32Regex.Match(tail);
            if (m.Success && m.Groups[1].Value.Trim() is { Length: > 0 } name)
                return name;
        }
        return project.Name + ".exe";
    }

    private static string ReadAndDeleteLog(string logPath)
    {
        try
        {
            if (!File.Exists(logPath)) return "";
            var text = File.ReadAllText(logPath);
            File.Delete(logPath);
            return text;
        }
        catch { return ""; }
    }

    private static void TryDeleteLog(string logPath)
    {
        try { if (File.Exists(logPath)) File.Delete(logPath); }
        catch { /* best-effort */ }
    }

    private static System.Collections.Generic.IEnumerable<(string uri, Diagnostic diagnostic)> ParseVb6Errors(
        string output, ProjectDefinition project)
    {
        foreach (Match m in ErrorRegex.Matches(output))
        {
            var filePath = m.Groups[1].Value.Trim();
            var line = int.Parse(m.Groups[2].Value) - 1; // LSP lines are 0-based
            var isError = m.Groups[3].Value.Equals("error", StringComparison.OrdinalIgnoreCase);
            var message = m.Groups[4].Value.Trim();

            var baseName = Path.GetFileNameWithoutExtension(filePath);
            var form = project.Forms.FirstOrDefault(f =>
                string.Equals(f.Name, baseName, StringComparison.OrdinalIgnoreCase));

            if (form is null)
                continue;

            var range = new HexIDE.Lsp.Messages.Range(new Position(line, 0), new Position(line, 999));
            var severity = isError ? DiagnosticSeverity.Error : DiagnosticSeverity.Warning;
            yield return (GetFormUri(form), new Diagnostic(range, message, severity, "VB6 Compiler"));
        }
    }
}
