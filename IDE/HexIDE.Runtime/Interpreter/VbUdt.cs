using System;
using System.Collections.Generic;
using Antlr4.Runtime;

namespace HexIDE.Runtime.Interpreter;

/// <summary>One field of a user-defined <c>Type</c>. A scalar field carries a <see cref="Vb6Value.ValueType"/>;
/// a nested-UDT field carries the referenced type's name (resolved against the program's type table at
/// instantiation). Array fields and fixed-length strings are deferred (spec walls).</summary>
public sealed record UdtField(string Name, Vb6Value.ValueType? ScalarType, string? UdtTypeName);

/// <summary>A user-defined <c>Type … End Type</c> definition, owned by the module that declares it.
///
/// <para>
/// A UDT's type identity is MODULE-scoped — measured. Two modules may each declare a <c>Public Type Point</c>
/// and they are two unrelated types; <c>Module2.Point</c> names one of them specifically, and a Private one is
/// invisible from anywhere else. (An Enum is different: its identity is PROJECT-scoped, so two modules may not
/// both export one and <c>Module2.MyEnum</c> is not a type name at all. See docs/vb6-fidelity-oracle.md.)
/// </para>
/// </summary>
public sealed class UdtTypeDef
{
    public string Name { get; }
    public IReadOnlyList<UdtField> Fields { get; }

    /// <summary>Declared <c>Private</c>, so invisible outside its declaring module — even when qualified.</summary>
    public bool IsPrivate { get; }

    public UdtTypeDef(string name, IReadOnlyList<UdtField> fields, bool isPrivate = false)
    {
        Name = name;
        Fields = fields;
        IsPrivate = isPrivate;
    }
}

/// <summary>
/// A UDT <em>instance</em> — a mutable, case-insensitive field bag. UDTs are VALUE types in VB6: the interpreter
/// deep-copies a <see cref="VbUdt"/> at every value-assignment boundary (Let, ByVal param, whole-UDT field
/// write) via <see cref="DeepClone"/>, so <c>b = a</c> then mutating <c>b.Field</c> never touches <c>a</c>.
/// Field <em>writes</em> (and nested <c>e.Address.City = x</c>) mutate the bag in place — safe because
/// copy-on-assign guarantees each variable owns its own bag.
/// </summary>
public sealed class VbUdt
{
    public string TypeName { get; }
    private readonly Dictionary<string, Vb6Value> fields;

    // How a read of each field reads (#987, measured): a field declared with a scalar type is typed (`u.n * 7000` with
    // `n As Integer` holding 5 is 6), and a Variant field a Variant's (`u.x * 7000` is 35000), whatever was stored in it.
    // A nested Type or anything else is in neither and reads as it was stored. A typed field's declared type is also what a
    // store into it converts into (#221).
    private readonly IReadOnlyDictionary<string, Vb6Value.ValueType> typedFields;
    private readonly IReadOnlySet<string> variantFields;

    public VbUdt(string typeName, Dictionary<string, Vb6Value> fields)
        : this(typeName, fields, new Dictionary<string, Vb6Value.ValueType>(), new HashSet<string>())
    {
    }

    public VbUdt(string typeName, Dictionary<string, Vb6Value> fields, IReadOnlyDictionary<string, Vb6Value.ValueType> typedFields,
        IReadOnlySet<string> variantFields)
    {
        this.typedFields = typedFields;
        this.variantFields = variantFields;
        TypeName = typeName;
        this.fields = fields;
    }

    public bool TryGet(string field, out Vb6Value value)
    {
        if (!fields.TryGetValue(field, out value))
            return false;
        value = typedFields.ContainsKey(field) ? value.AsFixedType()
            : variantFields.Contains(field) ? value.AsVariant()
            : value;
        return true;
    }

    /// <summary>Enumerate the fields (name → value) in declaration order — the dictionary preserves the
    /// typedef's insertion order (fields are never removed). Read-only; used by the debugger's Locals inspector.</summary>
    public IEnumerable<KeyValuePair<string, Vb6Value>> EnumerateFields() => fields;

    /// <summary>
    /// Store into a field, converted into its declared type as a store into a variable of that type is (measured, #221:
    /// `u.l = 5` with `l As Long` is a Long, `u.dt = "1/2/2003"` a Date, `u.s = 5` the String "5", and `u.i = 40000` with
    /// `i As Integer` is 6). A value that will not convert raises before the store, so the field keeps what it held.
    /// </summary>
    public bool TrySet(string field, Vb6Value value, ParserRuleContext? ctx = null)
    {
        if (!fields.ContainsKey(field)) return false;
        if (typedFields.TryGetValue(field, out var declared))
            value = VbNumeric.CoerceOnStore(value, declared, ctx);
        fields[field] = value;
        return true;
    }

    /// <summary>Whether the Type has a field of this name.</summary>
    public bool Has(string field) => fields.ContainsKey(field);

    /// <summary>The scalar type a field is declared as, or null for a Variant, a nested Type or an object field.</summary>
    public Vb6Value.ValueType? DeclaredTypeOf(string field) =>
        typedFields.TryGetValue(field, out var declared) ? declared : null;

    /// <summary>Whether a field is declared As Variant, or with no As clause.</summary>
    public bool IsVariantField(string field) => variantFields.Contains(field);

    /// <summary>A deep, independent copy — nested UDT fields clone recursively; scalar fields (immutable
    /// <see cref="Vb6Value"/> structs) copy by value. This is the whole of UDT value semantics.</summary>
    public VbUdt DeepClone()
    {
        var copy = new Dictionary<string, Vb6Value>(fields.Comparer);
        foreach (var (name, value) in fields)
            copy[name] = value.Value is VbUdt nested ? Vb6Value.NewUdt(nested.DeepClone()) : value;
        return new VbUdt(TypeName, copy, typedFields, variantFields);
    }
}

/// <summary>
/// One field of one Type instance, as a ByRef argument passes it (#1451): VB6 passes the field itself, so a store through the
/// parameter lands in the field at once and converts into the field's type, and the field reads back the new value while the
/// procedure is still running (measured: `PL u.l`, with `PL(x As Long)` storing `x = "9"`, leaves 9; a module's `g.l`
/// passed so reads 3 inside the procedure straight after `x = 3`; and a Variant parameter given a Long field stores 2 for
/// `x = 2.5`, and raises 13 for `x = "hello"`). The parameter reads as the field does: given `u.i As Integer` holding 5,
/// `x * 7000` is 6 in `PI(x As Integer)` and 35000 in a Variant or untyped parameter.
/// </summary>
public sealed record UdtFieldRef(VbUdt Bag, string Name)
{
    /// <summary>The scalar type the field is declared as, or null for a Variant, a nested Type or an object field.</summary>
    public Vb6Value.ValueType? DeclaredType => Bag.DeclaredTypeOf(Name);

    /// <summary>Whether the field is a Variant.</summary>
    public bool IsVariant => Bag.IsVariantField(Name);

    public Vb6Value Read() => Bag.TryGet(Name, out var value) ? value : Vb6Value.Variant;

    public void Write(Vb6Value value) => Bag.TrySet(Name, value);
}
