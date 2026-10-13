using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HexIDE.Bookmarks;
using HexIDE.Debugging;
using HexIDE.Events;
using HexIDE.Forms.ViewModels;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;
using Serilog;

namespace HexIDE.IDE;

/// <summary>
/// The one implementation of <see cref="IHeaderRefresher"/>. The contract, and why the seam is where it is,
/// are on the interface; what is here is how each trigger reaches it.
/// </summary>
public sealed class HeaderRefresher : IHeaderRefresher, IDisposable
{
    private readonly IDocumentDockService dock;
    private readonly IBreakpointService breakpoints;
    private readonly IBookmarkService bookmarks;
    private readonly IDisposable subscription;

    public HeaderRefresher(
        IEventBus eventBus,
        IDocumentDockService dock,
        IBreakpointService breakpoints,
        IBookmarkService bookmarks)
    {
        this.dock = dock;
        this.breakpoints = breakpoints;
        this.bookmarks = bookmarks;
        subscription = eventBus.Subscribe<FormLayoutChangedEvent>(e => LayoutChanged(e.Form));
    }

    public void Dispose() => subscription.Dispose();

    /// <summary>
    /// Trigger one: a committed change to a form's designer half. Re-renders the designer text and makes it
    /// the header, when the render moved and when the form is one HexIDE may render at all.
    /// </summary>
    /// <remarks>
    /// <b>One gate: a form HexIDE cannot reproduce is never re-rendered.</b> <c>FormSerializer</c> carries
    /// no fidelity check of its own — <c>SerializeFormToFile</c> refuses <em>before</em> calling it — so a
    /// refresh without this would put a flattened menu hierarchy in the code window as though it were the
    /// file, for exactly the forms whose save is refused to stop that reaching disk. The developer's window
    /// would then disagree with both the file and the refusal. That gate is also the whole of the design
    /// record's "a form held read-only is never re-rendered": being unable to reproduce a form is the only
    /// thing in the tree that holds one read-only, and both read-only properties that can be backed by a
    /// form are the same expression over <c>CanSaveFaithfully</c>.
    ///
    /// <para>
    /// <b>A form with no file is rendered, under the name its first save will use</b> (task 3.4). It used
    /// to be skipped. The name only reaches the output through the companion citations, so for the forms
    /// that carry no blob — which is every form HexIDE has just created — this changes the render not at
    /// all; it is right for the one that has been pasted into from a form that was loaded.
    /// </para>
    ///
    /// <para>
    /// <b>That branch cannot reach an unfaithful form, and the reason is structural rather than checked
    /// twice.</b> Only <c>FormDeserializer</c> ever marks a form unfaithful, so a form with no file has
    /// never been through it and is faithful by construction. The gate above still runs, because a form
    /// that HAS a file reaches the same line.
    /// </para>
    ///
    /// <para>
    /// Both refusals log at Debug, because "the header did not refresh" is otherwise indistinguishable from
    /// "nothing was raised".
    /// </para>
    /// </remarks>
    public void LayoutChanged(FormDefinition form)
    {
        var identity = DocumentIdentity.For(form);

        if (!form.CanSaveFaithfully)
        {
            Log.Debug("HeaderRefresher: not re-rendering {Document} — {Reason}",
                identity.Display, form.UnfaithfulSaveReason);
            return;
        }

        if (FormCodeText.RenderFileNameFor(form) is not { } fileName)
        {
            Log.Debug("HeaderRefresher: not re-rendering {Document} — it has no name to render against",
                identity.Display);
            return;
        }

        var header = new FormSerializer().SerializeDesignerText(form, fileName);

        // One undo entry per commit, however many of the IDE's writes it takes. A rename moves both the
        // designer block's Begin line and the code section's VB_Name, and two entries would let any undo that
        // does not come through UndoRequested (#513 lists the routes that still do not) separate the two
        // halves of one gesture. AvaloniaEdit folds a nested group into the outer one.
        var undo = EditorFor(identity)?.Document.UndoStack;
        undo?.StartUndoGroup();
        try
        {
            ApplyHeader(form, header);
            NameChanged(identity);
        }
        finally
        {
            undo?.EndUndoGroup();
        }
    }

    /// <summary>
    /// Makes <paramref name="header"/> the document's header: recorded on the model, the marks below it
    /// moved, and an open buffer re-headed in place. Does nothing when it is the header already.
    /// </summary>
    /// <remarks>
    /// Also trigger two. A save passes the header it just wrote rather than letting this re-render, because
    /// the two can differ: the render a save makes is the one that reached disk, and a second render taken
    /// afterwards would be of a model that may have moved in between. It is called before the save is
    /// announced so that a server reading the buffer and a server reading the file agree about line one.
    /// </remarks>
    public void ApplyHeader(FormDefinition form, string header)
    {
        var identity = DocumentIdentity.For(form);
        var current = FormCodeText.Prefix(form);

        // The model half only when the model actually moves, because the marks move with it and a shift of
        // zero must not announce itself to two stores, three gutters and the sidecar.
        if (!string.Equals(current, header, StringComparison.Ordinal))
        {
            form.RecordDesignerText(header);
            ShiftMarks(identity, current, header);
        }

        // The buffer half unconditionally, and that is not belt-and-braces. An open window can hold a header
        // the model does not -- an undo puts the previous one back in the buffer while the model keeps the
        // current one -- and gating this on the model having moved would leave that window showing a header
        // for a form that no longer looks like it, permanently: the next render equals what is recorded, so
        // nothing would ever repair it. RefreshPrefix is a no-op when the two already agree.
        EditorFor(identity)?.RefreshPrefix(header);
    }

