using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.Interpreter;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.Components.InBox;

namespace HexIDE.Runtime.AvaloniaInterop;

/// <summary>
/// A ListBox's and a ComboBox's list at run time: ListCount, List(i), AddItem with an index and RemoveItem (#821).
/// Measured on vb6.exe, native and p-code agreeing, on a ListBox, a ComboBox of either style and a sorted ListBox, which
/// agree row for row (the oracle's "A list's members"):
/// <list type="bullet">
/// <item>ListCount is an Integer, and assigning it is the compile error "Can't assign to read-only property".</item>
/// <item>An index converts as an Integer argument does: "1" is 1, 1.6 is 2, past 32767 is 6 and "q" is 13.</item>
/// <item>List(i) reads "" for any index outside the list. Written, it replaces item i; i = ListCount or -1 appends; past
/// ListCount is 381, and below -1 is "Out of memory", -2147024882. The late-bound write at -2 is 7 (#5117).</item>
/// <item>AddItem takes an index from 0 to ListCount and RemoveItem from 0 to ListCount - 1, and both are 5 outside. Sorted
/// does not override AddItem's index.</item>
/// <item>The selection follows its item through all of these, and is emptied when its item is removed. None raises
/// Click. Checkbox lists instead keep a numeric position and move checks with their items (#4209).</item>
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
/// selection even for an item's text. Null is 13. At Style 2, a Dropdown List, it is the selected item's text, or "".
/// Written, it is made a String as CStr makes it and selects the first item whose whole text it is, without regard to case,
/// raising Click every time; with no such item, "" and Empty included, it is 383 and nothing changes. Null is 13 there too
/// (#1428).
/// </summary>
internal static class ListMembers
{
    public static bool IsExtension(Control control, string name)
    {
        var component = control switch
        {
            ListBox => ListBoxComponentClass.Instance,
            ComboBox => ComboBoxComponentClass.Instance,
            _ => null,
        };
        return component?.Members.Any(m => m.Name.Equals(name, StringComparison.OrdinalIgnoreCase)
            && m.Origin.HasFlag(MemberOrigin.HexIde)) == true;
    }

    // HexIDE's code-only search contract (#1116), independent of VB6's Integer list indices.
    public static Vb6Value CallExtension(ItemsControl list, string name, IReadOnlyList<Vb6Value> args)
    {
        VBProps.RefuseIfRemoved(list);
        if (args.Count is < 1 or > 2 || args[0].IsMissing)
            throw new VBRunTimeException(VBStandardError.ArgumentNotOptionalOrInvalidPropertyAssignment);
        if (args[0].IsNull || args.Count == 2 && args[1].IsNull)
            throw new VBRunTimeException(VBStandardError.TypeMismatch);
        var text = Text(args[0]);
        var start = args.Count == 2 && !args[1].IsMissing
            ? Convert.ToInt64(VbNumeric.CoerceOnStore(args[1], Vb6Value.ValueType.Long, null).Value) : -1;
        // The extension contract reports an invalid argument as type mismatch (#1116).
        if (start < -1 || start >= list.ItemCount)
            throw new VBRunTimeException(VBStandardError.TypeMismatch);
        var exact = name.Equals("FindStringExact", StringComparison.OrdinalIgnoreCase);
        for (var offset = 1; offset <= list.ItemCount; offset++)
        {
            var index = (int)((start + offset) % list.ItemCount);
            var item = list.Items[index] as string ?? "";
            if (exact ? string.Equals(item, text, StringComparison.CurrentCultureIgnoreCase)
                : item.StartsWith(text, StringComparison.CurrentCultureIgnoreCase))
                return new Vb6Value((long)index);
        }
        return new Vb6Value(-1L);
    }

    private const string AssignToReadOnly = "Can't assign to read-only property";

    // What VB6 raises for a List(i) write below -1: E_OUTOFMEMORY, presumably the window refusing the insert.
    private static readonly VBStandardError OutOfMemoryHResult = new(-2147024882, "Out of memory");

    public static bool IsList(Control control) => control is ListBox or ComboBox;

