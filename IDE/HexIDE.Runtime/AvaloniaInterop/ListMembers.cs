using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.AvaloniaInterop;

/// <summary>
/// A ListBox's and a ComboBox's list at run time: ListCount, List(i), AddItem with an index and RemoveItem (#821).
/// Measured on vb6.exe, native and p-code agreeing, on a ListBox, a ComboBox of either style and a sorted ListBox, which
/// agree row for row (the oracle's "A list's members"):
/// <list type="bullet">
/// <item>ListCount is an Integer, and assigning it is the compile error "Can't assign to read-only property".</item>
/// <item>An index converts as an Integer argument does: "1" is 1, 1.6 is 2, past 32767 is 6 and "q" is 13.</item>
/// <item>List(i) reads "" for any index outside the list. Written, it replaces item i; i = ListCount or -1 appends; past
/// ListCount is 381, and below -1 is "Out of memory", -2147024882.</item>
/// <item>AddItem takes an index from 0 to ListCount and RemoveItem from 0 to ListCount - 1, and both are 5 outside. Sorted
/// does not override AddItem's index.</item>
/// <item>The selection follows its item through all of these, and is emptied when its item is removed. None raises
/// Click.</item>
/// </list>
/// A ListBox's Selected(i), SelCount and Text (#1395), measured the same way (the oracle's "A ListBox's Selected, SelCount
/// and Text"):
/// <list type="bullet">
/// <item>Selected(i) is a Boolean, and 381 outside the list, read or written. Set True it selects item i, and set False
/// on the selected item it selects none, with Click only when the selection moves, as for ListIndex.</item>
/// <item>SelCount is an Integer, 0 or 1.</item>
/// <item>Text reads the selected item's text, or "". Written, it selects the first item whose whole text it is, without
/// regard to case, or none, and raises Click every time.</item>
/// <item>On a list with MultiSelect 1 or 2, Selected(i) and SelCount read and set a selection of any size, and Text
/// reads the focused item, which ListIndex names. Text written raises Click and changes nothing (#1413).</item>
/// <item>MultiSelect itself reads as an Integer, and assigning it is the compile error "Can't assign to read-only
/// property" (measured, #1413).</item>
/// </list>
/// A ComboBox's Text (#926), measured the same way (the oracle's "A ComboBox's Text, at each Style"). At Style 0 and 1 it
/// is the edit box's: written, it does nothing when it is the Text already, and otherwise takes any text, clearing the
/// selection even for an item's text. Null is 13. A Dropdown List's is refused (#1428).
/// </summary>
internal static class ListMembers
{
    private const string AssignToReadOnly = "Can't assign to read-only property";

    // What VB6 raises for a List(i) write below -1: E_OUTOFMEMORY, presumably the window refusing the insert.
    private static readonly VBStandardError OutOfMemoryHResult = new(-2147024882, "Out of memory");

    public static bool IsList(Control control) => control is ListBox or ComboBox;

    public static bool TryGet(Control control, string member, out Vb6Value value)
    {
        value = default;
        if (control is not SelectingItemsControl list || !IsList(control))
            return false;
        if (control is VBListBox listBox && member.Equals("SelCount", StringComparison.OrdinalIgnoreCase))
        {
            value = new Vb6Value(listBox.SelectedCount);
            return true;
        }
        if (control is VBComboBox combo && member.Equals("Text", StringComparison.OrdinalIgnoreCase))
        {
            RequireEditable(combo);
            value = new Vb6Value(combo.Text ?? "");
            return true;
        }
        if (control is VBListBox textList && member.Equals("Text", StringComparison.OrdinalIgnoreCase))
        {
            value = new Vb6Value(textList.ListIndexText);
            return true;
        }
        if (member.Equals("ListCount", StringComparison.OrdinalIgnoreCase))
        {
            // An Integer: a VB6 list holds at most 32767 items.
            value = new Vb6Value(list.ItemCount);
            return true;
        }
        // List with no index, `x = List1.List`: VB6's compile error, measured as `List1.List()`.
        if (member.Equals("List", StringComparison.OrdinalIgnoreCase))
            throw new VBCompileErrorException("Argument not optional");
        return false;
    }

    public static bool TrySet(Control control, string member, Vb6Value value)
    {
        if (!IsList(control))
            return false;
        if (control is VBListBox listBox && member.Equals("Text", StringComparison.OrdinalIgnoreCase))
        {
            listBox.SetTextFromCode(Text(value));
            return true;
        }
        if (control is VBComboBox combo && member.Equals("Text", StringComparison.OrdinalIgnoreCase))
        {
            RequireEditable(combo);
            // Type mismatch, where the list's own members take Null as 94 (measured).
            if (value.IsNull)
                throw new VBRunTimeException(VBStandardError.TypeMismatch);
            combo.SetTextFromCode(Text(value));
            return true;
        }
        if (member.Equals("ListCount", StringComparison.OrdinalIgnoreCase))
            throw new VBCompileErrorException(AssignToReadOnly);
        if (member.Equals("List", StringComparison.OrdinalIgnoreCase))
            throw new VBCompileErrorException("Argument not optional");
        return false;
    }

    /// <summary><c>List(i)</c>, read: the item's text, or "" outside the list.</summary>
    public static Vb6Value ReadItem(Control control, IReadOnlyList<Vb6Value> indexes)
    {
        var list = (SelectingItemsControl)control;
        var index = Index(indexes, "List");
        return new Vb6Value(index >= 0 && index < list.ItemCount ? list.Items[index] as string ?? "" : "");
    }

