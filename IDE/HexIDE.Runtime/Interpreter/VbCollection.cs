using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace HexIDE.Runtime.Interpreter;

/// <summary>
/// VB6's in-box Collection (#1471): <c>Add</c>, <c>Item</c> (its default member), <c>Remove</c> and <c>Count</c>, and
/// For Each. Everything here is measured with vb6.exe, native and p-code alike (the oracle's "Collection").
/// <list type="bullet">
/// <item>An index that is a String is always a key (<c>c("2")</c> is 5 when no item has the key "2"). Any other index is
/// a position, from 1, converted as CLng converts (<c>c(1.5)</c> is the second item, <c>c(0.5)</c> is 9). A position out
/// of range is 9, a key not there is 5, Null is 13, and a number past a Long is 13 rather than 6.</item>
/// <item>A key must be a String, or <c>Add</c> raises 13; <c>""</c> is a key like any other. A key already there is 457.</item>
/// <item><c>Add</c> checks Before and After both given (5) first, then the key's type (13), then a duplicate (457), and
/// only then the position.</item>
/// <item>Items are held as a Variant holds them: an object by reference, counted, and anything else by value.</item>
/// <item>When the last reference to the Collection goes, its items are released, first to last.</item>
/// </list>
/// </summary>
public sealed class VbCollection(BasicInterpreter interpreter) : ICSharpAsyncProxy, INamedParameterSource
{
    // A key is equal to another as VB6's text compare finds it (measured, #1479): "strasse" and "straße" are one key, and
    // so are "a" and "a" & Chr(0), while "e" and "é" are two. VbTextCompare compares and hashes them as vb6.exe does.
    internal static readonly StringComparer KeyComparer = VbTextCompare.Comparer;

    private sealed record Entry(Vb6Value Value, string? Key);

    private readonly List<Entry> items = [];
    private readonly Dictionary<string, Entry> byKey = new(KeyComparer);
    private readonly List<Cursor> cursors = [];

    /// <summary>The references the program holds to it, as <see cref="VbObject.RefCount"/> counts a class instance's.</summary>
    internal int RefCount;

    /// <summary>How many items it holds, for the debugger's Locals and Watch.</summary>
    internal int ItemCount => items.Count;

    /// <summary>The items in order, for the debugger's Locals and Watch.</summary>
    internal IReadOnlyList<Vb6Value> Snapshot()
    {
        var values = new List<Vb6Value>(items.Count);
        foreach (var entry in items)
            values.Add(entry.Value);
        return values;
    }

    /// <summary>
    /// Where a For Each over it has got to: the position of the item it is on, from 1, or 0 before the first. An item
    /// added or removed at or before it moves it, so the loop goes on to the item that now follows the one it was on
    /// (measured: removing the current item goes on to the next, an item added at the end is reached, and one added
    /// before is not). A cursor holds no reference: when the last one goes, the Collection empties and the loop ends.
    /// </summary>
    internal sealed class Cursor
    {
        public int Position;
    }

    public int Count => items.Count;

    internal Cursor StartEnumeration()
    {
        var cursor = new Cursor();
        cursors.Add(cursor);
        return cursor;
    }

    internal void EndEnumeration(Cursor cursor) => cursors.Remove(cursor);

    internal bool TryNext(Cursor cursor, out Vb6Value item)
    {
        if (cursor.Position >= items.Count)
        {
            item = default;
            return false;
        }
        item = items[cursor.Position++].Value;
        return true;
    }

    void ICSharpProxy.Call(string method, List<Vb6Value> args) =>
        throw new InvalidOperationException("Collection calls must be awaited");

    public async Task<Vb6Value?> CallAsync(string method, List<Vb6Value> args)
    {
        switch (method.ToLowerInvariant())
        {
            case "add":
                ArgumentCount(args, 1, 4);
                Add(args[0], Argument(args, 1), Argument(args, 2), Argument(args, 3));
                return null;
            case "item":
                ArgumentCount(args, 1, 1);
                return Item(args[0]);
            case "remove":
                ArgumentCount(args, 1, 1);
                await Remove(args[0]);
                return null;
            case "count":
                ArgumentCount(args, 0, 0);
                return new Vb6Value((long)items.Count);
            default:
                // Measured: an unknown member is 438 at run time, even through a variable declared As Collection.
                throw new VBRunTimeException(VBStandardError.ObjectDoesntSupportThisPropertyOrMethod);
        }
    }

    // The members' parameter names, which VB6 takes as named arguments (measured: `c.Add Item:=…, Key:=…`, `Before:=`,
    // `After:=`, and `Index:=` to Remove and Item).
    private static readonly string[] AddParameters = ["Item", "Key", "Before", "After"];
    private static readonly string[] IndexParameter = ["Index"];

