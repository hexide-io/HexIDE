namespace HexIDE.Runtime.Interpreter;

/// <summary>
/// What a ByRef parameter stands for when its argument is not a variable: a UDT field (#1451) or an array element (#1455).
/// VB6 passes either by reference, so the parameter's slot reads and writes it for as long as the call runs
/// (<see cref="ExecutionState.AllocRef"/>).
/// </summary>
public interface IByRefTarget
{
    /// <summary>The scalar type the target is declared as, or null for a Variant, a Type or an object.</summary>
    Vb6Value.ValueType? DeclaredType { get; }

    /// <summary>Whether the target is a Variant.</summary>
    bool IsVariant { get; }

    Vb6Value Read();

    void Write(Vb6Value value);

    /// <summary>When the call that passed the target starts.</summary>
    void Lock() { }

    /// <summary>When that call returns.</summary>
    void Unlock() { }
}

/// <summary>
/// One element of an array, as a ByRef argument passes it (#1455). Measured, native and p-code: `PL a(1)`, with `PL(x As
/// Long)` storing 9, leaves 9, and a Variant array's element takes a String. The subscripts are evaluated once, at the call:
/// `PLI al(gi)` writes al(1) although the procedure sets gi to 2 first. A store into the array itself shows through the
/// parameter at once. The array is locked while the call runs: ReDim, ReDim Preserve, Erase, `d = e`, and `v = 5` for a
/// Variant holding the array, each raise 10, "This array is fixed or temporarily locked", and after the call ReDim works.
/// </summary>
public sealed record ArrayElementRef(VBArray Array, IReadOnlyList<int> Index, bool HeldAsVariant) : IByRefTarget
{
    // As the element reads (ExpressionExecutor.ElementAsRead, #987): a Variant's in a Variant array or in one reached through
    // a Variant, typed in a declared scalar array, and otherwise as it was stored.
    public Vb6Value.ValueType? DeclaredType =>
        !HeldAsVariant && Array.ElementType is { } type && type != Vb6Value.ValueType.EmptyVariant && VbNumeric.IsDeclarableScalar(type)
            ? type
            : null;

    public bool IsVariant => HeldAsVariant || Array.ElementType is not { } type || type == Vb6Value.ValueType.EmptyVariant;

    public Vb6Value Read() => Array.GetValue(Index);

    public void Write(Vb6Value value) => Array.SetValue(Index, value);

    public void Lock() => Array.LockCount++;

    public void Unlock() => Array.LockCount--;
}

/// <summary>
/// A slot, as one element of a ParamArray stands for it (#2462): the caller's variable, a slot standing for a field or an
/// element (<see cref="ExecutionState.AllocRef"/>), or the call's own copy of any other argument. A store converts as a store
/// into the variable does. Measured, native and p-code: `a(0) = 5.7` through an Integer's element leaves it 6, still an
/// Integer, `a(0) = "abc"` raises 13 and leaves it as it was, and a Variant's takes the String.
/// </summary>
public sealed record SlotRef(ExecutionState State, int Location) : IByRefTarget
{
    public Vb6Value.ValueType? DeclaredType => State.DeclaredTypeOf(Location);

    public bool IsVariant => DeclaredType is null;

    public Vb6Value Read() => State[Location];

    public void Write(Vb6Value value)
    {
        if (State.DeclaredTypeOf(Location) is { } declared)
            value = VbNumeric.CoerceOnStore(value, declared, null);
        ModuleExecutionContext.CheckObjectStore(value, State.DeclaredClassOf(Location));
        State[Location] = value;
    }
}