    public static bool TryGet(Control control, string member, out Vb6Value value, bool lateBound = false)
    {
        value = default;
        if (control is not SelectingItemsControl list || !IsList(control))
            return false;
        if (control is VBListBox savedList)
        {
            if (member.Equals("Style", StringComparison.OrdinalIgnoreCase))
            {
                value = new Vb6Value(savedList.SavedStyle);
                return true;
            }
            if (member.Equals("Columns", StringComparison.OrdinalIgnoreCase))
            {
                value = new Vb6Value(savedList.Columns);
                return true;
            }
        }
        if (control is VBListBox topList && member.Equals("TopIndex", StringComparison.OrdinalIgnoreCase))
        {
            value = new Vb6Value(topList.TopIndex);
            return true;
        }
        if (control is VBListBox listBox && member.Equals("SelCount", StringComparison.OrdinalIgnoreCase))
        {
            value = new Vb6Value(listBox.SelectedCount);
            return true;
        }
        if (control is VBComboBox combo && member.Equals("Text", StringComparison.OrdinalIgnoreCase))
        {
            value = new Vb6Value(combo.VBText);
            return true;
        }
        if (control is VBListBox textList && member.Equals("Text", StringComparison.OrdinalIgnoreCase))
        {
            value = new Vb6Value(textList.ListIndexText);
            return true;
        }
        if (member.Equals("NewIndex", StringComparison.OrdinalIgnoreCase))
        {
            value = new Vb6Value(ListItemState.For(list).NewIndex);
            return true;
        }
        if (member.Equals("ListCount", StringComparison.OrdinalIgnoreCase))
        {
            // An Integer: a VB6 list holds at most 32767 items.
            value = new Vb6Value(list.ItemCount);
            return true;
        }
        if (lateBound && member.Equals("ItemData", StringComparison.OrdinalIgnoreCase))
            throw new VBRunTimeException(VBStandardError.NeedPropertyArrayIndex);
        // List with no index, `x = List1.List`: VB6's compile error, measured as `List1.List()`.
        if (member.Equals("List", StringComparison.OrdinalIgnoreCase) || member.Equals("ItemData", StringComparison.OrdinalIgnoreCase))
            throw new VBCompileErrorException("Argument not optional");
        return false;
    }

