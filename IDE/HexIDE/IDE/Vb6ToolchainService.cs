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

        // VB6.exe is a GUI app: under /make it writes compile errors to the file named by /out, not
        // reliably to stdout/stderr. Capture the /out log (the authoritative error source) as well as
        // the console streams, then parse all of them together.
        var logPath = Path.Combine(Path.GetTempPath(), $"hexide-vb6make-{Guid.NewGuid():N}.log");

        var psi = new ProcessStartInfo(Vb6ExePath!)
        {
            Arguments = $"/make \"{project.AbsolutePath}\" /out \"{logPath}\"",
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
            Log.Warning("Vb6ToolchainService: VB6 /make exceeded {Timeout}s and was terminated",
                BuildTimeout.TotalSeconds);
            await windowManager.MessageBox(
                _localization.GetString("Str.Vb6Toolchain.CompilationFailedNoOutput"),
                _localization.GetString("Str.Vb6Toolchain.BuildFailedTitle"), MessageBoxButtons.Ok, MessageBoxIcon.Error);
            return false;
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        var logText = ReadAndDeleteLog(logPath);

        // Clear stale compiler diagnostics from previous builds.
        foreach (var form in project.Forms)
            await lspClient.InjectDiagnosticsAsync(GetFormUri(form), []);

        if (process.ExitCode != 0)
        {
            var combined = string.Join("\n",
                new[] { logText, stdout, stderr }.Where(s => !string.IsNullOrWhiteSpace(s)));
            var byUri = ParseVb6Errors(combined, project)
                .GroupBy(x => x.uri)
                .ToDictionary(g => g.Key, g => g.Select(x => x.diagnostic).ToArray());

            foreach (var (uri, diags) in byUri)
                await lspClient.InjectDiagnosticsAsync(uri, diags);

            // If we couldn't parse any structured errors, surface the raw output.
            if (byUri.Count == 0)
            {
                var msg = string.IsNullOrWhiteSpace(combined)
                    ? _localization.GetString("Str.Vb6Toolchain.CompilationFailedNoOutput")
                    : string.Format(_localization.GetString("Str.Vb6Toolchain.CompilationFailed"), combined);
                await windowManager.MessageBox(msg, _localization.GetString("Str.Vb6Toolchain.BuildFailedTitle"), MessageBoxButtons.Ok, MessageBoxIcon.Error);
            }
        }

        return process.ExitCode == 0;
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
