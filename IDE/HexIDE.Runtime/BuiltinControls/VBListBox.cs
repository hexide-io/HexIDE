using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using HexIDE.Runtime.BuiltinTypes;
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
/// <para>
/// With MultiSelect 1 (Simple) or 2 (Extended) the list keeps VB6's own rules rather than Avalonia's (the oracle's "A
/// ListBox's Selected, SelCount and Text", #1413). ListIndex is the item with the focus, 0 from the start, and the
/// selection is a set of items apart from it. Simple toggles the item pressed, whatever the modifiers, and its arrow
/// keys move only the focus, with Space toggling. Extended selects the item pressed alone, toggles it with Ctrl, and
/// with Shift selects from the anchor, the last item pressed or reached without Shift. A drag moves the focus, and on an
/// Extended list selects from the anchor too. Every key such a list takes raises Click, whether or not anything changed.
/// </para>
/// </summary>
public class VBListBox : ListBox
{
    protected override Type StyleKeyOverride => typeof(ListBox);

    // Whether the press now held is a left press on an item, which raises Click at its release.
    private bool pressedOnItem;

    private readonly VBMultiSelect multiSelect;
    // A multi-select list's ListIndex: the focused item, which need not be selected.
    private int focusIndex;
    // An Extended list's anchor, which a Shift range runs from.
    private int anchorIndex;
    // A Shift press on an Extended list, whose range comes at the release.
    private bool shiftPressed;

