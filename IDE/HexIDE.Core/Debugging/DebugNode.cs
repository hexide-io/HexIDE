namespace HexIDE.Runtime.Debugging;

/// <summary>
/// A single row in the Locals tree — a variable/field/element with its formatted value + type, and (for
/// aggregates) a LAZY child provider. Children are built only when <see cref="Expand"/> is called, which both
/// bounds cost and — together with the ancestor-path cycle guard in <c>DebugInspector</c> — lets the tree
/// survive cyclic object graphs. Immutable + front-end-agnostic (the IDE tree, the MCP tool, and any DAP client
/// all consume the same nodes). This is execution-state introspection (in-bounds under the CST-not-AST limit).
/// </summary>
public sealed class DebugNode
{
    /// <summary>Display expression, e.g. <c>i</c>, <c>grid(0)</c>, <c>Me</c>, <c>Caption</c>. Empty for a
    /// truncation marker (<see cref="TruncatedRemaining"/> &gt; 0) — the front-end renders that localized.</summary>
    public string Name { get; }

    /// <summary>Formatted value for the Value column (strings quoted, <c>Nothing</c>/<c>Empty</c>/<c>Null</c>,
    /// <c>True</c>/<c>False</c>). Empty for an expandable array/UDT/object, whose value lives in its children — the
    /// deliberate exception is the Me/module root, which carries its class or module name here.</summary>
    public string Value { get; }

    /// <summary>VB6 type label, e.g. <c>Integer</c>, <c>String</c>, <c>Integer()</c>, a class or UDT name.</summary>
    public string TypeName { get; }

    /// <summary>True if this node can be expanded (has a lazy child provider).</summary>
    public bool HasChildren => _expand is not null;

    /// <summary>The node opens an object already open above it, as a Timer's Parent under Me does. VB6's Locals opens it
    /// all the same, so the window does too, but a walk that expands everything stops here: below it is the same tree
    /// again (#2329).</summary>
    public bool Repeats { get; init; }

    /// <summary>&gt; 0 on the synthetic "N more elements" marker appended when an array exceeds the element cap;
    /// the front-end shows a localized "… N more". 0 for an ordinary node.</summary>
    public int TruncatedRemaining { get; }

    private readonly Func<IReadOnlyList<DebugNode>>? _expand;

    public DebugNode(string name, string value, string typeName, Func<IReadOnlyList<DebugNode>>? expand = null)
    {
        Name = name;
        Value = value;
        TypeName = typeName;
        _expand = expand;
    }

    private DebugNode(int truncatedRemaining)
    {
        Name = string.Empty;
        Value = string.Empty;
        TypeName = string.Empty;
        TruncatedRemaining = truncatedRemaining;
    }

    /// <summary>Build this node's children (empty if it is a leaf). Safe to call repeatedly — the provider re-reads
    /// live state (the walk is frozen while paused, so reads are stable).</summary>
    public IReadOnlyList<DebugNode> Expand() => _expand?.Invoke() ?? Array.Empty<DebugNode>();

    public static DebugNode Truncation(int remaining) => new(remaining);
}

/// <summary>The inspectable state of one paused frame: a human-readable <see cref="Context"/> (Module.Procedure)
/// for the pane's header, and the top-level Locals rows (the Me/module root, then the procedure's params/locals).</summary>
public sealed record DebugScope(string Context, IReadOnlyList<DebugNode> Locals);

/// <summary>One row of the Call Stack — a running procedure activation (its module, procedure name, and the 1-based
/// line it is currently at). Front-end-agnostic (DAP <c>stackTrace</c>-shaped).</summary>
public sealed record CallStackFrame(string ProcName, string Module, int Line);

/// <summary>The typed result of evaluating a Watch / condition expression against a paused frame. Richer than the
/// Immediate window's flat string: <see cref="Node"/> is an expandable <see cref="DebugNode"/> (so an object/array/
/// UDT watch expands like Locals), <see cref="Truthy"/> is the VB6 Boolean coercion (for a "Break When True" watch),
/// and <see cref="Ok"/> distinguishes a real value from an evaluation error (so a condition tells False from an
/// error). On error, <see cref="Display"/> carries the VB6-style message and <see cref="TypeName"/> is empty.</summary>
public sealed record DebugEvalResult(bool Ok, string Display, string TypeName, bool Truthy, DebugNode Node)
{
    /// <summary>
    /// What "Break When Value Changes" compares, where it differs from <see cref="Display"/>: the value in full, with
    /// its type. VB6's text has 7 or 15 significant digits, so a change below them would not show in it (#772).
    /// </summary>
    public string? ChangeKey { get; init; }

    public bool UndefinedName { get; init; }
}
