using System;
using System.Collections.Generic;
using System.Linq;

namespace HexIDE.Runtime.Interpreter;

public abstract class VB6Visitor<T> : VB6BaseVisitor<T>
{
    public TT AsType<TT>(Vb6Value value)
    {
        if (!TryUnpack(value, out TT val))
            throw new VBCompileErrorException("Type mismatch");
        return val;
    }

    public List<TT> AsType<TT>(IReadOnlyList<Vb6Value> indexes)
    {
        return indexes.Select(AsType<TT>).ToList();
    }

    /// <summary>
    /// A subscript or an array bound, coerced to a Long as <c>CLng</c> coerces it. Measured on vb6.exe, for an
    /// element, an element's assignment and a ReDim bound alike: banker's rounding (<c>a(2.5)</c> is <c>a(2)</c>,
    /// <c>a(3.5)</c> is <c>a(4)</c>), a numeric string read (<c>a(" 3 ")</c>), <c>True</c> -1, <c>Empty</c> 0,
    /// <c>Null</c> 94, <c>"x"</c> 13, and beyond a Long 6, all before the bounds are checked. One exception to CLng:
    /// an omitted <c>Optional</c> Variant is 13, where CLng reads it as 448 (measured, and so is <c>CVErr(5)</c>,
    /// which also raises 13).
    /// </summary>
    /// <remarks>
    /// It accepted only an Integer, a Byte or an integral string, and refused anything else as an untrappable
    /// "Type mismatch", so <c>a(i)</c> with <c>i</c> a Long, and <c>a(UBound(a))</c>, stopped the run.
    /// </remarks>
    public static int Subscript(Vb6Value value)
    {
        if (value.IsMissing || value.Type == Vb6Value.ValueType.Error)
            throw new VBRunTimeException(VBStandardError.TypeMismatch);
        return Convert.ToInt32(VB6BuiltIns.NarrowNum(value, Vb6Value.ValueType.Long).Value);
    }

    /// <summary>
    /// A control-array index, coerced as <c>CInt</c> coerces it, not as a subscript is. Measured on vb6.exe, native and
    /// in the IDE, for <c>Load</c>, <c>Unload</c> and an element's read alike: anything outside an Integer is 6, the
    /// rounding included (<c>32767.6</c> is 6, <c>32766.5</c> is 32766); a negative index, -32768 and <c>True</c>
    /// included, is 341; only then is a missing element 340.
    /// </summary>
    public static int ControlArrayIndex(Vb6Value value)
    {
        if (value.IsMissing || value.Type == Vb6Value.ValueType.Error)
            throw new VBRunTimeException(VBStandardError.TypeMismatch);
        var index = Convert.ToInt32(VB6BuiltIns.NarrowNum(value, Vb6Value.ValueType.Integer).Value);
        if (index < 0)
            throw new VBRunTimeException(VBStandardError.InvalidControlArrayIndex);
        return index;
    }

    /// <summary>Each of <paramref name="values"/> as a <see cref="Subscript"/>.</summary>
    public static List<int> Subscripts(IReadOnlyList<Vb6Value> values) => values.Select(Subscript).ToList();

    public bool TryUnpack<TT>(Vb6Value val, out TT tout)
    {
        tout = default!;
        if (typeof(TT) == typeof(int))
        {
            if (val.Type == Vb6Value.ValueType.Integer)
            {
                tout = (TT)(object)(int)val.Value!;
                return true;
            }
            if (val.Type == Vb6Value.ValueType.Byte)
            {
                tout = (TT)(object)(int)(byte)val.Value!;   // Byte widens to Integer (VB6: Byte arithmetic promotes to Integer)
                return true;
            }
            if (val.Type == Vb6Value.ValueType.String)
            {
                if (int.TryParse((string?)val.Value ?? "", out var asInt))
                {
                    tout = (TT)(object)asInt;
                    return true;
                }
            }
            return false;
        }
        if (typeof(TT) == typeof(bool))
        {
            if (val.Type == Vb6Value.ValueType.Boolean)
            {
                tout = (TT)(object)(bool)val.Value!;
                return true;
            }
            return false;
        }
        if (typeof(TT) == typeof(float))
        {
            if (val.Type == Vb6Value.ValueType.Integer)
            {
                tout = (TT)(object)(float)(int)val.Value!;
                return true;
            }
            if (val.Type == Vb6Value.ValueType.Byte)
            {
                tout = (TT)(object)(float)(byte)val.Value!;
                return true;
            }
            if (val.Type == Vb6Value.ValueType.Single)
            {
                tout = (TT)(object)(float)val.Value!;
                return true;
            }
            return false;
        }
        if (typeof(TT) == typeof(double))
        {
            // The catch-all numeric rung: reads every numeric subtype (Byte/Integer/Long/Single/Double/
            // Currency/Decimal/Date) as a double. Also fixes the old Single branch, which boxed a float and
            // then unboxed it as double (an InvalidCastException that was only dormant because Single never
            // reached this rung).
            if (Vb6Value.TryNumericToDouble(val, out var asDouble))
            {
                tout = (TT)(object)asDouble;
                return true;
            }
            return false;
        }

        return false;
    }

    public bool TryUnpack<TT>(Vb6Value left, Vb6Value right, out TT tleft, out TT tright)
    {
        tright = default!;
        return TryUnpack(left, out tleft) && TryUnpack(right, out tright);
    }

    public bool TryConvert(ref Vb6Value a, ref Vb6Value b)
    {
        if (a.Type == b.Type)
            return true;

        if (a.Type == Vb6Value.ValueType.Integer &&
            b.Type == Vb6Value.ValueType.String)
            return TryConvert(ref b, ref a);

        if (a.Type == Vb6Value.ValueType.String &&
            b.Type == Vb6Value.ValueType.Integer)
        {
            if (int.TryParse(a.Value?.ToString() ?? "", out var aInt))
            {
                a = new Vb6Value(aInt);
                return true;
            }
            else
                throw new Exception("Type mismatch");
        }
        throw new Exception("Type mismatch");
    }

    public Vb6Value.ValueType ExtractType(VB6Parser.AsTypeClauseContext? asTypeClause, bool isArray)
    {
        Vb6Value.ValueType type = Vb6Value.ValueType.EmptyVariant;
        if (asTypeClause != null)
        {
            if (asTypeClause.NEW() != null)
                throw new NotImplementedException("An array declared As New is not implemented (#1482)");
            if (asTypeClause.fieldLength() != null)
                throw new NotImplementedException("fieldLength as type not implemented");
            if (asTypeClause.type().complexType() != null)
                // A UDT/Enum param or return type — one generic UDT ValueType (the concrete identity is on the
                // value). Enough for ByRef aliasability (both sides UDT) and to skip numeric coercion; a UDT-typed
                // Dim itself is built directly in VisitVariableStmt, not here.
                type = Vb6Value.ValueType.UserDefinedType;
            else
                type = asTypeClause.type().baseType() is { } objectBase && (objectBase.OBJECT() != null || objectBase.COLLECTION() != null)
                    ? Vb6Value.ValueType.Object
                    : BaseTypeMapper.Map(asTypeClause.type().baseType())
                    ?? throw new NotImplementedException("base type " + asTypeClause.type().baseType().GetChild(0) + " not implemented");
        }

        if (isArray)
            type = new Vb6Value.ValueType(type, true);

        return type;
    }
}