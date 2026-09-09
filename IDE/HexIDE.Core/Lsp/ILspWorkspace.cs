using System.Collections.Generic;

namespace HexIDE.Lsp;

/// <summary>
/// Where a language server should think it is working.
///
/// <para>
/// Servers routinely resolve their own configuration relative to where they run — a Markdown linter reads
/// its rule file from the workspace, a formatter reads its style file. A server started somewhere arbitrary
/// therefore reads none of the user's settings for it and reports subtly different results with no
/// indication why, which is a wrong answer rather than a missing one.
/// </para>
///
/// <para>
/// <b>Asked at start rather than given at registration</b>, because servers start lazily — on the first
/// document of a language they claim — and which project is open by then is not knowable when the
/// registration is built.
/// </para>
///
/// <para>
/// Lives here, in the language layer's own vocabulary, rather than taking a dependency on the project
/// model: this layer needs one directory, not a project.
/// </para>
/// </summary>
public interface ILspWorkspace
{
    /// <summary>
    /// The current workspace directory, or null when there is no project open.
    ///
    /// <para>
    /// Null is a real answer and callers must handle it. A server rooted at nothing is worse than one
    /// rooted at the wrong place, because "wrong" is at least diagnosable.
    /// </para>
    /// </summary>
    string? Directory { get; }

    /// <summary>
    /// Every folder in the workspace, one per loaded project — empty when there is no project open.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="Directory"/> answers "the one root", which is only ever right for a single project. A
    /// <c>.vbg</c> group names its members by relative path, so its projects routinely live in completely
    /// different directories, and one root then means every server believes the workspace is wherever the
    /// STARTUP project happens to be. Changing which member is the startup project silently re-roots every
    /// server, including for documents that did not move.
    /// </para>
    ///
    /// <para>
    /// The protocol says the same thing: <c>rootUri</c> is deprecated in favour of <c>workspaceFolders</c>,
    /// plural, precisely because one root was never enough. <see cref="Directory"/> remains, because a
    /// server that reads only <c>rootUri</c> still has to be told something.
    /// </para>
    /// </remarks>
    IReadOnlyList<LspWorkspaceFolder> Folders { get; }
}

/// <summary>One folder of the workspace: a directory, and the name a user would recognise it by.</summary>
/// <remarks>
/// A directory rather than a URI, because expressing a path as a URI can fail and the language layer
/// already owns that conversion and its error handling. This type says where, not how to spell it.
/// </remarks>
public sealed record LspWorkspaceFolder(string Name, string Directory);
