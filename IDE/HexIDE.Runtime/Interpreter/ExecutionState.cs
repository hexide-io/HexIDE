using System.Collections.Generic;

namespace HexIDE.Runtime.Interpreter;

public class ExecutionState
{
    public Dictionary<int, Vb6Value> memory = new();

    /// <summary>
    /// The type a slot was DECLARED with, for the slots that have one. A slot with no entry here is a
    /// Variant, and that difference is what VB6 draws almost every arithmetic distinction from — `Dim l As
    /// Long : l = 3` holds a Long where a Variant holds an Integer, so `TypeName` differs, the result type
    /// of `i * l` differs, and division by zero even reports a different error number.
    ///
    /// Keyed by SLOT rather than by name, which is what makes a ByRef parameter work: it aliases the
    /// caller's slot, so it inherits the caller's declared type with nothing to copy. VB6 makes that safe by
    /// refusing at COMPILE time to bind a `ByRef x As Long` to a Variant argument (measured: "ByRef argument
    /// type mismatch"), so the two sides always agree and there is no coercion to do at the boundary.
    /// </summary>
    private readonly Dictionary<int, Vb6Value.ValueType> declaredTypes = new();

    /// <summary>
    /// The CLASS or INTERFACE a slot was declared with — <c>Dim x As IFoo</c> — for the slots that have one.
    ///
    /// Separate from <see cref="declaredTypes"/> because that records a coercible scalar, and an object slot
    /// has nothing to coerce: the name is the whole content. Recording it as <c>Object</c> (which is what the
    /// scalar table would hold) throws away the only thing that matters, and it matters twice — a Set is
    /// refused if the object doesn't satisfy the name (Err 13, measured), and a read through the name decides
    /// which members are reachable.
    /// </summary>
    private readonly Dictionary<int, string> declaredClasses = new();

    private int nextFreeLocation = 0;

    public int Alloc(Vb6Value value) => Alloc(value, null);

    public int Alloc(Vb6Value value, Vb6Value.ValueType? declaredType, string? declaredClass = null)
    {
        var loc = nextFreeLocation++;
        memory[loc] = value;
        if (declaredType != null)
            declaredTypes[loc] = declaredType;
        if (declaredClass != null)
            declaredClasses[loc] = declaredClass;
        return loc;
    }

    /// <summary>The declared type of a slot, or null when the slot is a Variant.</summary>
    public Vb6Value.ValueType? DeclaredTypeOf(int location) =>
        declaredTypes.TryGetValue(location, out var t) ? t : null;

    /// <summary>The declared class/interface name of a slot, or null when the slot isn't class-typed.</summary>
    public string? DeclaredClassOf(int location) =>
        declaredClasses.TryGetValue(location, out var c) ? c : null;

    public Vb6Value this[int location]
    {
        get => memory[location];
        set => memory[location] = value;
    }
}
