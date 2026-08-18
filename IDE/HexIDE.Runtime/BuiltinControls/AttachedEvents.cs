using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using HexIDE.Runtime.Components;

namespace HexIDE.Runtime.BuiltinControls;

public static class AttachedEvents
{
    public static void AttachFocusEvents<T>() where T : Control
    {
        InputElement.GotFocusEvent.AddClassHandler<T>((control, e) => control.ExecuteSub(ComponentBaseClass.GotFocusEvent));
        InputElement.LostFocusEvent.AddClassHandler<T>((control, e) => control.ExecuteSub(ComponentBaseClass.LostFocusEvent));
    }

    public static void AttachClick<T>() where T : Control
    {
        if (typeof(T).IsSubclassOf(typeof(Button)))
        {
            Button.ClickEvent.AddClassHandler<T>((control, e) => control.ExecuteSub(ComponentBaseClass.ClickEvent));
        }
        else
        {
            InputElement.PointerReleasedEvent.AddClassHandler<T>((control, e) =>
            {
                if (OwnsThePointerEvent(control, e.Source))
                    control.ExecuteSub(ComponentBaseClass.ClickEvent);
            });
        }
    }

    /// <summary>
    /// True when <paramref name="control"/> is the innermost VB6 control under the pointer.
    ///
    /// PointerReleased is a bubbling routed event and this is a CLASS handler, so once controls are nested a
    /// click on a button inside a Frame reaches the Frame as well. VB6 raises the innermost control's Click
    /// and nothing above it, so without this a single click fires <c>Command1_Click</c> AND
    /// <c>Frame1_Click</c> — and, once containers can hold containers, every ancestor's too.
    ///
    /// The walk stops at the first thing that is either us or another VB6 control, which is why it cannot be
    /// simplified to "is the source this control". Template parts are not VB6 controls and carry no name, so
    /// a click on the TextBlock inside a Label still walks up to the Label and counts as the Label's — the
    /// behaviour every non-container control has today and must keep.
    /// </summary>
    internal static bool OwnsThePointerEvent(Control control, object? source)
    {
        for (var visual = source as Visual; visual is not null; visual = visual.GetVisualParent())
        {
            if (ReferenceEquals(visual, control))
                return true;
            if (visual is Control candidate && VBProps.GetName(candidate) is not null)
                return false;
        }
        return false;
    }
}