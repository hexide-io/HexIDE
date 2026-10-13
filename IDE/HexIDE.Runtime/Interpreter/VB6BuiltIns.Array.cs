using System;
using System.Collections.Generic;
using System.Text;

namespace HexIDE.Runtime.Interpreter;

// Phase 3 — Array intrinsics. LBound/UBound plus the array-producing family: Array (Variant array), Split
// (String array), Join, Filter (String array). Behaviour pinned against vb6.exe — notably Split's default
// delimiter is a space, Split("") is an empty array (0,-1), and Filter with no matches is likewise (0,-1).
public partial class VB6BuiltIns
{
    private static void RegisterArray(Dictionary<string, BuiltinFn> d)
    {
        d["LBound"] = (_, a, _) => Bound(a, upper: false);
        d["UBound"] = (_, a, _) => Bound(a, upper: true);
        d["Array"]  = (_, a, _) => MakeArray(Vb6Value.ValueType.EmptyVariant, a);   // 0-based Variant array
        d["Split"]  = (_, a, _) => Split(a);
        d["Join"]   = (_, a, _) => Join(a);
        d["Filter"] = (_, a, _) => Filter(a);
    }

    private static Vb6Value Bound(IReadOnlyList<Vb6Value> a, bool upper)
    {
        if (a.Count < 1 || a.Count > 2)
            throw InvalidCall();
        if (a[0].Value is not VBArray array)
            throw new VBRunTimeException(VBStandardError.TypeMismatch);
        int dimension = Supplied(a, 1) ? AsInt(a[1]) : 1;
        // A dimension < 1 or > the array's rank is a trappable Subscript-out-of-range (Err 9, oracle-verified);
        // indexing bounds[dimension-1] directly would throw an ArgumentOutOfRangeException that On Error can't trap.
        if (dimension < 1 || dimension > array.Rank)
            throw new VBRunTimeException(VBStandardError.SubscriptOutOfRange);
        // Long, not the magnitude rule — LBound/UBound have a fixed declared return type. See InStr. (#193)
        return new Vb6Value((long)(upper ? array.UpperBound(dimension) : array.LowerBound(dimension)));
    }

    // Builds a 0-based 1-D array from items. Empty items -> bounds (0,-1), matching VB6's empty-array result.
    private static Vb6Value MakeArray(Vb6Value.ValueType elementType, IReadOnlyList<Vb6Value> items)
    {
        var arrType = new Vb6Value.ValueType(elementType, true);
        var v = new Vb6Value(arrType, new (int, int)[] { (0, items.Count - 1) });
        var arr = (VBArray)v.Value!;
        for (int i = 0; i < items.Count; i++)
            arr.SetValue(new[] { i }, items[i]);
        return v;
    }

    // Split(expr, [delimiter=" "], [limit=-1], [compare]) -> a 0-based String array.
    private static Vb6Value Split(IReadOnlyList<Vb6Value> a)
    {
        if (a.Count < 1) throw InvalidCall();
        string s = AsStr(a[0]);
        string delim = Supplied(a, 1) ? AsStr(a[1]) : " ";
        int limit = Supplied(a, 2) ? AsInt(a[2]) : -1;
        var cmp = Supplied(a, 3) && AsInt(a[3]) == 1 ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        var parts = SplitVb(s, delim, limit, cmp);
        var items = new List<Vb6Value>(parts.Count);
        foreach (var p in parts) items.Add(new Vb6Value(p));
        return MakeArray(Vb6Value.ValueType.String, items);
    }

    private static List<string> SplitVb(string s, string delim, int limit, StringComparison cmp)
    {
        var result = new List<string>();
        if (s.Length == 0 || limit == 0) return result;      // empty input / limit 0 -> empty array
        if (delim.Length == 0) { result.Add(s); return result; }
        int start = 0;
        while (true)
        {
            if (limit > 0 && result.Count == limit - 1) { result.Add(s.Substring(start)); break; }  // remainder
            int idx = s.IndexOf(delim, start, cmp);
            if (idx < 0) { result.Add(s.Substring(start)); break; }
            result.Add(s.Substring(start, idx - start));
            start = idx + delim.Length;
        }
        return result;
    }