    bool INamedParameterSource.TryGetParameters(string member, out IReadOnlyList<string> names, out int required)
    {
        switch (member.ToLowerInvariant())
        {
            case "add":
                (names, required) = (AddParameters, 1);
                return true;
            case "item" or "remove":
                (names, required) = (IndexParameter, 1);
                return true;
            case "count":
                (names, required) = ([], 0);
                return true;
            default:
                (names, required) = ([], 0);
                return false;
        }
    }

    /// <summary>The default member, <c>c(i)</c> and <c>c!key</c>: Item.</summary>
    internal Vb6Value Default(IReadOnlyList<Vb6Value> args)
    {
        ArgumentCount(args, 1, 1);
        return Item(args[0]);
    }

    private Vb6Value Item(Vb6Value index) => items[Position(index) - 1].Value.AsVariant();

    private void Add(Vb6Value item, Vb6Value key, Vb6Value before, Vb6Value after)
    {
        if (!before.IsMissing && !after.IsMissing)
            throw new VBRunTimeException(VBStandardError.InvalidProcedureCall);
        string? keyText = null;
        if (!key.IsMissing)
        {
            // A key that isn't a String is first looked up as Item would look up an index: one naming an item already there
            // is "already associated", 457, and anything else 13 (measured at Count 3: keys 1, 3, 1.5, CByte(2) and
            // CDate(2) are 457; 4, 0, 3.5, True, an array and a Collection are 13).
            if (key.Type != Vb6Value.ValueType.String)
                throw new VBRunTimeException(NamesAnItem(key)
                    ? VBStandardError.ThisKeyIsAlreadyAssociatedWithAnElementOfThisCollection
                    : VBStandardError.TypeMismatch);
            keyText = (string)key.Value!;
            if (byKey.ContainsKey(keyText))
                throw new VBRunTimeException(VBStandardError.ThisKeyIsAlreadyAssociatedWithAnElementOfThisCollection);
        }
        var at = !before.IsMissing ? Position(before) - 1
            : !after.IsMissing ? Position(after)
            : items.Count;

        var stored = interpreter.CopyForStore(item);
        interpreter.AddRef(stored);
        var entry = new Entry(stored, keyText);
        items.Insert(at, entry);
        if (keyText is not null)
            byKey.Add(keyText, entry);
        foreach (var cursor in cursors)
            if (at < cursor.Position)
                cursor.Position++;
    }

    private async Task Remove(Vb6Value index)
    {
        var at = Position(index) - 1;
        var entry = items[at];
        items.RemoveAt(at);
        if (entry.Key is not null)
            byKey.Remove(entry.Key);
        foreach (var cursor in cursors)
            if (at < cursor.Position)
                cursor.Position--;
        await interpreter.ReleaseRef(entry.Value);
    }

    /// <summary>The last reference has gone: every item is released, first to last (measured).</summary>
    internal async Task ReleaseItems()
    {
        var held = items.ToArray();
        items.Clear();
        byKey.Clear();
        foreach (var cursor in cursors)
            cursor.Position = 0;
        foreach (var entry in held)
            await interpreter.ReleaseRef(entry.Value);
    }

    // An index as a position from 1, raising what VB6 raises for one that names no item. In an empty Collection that is 5
    // for any index, a position, a key, Null or Empty, where a Collection with items gives 9, 5 or 13 (measured, #1492).
    private int Position(Vb6Value index)
    {
        if (index.IsMissing)
            throw new VBCompileErrorException("Argument not optional");
        if (items.Count == 0)
            throw new VBRunTimeException(VBStandardError.InvalidProcedureCall);
        if (index.Type == Vb6Value.ValueType.String)
        {
            if (!byKey.TryGetValue((string)index.Value!, out var entry))
                throw new VBRunTimeException(VBStandardError.InvalidProcedureCall);
            return items.IndexOf(entry) + 1;
        }
        var position = AsPosition(index);
        if (position < 1 || position > items.Count)
            throw new VBRunTimeException(VBStandardError.SubscriptOutOfRange);
        return (int)position;
    }

    private bool NamesAnItem(Vb6Value index)
    {
        try
        {
            Position(index);
            return true;
        }
        catch (VBRunTimeException)
        {
            return false;
        }
    }

