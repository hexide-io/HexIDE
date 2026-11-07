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
        d["Split"]  = (_, a, text) => Split(a, text);
        d["Join"]   = (_, a, _) => Join(a);
        d["Filter"] = (_, a, text) => Filter(a, text);
    }

    private static Vb6Value Bound(IReadOnlyList<Vb6Value> a, bool upper)
    {
        if (a.Count < 1 || a.Count > 2)
            throw InvalidCall();
        if (a[0].Value is not VBArray array)
            throw new VBRunTimeException(VBStandardError.TypeMismatch);
        // The dimension coerces as CInt does (measured, native and in the IDE): Null 94, "x" 13, and 40000, 32768, 70000&,
        // -32769 and "40000" all 6, where 32767 is 9; True, Empty, -1 and 1.5 (which rounds to 2) name no dimension of a
        // one-dimensional array, so 9. An omitted Optional is 13, as an Error value is: it is passed, not left out, so it
        // is not read as dimension 1.
        if (a.Count > 1 && a[1].IsMissing)
            throw new VBRunTimeException(VBStandardError.TypeMismatch);
        int dimension = Supplied(a, 1) ? Convert.ToInt32(NarrowNum(a[1], Vb6Value.ValueType.Integer).Value) : 1;
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

    /// <summary>
    /// A String's bytes, as VB6 makes them when a String is stored into a Byte() (measured, #998): two to a character, low
    /// byte first, from 0, so "ab" is 97 0 98 0, ChrW(256) is 0 1, and "" is a Byte() 0 To -1.
    /// </summary>
    internal static Vb6Value StringBytes(string text)
    {
        var bytes = new List<Vb6Value>(text.Length * 2);
        foreach (var c in text)
        {
            bytes.Add(new Vb6Value((byte)(c & 0xFF)));
            bytes.Add(new Vb6Value((byte)(c >> 8)));
        }
        return MakeArray(Vb6Value.ValueType.Byte, bytes);
    }

    // Split(expr, [delimiter=" "], [limit=-1], [compare]) -> a 0-based String array.
    private static Vb6Value Split(IReadOnlyList<Vb6Value> a, bool compareText)
    {
        if (a.Count < 1) throw InvalidCall();
        var text = Supplied(a, 3) ? IsTextCompare(CompareArgument(a[3]), databaseAsText: true) : compareText;
        string s = AsStr(a[0]);
        string delim = Supplied(a, 1) ? AsStr(a[1]) : " ";
        int limit = Supplied(a, 2) ? AsInt(a[2]) : -1;

        var parts = SplitVb(s, delim, limit, text);
        var items = new List<Vb6Value>(parts.Count);
        foreach (var p in parts) items.Add(new Vb6Value(p));
        return MakeArray(Vb6Value.ValueType.String, items);
    }

    // As text a delimiter matches whole characters only, as Replace's find does: "s" does not split "aßb" (measured, #1567).
    private static List<string> SplitVb(string s, string delim, int limit, bool text)
    {
        var result = new List<string>();
        if (s.Length == 0 || limit == 0) return result;      // empty input / limit 0 -> empty array
        if (delim.Length == 0) { result.Add(s); return result; }
        var searchable = text ? new VbTextCompare.Searchable(s) : null;
        int start = 0;
        while (true)
        {
            if (limit > 0 && result.Count == limit - 1) { result.Add(s.Substring(start)); break; }  // remainder
            int idx = Find(s, delim, start, searchable, wholeCharacters: true, out var length);
            if (idx < 0) { result.Add(s.Substring(start)); break; }
            result.Add(s.Substring(start, idx - start));
            start = idx + length;
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
    // read either; with one, `Join(Array("a"), Null)` is 94, Nothing 91 and a class with no default member 438. A Null
    // or Nothing delimiter read as "".
    private static Vb6Value Join(IReadOnlyList<Vb6Value> a)
    {
        if (a.Count < 1) throw InvalidCall();
        if (a[0].Value is not VBArray arr) throw new VBRunTimeException(VBStandardError.TypeMismatch);
        if (arr.Rank == 0 || arr.UpperBound(1) < arr.LowerBound(1)) return new Vb6Value("");
        // Allocated arrays must be one-dimensional String or Variant arrays (measured, #875).
        if (arr.Rank != 1 || (arr.ElementType is { } type
            && type != Vb6Value.ValueType.String && type != Vb6Value.ValueType.EmptyVariant))
            throw InvalidCall();
        string delim = Supplied(a, 1) ? ArgumentText(a[1]) : " ";
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
    // - The arguments convert right to left, compare, then switch, then match, before the array is looked at, and an
    //   object among them is read only as its turn comes: a Null one is 94, `Filter(d, Null, "x")` is the switch's 13,
    //   and `Filter(d, k, "x")` is 13 too, where k is an object with no default member. So `Filter("abc", Null)` is 94,
    //   not 13. The dispatcher leaves Filter's arguments to it for that reason.
    // - The switch reads as CBool reads it: Empty and "False" exclude, "True" and 2 include, "x" is 13.
    // - An array with no element, never allocated, erased or 0 To -1, gives back an unallocated String array, with no
    //   error.
    // - Every element converts before any is compared: `Filter(Array("x", Null), "a", True, 2)` is the element's 94, not
    //   the compare's 5.
    // - The compare is checked only when a comparison is made, which an empty match or an empty element never needs
    //   (see IsTextCompare): `Filter(list, "", True, 2)` is the whole list.
    private static Vb6Value Filter(IReadOnlyList<Vb6Value> a, bool compareText)
    {
        if (a.Count < 2) throw InvalidCall();
        long compare = Supplied(a, 3) ? CompareArgument(a[3]) : (compareText ? 1 : 0);
        // Each conversion reads an object as it converts it: NarrowNum and CBool through RefuseNonScalar, after the Null
        // check, and ArgumentText through VbDisplay.ToText.
        bool include = !Supplied(a, 2) || CBool(RefuseNull(a[2])).Value is true;
        string match = ArgumentText(a[1]);
        if (a[0].Value is not VBArray arr) throw new VBRunTimeException(VBStandardError.TypeMismatch);
        if (arr.Rank == 0 || arr.UpperBound(1) < arr.LowerBound(1))
            return new Vb6Value(new Vb6Value.ValueType(Vb6Value.ValueType.String, true));

        if (arr.Rank != 1 || (arr.ElementType is { } type
            && type != Vb6Value.ValueType.String && type != Vb6Value.ValueType.EmptyVariant))
            throw new VBRunTimeException(VBStandardError.TypeMismatch);

        var elements = new List<string>();
        int lo = arr.LowerBound(1), hi = arr.UpperBound(1);
        for (int i = lo; i <= hi; i++)
            elements.Add(ElementText(arr.GetValue(new[] { i })));

        bool? text = null;
        var items = new List<Vb6Value>();
        foreach (var element in elements)
        {
            // An empty match is in every element, and an empty element holds no other match, without a comparison. As text
            // a match may start or end inside an expansion, as InStr's does: "s" is in "ß" (measured, #1567).
            bool contains = match.Length == 0
                || (element.Length > 0
                    && Find(element, match, 0, text ??= IsTextCompare(compare), wholeCharacters: false, out _) >= 0);
            if (contains == include) items.Add(new Vb6Value(element));
        }
        return MakeArray(Vb6Value.ValueType.String, items);
    }

    // A compare argument read as CLng reads it (banker's rounding, so 1.5 is 2 and 0.5 is 0), all measured for Filter:
    // Null is 94, Nothing 91, an array and a string that is not a number 13, and 3E+10 is 6. An object is read as it is
    // converted. It read Nothing as 0, a binary compare, and passed Err on as an object.
    private static long CompareArgument(Vb6Value compare) =>
        Convert.ToInt64(NarrowNum(compare, Vb6Value.ValueType.Long).Value);

    // Filter's compare, measured: 0 is binary and 1 text. 2 (vbDatabaseCompare, which needs a database), any negative
    // value and True are 5. From 3 up, the rows are consistent with a Windows locale identifier: valid ones (3, 4, 5,
    // 1033, 2057, 4096) compare as text, and invalid ones (3000, 4095, 32767, 65536) are 5. Whether a valid one compares
    // in that locale, or simply as text, is not measured. HexIDE cannot tell which identifiers Windows accepts, so it
    // refuses those loudly rather than guess (#881).
    private static bool IsTextCompare(long compare, bool databaseAsText = false) => compare switch
    {
        0 => false,
        1 => true,
        2 when databaseAsText => true,
        2 or < 0 => throw InvalidCall(),
        _ => throw new NotImplementedException(
            $"A compare of {compare} is not supported. VB6 compares as text for some values from 3 up and raises 5 for " +
            "others, consistently with reading them as Windows locale identifiers, and HexIDE cannot tell which (#881)."),
    };

    // Nothing is the Object type holding no reference (Vb6Value.Nothing).
    private static bool IsNothing(Vb6Value value) => value.Type == Vb6Value.ValueType.Object && value.Value is null;

    private static Vb6Value RefuseNull(Vb6Value value) =>
        value.IsNull ? throw new VBRunTimeException(VBStandardError.InvalidUseOfNull) : value;

    // A match or a delimiter as text. Null is 94 and Nothing 91 (measured for both); anything else reads as a value.
    private static string ArgumentText(Vb6Value value)
    {
        if (IsNothing(RefuseNull(value)))
            throw new VBRunTimeException(VBStandardError.ObjectVariableOrWithBlockVariableNotSet);
        return AsStr(value);
    }

    // Join and Filter convert each element as a store into a String does (measured, native and in the IDE, for both):
    // Null is 94, Nothing 91, an array 13, and a class with no default member 438. A Boolean, a number, a Date and Empty
    // give their text, and a class with a default member its value (here refused, #174). A Null element gave "", and
    // an array the CLR type name, with no error. VB6 also raises 13 for an Error value and 450 for a Collection, which
    // HexIDE has neither of yet.
    private static string ElementText(Vb6Value element)
    {
        if (element.Value is VBArray) throw new VBRunTimeException(VBStandardError.TypeMismatch);
        if (IsNothing(element))
            throw new VBRunTimeException(VBStandardError.ObjectVariableOrWithBlockVariableNotSet);
        return VbNumeric.CoerceOnStore(element, Vb6Value.ValueType.String, null).Value as string ?? "";
    }
}
