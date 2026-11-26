using System.Diagnostics;
using System.Text.RegularExpressions;
using HexIDE.IDE;
using HexIDE.Language;
using HexIDE.Lsp;
using HexIDE.Lsp.Messages;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;
using Serilog;

namespace HexIDE.Runtime.Making;

/// <summary>
/// VB6's own toolchain, <c>VB6.EXE /make</c>, which the Basic provider offers for VB6's projects (#3411 slice 2.8,
/// #5621): Make with VB6, and the build Run > Start with VB6 runs first.
/// </summary>
/// <remarks>
/// <para>
/// Moved from the IDE's <c>Vb6ToolchainService</c>, which keeps what a user meets: its dialogs, the trust prompt, the
/// compiler's diagnostics in the editor, and starting the program. This finds the compiler, builds, and reads its log.
/// </para>
/// <para>
/// <b>Where the compiler is comes from the user's machine, never from a project:</b> the <c>VB6_EXE</c> variable, then
/// VB6's install folders, read once when this is made. Nothing a project holds changes which program starts.
/// </para>
/// </remarks>
internal class Vb6Toolchain : IProjectToolchain
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
    // `/out`, and what "Line N" counts" in docs/vb6-fidelity-oracle/what-make-writes-to-out-and-what-line-n-counts.md:
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

    /// <summary>The compiler this runs, or null when none was found.</summary>
    public string? Vb6ExePath { get; }

    public bool IsInstalled => Vb6ExePath != null;

    public Vb6Toolchain()
    {
        var envPath = Environment.GetEnvironmentVariable("VB6_EXE");
        Vb6ExePath = envPath != null && File.Exists(envPath)
            ? envPath
            : DefaultPaths.FirstOrDefault(File.Exists);
    }

    public Task<ToolchainBuild?> BuildAsync(ProjectDefinition project, string program, Action<string> keepRecovery) =>
        BuildAndPublishAsync(project, program, keepRecovery);

    public IReadOnlyList<(string Uri, Diagnostic Diagnostic)> Diagnostics(string output, ProjectDefinition project) =>
        [.. ParseVb6Errors(output, project)];

    private async Task<ToolchainBuild?> BuildAndPublishAsync(ProjectDefinition project, string destination,
        Action<string> keepRecovery)
    {
        string? staging = null;
        try
        {
            var folder = Path.GetDirectoryName(destination)!;
            if (project.ProjectType != VBProjectType.EXE || !Directory.Exists(folder))
                return await BuildInPlaceAsync(project, destination, keepRecovery);

            var original = File.ReadAllBytes(project.AbsolutePath!);
            // /outdir writes AutoIncrementVer projects back and ActiveX outputs register in place
            // (oracle PR #4756, issue #4757). Those must keep building at their final location.
            if (Regex.IsMatch(System.Text.Encoding.Latin1.GetString(original),
                    @"^[\t ]*AutoIncrementVer[\t ]*=[\t ]*1[\t ]*\r?$", RegexOptions.Multiline | RegexOptions.IgnoreCase))
                return await BuildInPlaceAsync(project, destination, keepRecovery);

            staging = Path.Combine(folder, ".hexide-vb6make-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(staging);
            ToolchainBuild? outcome;
            try
            {
                outcome = await RunVb6Async(project.AbsolutePath!, staging);
            }
            finally
            {
                RestoreMakePath(project.AbsolutePath!, original);
            }
            if (outcome?.ExitCode == 0)
            {
                var staged = Path.Combine(staging, Path.GetFileName(destination));
                using (var stream = new FileStream(staged, FileMode.Open, FileAccess.Write, FileShare.None))
                    stream.Flush(flushToDisk: true);
                try
                {
                    File.Move(staged, destination, overwrite: true);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Windows' message doesn't name the file ("Access to the path is denied."), and an exe that's
                    // running is the usual reason, so say which one couldn't be replaced.
                    return new ToolchainBuild(1, destination + ": " + ex.Message);
                }
            }
            return outcome;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new ToolchainBuild(1, ex.Message);
        }
        finally
        {
            if (staging is not null)
            {
                try { Directory.Delete(staging, recursive: true); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    Log.Warning(ex, "Could not remove VB6 staging folder {Path}", staging);
                }
            }
        }
    }

    private async Task<ToolchainBuild?> BuildInPlaceAsync(ProjectDefinition project, string destination,
        Action<string> keepRecovery)
    {
        string? recovery = null;
        if (Directory.Exists(Path.GetDirectoryName(destination)) && File.Exists(destination))
        {
            recovery = destination + "." + Guid.NewGuid().ToString("N") + ".recovery";
            var created = false;
            try
            {
                using var input = File.OpenRead(destination);
                using var copy = new FileStream(recovery, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                created = true;
                input.CopyTo(copy);
                copy.Flush(flushToDisk: true);
            }
            catch
            {
                if (created) TryDeleteRecovery(recovery);
                throw;
            }
        }

        ToolchainBuild? outcome;
        try
        {
            outcome = await RunVb6Async(project.AbsolutePath!, null);
        }
        catch
        {
            // A build that never reported is settled as a failed one: the copy goes only where it's no longer needed.
            if (recovery is not null) SettleAfterFailure(recovery);
            throw;
        }
        if (outcome?.ExitCode == 0)
        {
            try
            {
                using var output = new FileStream(destination, FileMode.Open, FileAccess.Write, FileShare.None);
                output.Flush(flushToDisk: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Warning(ex, "Could not flush VB6 output {Path}", destination);
            }
            if (recovery is not null) TryDeleteRecovery(recovery);
            return outcome;
        }

        if (recovery is not null) SettleAfterFailure(recovery);
        return outcome;

        void SettleAfterFailure(string copy)
        {
            try
            {
                var unchanged = File.Exists(destination) &&
                    File.ReadAllBytes(destination).AsSpan().SequenceEqual(File.ReadAllBytes(copy));
                if (!unchanged)
                {
                    // Registration timing is unmeasured (#4952); only a Standard EXE is safe to restore.
                    if (project.ProjectType != VBProjectType.EXE)
                    {
                        keepRecovery(copy);
                        return;
                    }
                    AtomicFileWriter.Copy(copy, destination);
                }
                TryDeleteRecovery(copy);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Warning(ex, "Could not restore VB6 output {Path}; previous output kept at {Recovery}", destination, copy);
                keepRecovery(copy);
            }
        }
    }

    private static void TryDeleteRecovery(string path)
    {
        try { File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warning(ex, "Could not remove VB6 recovery copy {Path}", path);
        }
    }

    private static void RestoreMakePath(string projectPath, byte[] original)
    {
        var updated = File.ReadAllBytes(projectPath);
        if (original.AsSpan().SequenceEqual(updated)) return;

        // Latin-1 is a byte-preserving view: repair the ASCII key without re-encoding the rest
        // of VB6's write-back, including its line endings and any ANSI or UTF-8 source bytes.
        var codec = System.Text.Encoding.Latin1;
        var pathLine = new Regex(@"^[\t ]*Path32[\t ]*=[^\r\n]*(?:\r\n|\r|\n|$)",
            RegexOptions.Multiline | RegexOptions.IgnoreCase);
        var previous = pathLine.Match(codec.GetString(original));
        var text = codec.GetString(updated);
        if (pathLine.IsMatch(text))
            text = pathLine.Replace(text, _ => previous.Success ? previous.Value : "", 1);
        else if (previous.Success)
            text += (text.EndsWith('\n') || text.EndsWith('\r') ? "" : "\r\n") + previous.Value;
        AtomicFileWriter.WriteAllBytes(projectPath, codec.GetBytes(text));
    }

    /// <summary>
    /// What starts the compiler for one build: <see cref="Vb6ExePath"/>, found on this machine, with the project's file
    /// and the folders as its arguments. Nothing in the project picks the program (#5621).
    /// </summary>
    internal ProcessStartInfo StartInfo(string projectPath, string? outputDirectory, string logPath) =>
        new(Vb6ExePath ?? throw new InvalidOperationException("VB6.EXE wasn't found, so nothing can be built with it."))
        {
            Arguments = $"/make \"{projectPath}\" /out \"{logPath}\"" +
                (outputDirectory is null ? "" : $" /outdir \"{outputDirectory}\""),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

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
    internal virtual async Task<ToolchainBuild?> RunVb6Async(string projectPath, string? outputDirectory)
    {
        // VB6.exe is a GUI app: under /make it writes compile errors to the file named by /out, not
        // reliably to stdout/stderr. Capture the /out log (the authoritative error source) as well as
        // the console streams, then parse all of them together.
        var logPath = Path.Combine(Path.GetTempPath(), $"hexide-vb6make-{Guid.NewGuid():N}.log");

        using var process = Process.Start(StartInfo(projectPath, outputDirectory, logPath))!;

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
            try
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
            catch { /* already exited */ }
            TryDeleteLog(logPath);
            return null;
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        var logText = ReadAndDeleteLog(logPath);

        return new ToolchainBuild(
            process.ExitCode,
            string.Join("\n", new[] { logText, stdout, stderr }.Where(s => !string.IsNullOrWhiteSpace(s))));
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
    /// minus <b>every</b> logical <c>Attribute</c> line and its continuations, module-level and procedure-level
    /// alike. Measured in the oracle, including #968. Both of the obvious readings are wrong: it is not a file line, and it
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
        var continued = false;
        var hidden = false;
        for (var i = 0; i < lines.Length; i++)
        {
            if (!continued) hidden = IsHiddenFromVb6LineCount(lines[i]);
            continued = ReadOnlyRegions.EndsWithContinuation(lines[i].AsSpan());
            if (hidden) continue;
            if (++counted == printedLine) return headerLines + i;
        }

        // Past the end of what we hold. Clamped rather than dropped: the developer is better served by a
        // marker on the last line with the compiler's message than by silence about a build that failed.
        return headerLines + Math.Max(lines.Length - 1, 0);
    }

    /// <summary>A logical line the compiler did not count when numbering the code view.</summary>
    private static bool IsHiddenFromVb6LineCount(string line) =>
        line.TrimStart().StartsWith("Attribute ", StringComparison.OrdinalIgnoreCase);
}
