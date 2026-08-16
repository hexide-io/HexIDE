using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Media;
using HexIDE.Runtime.BuiltinTypes;
using LanguageExt;

namespace HexIDE.Runtime.Interpreter;

public readonly struct Vb6Value : IEquatable<Vb6Value>
{
    public bool Equals(Vb6Value other) => Type == other.Type && Equals(Value, other.Value);

    public int? TryCompareTo(Vb6Value other)
    {
        if (Type == other.Type)
        {
            if (Type == ValueType.Integer)
                return ((int)Value!).CompareTo((int)other.Value!);
            if (Type == ValueType.Long)
                return ((long)Value!).CompareTo((long)other.Value!);
            if (Type == ValueType.Byte)
                return ((byte)Value!).CompareTo((byte)other.Value!);
            if (Type == ValueType.Single)
                return ((float)Value!).CompareTo((float)other.Value!);
            if (Type == ValueType.Double)
                return ((double)Value!).CompareTo((double)other.Value!);
            if (Type == ValueType.Currency || Type == ValueType.Decimal)
                return ((decimal)Value!).CompareTo((decimal)other.Value!);
            if (Type == ValueType.Date)
                return ((DateTime)Value!).CompareTo((DateTime)other.Value!);
            if (Type == ValueType.String)
                return String.Compare((string)Value!, (string)other.Value!, StringComparison.Ordinal);
            if (Type == ValueType.Boolean)
                return ((bool)Value!).CompareTo((bool)other.Value!);
            return null;
        }

        // Cross-type numeric comparison (e.g. Select Case over Integer/Long/Currency). Stopgap via double;
        // width-exact promotion arrives with VbNumeric in 2.3.
        if (TryNumericToDouble(this, out var a) && TryNumericToDouble(other, out var b))
            return a.CompareTo(b);

        return null;
    }

    /// <summary>True and the numeric value as a double if <paramref name="v"/> is any VB6 numeric subtype
    /// (Byte/Integer/Long/Single/Double/Currency/Decimal, or a Date via its OLE serial). The single place
    /// that widens the value model's numeric subtypes to double for the operator/comparison cascades.</summary>
    public static bool TryNumericToDouble(Vb6Value v, out double d)
    {
        switch (v.Value)
        {
            case byte b: d = b; return true;
            case int i: d = i; return true;
            case long l: d = l; return true;
            case float f: d = f; return true;
            case double db: d = db; return true;
            case decimal m: d = (double)m; return true;
            case DateTime dt: d = dt.ToOADate(); return true;
        }
        d = 0;
        return false;
    }

    public override bool Equals(object? obj) => obj is Vb6Value other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Type.GetHashCode(), Value);

    public static bool operator ==(Vb6Value left, Vb6Value right) => left.Equals(right);

    public static bool operator !=(Vb6Value left, Vb6Value right) => !left.Equals(right);

    public readonly ValueType Type;
    public readonly object? Value;

    private Vb6Value(ValueType type, object? value)
    {
        Type = type;
        Value = value;
    }

    public Vb6Value(int value)
    {
        // Magnitude rule: a C# int that fits Int16 is a VB6 Integer (kept int-boxed — its 16-bit range is
        // enforced by range-checks at operator/store boundaries later, not by the box); otherwise it is a Long.
        // This keeps existing `new Vb6Value(3)` an Integer (zero test churn) while `new Vb6Value(40000)` is a Long.
        if (value is >= short.MinValue and <= short.MaxValue)
        {
            Type = ValueType.Integer;
            Value = value;
        }
        else
        {
            Type = ValueType.Long;
            Value = (long)value;
        }
    }
    public Vb6Value(long value) : this(ValueType.Long, value) {}
    public Vb6Value(byte value) : this(ValueType.Byte, value) {}
    public Vb6Value(DateTime value) : this(ValueType.Date, value) {}

    // Currency and Decimal both box a C# decimal, so a single Vb6Value(decimal) ctor would be ambiguous;
    // these factories pick the intended VB6 subtype (distinguished at runtime by Type).
    // Currency is always a fixed 4 decimal places (banker's-rounded at construction); Decimal keeps full scale.
    public static Vb6Value NewCurrency(decimal value) => new Vb6Value(ValueType.Currency, Math.Round(value, 4, MidpointRounding.ToEven));
    public static Vb6Value NewDecimal(decimal value) => new Vb6Value(ValueType.Decimal, value);

    // A user-defined Type instance (the concrete type is on the VbUdt).
    public static Vb6Value NewUdt(VbUdt udt) => new Vb6Value(ValueType.UserDefinedType, udt);

    // A class instance (reference type). Nothing is Vb6Value.Nothing (Object type, null Value).
    public static Vb6Value NewObject(VbObject obj) => new Vb6Value(ValueType.Object, obj);

    public Vb6Value(float value) : this(ValueType.Single, value) {}
    public Vb6Value(double value) : this(ValueType.Double, value) {}
    public Vb6Value(bool value) : this(ValueType.Boolean, value) {}
    public Vb6Value(string? value) : this(value == null ? ValueType.Null : ValueType.String, value) {}

    public Vb6Value(bool? b) : this(b.HasValue ? ValueType.Boolean : ValueType.Null, b) { }

    public Vb6Value(ICSharpProxy proxy) : this(ValueType.CSharpProxyObject, proxy) { }

    public Vb6Value(Control control) : this(ValueType.Control, control) { }

    public Vb6Value(VBColor color) : this(ValueType.Color, color) { }

    public Vb6Value(ValueType type)
    {
        Type = type;
        Value = GetDefaultValueFor(type);
    }

    public Vb6Value(ValueType type, IReadOnlyList<(int lowerBound, int upperBound)> dimensions)
    {
        if (!type.IsArray)
            throw new Exception("This should be only called when type is an array");

        Type = type;
        var array = new VBArray(type.Type.Match(prim => new ValueType(prim, false), inner => new ValueType(inner, false)), dimensions);
        Value = array;
    }

    private static object? GetDefaultValueFor(ValueType type)
    {
        if (type.IsArray)
            return new VBArray();
        return type.Type.Match<object?>(
            _ => throw new NotImplementedException(),
            prim => prim switch
            {
                ValueTypePrimitive.Color => VBColor.FromColor(0, 0, 0),
                ValueTypePrimitive.Date => DateTime.FromOADate(0.0),   // VB6 serial 0 = 1899-12-30 00:00
                ValueTypePrimitive.Double => 0.0,
                ValueTypePrimitive.Single => 0.0f,
                ValueTypePrimitive.File => throw new NotImplementedException(),
                ValueTypePrimitive.Integer => 0,
                ValueTypePrimitive.Long => 0L,
                ValueTypePrimitive.Byte => (byte)0,
                ValueTypePrimitive.Currency => 0m,
                ValueTypePrimitive.Decimal => 0m,
                ValueTypePrimitive.String => "",
                ValueTypePrimitive.Boolean => false,
                ValueTypePrimitive.Control => throw new NotImplementedException(),
                ValueTypePrimitive.Nothing => null,
                ValueTypePrimitive.EmptyVariant => null,
                ValueTypePrimitive.CSharpProxyObject => null,
                ValueTypePrimitive.Null => null,
                // A UDT's zero value is field-by-field and needs the type table, so it is built by the
                // interpreter (NewUdt), never through the type-only default path — null here is a guard against
                // constructing an unpopulated UDT via `new Vb6Value(ValueType.UserDefinedType)`.
                ValueTypePrimitive.UserDefinedType => null,
                // A class-typed variable defaults to Nothing (a null object reference) — handled where a
                // class-typed Dim is executed; the type-only default path just yields null.
                ValueTypePrimitive.Object => null,
                _ => throw new ArgumentOutOfRangeException(nameof(prim), prim, null)
            });

    }

    public class ValueType
    {
        private readonly Either<ValueType, ValueTypePrimitive> type;
        private readonly bool array;

        public ValueType(ValueTypePrimitive primitive, bool array)
        {
            this.type = primitive;
            this.array = array;
        }

        public ValueType(ValueType innerType, bool array)
        {
            if (!innerType.IsArray)
                this.type = innerType.Type;
            else
                this.type = innerType;
            this.array = array;
        }

        public bool IsArray => array;
        public Either<ValueType, ValueTypePrimitive> Type => type;

        public static readonly ValueType Color = new ValueType(ValueTypePrimitive.Color, false);
        public static readonly ValueType Date = new ValueType(ValueTypePrimitive.Date, false);
        public static readonly ValueType Double = new ValueType(ValueTypePrimitive.Double, false);
        public static readonly ValueType Single = new ValueType(ValueTypePrimitive.Single, false);
        public static readonly ValueType File = new ValueType(ValueTypePrimitive.File, false);
        public static readonly ValueType Integer = new ValueType(ValueTypePrimitive.Integer, false);
        public static readonly ValueType String = new ValueType(ValueTypePrimitive.String, false);
        public static readonly ValueType Boolean = new ValueType(ValueTypePrimitive.Boolean, false);
        public static readonly ValueType Control = new ValueType(ValueTypePrimitive.Control, false);
        public static readonly ValueType Nothing = new ValueType(ValueTypePrimitive.Nothing, false);
        public static readonly ValueType EmptyVariant = new ValueType(ValueTypePrimitive.EmptyVariant, false);
        public static readonly ValueType CSharpProxyObject = new ValueType(ValueTypePrimitive.CSharpProxyObject, false);
        public static readonly ValueType Null = new ValueType(ValueTypePrimitive.Null, false);
        public static readonly ValueType Long = new ValueType(ValueTypePrimitive.Long, false);
        public static readonly ValueType Byte = new ValueType(ValueTypePrimitive.Byte, false);
        public static readonly ValueType Currency = new ValueType(ValueTypePrimitive.Currency, false);
        public static readonly ValueType Decimal = new ValueType(ValueTypePrimitive.Decimal, false);
        // A user-defined Type instance. One shared ValueType for all UDTs — the concrete type identity lives on
        // the VbUdt (its TypeName), not here; MVP does no cross-UDT-type checking (approximation only).
        public static readonly ValueType UserDefinedType = new ValueType(ValueTypePrimitive.UserDefinedType, false);
        // A class-module instance (REFERENCE type). One shared ValueType; the concrete class + per-instance state
        // live on the VbObject. A null Value = Nothing.
        public static readonly ValueType Object = new ValueType(ValueTypePrimitive.Object, false);

        protected bool Equals(ValueType other) => type.Equals(other.type) && array == other.array;

        public override bool Equals(object? obj)
        {
            if (obj is null) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != GetType()) return false;
            return Equals((ValueType)obj);
        }

        public override int GetHashCode() => HashCode.Combine(type, array);

        public static bool operator ==(ValueType? left, ValueType? right) => Equals(left, right);

        public static bool operator !=(ValueType? left, ValueType? right) => !Equals(left, right);

        public override string ToString()
        {
            if (array)
                return type + "()";
            return type.ToString();
        }
    }

    public enum ValueTypePrimitive
    {
        Color,
        Date,
        Double,
        Single,
        File,
        Integer,
        String,
        Boolean,
        Control,
        Nothing,
        EmptyVariant,
        CSharpProxyObject,
        Null,
        // Appended (not inserted) so existing ordinal values are unchanged.
        Long,
        Byte,
        Currency,
        Decimal,
        UserDefinedType,
        Object
    }

    public static implicit operator Vb6Value(int value) =>
        new Vb6Value(value);   // magnitude rule (Integer if it fits Int16, else Long)

    public static implicit operator Vb6Value(string value) =>
        new Vb6Value(ValueType.String, value);

    public static implicit operator Vb6Value(bool value) =>
        new Vb6Value(ValueType.Boolean, value);

    public static implicit operator Vb6Value(bool? value) =>
        new Vb6Value(value);

    public static implicit operator Vb6Value(double value) =>
        new Vb6Value(ValueType.Double, value);

    public override string ToString()
    {
        return $"<{Type}>({Value})";
    }

    public static readonly Vb6Value Null = new Vb6Value(ValueType.Null);
    // Nothing = a null object reference. Represented as the Object type with a null Value (NOT the legacy
    // value-less ValueTypePrimitive.Nothing) so `Is` / `Is Nothing` / an unset object var all collapse to one
    // ReferenceEquals-on-Value path and never split Nothing across two types.
    public static readonly Vb6Value Nothing = new Vb6Value(ValueType.Object, (object?)null);
    public static readonly Vb6Value Variant = new Vb6Value(ValueType.EmptyVariant);
    public bool IsNull => Type == ValueType.Null;
}

