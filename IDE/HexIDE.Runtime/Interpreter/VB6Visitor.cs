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
                throw new NotImplementedException("New as type not implemented");
            if (asTypeClause.fieldLength() != null)
                throw new NotImplementedException("fieldLength as type not implemented");
            if (asTypeClause.type().complexType() != null)
                // A UDT/Enum param or return type — one generic UDT ValueType (the concrete identity is on the
                // value). Enough for ByRef aliasability (both sides UDT) and to skip numeric coercion; a UDT-typed
                // Dim itself is built directly in VisitVariableStmt, not here.
                type = Vb6Value.ValueType.UserDefinedType;
            else
                type = BaseTypeMapper.Map(asTypeClause.type().baseType())
                    ?? throw new NotImplementedException("base type " + asTypeClause.type().baseType().GetChild(0) + " not implemented");
        }

        if (isArray)
            type = new Vb6Value.ValueType(type, true);

        return type;
    }
}