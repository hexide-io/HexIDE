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
using HexIDE.Runtime.Serialization;
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

    // What vb6.exe actually writes to /out, measured rather than assumed -- see "What `/make` writes to
    // `/out`, and what "Line N" counts" in docs/vb6-fidelity-oracle.md:
    //
    //     <CRLF>Compile Error in File '<ABSOLUTE path>', Line <N> : <message><CRLF>Build of 'x.exe' failed.
    //
    // Note the spaces either side of the colon, and that the path is absolute even where the .vbp named
    // the file relatively.
    //
    // This used to match `path(N) : error C0001: ...`, which is a C compiler's format and nothing VB6 has
    // ever produced -- so every compiler diagnostic was silently dropped and that consumer had never once
    // had live traffic (#477). Anchored on `', Line ` and on ` : ` rather than on the message, because a
    // message may contain both a colon and an apostrophe and a path may contain either.
    private static readonly Regex ErrorRegex = new(
        @"^Compile Error in File '(?<path>.+)', Line (?<line>\d+) : (?<message>.*)$",
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

    private static string GetFormUri(FormDefinition form) => DocumentWireName.For(form);

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
            // Decoded rather than File.ReadAllText, which assumes UTF-8. The log is ANSI (measured), so
            // a non-ASCII character anywhere in the absolute path becomes U+FFFD before the regex sees it
            // -- and the path is how a diagnostic finds its document, so every error in that project would
            // be dropped in silence. Vb6TextFile.Decode covers Latin-1 with no new dependency; a true ANSI
            // codepage would need System.Text.Encoding.CodePages, a package, and a licence row for it.
            var text = Vb6TextFile.Decode(File.ReadAllBytes(logPath));
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

    /// <summary>
    /// The log parser, for tests. Materialised, because every caller wants to index it and a lazy sequence
    /// over a regex reads as a list that is sometimes empty for a different reason than it looks.
    /// </summary>
    internal static System.Collections.Generic.IReadOnlyList<(string uri, Diagnostic diagnostic)> ParseForTests(
        string output, ProjectDefinition project) => [.. ParseVb6Errors(output, project)];

    private static System.Collections.Generic.IEnumerable<(string uri, Diagnostic diagnostic)> ParseVb6Errors(
        string output, ProjectDefinition project)
    {
        foreach (Match m in ErrorRegex.Matches(output))
        {
            var filePath = m.Groups["path"].Value.Trim();
            var printed = int.Parse(m.Groups["line"].Value);
            var message = m.Groups["message"].Value.Trim();

            // By PATH, across every kind. It used to match forms only, by file stem -- so a .bas, a .cls,
            // a .ctl and a .pag were all dropped, and a form whose file was not named after it was dropped
            // too. The compiler prints an absolute path; that identifies a document exactly.
            var document = DocumentLookup.FindByPath([project], filePath);
            if (document is null) continue;

            var line = EditorLineOf(document, printed);
            var range = new HexIDE.Lsp.Messages.Range(new Position(line, 0), new Position(line, 999));

            // vb6.exe writes no severity: the log says "Compile Error in File" and nothing else, and a
            // compile error is never a warning. Read from the format rather than parsed out of it.
            yield return (
                DocumentWireName.For(document),
                new Diagnostic(range, message, DiagnosticSeverity.Error, "VB6 Compiler"));
        }
    }

    /// <summary>
    /// Turns the line number <c>vb6.exe</c> printed into a 0-based line in the editor's buffer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>VB6's <c>N</c> is a 0-based index into the CODE VIEW</b>, which is the file minus its header and
    /// minus <b>every</b> <c>Attribute</c> line, module-level and procedure-level alike. Measured across
    /// nine probes; see the oracle. Both of the obvious readings are wrong: it is not a file line, and it
    /// is not 1-based.
    /// </para>
    /// <para>
    /// <b>Counted rather than offset.</b> The hidden total is not a constant per file kind -- a class
    /// module with two procedure attributes above the error hides fifteen lines where one without them
    /// hides thirteen. So this walks the buffer skipping exactly what VB6 skipped, which needs no table
    /// and cannot drift from one.
    /// </para>
    /// <para>
    /// <b>The buffer is the whole file (#273 task 3.2), so the header's lines come first.</b> A form's or a
    /// designer module's designer block, and a module's own header, are all lines VB6 did not count, so the
    /// walk is over the code and the header's line count is added after. Inside the code a form's
    /// <c>Attribute</c> run and every procedure-level one are skipped, as VB6 skipped them.
    /// </para>
    /// </remarks>
    internal static int EditorLineOf(DocumentIdentity document, int printedLine)
    {
        var code = document.Module?.Code ?? document.Form?.Code;
        var header = document.Module is { } module ? FormCodeText.Prefix(module)
            : document.Form is { } form ? FormCodeText.Prefix(form) : "";
        var headerLines = header.Count(c => c == '\n');
        if (string.IsNullOrEmpty(code)) return headerLines + Math.Max(printedLine, 0);

        var lines = code.Replace("\r\n", "\n").Split('\n');
        var counted = -1;
        for (var i = 0; i < lines.Length; i++)
        {
            if (IsHiddenFromVb6LineCount(lines[i])) continue;
            if (++counted == printedLine) return headerLines + i;
        }

        // Past the end of what we hold. Clamped rather than dropped: the developer is better served by a
        // marker on the last line with the compiler's message than by silence about a build that failed.
        return headerLines + Math.Max(lines.Length - 1, 0);
    }

    /// <summary>A line the compiler did not count when numbering the code view.</summary>
    private static bool IsHiddenFromVb6LineCount(string line) =>
        line.TrimStart().StartsWith("Attribute ", StringComparison.OrdinalIgnoreCase);
}
