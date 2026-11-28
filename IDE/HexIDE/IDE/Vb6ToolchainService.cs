using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using HexIDE.Events;
using HexIDE.Localization;
using HexIDE.Projects;
using HexIDE.Lsp;
using HexIDE.Runtime.ProjectElements;
using Serilog;
using HexIDE.Sidecar;

namespace HexIDE.IDE;

/// <summary>
/// Make with VB6 and Run > Start with VB6 as a user meets them (#3411 slice 2.8, #5621): the dialogs, the trust prompt Start
/// with VB6 asks first, the compiler's diagnostics in the editor, the last-run record, and starting what was made.
/// </summary>
/// <remarks>
/// The build is the project type's toolchain, which its maker brings: for VB6's type, the Basic provider's, which finds
/// <c>VB6.EXE</c>, builds and reads its log. With no maker registered for the project's type, or no compiler installed,
/// neither command is offered.
/// </remarks>
public class Vb6ToolchainService : IVb6ToolchainService
{
    private readonly ILspClient lspClient;
    private readonly IEventBus eventBus;
    private readonly IWindowManager windowManager;
    private readonly ILocalizationService _localization;
    private readonly CodeTrust trust;
    private readonly IUserSidecarService sidecar;
    private readonly ProjectMakers makers;

    public Vb6ToolchainService(ILspClient lspClient, IEventBus eventBus, IWindowManager windowManager, ILocalizationService localization,
        CodeTrust trust, IUserSidecarService sidecar, ContributedProjects contributed)
    {
        this.trust = trust;
        this.sidecar = sidecar;
        this.lspClient = lspClient;
        this.eventBus = eventBus;
        this.windowManager = windowManager;
        _localization = localization;
        makers = contributed.Makers;
    }

    public bool IsAvailableFor(ProjectDefinition? project) => makers.InstalledToolchainFor(project) is not null;

    public Task<bool> MakeWithVb6Async(ProjectDefinition project) => BuildAsync(project, forRun: false);

    private async Task<bool> BuildAsync(ProjectDefinition project, bool forRun)
    {
        if (makers.For(project) is not { } maker || makers.InstalledToolchainFor(project) is not { } toolchain)
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

        // The program's name comes from the project. VB6 writes it where a path there points when it builds in place, and
        // the build would move it there, and Start with VB6 would start it there, so one that isn't a plain file name is
        // refused before VB6 starts (#5622).
        string exePath;
        try
        {
            if (maker.RefusedProgramName(project) is { } refused)
            {
                await windowManager.MessageBox(
                    string.Format(_localization.GetString("Str.Naming.Msg.NotAFileName"), project.Name, refused.Field, refused.Value),
                    _localization.GetString("Str.Vb6Toolchain.BuildFailedTitle"), MessageBoxButtons.Ok, MessageBoxIcon.Warning);
                return false;
            }
            exePath = maker.ProgramPath(project);
        }
        catch (Exception ex)
        {
            return await ReportThrownAsync(ex, project);
        }

        if (File.Exists(exePath) && !(forRun && IsLastRunExe(project, exePath)))
        {
            if (await windowManager.MessageBox(
                    string.Format(_localization.GetString("Str.FilePicker.ReplaceExisting"), Path.GetFileName(exePath)),
                    buttons: MessageBoxButtons.YesNo, icon: MessageBoxIcon.Warning,
                    layout: MessageBoxLayout.SecondButtonDefault) != MessageBoxResult.Yes)
                return false;
        }

        // Flush any pending in-memory changes to disk.
        eventBus.Publish(new ApplyAllUnsavedChangesEvent());
        await Task.Delay(150);

        string? recovery = null;
        ToolchainBuild? outcome;
        IReadOnlyList<(string Uri, HexIDE.Lsp.Messages.Diagnostic Diagnostic)> diagnostics;
        try
        {
            outcome = await toolchain.BuildAsync(project, exePath, path => recovery = path);
            diagnostics = outcome is { ExitCode: not 0 } ? toolchain.Diagnostics(outcome.Output, project) : [];
        }
        catch (Exception ex)
        {
            await lspClient.ClearDiagnosticsFromAsync(DiagnosticOwner.Vb6Compiler);
            await ReportThrownAsync(ex, project);
            // A build that threw after VB6 began writing in place has kept the program it replaced, so the user is told
            // where, as after a failed build.
            await ReportRecoveryAsync();
            return false;
        }

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
            Log.Warning("Vb6ToolchainService: the VB6 build of {Project} ran too long and was stopped", project.Name);
            await windowManager.MessageBox(
                _localization.GetString("Str.Vb6Toolchain.CompilationTimedOut"),
                _localization.GetString("Str.Vb6Toolchain.BuildFailedTitle"), MessageBoxButtons.Ok, MessageBoxIcon.Error);
            await ReportRecoveryAsync();
            return false;
        }

