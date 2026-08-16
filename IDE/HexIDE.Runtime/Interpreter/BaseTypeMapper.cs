namespace HexIDE.Runtime.Interpreter;

/// <summary>
/// Single source of truth for mapping a VB6 <c>As &lt;baseType&gt;</c> clause to a runtime
/// <see cref="Vb6Value.ValueType"/>. Replaces three previously-duplicated copies (the PrePass Dim mapper, the
/// PrePass param/return mapper, and <c>VB6Visitor.ExtractType</c>) so a new keyword is wired in exactly once.
///
/// Returns null when the base type is absent or is not a supported scalar (Object/Collection, or Currency —
/// which has no grammar token until 2.5). Callers decide whether that is an error (a declared local) or an
/// untyped default (a parameter/return type).
/// </summary>
public static class BaseTypeMapper
{
    public static Vb6Value.ValueType? Map(VB6Parser.BaseTypeContext? baseType)
    {
        if (baseType == null) return null;
        if (baseType.STRING() != null) return Vb6Value.ValueType.String;
        if (baseType.INTEGER() != null) return Vb6Value.ValueType.Integer;
        if (baseType.LONG() != null) return Vb6Value.ValueType.Long;
        if (baseType.BYTE() != null) return Vb6Value.ValueType.Byte;
        if (baseType.SINGLE() != null) return Vb6Value.ValueType.Single;
        if (baseType.DOUBLE() != null) return Vb6Value.ValueType.Double;
        if (baseType.BOOLEAN() != null) return Vb6Value.ValueType.Boolean;
        if (baseType.CURRENCY() != null) return Vb6Value.ValueType.Currency;
        if (baseType.DATE() != null) return Vb6Value.ValueType.Date;
        if (baseType.VARIANT() != null) return Vb6Value.ValueType.EmptyVariant;
        return null;   // COLLECTION / OBJECT / CURRENCY (no token yet) / anything else -> caller decides
    }
}