public class VBArray
{
    private List<(int lbound, int ubound)> bounds = new();
    private object[] array;
    private Vb6Value.ValueType? elementType;   // remembered so Erase can reset a FIXED array's elements to default

    // Dynamic (`Dim a()`, ReDim, Array()/Split()/Filter()) vs fixed (`Dim a(N)`). Only Erase distinguishes them:
    // a dynamic array is freed (undimensioned), a fixed one keeps its bounds and resets elements. Default dynamic;
    // the fixed-`Dim` declaration sites flip it to false.
    public bool IsDynamic { get; set; } = true;

    public bool IsDefined => bounds.Count > 0;

    public VBArray()
    {
        array = [];
    }

    public VBArray(Vb6Value.ValueType innerType, IReadOnlyList<(int lowerBound, int upperBound)> bounds)
    {
        this.bounds.AddRange(bounds);
        elementType = innerType;
        array = CreateArray(innerType, 0);
    }

    /// <summary>Erase of a DYNAMIC array: free it → undimensioned. Subsequent LBound/UBound/element access raise
    /// Err 9 (oracle-verified) and it can be ReDim'd again. In-place, so the holding slot sees the change.</summary>
    public void Free()
    {
        bounds.Clear();
        array = [];
    }

    /// <summary>Erase of a FIXED array: bounds preserved, every element reset to the element type's default
    /// (0 / "" / Empty). In-place. Fixed arrays of a UDT/class element type are left unchanged — resetting those
    /// needs interpreter context the array doesn't hold (a documented limitation; scalar fixed arrays are correct).</summary>
    public void ResetElements()
    {
        if (elementType is { } et && et.Type.Match(_ => false, _ => true))   // scalar element types only
            ResetScalarElements(array, et);
    }