        if (outcome.ExitCode != 0)
        {
            var byUri = diagnostics
                .GroupBy(x => x.Uri)
                .ToDictionary(g => g.Key, g => g.Select(x => x.Diagnostic).ToArray());

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

        await ReportRecoveryAsync();
        return outcome.ExitCode == 0;

        async Task ReportRecoveryAsync()
        {
            if (recovery is not null)
                await windowManager.MessageBox(
                    string.Format(_localization.GetString("Str.Vb6Toolchain.PreviousOutputKept"), exePath, recovery),
                    _localization.GetString("Str.Vb6Toolchain.BuildFailedTitle"), MessageBoxButtons.Ok, MessageBoxIcon.Warning);
        }
    }

    // A maker that throws is a failed build: the IDE says so and stays usable, and nothing is started (#5621).
    private async Task<bool> ReportThrownAsync(Exception ex, ProjectDefinition project)
    {
        Log.Warning(ex, "Vb6ToolchainService: the toolchain for {Type} projects threw while building {Project}",
            project.ContributedBy, project.Name);
        await windowManager.MessageBox(
            string.Format(_localization.GetString("Str.Vb6Toolchain.CompilationFailed"), ex.Message),
            _localization.GetString("Str.Vb6Toolchain.BuildFailedTitle"), MessageBoxButtons.Ok, MessageBoxIcon.Error);
        return false;
    }

    public async Task RunWithVb6Async(ProjectDefinition project)
    {
        // VB6 runs the code it makes, so code from outside this computer is asked about first, as F5 asks (#1121). Asked
        // here, before anything of the toolchain's runs.
        if (trust.Unapproved(project) is { Count: > 0 } files && !await trust.AskAsync(project.Name, files))
            return;
        var success = await BuildAsync(project, forRun: true);
        if (!success)
            return;

        // /make uses Path32 only with ExeName32; otherwise the EXE sits beside the .vbp (#4538). The build just refused a
        // name that would put it elsewhere (#5622).
        string exePath;
        try
        {
            exePath = makers.For(project)?.ProgramPath(project) ?? throw new InvalidOperationException("Its maker has gone.");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Vb6ToolchainService: {Project}'s program couldn't be found after its build", project.Name);
            return;
        }

        if (!File.Exists(exePath))
        {
            await windowManager.MessageBox(
                string.Format(_localization.GetString("Str.Vb6Toolchain.ExeNotFound"), exePath),
                _localization.GetString("Str.Vb6Toolchain.RunFailedTitle"), MessageBoxButtons.Ok, MessageBoxIcon.Warning);
            return;
        }

        if (HashOf(exePath) is { } hash)
            sidecar.RecordLastRunExe(project, new LastRunExe(Path.GetFileName(exePath), hash));
        StartExe(exePath);
    }

    internal virtual void StartExe(string exePath) =>
        Process.Start(new ProcessStartInfo(exePath) { UseShellExecute = true });

    private bool IsLastRunExe(ProjectDefinition project, string exePath) =>
        sidecar.GetLastRunExe(project) is { } previous &&
        HexIDE.IO.HostFilePath.Comparer.Equals(previous.FileName, Path.GetFileName(exePath)) &&
        HashOf(exePath) is { } hash && hash == previous.Hash;

    private static string? HashOf(string exePath)
    {
        try
        {
            using var stream = File.OpenRead(exePath);
            return Convert.ToHexString(SHA256.HashData(stream));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warning(ex, "Could not hash VB6 output {Path}", exePath);
            return null;
        }
    }
}
