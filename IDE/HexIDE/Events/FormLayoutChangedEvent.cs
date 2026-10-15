using HexIDE.IDE;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Events;

/// <summary>
/// A committed change to a form's designer half: the layout, a control's properties, the menu, the form's
/// own name. Raised once per commit, never per property write.
/// </summary>
/// <remarks>
/// <para>
/// <b>Per commit, because the alternative is per pixel.</b> A drag writes the model on every mouse move —
/// the resize adorner sets <c>Canvas.Left</c> through a two-way binding straight onto the
/// <c>ComponentInstance</c> — so a notification per property write would mean a full re-render of the
/// designer text and a whole-document <c>didChange</c> to every attached language server for each pixel of
/// a drag. <c>DesignerUndoStack</c> already has exactly the right shape: it discards pushes while a drag is
/// in flight and takes a single command when the drag ends.
/// </para>
/// <para>
/// <b>Raised by four paths, three of which are not the undo stack.</b> The designer's own gestures all
/// commit through <c>DesignerUndoStack</c>, but the menu editor, the colour palette and automation's
/// <c>set_control_property</c> mutate the model without pushing anything. They raise this instead, so there
/// is one signal rather than one per surface. See hexide-io/HexIDE#273 task 3.3.
/// </para>
/// <para>
/// <b>The publisher flushes first.</b> The designer's working collections run ahead of
/// <c>FormDefinition.Components</c> until <see cref="ApplyAllUnsavedChangesEvent"/>, so anything that
/// re-renders the form on this notification would miss the control just added. <c>FormEditViewModel</c>'s
/// <c>CommitLayout</c> is the publisher for every path that has a designer open, and it flushes.
/// </para>
/// </remarks>
public class FormLayoutChangedEvent : IEvent
{
    public FormLayoutChangedEvent(FormDefinition form)
    {
        Form = form;
    }

    /// <summary>
    /// The form whose designer half changed — for a UserControl or PropertyPage, its designer half rather
    /// than the module that owns the code. <c>DocumentIdentity.For</c> resolves the two to one identity.
    /// </summary>
    public FormDefinition Form { get; }
}