    /// <summary><c>List(i) = text</c>: replaces item i, or appends at ListCount and -1.</summary>
    public static void WriteItem(Control control, IReadOnlyList<Vb6Value> indexes, Vb6Value value)
    {
        var list = (SelectingItemsControl)control;
        var index = Index(indexes, "List");
        var text = Text(value);
        if (index > list.ItemCount)
            throw new VBRunTimeException(VBStandardError.InvalidPropertyArrayIndex);
        if (index < -1)
            throw new VBRunTimeException(OutOfMemoryHResult);
        if (index == -1 || index == list.ItemCount)
        {
            list.Items.Add(text);
            return;
        }
        // Avalonia drops the selection of an item replaced; VB6 keeps it. On a multi-select list each item keeps its own,
        // which is not measured.
        if (list is VBListBox { IsMultiSelect: true } multi)
        {
            var wasSelected = multi.Selection.IsSelected(index);
            list.Items[index] = text;
            if (wasSelected)
                multi.Selection.Select(index);
            return;
        }
        var selected = list.SelectedIndex;
        list.Items[index] = text;
        list.SelectedIndex = selected;
    }

    /// <summary><c>Selected(i)</c>, read: whether item i is selected; 381 outside the list.</summary>
    public static Vb6Value ReadSelected(VBListBox list, IReadOnlyList<Vb6Value> indexes) =>
        new(list.IsItemSelected(ItemIndex(list, indexes, "Selected")));

    /// <summary>
    /// <c>Selected(i) = b</c>: True selects item i, and False clears it. Click comes only when that changes something.
    /// The index and the value are converted first, as arguments are, and the range is checked after.
    /// </summary>
    public static void WriteSelected(VBListBox list, IReadOnlyList<Vb6Value> indexes, Vb6Value value)
    {
        var index = Index(indexes, "Selected");
        // "True" and any non-zero number are True, Null is 94 and "x" 13 (measured).
        var selected = (bool)VbNumeric.CoerceOnStore(value, Vb6Value.ValueType.Boolean, null).Value!;
        if (index < 0 || index >= list.ItemCount)
            throw new VBRunTimeException(VBStandardError.InvalidPropertyArrayIndex);
        list.SetSelectedFromCode(index, selected);
    }

    // An index into the items: 381 outside them, from 0 to ListCount - 1 (measured for Selected).
    private static int ItemIndex(SelectingItemsControl list, IReadOnlyList<Vb6Value> indexes, string member)
    {
        var index = Index(indexes, member);
        if (index < 0 || index >= list.ItemCount)
            throw new VBRunTimeException(VBStandardError.InvalidPropertyArrayIndex);
        return index;
    }

    /// <summary><c>AddItem item[, index]</c>: appends, or inserts at 0 to ListCount.</summary>
    public static void AddItem(ItemsControl list, IReadOnlyList<Vb6Value> args)
    {
        // AddItem's Item is a String, so the list holds CStr's text of it: `List1.AddItem 1 / 3` lists 0.333333333333333.
        var text = Text(args[0]);
        if (args.Count < 2 || args[1].IsMissing)
        {
            list.Items.Add(text);
            return;
        }
        // A Null index is 13, where a Null item is 94 (measured).
        if (args[1].IsNull)
            throw new VBRunTimeException(VBStandardError.TypeMismatch);
        var index = Integer(args[1]);
        if (index < 0 || index > list.ItemCount)
            throw new VBRunTimeException(VBStandardError.InvalidProcedureCall);
        // Avalonia's selection follows its item, as VB6's does.
        list.Items.Insert(index, text);
    }

    /// <summary><c>RemoveItem index</c>: 0 to ListCount - 1; the selection follows its item, or empties with it.</summary>
    public static void RemoveItem(ItemsControl list, IReadOnlyList<Vb6Value> args)
    {
        if (args.Count != 1 || args[0].IsMissing)
            throw new VBRunTimeException(VBStandardError.ArgumentNotOptionalOrInvalidPropertyAssignment);
        var index = Integer(args[0]);
        if (index < 0 || index >= list.ItemCount)
            throw new VBRunTimeException(VBStandardError.InvalidProcedureCall);
        // Avalonia's selection follows its item, and empties when it goes, as VB6's does.
        list.Items.RemoveAt(index);
    }

    private static int Index(IReadOnlyList<Vb6Value> indexes, string member)
    {
        if (indexes.Count != 1)
            throw new NotImplementedException($"{member} with {indexes.Count} indexes is not implemented");
        return Integer(indexes[0]);
    }

    // An index is an Integer: "1" is 1, 1.6 rounds to 2, past 32767 is 6, "q" is 13 and Null 94 (measured).
    private static int Integer(Vb6Value value) =>
        Convert.ToInt32(VbNumeric.CoerceOnStore(value, Vb6Value.ValueType.Integer, null).Value);

    // An item is a String, as CStr makes it; Null is 94 (measured).
    // A Dropdown List's Text selects an item or raises 383 (measured), which needs its Style modelled (#1428). An edit
    // box's Text there would take anything, so it is refused rather than answered wrong.
    private static void RequireEditable(VBComboBox combo)
    {
        if (combo.IsDropdownList)
            throw new NotImplementedException(
                $"Text on a ComboBox whose Style is {combo.ComboStyle} is not implemented: HexIDE runs no Dropdown List yet (#1428)");
    }

    private static string Text(Vb6Value value) =>
        (string)VbNumeric.CoerceOnStore(value, Vb6Value.ValueType.String, null).Value!;
}
