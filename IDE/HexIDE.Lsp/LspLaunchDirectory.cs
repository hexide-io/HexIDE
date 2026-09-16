using Microsoft.Extensions.Logging;

namespace HexIDE.Lsp;

/// <summary>
/// Where a launched language server's process should RUN — in one place, because two transports need the
/// same answer and a rule asserted twice is a rule only by coincidence.
/// </summary>
/// <remarks>
/// <b>This is not <see cref="ILspWorkspace.Directory"/>, and the difference is the whole point.</b> "Where
/// the process runs" and "which tree it analyses" are separate questions that happen to coincide most of
/// the time. The second is answered by <c>rootUri</c>, <c>workspaceFolders</c> and the <c>{workspaceUri}</c>
/// placeholder, and is deliberately left alone here: a workspace that does not exist yet is still the
/// directory the project's files are about to be written into, and the server should be told so.
///
/// <para>
/// <b>The case that made this necessary (hexide-io/HexIDE#278).</b> An unsaved project's directory is a
/// scratch path under TEMP that <c>ProjectService.ProjectFilesDirectory</c> mints and memoises without
/// creating — the four call sites that write a file into it create it at that moment, which is the correct
/// owner. So between File → New Project and the first save, the path is a real string naming nothing. Handed
/// to <see cref="System.Diagnostics.ProcessStartInfo.WorkingDirectory"/> it throws (<c>The directory name is
/// invalid</c>), the transport returns no handler, and the connection goes to <c>Failed</c> — which is
/// terminal for the session. The user gets an IDE with no language features and no stated reason.
/// </para>
///
/// <para>
/// <b>Why fall back rather than create the directory.</b> The stdio transport already argued this for the
/// no-workspace case and the argument carries: inventing a temp path "would silently point a server's
/// configuration lookup somewhere the user has never heard of". Creating the scratch directory IS inventing
/// that path. Three further costs. Nothing in the tree ever deletes a scratch directory, so creating one per
/// server start would litter TEMP with empty GUID-suffixed directories for every unsaved project ever
/// opened, with no owner to reap them — today a directory there means real content. It would also put
/// filesystem I/O behind a hot static property that the workspace consults on every <c>didOpen</c>, and have
/// the unit suite create directories merely for asserting a path's shape. And it would buy no guarantee
/// anyway: the directory can vanish between the create and the start, so the honest failure message below is
/// needed either way.
/// </para>
///
/// <para>
/// <b>An explicit working directory is never checked.</b> Somebody who named one meant it, and a name that
/// does not resolve is a configuration error they can fix and must be told about. Silently inheriting there
/// would start the server against the wrong tree — a wrong answer rather than a failure, which is the
/// outcome the whole workspace/launch split exists to prevent.
/// </para>
/// </remarks>
public static class LspLaunchDirectory
{
    /// <summary>
    /// The working directory to launch with: the explicit setting if there is one, else the workspace if it
    /// exists on disk, else empty — which hands the child the IDE's own working directory, the same thing
    /// .NET does when none is given.
    /// </summary>
    /// <param name="explicitDirectory">What the server's own registration named, if anything.</param>
    /// <param name="workspace">The open project, consulted only when there is no explicit setting.</param>
    /// <param name="logger">Told why, when a workspace directory is skipped. See the call for the level.</param>
    public static string For(string? explicitDirectory, ILspWorkspace? workspace, ILogger logger)
    {
        if (!string.IsNullOrWhiteSpace(explicitDirectory))
            return explicitDirectory;

        if (workspace?.Directory is not { } directory || string.IsNullOrWhiteSpace(directory))
            return "";

        // No try/catch, and that is deliberate rather than an oversight: Directory.Exists answers false for
        // a malformed, too-long or unauthorised path instead of throwing, which is exactly the answer this
        // wants. Every one of those cases should degrade to an inherited directory, not to an exception.
        if (Directory.Exists(directory))
            return directory;

        // Information, not Warning, and the message itself is the argument: this fires on every project
        // that opens a code window before being saved, which is the common path rather than an exceptional
        // one. A warning that says "this is normal" trains its reader to skip warnings, which is the cost
        // this repository has already priced elsewhere. Information is the default minimum level, so the
        // line is still there for whoever is asking why a server resolved its configuration oddly.
        logger.LogInformation(
            "Workspace directory '{Directory}' does not exist yet, so the language server will run in "
            + "HexIDE's own working directory instead. Expected for a project that has not been saved; the "
            + "server is still told that directory as the workspace to analyse.",
            directory);

        return "";
    }
}
