using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace HexIDE.Runtime.Interpreter;

public class ExecutionState
{
    public Dictionary<int, Vb6Value> memory = new();

    /// <summary>The slots of a standard module's procedures' <c>Static</c> locals, for the run (#1491).</summary>
    internal StaticLocals ModuleStatics { get; } = new();

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
        int loc;
        try
        {
            loc = nextFreeLocation;
            nextFreeLocation = checked(nextFreeLocation + 1);
        }
        catch (OverflowException)
        {
            throw new VBRunTimeException(VBStandardError.OutOfMemory);
        }
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
        DeclaredViewOf(location) is { } name && name != "Object" ? name : null;

    // Object selects late binding on a read, but imposes no class constraint on a store (#1435).
    public string? DeclaredViewOf(int location) =>
        declaredClasses.TryGetValue(location, out var c) ? c : null;

    /// <summary>Mark a slot as never-writable. Used for Enum members, which VB6 treats as constants.</summary>
    public void MarkReadOnly(int location) => readOnlyLocations.Add(location);

    /// <summary>Whether a slot refuses assignment.</summary>
    public bool IsReadOnly(int location) => readOnlyLocations.Contains(location);

    // A form's control slots while the form isn't loaded, by slot, naming the form instance (#822): before its Load, or
    // after its Unload. A read of one asks for the form to be loaded. Empty unless a form is in one of those states.
    private readonly Dictionary<int, VbObject> pendingControls = new();

    /// <summary>Hold a form's control slot until the form loads: a read asks for the load (#822).</summary>
    public void MarkPendingControl(int location, VbObject form) => pendingControls[location] = form;

    /// <summary>Release a form's control slots as the form begins to load.</summary>
    public void ClearPendingControls(VbObject form)
    {
        foreach (var (location, owner) in pendingControls.ToList())
            if (ReferenceEquals(owner, form))
                pendingControls.Remove(location);
    }

    /// <summary>The form instance whose not-loaded control this slot is, or null.</summary>
    public VbObject? PendingControlOf(int location) =>
        pendingControls.Count > 0 && pendingControls.TryGetValue(location, out var form) ? form : null;

    // The slots of forms' names, by slot, naming the form module (#822). A form's name is a predeclared variable: read
    // while it holds Nothing, VB6 makes the form's default instance there (measured).
    private readonly Dictionary<int, ModuleInfo> defaultForms = new();

    /// <summary>Mark a slot as a form's name, whose instance is made on its first read.</summary>
    public void MarkDefaultForm(int location, ModuleInfo form) => defaultForms[location] = form;

    /// <summary>The form module whose name this slot is, or null.</summary>
    public ModuleInfo? DefaultFormOf(int location) =>
        defaultForms.Count > 0 && defaultForms.TryGetValue(location, out var form) ? form : null;

    // The slots of variables declared `As New`, by slot (#435): read while they hold Nothing, other than through a
    // parameter, VB6 makes the object there, as it makes a form's default instance (measured).
    private readonly Dictionary<int, AutoNew> autoNews = new();

    /// <summary>Mark a slot as declared <c>As New</c> <paramref name="className"/> under <paramref name="name"/>.</summary>
    public void MarkAutoNew(int location, string className, string name) => autoNews[location] = new AutoNew(className, name);

    /// <summary>What an <c>As New</c> slot makes, and the name it was declared with, or null for any other slot.</summary>
    public AutoNew? AutoNewOf(int location) =>
        autoNews.Count > 0 && autoNews.TryGetValue(location, out var autoNew) ? autoNew : null;

    // A user Const's slot. Not read-only, since the Const statement fills it after the pre-pass makes it, and a class's
    // top-level declarations run again for each instance. Marked so that an argument naming it is passed by value, and so
    // that any other store into it is refused (#1003).
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

    // A ByRef parameter given a UDT field (#1451) or an array element (#1455): its slot reads and writes the target itself,
    // for as long as the call runs.
    private readonly Dictionary<int, IByRefTarget> refSlots = new();

    /// <summary>Whether any call is running with a ByRef parameter given a field or an element, so an array may be locked.</summary>
    public bool HasRefSlots => refSlots.Count > 0;

    /// <summary>
    /// A slot that stands for <paramref name="target"/>, as a ByRef parameter's slot stands for its caller's. It is declared
    /// as the target is, so a read through it is typed or a Variant's as a read of the target is (#987), and a store converts
    /// there. An element's array is locked until <see cref="ReleaseRef"/>.
    /// </summary>
    public int AllocRef(IByRefTarget target, ExecutionEnvironment env)
    {
        var loc = Alloc(target.Read(), target.DeclaredType);
        env.AllocatedSlots?.Add(loc);
        if (target.IsVariant)
            MarkVariant(loc);
        refSlots[loc] = target;
        target.Lock();
        return loc;
    }

    /// <summary>Ends <see cref="AllocRef"/>'s link, when the call that made it returns.</summary>
    public void ReleaseRef(int location)
    {
        if (refSlots.Remove(location, out var target))
            target.Unlock();
    }

    internal void Free(int location)
    {
        ReleaseRef(location);
        memory.Remove(location);
        declaredTypes.Remove(location);
        declaredClasses.Remove(location);
        variantLocations.Remove(location);
        declaredArrayLocations.Remove(location);
        fixedArrayLocations.Remove(location);
        constantLocations.Remove(location);
        readOnlyLocations.Remove(location);
        autoNews.Remove(location);
        pendingControls.Remove(location);
        defaultForms.Remove(location);
    }

    public bool TryRead(int location, out Vb6Value value)
    {
        if (!memory.TryGetValue(location, out value))
            return false;
        if (refSlots.Count > 0 && refSlots.TryGetValue(location, out var target))
            value = target.Read();
        return true;
    }

    public Vb6Value this[int location]
    {
        get => refSlots.Count > 0 && refSlots.TryGetValue(location, out var target) ? target.Read() : memory[location];
        set
        {
            if (refSlots.Count > 0 && refSlots.TryGetValue(location, out var target))
                target.Write(value);
            else
            {
                ref var stored = ref CollectionsMarshal.GetValueRefOrNullRef(memory, location);
                if (Unsafe.IsNullRef(ref stored))
                    throw new InvalidOperationException($"Execution slot {location} is out of scope.");
                stored = value;
            }
        }
    }
}