    private static void ResetScalarElements(object[] arr, Vb6Value.ValueType elementType)
    {
        for (int i = 0; i < arr.Length; i++)
        {
            if (arr[i] is object[] sub)
                ResetScalarElements(sub, elementType);
            else
                arr[i] = new Vb6Value(elementType);
        }
    }

    private object[] CreateArray(Vb6Value.ValueType innerType, int dimension)
    {
        // An inverted/negative range (Dim a(-2), a(2 To 0), ReDim a(5 To 1)) gives a negative element count. VB6
        // raises a trappable Subscript-out-of-range (Err 9); `new object[-1]` would throw an UNCATCHABLE
        // OverflowException. An empty array — bounds (0,-1), size 0 — is valid (used by Split/Filter/Array).
        int size = bounds[dimension].ubound - bounds[dimension].lbound + 1;
        if (size < 0)
            throw new VBRunTimeException(VBStandardError.SubscriptOutOfRange);
        var arr = new object[size];
        for (int i = 0; i < arr.Length; ++i)
        {
            if (dimension == bounds.Count - 1)
            {
                arr[i] = new Vb6Value(innerType);
            }
            else
            {
                arr[i] = CreateArray(innerType, dimension + 1);
            }
        }

        return arr;
    }

    public Vb6Value GetValue(IReadOnlyList<int> index)
    {
        if (index.Count != bounds.Count)
            throw new VBRunTimeException(VBStandardError.SubscriptOutOfRange);   // undimensioned/wrong-dim → Err 9 (oracle), trappable
        object arr = array;
        for (var index1 = 0; index1 < index.Count; index1++)
        {
            var i = index[index1];
            arr = ((object[])arr)[i - bounds[index1].lbound];
        }

        return (Vb6Value)arr;
    }

