using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.BuiltinControls;

/// <summary>
/// A VB6 ListBox. Its Click comes from two places, measured with vb6.exe, native and p-code agreeing (the oracle's
/// "A ListBox's Click", #1374):
/// <list type="bullet">
/// <item>A left press on an item raises Click at its release, before MouseUp, whether or not the selection changed and
/// wherever the release lands. The press selects at once, and the selection follows the pointer over the items while
/// the button is held. A left press in the empty area below the items, and any right or middle press, selects nothing
/// and raises no Click.</item>
/// <item>Code or a key that moves the selection raises Click, with ListIndex already the new value. Setting it again,
/// or a key that can go no further, raises none, and nor do AddItem, RemoveItem and Clear.</item>
/// </list>
/// So Click is not raised from a selection change, which those three also make.
/// </summary>
public class VBListBox : ListBox
{
    protected override Type StyleKeyOverride => typeof(ListBox);

    // Whether the press now held is a left press on an item, which raises Click at its release.
    private bool pressedOnItem;

    static VBListBox()
    {
        // VB6 selects nothing for a right or middle press, where Avalonia's ListBox selects on a right one. Handled on
        // the way down, its own press handler never sees it; the mouse events take handled presses too.
        PointerPressedEvent.AddClassHandler<VBListBox>((list, e) =>
        {
            if (!e.GetCurrentPoint(list).Properties.IsLeftButtonPressed)
                e.Handled = true;
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        // Before the mouse events, so Click comes before MouseUp.
        PointerPressedEvent.AddClassHandler<VBListBox>((list, e) => list.Pressed(e), handledEventsToo: true);
        PointerMovedEvent.AddClassHandler<VBListBox>((list, e) => list.Moved(e), handledEventsToo: true);
        PointerReleasedEvent.AddClassHandler<VBListBox>((list, e) => list.Released(e), handledEventsToo: true);
        AttachedEvents.AttachMouse<VBListBox>(AttachedEvents.DblClick.InPlaceOfMouseDown);
    }

    private void Pressed(PointerPressedEventArgs e)
    {
        pressedOnItem = e.GetCurrentPoint(this).Properties.PointerUpdateKind == PointerUpdateKind.LeftButtonPressed
                        && IndexAt(e.Source) >= 0;
    }

    private void Moved(PointerEventArgs e)
    {
        if (!pressedOnItem || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;
        // Over the empty area, or outside the list, the selection stays on the last item reached.
        if (IndexAt(this.InputHitTest(e.GetPosition(this))) is var index and >= 0 && index != SelectedIndex)
            SelectedIndex = index;
    }

    private void Released(PointerReleasedEventArgs e)
    {
        var click = pressedOnItem && e.InitialPressMouseButton == MouseButton.Left && !AttachedEvents.IsSecondPress(this);
        pressedOnItem = false;
        // A double click's second release raises no Click (#1031).
        if (click)
            this.ExecuteSub(ComponentBaseClass.ClickEvent);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        var before = SelectedIndex;
        base.OnKeyDown(e);
        // An arrow that moves the selection raises Click; one that can go no further does not.
        if (SelectedIndex != before)
            this.ExecuteSub(ComponentBaseClass.ClickEvent);
    }

    /// <summary>
    /// The form's MultiSelect: 0, or 1 (Simple) or 2 (Extended). HexIDE does not implement the last two, and runs such a
    /// list single-select (#1413).
    /// </summary>
    internal int MultiSelect { get; init; }

    /// <summary>
    /// Sets ListIndex from code: Click when the selection moves, and none when it is set again (measured, #1374). Range
    /// checked by the caller.
    /// </summary>
    internal void SetListIndexFromCode(int index)
    {
        if (index == SelectedIndex)
            return;
        SelectedIndex = index;
        this.ExecuteSub(ComponentBaseClass.ClickEvent);
    }

    /// <summary>
    /// Sets Text from code: selects the first item whose whole text it is, without regard to case, or nothing, and raises
    /// Click even when the selection does not move (measured, #1395).
    /// </summary>
    internal void SetTextFromCode(string text)
    {
        var index = -1;
        for (var i = 0; i < ItemCount && index < 0; i++)
            if (string.Equals(Items[i] as string, text, StringComparison.OrdinalIgnoreCase))
                index = i;
        SelectedIndex = index;
        this.ExecuteSub(ComponentBaseClass.ClickEvent);
    }

    // The index of the item a point or an event's source is on, or -1 for the empty area and anything outside the items.
    private int IndexAt(object? source)
    {
        for (var visual = source as Visual; visual is not null && !ReferenceEquals(visual, this); visual = visual.GetVisualParent())
            if (visual is ListBoxItem item)
                return IndexFromContainer(item);
        return -1;
    }
}