    /// <summary>
    /// Makes the <c>Attribute VB_Name</c> the document carries say the document's current name, on the model
    /// and in an open buffer (task 3.5; hexide-io/HexIDE#473).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Where the line lives depends on the kind, and so does the write.</b> A <c>.bas</c> or <c>.cls</c>
    /// keeps it in the header, which the model already renders from the live name, so only an open buffer
    /// can be stale and re-heading it is the whole job. A form keeps it in <c>Code</c>, and a UserControl or
    /// PropertyPage in its module's <c>Code</c>, because their code section opens with the attribute run —
    /// so for those the model's text is rewritten too, which is what makes a closed document's next save say
    /// the right thing.
    /// </para>
    /// <para>
    /// <b>It follows the document's name, which for a UserControl is its module's.</b> Renaming the root of a
    /// UserControl in its designer renames the <c>Begin</c> line and nothing else, because the module's name
    /// is a separate field nothing connects to it — the rename itself is missing (hexide-io/HexIDE#493), and
    /// following the root here would make the file disagree with the name the project knows it by instead.
    /// </para>
    /// <para>
    /// <b>No mark moves.</b> One line is replaced by one line, so no line number below it changes.
    /// </para>
    /// </remarks>
    public void NameChanged(DocumentIdentity document)
    {
        var name = document.Name;
        if (name.Length == 0)
            return;

        if (document.Module is { } module)
        {
            if (!ModuleFileFormat.HandlesHeader(module.Kind))
                RetargetCode(module.Code, name, module.UpdateCode);
        }
        else if (document.Form is { } form)
        {
            RetargetCode(form.Code, name, form.UpdateCode);
        }

        if (EditorFor(document) is { } editor)
        {
            if (document.Module is { } headed && ModuleFileFormat.HandlesHeader(headed.Kind))
                editor.RefreshPrefix(FormCodeText.Prefix(headed));
            editor.RetargetVbName(name);
        }
    }

    private static void RetargetCode(string code, string name, Action<string> update)
    {
        var retargeted = FormCodeText.RetargetVbName(code, name);
        if (!ReferenceEquals(retargeted, code))
            update(retargeted);
    }

    /// <summary>
    /// Moves a document's breakpoints and bookmarks by the change in the header's line count.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>In the stores, not in a gutter.</b> Both gutters read their store live and repaint on its change
    /// event, the sidecar saves from it and the debugger is re-pushed from it, so a shift written here
    /// reaches all four — and reaches a document nobody has open, which a gutter could not.
    /// </para>
    /// <para>
    /// <b>A mark inside the header stays where it is.</b> The header is being replaced by another header, so
    /// a mark on line 3 of it is still on line 3 of it. Only marks below the old header move, and the
    /// arithmetic makes that automatic: a mark at or past the first body line lands at or past the new one.
    /// Task 3.7 stops a mark being set in the header at all; until then this is the honest answer rather
    /// than a clamp.
    /// </para>
    /// <para>
    /// The two stores disagree about the base — breakpoints are 1-based and bookmarks 0-based, both stated
    /// to callers on the shipped automation tools — so the comparison differs by one between them and the
    /// shift does not.
    /// </para>
    /// </remarks>
    public void ShiftMarks(DocumentIdentity document, string oldHeader, string newHeader)
    {
        // The count the sidecar's migration shifts by as well, so the two can never disagree about a header.
        var oldLines = FormCodeText.LineBreaks(oldHeader);
        var delta = FormCodeText.LineBreaks(newHeader) - oldLines;
        if (delta == 0)
            return;

        var marks = breakpoints.GetBreakpoints(document);
        if (marks.Count > 0)
            breakpoints.SetDocument(document, Shift(marks, oldLines + 1, delta));

        var marked = bookmarks.GetBookmarks(document);
        if (marked.Count > 0)
            bookmarks.SetBookmarks(document, Shift(marked, oldLines, delta));
    }

    /// <summary>Every line at or after <paramref name="firstBodyLine"/> moved by <paramref name="delta"/>.</summary>
    private static IEnumerable<int> Shift(IReadOnlyList<int> lines, int firstBodyLine, int delta) =>
        lines.Select(line => line >= firstBodyLine ? line + delta : line).ToList();

    private CodeEditorViewModel? EditorFor(DocumentIdentity document) =>
        dock.OpenDocuments.OfType<CodeEditorViewModel>()
            .FirstOrDefault(editor => editor.OpenDocument == document);
}
