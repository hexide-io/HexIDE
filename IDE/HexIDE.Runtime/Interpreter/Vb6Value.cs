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
        if (Type != other.Type)
            return null;

        if (Type == ValueType.Integer)
            return ((int)Value!).CompareTo((int)other.Value!);
        else if (Type == ValueType.Single)
            return ((float)Value!).CompareTo((float)other.Value!);
        else if (Type == ValueType.Double)
            return ((double)Value!).CompareTo((double)other.Value!);
        else if (Type == ValueType.String)
            return String.Compare(((string)Value!), (string)other.Value!, StringComparison.Ordinal);
        else if (Type == ValueType.Boolean)
            return ((bool)Value!).CompareTo((bool)other.Value!);

        return null;
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

    public Vb6Value(int value) : this(ValueType.Integer, value) {}
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
                ValueTypePrimitive.Date => throw new NotImplementedException(),
                ValueTypePrimitive.Double => 0.0,
                ValueTypePrimitive.Single => 0.0f,
                ValueTypePrimitive.File => throw new NotImplementedException(),
                ValueTypePrimitive.Integer => 0,
                ValueTypePrimitive.String => "",
                ValueTypePrimitive.Boolean => false,
                ValueTypePrimitive.Control => throw new NotImplementedException(),
                ValueTypePrimitive.Nothing => null,
                ValueTypePrimitive.EmptyVariant => null,
                ValueTypePrimitive.CSharpProxyObject => null,
                ValueTypePrimitive.Null => null,
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
        Null
    }

    public static implicit operator Vb6Value(int value) =>
        new Vb6Value(ValueType.Integer, value);

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
    public static readonly Vb6Value Nothing = new Vb6Value(ValueType.Nothing);
    public static readonly Vb6Value Variant = new Vb6Value(ValueType.EmptyVariant);
    public bool IsNull => Type == ValueType.Null;
}

public class VBArray
{
    private List<(int lbound, int ubound)> bounds = new();
    private object[] array;

    public bool IsDefined => bounds.Count > 0;

    public VBArray()
    {
        array = [];
    }

    public VBArray(Vb6Value.ValueType innerType, IReadOnlyList<(int lowerBound, int upperBound)> bounds)
    {
        this.bounds.AddRange(bounds);
        array = CreateArray(innerType, 0);
    }

    private object[] CreateArray(Vb6Value.ValueType innerType, int dimension)
    {
        var arr = new object[bounds[dimension].ubound - bounds[dimension].lbound + 1];
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
            throw new VBCompileErrorException("Dimension doesn't match");
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
            throw new VBCompileErrorException("Dimension doesn't match");
        object arr = array;
        for (var index1 = 0; index1 < index.Count - 1; index1++)
        {
            var i = index[index1];
            arr = ((object[])arr)[i - bounds[index1].lbound];
        }

        ((object[])arr)[index[^1] - bounds[^1].lbound] = val;
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
}