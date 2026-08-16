using System;
using System.Collections.Generic;

namespace HexIDE.Runtime.Interpreter;

/// <summary>One field of a user-defined <c>Type</c>. A scalar field carries a <see cref="Vb6Value.ValueType"/>;
/// a nested-UDT field carries the referenced type's name (resolved against the program's type table at
/// instantiation). Array fields and fixed-length strings are deferred (spec walls).</summary>
public sealed record UdtField(string Name, Vb6Value.ValueType? ScalarType, string? UdtTypeName);

/// <summary>A user-defined <c>Type … End Type</c> definition. Registered program-wide (Public by default) so a
/// <c>Dim e As Employee</c> anywhere in the project resolves it.</summary>
public sealed class UdtTypeDef
{
    public string Name { get; }
    public IReadOnlyList<UdtField> Fields { get; }

    public UdtTypeDef(string name, IReadOnlyList<UdtField> fields)
    {
        Name = name;
        Fields = fields;
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

    public VbUdt(string typeName, Dictionary<string, Vb6Value> fields)
    {
        TypeName = typeName;
        this.fields = fields;
    }

    public bool TryGet(string field, out Vb6Value value) => fields.TryGetValue(field, out value);

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
        return new VbUdt(TypeName, copy);
    }
}
