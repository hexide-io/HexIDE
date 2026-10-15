using System;
using System.Collections.Generic;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Controls;

/// <summary>
/// Redraws the code window when an edit changes which lines are read-only other than by moving them, so
/// <see cref="ReadOnlyRegionColorizer"/>'s grey and <see cref="ReadOnlyRegionLinks"/>' plain addresses follow
/// (hexide-io/HexIDE#273 task 4.1).
/// </summary>
/// <remarks>
/// <para>
/// <b>AvaloniaEdit rebuilds only the lines an edit touches.</b> That was enough while a line's membership
/// depended on its own text. Since task 4.1 a member's attribute run depends on the line above it: commenting
/// out a procedure's declaration leaves its run under a comment, and retyping one variable of a line declaring
/// two leaves the run that named it naming nothing, and either makes the run an orphan without an edit touching
/// it. Its lines stayed grey, with any address in them still plain, and a run that came to describe a member
/// stayed drawn as code while refusing a keystroke. A language server hid it, because each of its publishes
/// redraws the view; with none attached, or one that does not publish again, it stayed.
/// </para>
/// <para>
/// <b>Compared after every change, and redrawn only on a difference.</b> The regions before the change are
/// moved through it and compared with the regions after it. Typing on a declaration moves the run under it and
/// changes nothing else, so an ordinary keystroke redraws no more than it did. A difference is a run turned into
/// an orphan or out of one, which is rare, so the whole view is redrawn rather than the lines worked out.
/// </para>
/// </remarks>
internal sealed class ReadOnlyRegionRedraw : IDisposable
{
    private readonly TextDocument document;
    private readonly TextView view;
    private readonly Func<IReadOnlyList<TextRegion>> regions;
    private IReadOnlyList<TextRegion> shown;
    private ITextSourceVersion shownAt;

    /// <param name="document">The document the view shows.</param>
    /// <param name="view">The view to redraw.</param>
    /// <param name="regions">The buffer's read-only regions as they stand, in document order.</param>
    public ReadOnlyRegionRedraw(TextDocument document, TextView view, Func<IReadOnlyList<TextRegion>> regions)
    {
        this.document = document;
        this.view = view;
        this.regions = regions;
        shown = regions();
        shownAt = document.Version;
        // Raised once a change is complete, and once for a group of them, so the regions read here are the
        // finished text's.
        document.TextChanged += OnTextChanged;
    }

    public void Dispose() => document.TextChanged -= OnTextChanged;

    private void OnTextChanged(object? sender, EventArgs e)
    {
        var now = regions();
        var version = document.Version;
        if (!OnlyMoved(shown, shownAt, now, version))
            view.Redraw();
        shown = now;
        shownAt = version;
    }

    /// <summary>
    /// True when <paramref name="after"/> is <paramref name="before"/> moved through the changes between their
    /// versions, region for region.
    /// </summary>
    private static bool OnlyMoved(IReadOnlyList<TextRegion> before, ITextSourceVersion beforeVersion,
        IReadOnlyList<TextRegion> after, ITextSourceVersion afterVersion)
    {
        if (before.Count != after.Count || !beforeVersion.BelongsToSameDocumentAs(afterVersion))
            return false;
        for (var i = 0; i < before.Count; i++)
        {
            if (beforeVersion.MoveOffsetTo(afterVersion, before[i].Start, AnchorMovementType.Default) != after[i].Start
                || beforeVersion.MoveOffsetTo(afterVersion, before[i].End, AnchorMovementType.Default) != after[i].End)
                return false;
        }
        return true;
    }
}
