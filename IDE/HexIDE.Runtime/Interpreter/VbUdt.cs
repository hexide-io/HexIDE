using System;
using System.Collections.Generic;

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
    // A nested Type, an Enum or anything else is in neither set and reads as it was stored.
    private readonly IReadOnlySet<string> typedFields;
    private readonly IReadOnlySet<string> variantFields;

    public VbUdt(string typeName, Dictionary<string, Vb6Value> fields)
        : this(typeName, fields, new HashSet<string>(), new HashSet<string>())
    {
    }

    public VbUdt(string typeName, Dictionary<string, Vb6Value> fields, IReadOnlySet<string> typedFields, IReadOnlySet<string> variantFields)
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
        value = typedFields.Contains(field) ? value.AsFixedType()
            : variantFields.Contains(field) ? value.AsVariant()
            : value;
        return true;
    }

    /// <summary>Enumerate the fields (name → value) in declaration order — the dictionary preserves the
    /// typedef's insertion order (fields are never removed). Read-only; used by the debugger's Locals inspector.</summary>
    public IEnumerable<KeyValuePair<string, Vb6Value>> EnumerateFields() => fields;

    public bool TrySet(string field, Vb6Value value)
    {
        if (!fields.ContainsKey(field)) return false;
        fields[field] = value;
        return true;
    }

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
