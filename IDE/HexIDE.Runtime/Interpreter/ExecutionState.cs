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

    private int nextFreeLocation = 0;

    public int Alloc(Vb6Value value) => Alloc(value, null);

    public int Alloc(Vb6Value value, Vb6Value.ValueType? declaredType)
    {
        var loc = nextFreeLocation++;
        memory[loc] = value;
        if (declaredType != null)
            declaredTypes[loc] = declaredType;
        return loc;
    }

    /// <summary>The declared type of a slot, or null when the slot is a Variant.</summary>
    public Vb6Value.ValueType? DeclaredTypeOf(int location) =>
        declaredTypes.TryGetValue(location, out var t) ? t : null;

    public Vb6Value this[int location]
    {
        get => memory[location];
        set => memory[location] = value;
    }
}