    public void SetValue(IReadOnlyList<int> index, Vb6Value val)
    {
        if (index.Count != bounds.Count)
            throw new VBRunTimeException(VBStandardError.SubscriptOutOfRange);   // undimensioned/wrong-dim → Err 9 (oracle), trappable
        object arr = array;
        for (var index1 = 0; index1 < index.Count - 1; index1++)
        {
            var i = index[index1];
            arr = ((object[])arr)[i - bounds[index1].lbound];
        }

        ((object[])arr)[index[^1] - bounds[^1].lbound] = val;
    }

    /// <summary>The number of dimensions (0 for an undimensioned array). Used by the debugger's Locals inspector
    /// to label array elements by their real subscripts.</summary>
    public int Rank => bounds.Count;

    /// <summary>Like <see cref="EnumerateElements"/> but yields each element's SUBSCRIPT TUPLE alongside its value,
    /// in the same column-major (first-subscript-fastest) order. Each yielded index array is a fresh copy (safe to
    /// retain). Yields nothing for an undimensioned array. Debugger-inspector use only.</summary>
    public IEnumerable<(int[] index, Vb6Value value)> EnumerateIndexed()
    {
        int n = bounds.Count;
        if (n == 0)
            yield break;

        var idx = new int[n];
        for (int i = 0; i < n; i++)
        {
            if (bounds[i].ubound < bounds[i].lbound)
                yield break;   // an empty dimension (UBound < LBound, e.g. Split("")) => no elements
            idx[i] = bounds[i].lbound;
        }

        while (true)
        {
            yield return ((int[])idx.Clone(), GetValue(idx));
            int d = 0;
            while (d < n)
            {
                idx[d]++;
                if (idx[d] <= bounds[d].ubound)
                    break;
                idx[d] = bounds[d].lbound;   // carry into the next (slower) dimension
                d++;
            }
            if (d == n)
                yield break;
        }
    }

