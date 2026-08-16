using System;
using System.Collections.Generic;

namespace HexIDE.Debugging;

/// <summary>
/// The IDE-side store of breakpoints — 1-based source lines per document URI (<c>vb6://form/{name}</c> /
/// <c>vb6://module/{name}</c>), matching ANTLR <c>stmt.Start.Line</c> and AvaloniaEdit. In-memory; persistence is
/// owned by <c>UserSidecarService</c> (the per-user <c>&lt;project&gt;.user.hexproj</c> sidecar beside the .vbp, shared
/// with bookmarks — an Evolution improvement over VB6's session-only behaviour). The runtime match uses the bare
/// module name; this side keeps the full URI for the editor/margin.
/// </summary>
public interface IBreakpointService
{
    /// <summary>Raised when the breakpoint set for a document changes (the argument is its URI).</summary>
    event Action<string>? BreakpointsChanged;

    /// <summary>Toggle a breakpoint on a 1-based line of a document.</summary>
    void Toggle(string documentUri, int line);

    /// <summary>Replace all breakpoints in a document with the given 1-based lines (empty clears them).</summary>
    void SetDocument(string documentUri, IEnumerable<int> lines);

    bool IsBreakpoint(string documentUri, int line);

    /// <summary>The breakpoint lines (1-based, ascending) for a document.</summary>
    IReadOnlyList<int> GetBreakpoints(string documentUri);

    /// <summary>Remove all breakpoints in a document.</summary>
    void ClearDocument(string documentUri);

    /// <summary>Remove all breakpoints in the whole project.</summary>
    void ClearAll();

    /// <summary>All documents that currently have breakpoints, with their (1-based) lines.</summary>
    IReadOnlyDictionary<string, IReadOnlyList<int>> All();
}