    public static bool TrySet(Control control, string member, Vb6Value value, bool lateBound)
    {
        if (!IsList(control))
            return false;
        if (control is VBListBox topList && member.Equals("TopIndex", StringComparison.OrdinalIgnoreCase))
        {
            topList.SetTopIndexFromCode(Integer(value));
            return true;
        }
        if (control is VBListBox listBox && member.Equals("Text", StringComparison.OrdinalIgnoreCase))
        {
            listBox.SetTextFromCode(Text(value));
            return true;
        }
        if (control is VBComboBox combo && member.Equals("Text", StringComparison.OrdinalIgnoreCase))
        {
            // Type mismatch, where the list's own members take Null as 94 (measured), at every Style.
            if (value.IsNull)
                throw new VBRunTimeException(VBStandardError.TypeMismatch);
            if (combo.IsDropdownList)
                combo.SetListTextFromCode(Text(value));
            else
                combo.SetTextFromCode(Text(value));
            return true;
        }
        if (lateBound && member.Equals("NewIndex", StringComparison.OrdinalIgnoreCase))
            throw new VBRunTimeException(VBStandardError.PropertySetCantBeUsedWithAReadOnlyProperty);
        if (member.Equals("ListCount", StringComparison.OrdinalIgnoreCase) || member.Equals("NewIndex", StringComparison.OrdinalIgnoreCase))
            throw new VBCompileErrorException(AssignToReadOnly);
        if (member.Equals("List", StringComparison.OrdinalIgnoreCase) || member.Equals("ItemData", StringComparison.OrdinalIgnoreCase))
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
    public static void WriteItem(Control control, IReadOnlyList<Vb6Value> indexes, Vb6Value value, bool lateBound)
    {
        var list = (SelectingItemsControl)control;
        var index = Index(indexes, "List");
        var text = Text(value);
        if (index > list.ItemCount)
            throw new VBRunTimeException(VBStandardError.InvalidPropertyArrayIndex);
        if (index < -1)
        {
            // #5134: a failed ComboBox write advances its selection silently before raising the error.
            if (list is VBComboBox && list.SelectedIndex >= 0 && list.SelectedIndex < list.ItemCount - 1)
                list.SelectedIndex++;
            throw new VBRunTimeException(lateBound && index == -2 ? VBStandardError.OutOfMemory : OutOfMemoryHResult);
        }
        if (index == -1 || index == list.ItemCount)
        {
            var state = ListItemState.For(list);
            if (index == -1) state.ZeroData();
            list.Items.Add(text);
            return;
        }
        // VB6 keeps a selected item's selection and moves multi-select focus and anchor to it (#1424).
        if (list is VBListBox { IsMultiSelect: true } multi)
        {
            var wasSelected = multi.Selection.IsSelected(index);
            list.Items[index] = text;
            if (wasSelected)
            {
                multi.Selection.Select(index);
                multi.ItemTextReplaced(index);
            }
            return;
        }
        var selected = list.SelectedIndex;
        list.Items[index] = text;
        list.SelectedIndex = selected;
    }

    public static Vb6Value ReadItemData(Control control, IReadOnlyList<Vb6Value> indexes)
    {
        var list = (SelectingItemsControl)control;
        return new Vb6Value(ListItemState.For(list).Data[ItemIndex(list, indexes, "ItemData")]);
    }

    public static void WriteItemData(Control control, IReadOnlyList<Vb6Value> indexes, Vb6Value value, bool lateBound)
    {
        var list = (SelectingItemsControl)control;
        var index = ItemIndex(list, indexes, "ItemData");
        if (lateBound && value.IsNull)
            throw new VBRunTimeException(VBStandardError.TypeMismatch);
        var converted = VbNumeric.CoerceOnStore(value, Vb6Value.ValueType.Long, null);
        ListItemState.For(list).Data[index] = Convert.ToInt64(converted.Value);
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

    /// <summary><c>AddItem item[, index]</c>: sorts or appends, unless an explicit index wins.</summary>
    public static void AddItem(ItemsControl list, IReadOnlyList<Vb6Value> args)
    {
        _ = ListItemState.For(list);
        // AddItem's Item is a String, so the list holds CStr's text of it: `List1.AddItem 1 / 3` lists 0.333333333333333.
        var text = Text(args[0]);
        if (args.Count < 2 || args[1].IsMissing)
        {
            var at = list is VBListBox { Sorted: true } or VBComboBox { Sorted: true }
                ? SortedIndex(list, text) : list.ItemCount;
            if (list is VBListBox { HasChecks: true } checkbox)
                checkbox.InsertCheckboxItem(at, text);
            else
                list.Items.Insert(at, text);
            return;
        }
        // A Null index is 13, where a Null item is 94 (measured).
        if (args[1].IsNull)
            throw new VBRunTimeException(VBStandardError.TypeMismatch);
        var index = Integer(args[1]);
        if (index < 0 || index > list.ItemCount)
            throw new VBRunTimeException(VBStandardError.InvalidProcedureCall);
        if (list is VBListBox { HasChecks: true } checkboxList)
            checkboxList.InsertCheckboxItem(index, text);
        else
            list.Items.Insert(index, text);
    }

    // #5059, oracle: the-order-a-sorted-listbox-or-combobox-keeps.md. The lower midpoint and
    // inserting before the first equal item probed both matter for the measured ties.
    private static int SortedIndex(ItemsControl list, string text)
    {
        var min = 0;
        var max = list.ItemCount;
        while (min != max)
        {
            var probe = (min + max - 1) / 2;
            var order = CompareSorted(text, list.Items[probe] as string ?? "");
            if (order == 0)
                return probe;
            if (order < 0)
                max = probe;
            else
                min = probe + 1;
        }
        return max;
    }

    private static int CompareSorted(string first, string second)
    {
        // A leading '[' goes last: a list's own rule, separate from Windows' comparison.
        var firstBracket = first.StartsWith('[');
        var secondBracket = second.StartsWith('[');
        if (firstBracket != secondBracket)
            return firstBracket ? 1 : -1;
        var culture = VbLocale.Current;
        return OperatingSystem.IsWindows()
            ? WindowsLocaleCompare.Compare(first, second, culture.Name)
            : culture.CompareInfo.Compare(first, second, CompareOptions.IgnoreCase);
    }

    /// <summary><c>RemoveItem index</c>: 0 to ListCount - 1; the selection follows its item, or empties with it.</summary>
    public static void RemoveItem(ItemsControl list, IReadOnlyList<Vb6Value> args)
    {
        if (args.Count != 1 || args[0].IsMissing)
            throw new VBRunTimeException(VBStandardError.ArgumentNotOptionalOrInvalidPropertyAssignment);
        var index = Integer(args[0]);
        if (index < 0 || index >= list.ItemCount)
            throw new VBRunTimeException(VBStandardError.InvalidProcedureCall);
        if (list is VBListBox { HasChecks: true } checkbox)
            checkbox.RemoveCheckboxItem(index);
        else
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
    private static string Text(Vb6Value value) =>
        (string)VbNumeric.CoerceOnStore(value, Vb6Value.ValueType.String, null).WithoutOddBytes("ListMembers text conversion").Value!;
}