    public int LowerBound(int dimension = 1)
    {
        return bounds[dimension - 1].lbound;
    }

    public int UpperBound(int dimension = 1)
    {
        return bounds[dimension - 1].ubound;
    }

    public int Length(int dimension = 1)
    {
        return UpperBound(dimension) - LowerBound(dimension) + 1;
    }

    /// <summary>Enumerates every element in VB6 <c>For Each</c> order — the FIRST subscript varies FASTEST
    /// (column-major), verified against vb6.exe: a <c>(1..2, 1..3)</c> array yields (1,1),(2,1),(1,2),(2,2),
    /// (1,3),(2,3). Yields nothing for an undimensioned array.</summary>
    public IEnumerable<Vb6Value> EnumerateElements()
    {
        int n = bounds.Count;
        if (n == 0)
            yield break;

        var idx = new int[n];
        for (int i = 0; i < n; i++)
        {
            if (bounds[i].ubound < bounds[i].lbound)
                yield break;   // an empty dimension (UBound < LBound, e.g. Split("")) => For Each iterates zero times
            idx[i] = bounds[i].lbound;
        }

        while (true)
        {
            yield return GetValue(idx);
            int d = 0;
            while (d < n)
            {
                idx[d]++;
                if (idx[d] <= bounds[d].ubound)
                    break;
                idx[d] = bounds[d].lbound;   // carry into the next (slower) dimension
                d++;
            }
            if (d == n)
                yield break;                 // overflowed the slowest dimension
        }
    }
}