    // A numeric index rounds half to even, as CLng does; Empty is 0 and True -1. Null, and a number a Long can't hold, are
    // 13 (measured: `c(2147483648#)` is 13, where CLng gives 6).
    private static long AsPosition(Vb6Value index)
    {
        var value = ObjectAsValue.Scalar(index, null);
        if (value.Type == Vb6Value.ValueType.EmptyVariant)
            return 0;
        if (value.Type == Vb6Value.ValueType.Boolean)
            return (bool)value.Value! ? -1 : 0;
        if (value.IsNull || value.Type.IsArray || value.Value is VbUdt || value.Type == Vb6Value.ValueType.String
            || !Vb6Value.TryNumericToDouble(value, out var number))
            throw new VBRunTimeException(VBStandardError.TypeMismatch);
        var rounded = Math.Round(number, MidpointRounding.ToEven);
        if (rounded < int.MinValue || rounded > int.MaxValue)
            throw new VBRunTimeException(VBStandardError.TypeMismatch);
        return (long)rounded;
    }

    private static Vb6Value Argument(List<Vb6Value> args, int i) => i < args.Count ? args[i] : Vb6Value.Missing;

    /// <summary>The ways a program assigns a Collection's item, which VB6 refuses differently late-bound.</summary>
    internal enum ItemAssignment
    {
        /// <summary><c>c(1) = x</c>.</summary>
        Index,
        /// <summary><c>Set c(1) = x</c>.</summary>
        SetIndex,
        /// <summary><c>c.Item(1) = x</c> and <c>c!k = x</c>.</summary>
        Member,
    }

    /// <summary>
    /// An item assigned, which no Collection allows. Through a variable declared As Collection, VB6 looks the index up
    /// first and raises what that raises, so only an item that is there gives 424. Late-bound, <c>Set o(1) = x</c> is 438
    /// with no look, and so is <c>w(1) = x</c> through a Variant; any other form looks first and then gives 451 (all
    /// measured, #1492). The item is left as it was.
    /// </summary>
    internal VBRunTimeException ItemAssigned(Vb6Value reference, IReadOnlyList<Vb6Value> args, ItemAssignment form)
    {
        var earlyBound = IsEarlyBound(reference);
        if (!earlyBound && (form == ItemAssignment.SetIndex || (form == ItemAssignment.Index && reference.HeldAsVariant)))
            return new VBRunTimeException(VBStandardError.ObjectDoesntSupportThisPropertyOrMethod);
        ArgumentCount(args, 1, 1);
        Position(args[0]);
        return new VBRunTimeException(earlyBound ? VBStandardError.ObjectRequired : VBStandardError.ObjectNotACollection);
    }

    /// <summary>Whether a Collection is reached through a variable declared As Collection: early-bound, where VB6 refuses a
    /// call's arguments before the program runs. Through an Object or a Variant it is late-bound.</summary>
    internal static bool IsEarlyBound(Vb6Value reference) => reference.DeclaredAs is { } declared && ExpressionExecutor.IsCollectionType(declared);

    /// <summary>
    /// The run-time error a late-bound call raises where an early-bound one is refused (measured, through an Object and a
    /// Variant alike): a name the member doesn't take, or one given twice, is 448, and any other mismatch of arguments 450,
    /// too few among them. <c>Count</c> given an argument is 451. Null for any other compile error, which stands.
    /// </summary>
    internal static VBRunTimeException? LateBound(VBCompileErrorException refused, string member)
    {
        if (member.Equals("Count", StringComparison.OrdinalIgnoreCase) && refused.Message.Contains("Wrong number of arguments"))
            return new VBRunTimeException(VBStandardError.ObjectNotACollection);
        if (refused.Message.Contains("Named argument not found") || refused.Message.Contains("Named argument already specified"))
            return new VBRunTimeException(VBStandardError.NamedArgumentNotFound);
        if (refused.Message.Contains("Wrong number of arguments") || refused.Message.Contains("Argument not optional"))
            return new VBRunTimeException(VBStandardError.WrongNumberOfArgumentsOrInvalidPropertyAssignment);
        return null;
    }

    // Through a variable declared As Collection, too few arguments and too many are VB6's compile errors (measured: `c.Add`,
    // `c.Item`, `c.Remove`; `c.Add 1, , , , 5`, `c(1, 2)`, `c.Count(1)`). Through an Object or a Variant they are run-time
    // errors, which the caller makes of these (LateBound).
    private static void ArgumentCount(IReadOnlyList<Vb6Value> args, int least, int most)
    {
        if (args.Count > most)
            throw new VBCompileErrorException("Wrong number of arguments or invalid property assignment");
        var given = args.Count;
        while (given > 0 && args[given - 1].IsMissing)
            given--;
        if (given < least || (least > 0 && args[0].IsMissing))
            throw new VBCompileErrorException("Argument not optional");
    }
}