    static VBListBox()
    {
        // VB6 selects nothing for a right or middle press, where Avalonia's ListBox selects on a right one. Handled on
        // the way down, its own press handler never sees it; the mouse events take handled presses too. A left press on
        // a multi-select list is VB6's to handle, for the same reason.
        PointerPressedEvent.AddClassHandler<VBListBox>((list, e) =>
        {
            if (!e.GetCurrentPoint(list).Properties.IsLeftButtonPressed)
                e.Handled = true;
            else if (list.IsMultiSelect)
            {
                list.PressMulti(list.IndexAt(e.Source), e.KeyModifiers, e.ClickCount);
                e.Handled = true;
            }
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        // A multi-select list's keys, on the way down: its focused item would take Space first.
        KeyDownEvent.AddClassHandler<VBListBox>((list, e) =>
        {
            // What it takes raises Click, changed or not (measured).
            if (list.IsMultiSelect && list.KeyMulti(e.Key, e.KeyModifiers))
            {
                e.Handled = true;
                list.ExecuteSub(ComponentBaseClass.ClickEvent);
            }
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        // Before the mouse events, so Click comes before MouseUp.
        PointerPressedEvent.AddClassHandler<VBListBox>((list, e) => list.Pressed(e), handledEventsToo: true);
        PointerMovedEvent.AddClassHandler<VBListBox>((list, e) => list.Moved(e), handledEventsToo: true);
        PointerReleasedEvent.AddClassHandler<VBListBox>((list, e) => list.Released(e), handledEventsToo: true);
        AttachedEvents.AttachMouse<VBListBox>(AttachedEvents.DblClick.InPlaceOfMouseDown);
    }

    public VBListBox()
    {
        // Clear leaves a multi-select list's focus at 0 (measured). What RemoveItem does to it is not measured; it is
        // kept on an item while there is one.
        Items.CollectionChanged += (_, e) =>
        {
            if (e.Action == NotifyCollectionChangedAction.Reset)
                focusIndex = anchorIndex = 0;
            else if (focusIndex >= ItemCount)
                focusIndex = Math.Max(0, ItemCount - 1);
        };
    }

    /// <summary>The form's MultiSelect, which VB6 sets at design time only.</summary>
    public VBMultiSelect MultiSelect
    {
        get => multiSelect;
        init
        {
            multiSelect = value;
            if (value != VBMultiSelect.None)
                SelectionMode = SelectionMode.Multiple;
        }
    }

    internal bool IsMultiSelect => multiSelect != VBMultiSelect.None;

    /// <summary>ListIndex as VB6 reads it: the selected item, or on a multi-select list the focused one.</summary>
    internal int ListIndex => IsMultiSelect ? focusIndex : SelectedIndex;

    /// <summary>Whether item <paramref name="index"/> is selected; the caller checks the range.</summary>
    internal bool IsItemSelected(int index) => IsMultiSelect ? Selection.IsSelected(index) : index == SelectedIndex;

    /// <summary>SelCount: how many items are selected.</summary>
    internal int SelectedCount => IsMultiSelect ? Selection.SelectedIndexes.Count : SelectedIndex >= 0 ? 1 : 0;

    /// <summary>Text as VB6 reads it: the text of the item at ListIndex, or "".</summary>
    internal string ListIndexText => ListIndex >= 0 && ListIndex < ItemCount ? Items[ListIndex] as string ?? "" : "";

    private void Pressed(PointerPressedEventArgs e)
    {
        pressedOnItem = e.GetCurrentPoint(this).Properties.PointerUpdateKind == PointerUpdateKind.LeftButtonPressed
                        && IndexAt(e.Source) >= 0;
    }

    private void PressMulti(int index, KeyModifiers modifiers, int clickCount)
    {
        // The press is handled here, so Avalonia gives nothing the keyboard focus; its items take it, not the list.
        if (index >= 0)
            ContainerFromIndex(index)?.Focus(NavigationMethod.Pointer);
        // Below the items nothing changes, and a double click's second press toggles nothing again (measured).
        if (index < 0 || clickCount > 1)
            return;
        focusIndex = index;
        shiftPressed = false;
        if (multiSelect == VBMultiSelect.Simple)
            Toggle(index);
        else if (modifiers.HasFlag(KeyModifiers.Shift))
        {
            // The press leaves only the anchor's own state, and the range comes at the release (measured).
            var anchor = Anchor();
            var anchorSelected = Selection.IsSelected(anchor);
            SelectOnly(anchorSelected ? anchor : -1);
            shiftPressed = true;
        }
        else if ((modifiers & (KeyModifiers.Control | KeyModifiers.Meta)) != 0)
        {
            Toggle(index);
            anchorIndex = index;
        }
        else
        {
            SelectOnly(index);
            anchorIndex = index;
        }
    }

    private void Moved(PointerEventArgs e)
    {
        if (!pressedOnItem || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;
        // Over the empty area, or outside the list, the selection stays on the last item reached.
        var index = IndexAt(this.InputHitTest(e.GetPosition(this)));
        if (index < 0)
            return;
        if (IsMultiSelect)
        {
            // A drag moves the focus, and on an Extended list selects from the anchor to it (measured).
            if (index == focusIndex)
                return;
            focusIndex = index;
            if (multiSelect == VBMultiSelect.Extended)
                SelectRange(index);
        }
        else if (index != SelectedIndex)
            SelectedIndex = index;
    }

    private void Released(PointerReleasedEventArgs e)
    {
        var click = pressedOnItem && e.InitialPressMouseButton == MouseButton.Left && !AttachedEvents.IsSecondPress(this);
        pressedOnItem = false;
        if (shiftPressed)
        {
            shiftPressed = false;
            SelectRange(focusIndex);
        }
        // A double click's second release raises no Click (#1031).
        if (click)
            this.ExecuteSub(ComponentBaseClass.ClickEvent);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        // A multi-select list's keys are taken on the way down; Avalonia's own would select as Avalonia does.
        if (IsMultiSelect)
            return;
        var before = SelectedIndex;
        base.OnKeyDown(e);
        // An arrow that moves the selection raises Click; one that can go no further does not.
        if (SelectedIndex != before)
            this.ExecuteSub(ComponentBaseClass.ClickEvent);
    }

    // Up, Down and Space, the keys measured. Others do nothing on a multi-select list.
    private bool KeyMulti(Key key, KeyModifiers modifiers)
    {
        if (key is not (Key.Up or Key.Down or Key.Space) || ItemCount == 0)
            return false;
        if (key == Key.Space)
        {
            // Simple toggles the focused item, and Extended changes nothing, with Ctrl or without (measured).
            if (multiSelect == VBMultiSelect.Simple && focusIndex >= 0 && focusIndex < ItemCount)
                Toggle(focusIndex);
            return true;
        }
        focusIndex = Math.Clamp(focusIndex + (key == Key.Down ? 1 : -1), 0, ItemCount - 1);
        // The focused item comes into view and takes the keyboard focus, as with the mouse. Not by a directional move,
        // which would have Avalonia select it.
        ScrollIntoView(focusIndex);
        ContainerFromIndex(focusIndex)?.Focus(NavigationMethod.Unspecified);
        if (multiSelect == VBMultiSelect.Simple)
            return true;
        if (modifiers.HasFlag(KeyModifiers.Shift))
            SelectRange(focusIndex);
        else
        {
            // With Ctrl too: Ctrl+Up selected the item alone, as Up does (measured).
            SelectOnly(focusIndex);
            anchorIndex = focusIndex;
        }
        return true;
    }

    // From the anchor to target, with the anchor keeping its own state, and nothing else selected (measured).
    private void SelectRange(int target)
    {
        var anchor = Anchor();
        var anchorSelected = Selection.IsSelected(anchor);
        Selection.BeginBatchUpdate();
        try
        {
            Selection.Clear();
            for (var i = Math.Min(anchor, target); i <= Math.Max(anchor, target); i++)
                if (i != anchor || anchorSelected)
                    Selection.Select(i);
        }
        finally
        {
            Selection.EndBatchUpdate();
        }
    }

    private int Anchor() => Math.Clamp(anchorIndex, 0, Math.Max(0, ItemCount - 1));

    private void Toggle(int index)
    {
        if (Selection.IsSelected(index))
            Selection.Deselect(index);
        else
            Selection.Select(index);
    }

    // Only item index selected, or none for -1.
    private void SelectOnly(int index)
    {
        Selection.BeginBatchUpdate();
        try
        {
            Selection.Clear();
            if (index >= 0)
                Selection.Select(index);
        }
        finally
        {
            Selection.EndBatchUpdate();
        }
    }

    /// <summary>
    /// Sets ListIndex from code: Click when the selection moves, and none when it is set again (measured, #1374). On a
    /// multi-select list it moves the focus, and selects and clears nothing (#1413). Range checked by the caller.
    /// </summary>
    internal void SetListIndexFromCode(int index)
    {
        if (index == ListIndex)
            return;
        if (IsMultiSelect)
            focusIndex = index;
        else
            SelectedIndex = index;
        this.ExecuteSub(ComponentBaseClass.ClickEvent);
    }

    /// <summary>
    /// Sets Selected(index) from code, with Click when it changes (measured, #1395). On a single-select list it moves the
    /// selection; on a multi-select one, True adds the item and moves the focus to it, and False removes it and leaves
    /// the focus (#1413). Range checked by the caller.
    /// </summary>
    internal void SetSelectedFromCode(int index, bool selected)
    {
        if (!IsMultiSelect)
        {
            if (selected)
                SetListIndexFromCode(index);
            else if (index == SelectedIndex)
                SetListIndexFromCode(-1);
            return;
        }
        if (Selection.IsSelected(index) == selected)
            return;
        if (selected)
        {
            Selection.Select(index);
            focusIndex = index;
        }
        else
            Selection.Deselect(index);
        this.ExecuteSub(ComponentBaseClass.ClickEvent);
    }

    /// <summary>
    /// Sets Text from code: selects the first item whose whole text it is, without regard to case, or nothing, and raises
    /// Click even when the selection does not move (measured, #1395). On a multi-select list it raises Click and changes
    /// nothing (#1413).
    /// </summary>
    internal void SetTextFromCode(string text)
    {
        if (!IsMultiSelect)
        {
            var index = -1;
            for (var i = 0; i < ItemCount && index < 0; i++)
                if (string.Equals(Items[i] as string, text, StringComparison.OrdinalIgnoreCase))
                    index = i;
            SelectedIndex = index;
        }
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