    // Join(array, [delimiter=" "]) -> String. Anything but an array is 13, as it is for Filter and the bounds: a string,
    // a number, Empty, Null, Nothing and any other object (measured, native and in the IDE, #840). It raised 5. An array
    // never allocated, or erased, joins to "" with no error, whatever its type (measured for String, Variant and Long
    // arrays); it indexed a bounds list that was not there, an exception no On Error could trap.
    //
    // The delimiter is read only when the array has an element, even one that needs no delimiter (measured, native and
    // in the IDE): with no element, `Join(Array(), Null)` and `Join(ds, Null)` are "" and an object delimiter is not
    // read either; with one, `Join(Array("a"), Null)` is 94 and a class with no default member 438. A Null delimiter
    // read as "".
    private static Vb6Value Join(IReadOnlyList<Vb6Value> a)
    {
        if (a.Count < 1) throw InvalidCall();
        if (a[0].Value is not VBArray arr) throw new VBRunTimeException(VBStandardError.TypeMismatch);
        if (arr.Rank == 0 || arr.UpperBound(1) < arr.LowerBound(1)) return new Vb6Value("");
        if (Supplied(a, 1) && a[1].IsNull) throw new VBRunTimeException(VBStandardError.InvalidUseOfNull);
        string delim = Supplied(a, 1) ? AsStr(a[1]) : " ";
        var sb = new StringBuilder();
        int lo = arr.LowerBound(1), hi = arr.UpperBound(1);
        for (int i = lo; i <= hi; i++)
        {
            if (i > lo) sb.Append(delim);
            sb.Append(ElementText(arr.GetValue(new[] { i })));
        }
        return new Vb6Value(sb.ToString());
    }

    // Filter(array, match, [include=True], [compare]) -> a 0-based String array of the (non-)matching elements. Always a
    // String array (measured: VarType 8200 for a String input, a Variant input and no match alike); it was a Variant
    // array. All measured, native and in the IDE:
    // - A Null match, switch or compare is 94, even for an array never allocated, and even ahead of the array check:
    //   `Filter("abc", Null)` is 94, not 13.
    // - The switch reads as CBool reads it: Empty and "False" exclude, "True" and 2 include, "x" is 13.
    // - The compare reads as a number: Empty is a binary compare, 1, "1" and 5 a text compare, "x" is 13. It raised 13
    //   for Empty, and took 5 as binary.
    private static Vb6Value Filter(IReadOnlyList<Vb6Value> a)
    {
        if (a.Count < 2) throw InvalidCall();
        if (a[1].IsNull || (Supplied(a, 2) && a[2].IsNull) || (Supplied(a, 3) && a[3].IsNull))
            throw new VBRunTimeException(VBStandardError.InvalidUseOfNull);
        if (a[0].Value is not VBArray arr) throw new VBRunTimeException(VBStandardError.TypeMismatch);
        string match = AsStr(a[1]);
        bool include = !Supplied(a, 2) || CBool(a[2]).Value is true;
        bool text = Supplied(a, 3) && Convert.ToInt64(VbNumeric.CoerceOnStore(a[3], Vb6Value.ValueType.Long, null).Value ?? 0L) != 0;
        var cmp = text ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        // An array never allocated, or erased, gives back an unallocated String array with no error, whatever its own type
        // (measured for String, Variant and Long arrays and an erased one: LBound and UBound of the result raise 9).
        if (arr.Rank == 0) return new Vb6Value(new Vb6Value.ValueType(Vb6Value.ValueType.String, true));

        var items = new List<Vb6Value>();
        int lo = arr.LowerBound(1), hi = arr.UpperBound(1);
        for (int i = lo; i <= hi; i++)
        {
            var element = ElementText(arr.GetValue(new[] { i }));
            bool contains = element.IndexOf(match, cmp) >= 0;
            if (contains == include) items.Add(new Vb6Value(element));
        }
        return MakeArray(Vb6Value.ValueType.String, items);
    }

    // Join and Filter convert each element as a store into a String does (measured, native and in the IDE, for both):
    // Null is 94, an array 13, and a class with no default member 438. A Boolean, a number, a Date and Empty give their
    // text, and a class with a default member its value (here refused, #174). A Null element gave "" and an array the
    // CLR type name, with no error. VB6 also raises 13 for an Error value and 450 for a Collection, which HexIDE has
    // neither of yet.
    private static string ElementText(Vb6Value element)
    {
        if (element.Value is VBArray) throw new VBRunTimeException(VBStandardError.TypeMismatch);
        return VbNumeric.CoerceOnStore(element, Vb6Value.ValueType.String, null).Value as string ?? "";
    }
}
