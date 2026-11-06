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
    // The control's own focus only. GotFocus and LostFocus bubble, and the focus moving between the control and a part of
    // it, such as a ListBox's item or a ComboBox's text box, is neither: VB6 raises one GotFocus as the control is entered
    // and one LostFocus as it is left (measured, #1101). A control inside a container is its own, as a pointer event's is,
    // so a PictureBox loses the focus to a TextBox it holds.
    public static void AttachFocusEvents<T>() where T : Control
    {
        InputElement.GotFocusEvent.AddClassHandler<T>((control, e) =>
        {
            if (e is FocusChangedEventArgs moved
                && OwnsThePointerEvent(control, moved.NewFocusedElement) && !OwnsThePointerEvent(control, moved.OldFocusedElement))
                control.ExecuteSub(ComponentBaseClass.GotFocusEvent);
        }, handledEventsToo: true);
        InputElement.LostFocusEvent.AddClassHandler<T>((control, e) =>
        {
            if (e is FocusChangedEventArgs moved
                && OwnsThePointerEvent(control, moved.OldFocusedElement) && !OwnsThePointerEvent(control, moved.NewFocusedElement))
                control.ExecuteSub(ComponentBaseClass.LostFocusEvent);
        }, handledEventsToo: true);
    }

    public static void AttachClick<T>(ClickRelease release = ClickRelease.Anywhere) where T : Control
    {
        if (typeof(T).IsSubclassOf(typeof(Button)))
        {
            Button.ClickEvent.AddClassHandler<T>((control, e) => control.ExecuteSub(ComponentBaseClass.ClickEvent));
        }
        else
        {
            InputElement.PointerReleasedEvent.AddClassHandler<T>((control, e) =>
            {
                // A double click's second release raises no Click (measured, #1031).
                if (OwnsThePointerEvent(control, e.Source) && !SecondPresses.TryGetValue(control, out _)
                    && (release == ClickRelease.Anywhere || ReleasedOver(control, e)))
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
    public static void AttachMouse<T>(DblClick dblClick = DblClick.None, bool raiseDoubleClick = true, bool suppressSecondMouseUp = false) where T : Control
    {
        InputElement.PointerPressedEvent.AddClassHandler<T>((control, e) =>
        {
            var second = dblClick != DblClick.None && e.ClickCount % 2 == 0 && IsVbButton(e.GetCurrentPoint(control))
                         && OwnsThePointerEvent(control, e.Source);
            if (second)
                SecondPresses.AddOrUpdate(control, Marker);
            else
                SecondPresses.Remove(control);
            if (!second || dblClick == DblClick.AfterMouseDown)
                RaiseMouse(control, e, ComponentBaseClass.MouseDownEvent);
            if (second && raiseDoubleClick)
                control.ExecuteSub(ComponentBaseClass.DblClickEvent);
        }, handledEventsToo: true);
        InputElement.PointerReleasedEvent.AddClassHandler<T>((control, e) =>
        {
            if (!suppressSecondMouseUp || !IsSecondPress(control))
                RaiseMouse(control, e, ComponentBaseClass.MouseUpEvent);
        }, handledEventsToo: true);
        InputElement.PointerMovedEvent.AddClassHandler<T>(
            (control, e) => RaiseMouse(control, e, ComponentBaseClass.MouseMoveEvent), handledEventsToo: true);
    }

    /// <summary>
    /// Where a press's release has to land for the control to raise Click, measured with real input, native and p-code
    /// agreeing (the oracle's "A click with real input", #1373). The pressed control keeps the pointer either way, so
    /// its MouseUp comes wherever the release lands.
    /// </summary>
    public enum ClickRelease
    {
        /// <summary>Anywhere: a TextBox and a ListBox raise Click on any release of a press on them.</summary>
        Anywhere,
        /// <summary>Over the control again, whether or not the pointer left it: a Label, an Image, a Frame and a
        /// PictureBox. Released over the form or another control, they raise none.</summary>
        OverTheControl,
    }

    // Whether a release is over the control it ends a press on: hit-tested where it is, since the pressed control has
    // the pointer, and through the window, so a control drawn over this one has the point.
    private static bool ReleasedOver(Control control, PointerEventArgs e) =>
        TopLevel.GetTopLevel(control) is { } window && OwnsThePointerEvent(control, window.InputHitTest(e.GetPosition(window)));

    /// <summary>
    /// How a control's DblClick comes, measured with real input, native and p-code agreeing (the oracle's "A click with
    /// real input", #1031). Each control VB6 gives a DblClick raises it on a double click's second press, and that
    /// release raises no Click. A CommandButton and a CheckBox have none: a double click on either is two clicks.
    /// </summary>
    public enum DblClick
    {
        /// <summary>No DblClick: a double click is two clicks.</summary>
        None,
        /// <summary>The second press raises DblClick and no MouseDown: a Label, an Image, a PictureBox, a TextBox, a
        /// Frame, a ListBox, and the form.</summary>
        InPlaceOfMouseDown,
        /// <summary>The second press raises MouseDown, then DblClick: an OptionButton.</summary>
        AfterMouseDown,
    }

    // The controls whose current press is a double click's second, so that its release raises no Click.
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Control, object> SecondPresses = new();
    private static readonly object Marker = new();

    /// <summary>Whether the press now held on <paramref name="control"/> is a double click's second, whose release raises
    /// no Click.</summary>
    internal static bool IsSecondPress(Control control) => SecondPresses.TryGetValue(control, out _);

    // VB6 has no event for the other buttons, as in Raise.
    private static bool IsVbButton(PointerPoint point) => point.Properties.PointerUpdateKind
        is PointerUpdateKind.LeftButtonPressed or PointerUpdateKind.RightButtonPressed or PointerUpdateKind.MiddleButtonPressed;

    /// <summary>
    /// Raise <c>Form_MouseDown</c>, <c>Form_MouseUp</c>, <c>Form_MouseMove</c>, <c>Form_Click</c> and <c>Form_DblClick</c> from the form's client
    /// canvas, for a pointer over no control: a click on a Label is the Label's alone (measured). X and Y are in the form's
    /// ScaleMode.
    /// </summary>
    /// <remarks>
    /// Measured with real input, native and p-code agreeing (the oracle's "A click with real input", #1364):
    /// <list type="bullet">
    /// <item>Click comes on the release, after MouseUp, for every button, unlike a CommandButton's.</item>
    /// <item>Only when the press was on the form's own area and the release is over it again. Released over a control,
    /// windowless or not, or outside the window, the form has its MouseUp and no Click. So the release is hit-tested where
    /// it is: while the pointer is captured, the event's source is the canvas wherever the pointer went.</item>
    /// <item>The second press of a double click raises DblClick in place of MouseDown, and its release no Click
    /// (#1031).</item>
    /// </list>
    /// </remarks>
    public static void AttachFormMouse(Canvas client)
    {
        bool OverTheForm(object? source)
        {
            for (var visual = source as Visual; visual is not null && !ReferenceEquals(visual, client); visual = visual.GetVisualParent())
                if (visual is Control candidate && VBProps.GetName(candidate) is not null)
                    return false;
            return source is not null;
        }
        void Handler(PointerEventArgs e, EventClass eventClass)
        {
            if (OverTheForm(e.Source) && client.FindLogicalAncestorOfType<IModuleExecutionRoot>() is { } root)
                Raise(e, eventClass, [new ControlEvents.Handler(root, $"Form_{eventClass.Name}", true)], index: null, origin: client,
                    VBProps.GetScaleMode(client), inset: 0);
        }
        // Whether the press that a release ends began on the form, and was the first click of a double click or a lone one.
        var clicking = false;
        client.AddHandler(InputElement.PointerPressedEvent, (_, e) =>
        {
            clicking = OverTheForm(e.Source) && e.ClickCount % 2 == 1;
            if (e.ClickCount % 2 == 1)
                Handler(e, ComponentBaseClass.MouseDownEvent);
            else if (OverTheForm(e.Source) && IsVbButton(e.GetCurrentPoint(client))
                     && client.FindLogicalAncestorOfType<IModuleExecutionRoot>() is { } root && root.Handles("Form_DblClick"))
                root.ExecuteSub("Form_DblClick");
        }, handledEventsToo: true);
        client.AddHandler(InputElement.PointerReleasedEvent, (_, e) =>
        {
            Handler(e, ComponentBaseClass.MouseUpEvent);
            // VB6 has no event for the other buttons, as in Raise.
            var click = clicking && (e.InitialPressMouseButton is MouseButton.Left or MouseButton.Right or MouseButton.Middle)
                        && OverTheForm(client.InputHitTest(e.GetPosition(client)));
            var single = clicking;
            clicking = false;
            if (click && client.FindLogicalAncestorOfType<IModuleExecutionRoot>() is { } root && root.Handles("Form_Click"))
                root.ExecuteSub("Form_Click");
            // Real-input oracle #2417: only these left-release cases were measured. Hit-test after
            // MouseUp/Click, since capture still points at the pressed control in the routed event.
            Control? NamedControl(object? source)
            {
                for (var visual = source as Visual; visual is not null && !ReferenceEquals(visual, client); visual = visual.GetVisualParent())
                    if (visual is Control candidate && VBProps.GetName(candidate) is not null)
                        return candidate;
                return null;
            }
            var pressed = NamedControl(e.Source);
            if (e.InitialPressMouseButton == MouseButton.Left
                && (pressed is VBCommandButton || pressed is VBLabel or VBImage && !IsSecondPress(pressed)
                    || pressed is null && single))
            {
                var hit = client.InputHitTest(e.GetPosition(client));
                var target = NamedControl(hit);
                if (target is VBCommandButton or VBLabel or VBImage)
                    RaiseMouse(target, e, ComponentBaseClass.MouseMoveEvent, hit, () => client.IsEffectivelyVisible);
                else if (target is null && OverTheForm(hit)
                         && client.FindLogicalAncestorOfType<IModuleExecutionRoot>() is { } moveRoot)
                    Raise(e, ComponentBaseClass.MouseMoveEvent,
                        [new ControlEvents.Handler(moveRoot, "Form_MouseMove", true)], null, client,
                        VBProps.GetScaleMode(client), inset: 0, when: () => client.IsEffectivelyVisible);
            }
        }, handledEventsToo: true);
        client.AddHandler(InputElement.PointerMovedEvent, (_, e) => Handler(e, ComponentBaseClass.MouseMoveEvent), handledEventsToo: true);
    }

    internal static void RaiseMouse(Control control, PointerEventArgs e, EventClass eventClass, object? source = null, Func<bool>? when = null)
    {
        if (!OwnsThePointerEvent(control, source ?? e.Source) || VBProps.GetName(control) is null)
            return;
        // A PictureBox counts from inside its border, in its own ScaleMode, as its child host does. Every other control
        // counts in twips, from its client origin.
        var (origin, mode) = control is VBPictureBox { ChildHost: { } host }
            ? ((Visual)host, VBProps.GetScaleMode(host))
            : (control, VBScaleMode.Twip);
        // Its own handler, and each WithEvents variable holding it (#1556).
        Raise(e, eventClass, ControlEvents.Handlers(control, control.FindLogicalAncestorOfType<IModuleExecutionRoot>(), eventClass.Name),
            VBProps.GetIndex(control), origin, mode, control is VBTreeView tree ? tree.ClientEdge : VBProps.GetClientInset(control), when);
    }

    private static void Raise(PointerEventArgs e, EventClass eventClass, IReadOnlyList<ControlEvents.Handler> all, int? index,
        Visual origin, VBScaleMode mode, double inset, Func<bool>? when = null)
    {
        var handlers = all.Where(h => h.Root.Handles(h.Name)).ToList();
        if (handlers.Count == 0)
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
        // A move waiting behind a running handler is updated by the next one rather than queued beside it, as Windows
        // coalesces WM_MOUSEMOVE: only into the newest waiting event, never past a press (inferred, #766).
        var coalescing = ReferenceEquals(eventClass, ComponentBaseClass.MouseMoveEvent) ? EventCoalescing.Latest : EventCoalescing.None;
        foreach (var handler in handlers)
            handler.Root.ExecuteEvent(handler.Name, () =>
            {
                var args = new List<Vb6Value>();
                if (handler.Own && index is { } i)
                    args.Add(new Vb6Value(i));
                args.Add(new Vb6Value(button));
                args.Add(new Vb6Value(shift));
                args.Add(new Vb6Value(ControlPositions.MouseCoordinate(x, mode, vertical: false)));
                args.Add(new Vb6Value(ControlPositions.MouseCoordinate(y, mode, vertical: true)));
                return args;
            }, coalescing, when);
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
