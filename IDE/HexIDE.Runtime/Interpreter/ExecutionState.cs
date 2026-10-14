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

    /// <summary>
    /// Slots that may be read but never written — Enum members, today.
    ///
    /// Keyed by SLOT for the same reason the tables above are: it is the slot, not the name, that is the
    /// thing being protected, so an alias reaches the same answer. VB6 refuses `pTwo = 5` at compile time
    /// with "Assignment to constant not permitted"; a tree-walking interpreter can only refuse it as the
    /// statement executes, which is the translation interpreter-core:40-42 prescribes — same complaint,
    /// later. What matters is that it complains at all: a writable enum member means a program can
    /// overwrite `vbRed` and nothing anywhere says so.
    /// </summary>
    private readonly HashSet<int> readOnlyLocations = new();

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

    /// <summary>Mark a slot as never-writable. Used for Enum members, which VB6 treats as constants.</summary>
    public void MarkReadOnly(int location) => readOnlyLocations.Add(location);

    /// <summary>Whether a slot refuses assignment.</summary>
    public bool IsReadOnly(int location) => readOnlyLocations.Contains(location);

    // A form's control slots before the form has loaded, by slot, naming the form (#822). A read of one is refused
    // (#1009). Empty unless a form is between creating its instance and raising Load.
    private readonly Dictionary<int, string> pendingControls = new();

    /// <summary>Hold a form's control slot until the form loads: reading it is refused until then (#1009).</summary>
    public void MarkPendingControl(int location, string form) => pendingControls[location] = form;

    /// <summary>Release a form's control slots as the form begins to load.</summary>
    public void ClearPendingControls(string form)
    {
        foreach (var (location, owner) in pendingControls.ToList())
            if (owner == form)
                pendingControls.Remove(location);
    }

    /// <summary>The form whose not-yet-loaded control this slot is, or null.</summary>
    public string? PendingControlOf(int location) =>
        pendingControls.Count > 0 && pendingControls.TryGetValue(location, out var form) ? form : null;

    // A user Const's slot. Not read-only, since the Const statement fills it after the pre-pass makes it, and a class's
    // top-level declarations run again for each instance; marked so that an argument naming it is passed by value.
    private readonly HashSet<int> constantLocations = new();

    /// <summary>Mark a slot as a user <c>Const</c>'s (see <see cref="IsConstant"/>).</summary>
    public void MarkConstant(int location) => constantLocations.Add(location);

    /// <summary>Whether a slot holds a user <c>Const</c>, which VB6 passes to a ByRef parameter by value: a write through
    /// the parameter leaves the constant as it was (measured, #1001).</summary>
    public bool IsConstant(int location) => constantLocations.Contains(location);

    private readonly HashSet<int> declaredArrayLocations = new();

    /// <summary>
    /// Mark a slot as declared an array (<c>Dim b()</c>, <c>Dim b(3) As Long</c>), as against a Variant that holds one.
    /// ReDim tells them apart: an inverted range erases a declared array, and keeps or empties a Variant (measured).
    /// </summary>
    public void MarkDeclaredArray(int location) => declaredArrayLocations.Add(location);

    /// <summary>Whether a slot was declared an array, rather than a Variant that may hold one.</summary>
    public bool IsDeclaredArray(int location) => declaredArrayLocations.Contains(location);

    private readonly HashSet<int> fixedArrayLocations = new();

    /// <summary>
    /// Mark a slot as declared a fixed array, <c>Dim b(3)</c>, which ReDim refuses. Fixedness belongs to the
    /// declaration, not to the array the slot holds: after <c>d = f</c>, <c>d</c> is still dynamic and VB6 ReDims it
    /// (measured, #873), although HexIDE's Let shares <c>f</c>'s array with it (#970). Keyed by slot, so a ByRef
    /// parameter that shares the caller's slot reaches the caller's mark.
    /// </summary>
    public void MarkFixedArray(int location) => fixedArrayLocations.Add(location);

    /// <summary>Whether a slot was declared a fixed array (<see cref="MarkFixedArray"/>).</summary>
    public bool IsFixedArray(int location) => fixedArrayLocations.Contains(location);

    private readonly HashSet<int> variantLocations = new();

    /// <summary>
    /// Mark a slot as known to be a Variant: <c>Dim v</c>, <c>Dim v As Variant</c>, a Variant parameter, a variable VB6
    /// creates on first use. ReDim treats only these as Variants. A slot that is neither this nor a declared array is
    /// one whose declared type HexIDE does not carry, such as a <c>b() As Long</c> parameter, and is treated as the
    /// array it holds rather than guessed to be a Variant (measured: such a parameter keeps its type, #873).
    /// </summary>
    public void MarkVariant(int location) => variantLocations.Add(location);

    /// <summary>Whether a slot is known to be a Variant (<see cref="MarkVariant"/>).</summary>
    public bool IsVariant(int location) => variantLocations.Contains(location);

    public Vb6Value this[int location]
    {
        get => memory[location];
        set => memory[location] = value;
    }
}
