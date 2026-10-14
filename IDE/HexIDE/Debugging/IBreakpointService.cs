using System;
using System.Collections.Generic;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Debugging;

/// <summary>
/// The IDE-side store of breakpoints — 1-based file lines per document, matching ANTLR
/// <c>stmt.Start.Line</c> and AvaloniaEdit. In-memory; persistence is owned by <c>UserSidecarService</c> (the
/// per-user <c>&lt;project&gt;.user.hexproj</c> sidecar beside the .vbp, shared with bookmarks — an Evolution
/// improvement over VB6's session-only behaviour).
/// </summary>
/// <remarks>
/// <para>
/// <b>A line is counted from the top of the file, header included</b>, as the code window numbers it
/// (hexide-io/HexIDE#273): in a form that has a header, line 1 is its <c>VERSION</c> line, not the first line
/// of code. The interpreter is given the same whole file (<c>FormCodeText.WholeFile</c>), so its statement
/// lines are these lines and nothing converts between them. VB6's code window hid the header, so HexIDE's
/// numbers differ from VB6's there; that divergence is deliberate.
/// </para>
/// <para>
/// <b>Lines are bare integers, with no anchor.</b> Typing above a breakpoint does not move it. What does is
/// the IDE's own: when a committed designer change, a save or a reload changes the header's line count,
/// <c>HeaderRefresher.ShiftMarks</c> moves every mark below the header here, in the store, so a document with
/// no window open moves too; and the sidecar's load moves marks written before the header was counted. This
/// store accepts any line it is given. Refusing a new breakpoint on a line that never runs, in the header or
/// a member's <c>Attribute</c> lines, is its callers' job: the code window, and <c>MarkLineRules</c> for
/// automation.
/// </para>
/// <para>
/// Keyed by <see cref="DocumentIdentity"/>, not by a name or a URI. A key made from a name moves every
/// breakpoint on a document when the document is renamed, saved for the first time, saved elsewhere, or has
/// its project renamed — and two projects of a group each holding a <c>Module1</c> shared one key, so their
/// breakpoints bled into each other.
/// </para>
/// <para>
/// The runtime match is still by bare module name, because a run is one project and the interpreter's gate
/// knows nothing of identities. <c>ProjectRunnerService</c> is where the two meet, and it reads the name off
/// the identity at the moment it pushes.
/// </para>
/// </remarks>
public interface IBreakpointService
{
    /// <summary>Raised when the breakpoint set for a document changes (the argument is that document).</summary>
    event Action<DocumentIdentity>? BreakpointsChanged;

    /// <summary>Toggle a breakpoint on a 1-based line of a document.</summary>
    void Toggle(DocumentIdentity document, int line);

    /// <summary>Replace all breakpoints in a document with the given 1-based lines (empty clears them).</summary>
    void SetDocument(DocumentIdentity document, IEnumerable<int> lines);

    bool IsBreakpoint(DocumentIdentity document, int line);

    /// <summary>The breakpoint lines (1-based, ascending) for a document.</summary>
    IReadOnlyList<int> GetBreakpoints(DocumentIdentity document);

    /// <summary>Remove all breakpoints in a document.</summary>
    void ClearDocument(DocumentIdentity document);

    /// <summary>
    /// Remove every breakpoint held, in every loaded project.
    /// </summary>
    /// <remarks>
    /// Genuinely every one, not merely the startup project's: this is what Debug &gt; Clear All Breakpoints
    /// does today with a group loaded, and what it has always done. Whether VB6 agrees is unmeasured
    /// (hexide-io/HexIDE#492). Use <see cref="ClearProject"/> to forget one project's.
    /// </remarks>
    void ClearAll();

    /// <summary>
    /// Forget every document of a project, without touching any other project's.
    /// </summary>
    /// <remarks>
    /// Filters this store's own keys, and must keep doing so. Walking the project's current forms and
    /// modules instead looks equivalent and is not: a document <em>removed</em> from the project while it
    /// carried marks is in neither list, so its entry would be left behind — and the key is the definition,
    /// which holds its project, which holds every other document and buffer in it. An unreachable entry in
    /// a store that lives as long as the session would therefore pin the whole project graph in memory for
    /// the rest of it.
    /// </remarks>
    void ClearProject(ProjectDefinition project);

    /// <summary>All documents that currently have breakpoints, with their (1-based) lines.</summary>
    IReadOnlyDictionary<DocumentIdentity, IReadOnlyList<int>> All();
}
