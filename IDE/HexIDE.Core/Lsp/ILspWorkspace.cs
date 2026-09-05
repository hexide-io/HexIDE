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
}
