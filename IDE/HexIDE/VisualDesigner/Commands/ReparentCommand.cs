using Avalonia;
using HexIDE.Runtime.Components;

namespace HexIDE.VisualDesigner.Commands;

/// <summary>
/// Moves one control from one container to another, as a single undoable step.
///
/// No gesture produces this yet — interactive re-parenting needs a container hit-test that is out of scope —
/// and it exists anyway because none of the other commands can express the change and one of them would get
/// it silently wrong. <c>MoveResizeCommand</c> stores <c>Left</c>/<c>Top</c> as the container-relative numbers
/// the model holds, so replaying it against a control that has since changed container restores a coordinate
/// measured from somewhere else. Anything that re-parents therefore has to go through here, and this is what
/// "here" is.
///
/// The rectangles are carried as well as the containers because a re-parent is always also a coordinate
/// change: the same position on screen is a different pair of numbers in the new container's space. The
/// sibling index is carried because sibling order is z-order.
///
/// Whatever eventually drives this from a drag must compose it into a <c>BatchCommand</c> and push it from
/// <c>EndDrag</c> after <c>IsDragging</c> is cleared — <c>DesignerUndoStack.Push</c> discards silently while a
/// drag is in progress.
/// </summary>
internal class ReparentCommand : IDesignerCommand
{
    private readonly ComponentInstanceViewModel _target;
    private readonly ComponentInstance? _oldContainer;
    private readonly ComponentInstance? _newContainer;
    private readonly Rect _oldRect;
    private readonly Rect _newRect;
    private readonly int _oldSiblingIndex;
    private readonly int _newSiblingIndex;

    public string Description { get; }

    internal ReparentCommand(
        ComponentInstanceViewModel target,
        ComponentInstance? oldContainer, ComponentInstance? newContainer,
        Rect oldRect, Rect newRect,
        int oldSiblingIndex, int newSiblingIndex,
        string description)
    {
        _target = target;
        _oldContainer = oldContainer;
        _newContainer = newContainer;
        _oldRect = oldRect;
        _newRect = newRect;
        _oldSiblingIndex = oldSiblingIndex;
        _newSiblingIndex = newSiblingIndex;
        Description = description;
    }

    public void Execute(FormEditViewModel vm) => Apply(_newContainer, _newRect, _newSiblingIndex);

    public void Undo(FormEditViewModel vm) => Apply(_oldContainer, _oldRect, _oldSiblingIndex);

    private void Apply(ComponentInstance? container, Rect rect, int siblingIndex)
    {
        // Containment first, then the geometry: the view-model's Left/Top setters subtract the container's
        // accumulated origin, so writing the position before the link would store it against the wrong space.
        _target.Instance.SetContainer(container, siblingIndex);
        _target.InvalidateContainer();

        // Relative values, written straight to the model rather than through the view-model, because the
        // rectangles were captured in the container's own space.
        _target.Instance.SetProperty(VBProperties.LeftProperty, rect.X);
        _target.Instance.SetProperty(VBProperties.TopProperty, rect.Y);
        _target.Instance.SetProperty(VBProperties.WidthProperty, rect.Width);
        _target.Instance.SetProperty(VBProperties.HeightProperty, rect.Height);
    }
}
