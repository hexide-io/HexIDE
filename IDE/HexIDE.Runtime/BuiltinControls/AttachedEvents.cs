using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using HexIDE.Runtime.AvaloniaInterop;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.Interpreter;

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
    /// Raise MouseDown, MouseUp and MouseMove on <typeparamref name="T"/> (#974), with VB6's arguments: Button, Shift, X
    /// and Y, behind a control array's Index.
    /// </summary>
    /// <remarks>
    /// <para>Measured with vb6.exe, native and p-code agreeing (the oracle's "Mouse events"):</para>
    /// <list type="bullet">
    /// <item>Button is 1, 2 or 4 for the left, right and middle button; on MouseMove it is the buttons held.</item>
    /// <item>Shift is 1, 2 and 4 for Shift, Ctrl and Alt: the type library's vbShiftMask, vbCtrlMask and vbAltMask. VB6
    /// reads it from the keyboard, not the message, so it could not be measured from synthetic messages.</item>
    /// <item>X and Y are twips from the control's client origin, even on a pixel form; a form's and a PictureBox's follow
    /// their own ScaleMode. A 3-D bordered TextBox's and ListBox's client area starts 2 pixels in (GetClientRect).</item>
    /// <item>A CommandButton and a CheckBox raise MouseDown, Click, MouseUp: Click comes before MouseUp. A TextBox raises
    /// MouseDown, MouseUp, Click.</item>
    /// </list>
    /// <para>So the order is set by registration. A Button raises Click in its own pointer-release handler, which
    /// Avalonia registered first, so MouseUp follows it there. A control whose Click this class raises on release must
    /// attach the mouse events before its Click, so MouseUp comes first.</para>
    /// </remarks>
    public static void AttachMouse<T>() where T : Control
    {
        InputElement.PointerPressedEvent.AddClassHandler<T>(
            (control, e) => RaiseMouse(control, e, ComponentBaseClass.MouseDownEvent), handledEventsToo: true);
        InputElement.PointerReleasedEvent.AddClassHandler<T>(
            (control, e) => RaiseMouse(control, e, ComponentBaseClass.MouseUpEvent), handledEventsToo: true);
        InputElement.PointerMovedEvent.AddClassHandler<T>(
            (control, e) => RaiseMouse(control, e, ComponentBaseClass.MouseMoveEvent), handledEventsToo: true);
    }

    /// <summary>
    /// Raise <c>Form_MouseDown</c>, <c>Form_MouseUp</c> and <c>Form_MouseMove</c> from the form's client canvas, for a
    /// pointer over no control: a click on a Label is the Label's alone (measured). X and Y are in the form's ScaleMode.
    /// </summary>
    public static void AttachFormMouse(Canvas client)
    {
        void Handler(PointerEventArgs e, EventClass eventClass)
        {
            for (var visual = e.Source as Visual; visual is not null && !ReferenceEquals(visual, client); visual = visual.GetVisualParent())
                if (visual is Control candidate && VBProps.GetName(candidate) is not null)
                    return;
            Raise(client, e, eventClass, "Form", index: null, origin: client, VBProps.GetScaleMode(client), inset: 0);
        }
        client.AddHandler(InputElement.PointerPressedEvent, (_, e) => Handler(e, ComponentBaseClass.MouseDownEvent), handledEventsToo: true);
        client.AddHandler(InputElement.PointerReleasedEvent, (_, e) => Handler(e, ComponentBaseClass.MouseUpEvent), handledEventsToo: true);
        client.AddHandler(InputElement.PointerMovedEvent, (_, e) => Handler(e, ComponentBaseClass.MouseMoveEvent), handledEventsToo: true);
    }

    private static void RaiseMouse(Control control, PointerEventArgs e, EventClass eventClass)
    {
        if (!OwnsThePointerEvent(control, e.Source) || VBProps.GetName(control) is not { } name)
            return;
        // A PictureBox counts from inside its border, in its own ScaleMode, as its child host does. Every other control
        // counts in twips, from its client origin.
        var (origin, mode) = control is VBPictureBox { ChildHost: { } host }
            ? ((Visual)host, VBProps.GetScaleMode(host))
            : (control, VBScaleMode.Twip);
        Raise(control, e, eventClass, name, VBProps.GetIndex(control), origin, mode, VBProps.GetClientInset(control));
    }

    private static void Raise(Control source, PointerEventArgs e, EventClass eventClass, string name, int? index,
        Visual origin, VBScaleMode mode, double inset)
    {
        if (source.FindLogicalAncestorOfType<IModuleExecutionRoot>() is not { } root)
            return;
        var handler = $"{name}_{eventClass.Name}";
        if (!root.Handles(handler))
            return;
        var point = e.GetCurrentPoint(origin);
        var button = ReferenceEquals(eventClass, ComponentBaseClass.MouseMoveEvent)
            ? (point.Properties.IsLeftButtonPressed ? 1 : 0) | (point.Properties.IsRightButtonPressed ? 2 : 0)
              | (point.Properties.IsMiddleButtonPressed ? 4 : 0)
            : point.Properties.PointerUpdateKind switch
            {
                PointerUpdateKind.LeftButtonPressed or PointerUpdateKind.LeftButtonReleased => 1,
                PointerUpdateKind.RightButtonPressed or PointerUpdateKind.RightButtonReleased => 2,
                PointerUpdateKind.MiddleButtonPressed or PointerUpdateKind.MiddleButtonReleased => 4,
                _ => 0,
            };
        // VB6 has no event for the other buttons.
        if (button == 0 && !ReferenceEquals(eventClass, ComponentBaseClass.MouseMoveEvent))
            return;
        var shift = (e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 1 : 0) | (e.KeyModifiers.HasFlag(KeyModifiers.Control) ? 2 : 0)
                    | (e.KeyModifiers.HasFlag(KeyModifiers.Alt) ? 4 : 0);
        var (x, y) = (point.Position.X - inset, point.Position.Y - inset);
        root.ExecuteEvent(handler, () =>
        {
            var args = new List<Vb6Value>();
            if (index is { } i)
                args.Add(new Vb6Value(i));
            args.Add(new Vb6Value(button));
            args.Add(new Vb6Value(shift));
            args.Add(new Vb6Value(ControlPositions.MouseCoordinate(x, mode, vertical: false)));
            args.Add(new Vb6Value(ControlPositions.MouseCoordinate(y, mode, vertical: true)));
            return args;
        });
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