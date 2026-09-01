using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia.Controls;
using HexIDE.Runtime.AvaloniaInterop;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Debugging;

/// <summary>
/// A single row in the Locals tree — a variable/field/element with its formatted value + type, and (for
/// aggregates) a LAZY child provider. Children are built only when <see cref="Expand"/> is called, which both
/// bounds cost and — together with the ancestor-path cycle guard in <see cref="DebugInspector"/> — lets the tree
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

    internal static DebugNode Truncation(int remaining) => new(remaining);
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
public sealed record DebugEvalResult(bool Ok, string Display, string TypeName, bool Truthy, DebugNode Node);

/// <summary>A paused activation, able to hand the debugger its Locals on demand. Implemented by the interpreter's
/// per-activation <c>StatementExecutor</c> and captured by the controller at a break (read lazily while paused).</summary>
public interface IDebugFrame
{
    /// <summary>Snapshot the current frame's Locals (top level only; children are lazy on each node).</summary>
    DebugScope GetLocals();

    /// <summary>Evaluate an Immediate-window expression against this frame (read-only: intrinsics + variables +
    /// operators; user-procedure calls and assignment are rejected). Returns the formatted result, or a VB6-style
    /// error message. A leading <c>?</c> / <c>Print</c> is stripped by the caller.</summary>
    System.Threading.Tasks.Task<string> EvaluateAsync(string expression);

    /// <summary>Typed evaluation of a Watch / condition expression against this frame — same read-only rules as
    /// <see cref="EvaluateAsync"/>, but returns a <see cref="DebugEvalResult"/> (display / type / VB6 truthiness /
    /// expandable node), or <c>Ok = false</c> carrying the error message.</summary>
    System.Threading.Tasks.Task<DebugEvalResult> EvaluateTypedAsync(string expression);

    /// <summary>The current call DEPTH (1 = outermost). The controller reads it at the gate to decide Step Over
    /// (break when depth &lt;= the depth at the step) and Step Out (break when depth &lt; it).</summary>
    int Depth { get; }

    /// <summary>The current Call Stack, deepest (current) frame first down to the outermost.</summary>
    IReadOnlyList<CallStackFrame> GetCallStack();

    /// <summary>Set Next Statement — repoint execution to <paramref name="line"/> (TOP-LEVEL-body granularity only).
    /// Returns false if refused: not paused at a top-level statement (e.g. inside a nested If/For/Do block), or the
    /// target isn't a top-level statement of this procedure. Takes effect on the next resume.</summary>
    bool SetNextStatement(int line);
}

/// <summary>
/// Builds the Locals tree for a paused frame from live interpreter state. Runtime execution machinery — it reads
/// the frozen scope table + values, it does no static analysis, so it is in-bounds under the CST-not-AST limit.
///
/// Frame shape (see <c>BasicInterpreter.RunProcedure</c>): a proc's <c>currentEnv</c> is a CLONE of its base scope
/// (the class instance's field env for an instance method, else the module env) with <c>Me</c>, parameters and
/// locals added. So the procedure's own params/locals = <c>currentEnv</c> keys MINUS the base env's keys; the
/// base env's keys are the module-level / instance state shown under the root.
///
/// Known VB6 divergences (see docs/debugger-vb6-divergences.md): forms are singleton Standard modules with no
/// backing <c>VbObject</c>, so a form frame's root is a SYNTHETIC <c>Me</c> over the form module's variables and
/// carries no property/control surface (D7/D8); and locals appear only once their <c>Dim</c> has executed
/// (lazy allocation), where VB6 shows every declared local from procedure entry (D9).
/// </summary>
public static class DebugInspector
{
    // A deeply nested-but-acyclic structure (chained UDTs) can't loop forever, but could still expand hugely; this
    // caps expansion depth as a backstop beyond the ancestor cycle guard.
    private const int MaxDepth = 32;
    // Arrays can be arbitrarily large; show at most this many elements, then a single "N more" marker.
    private const int MaxArrayElements = 500;

    // Program-global objects seeded into every module env (see BasicInterpreter.SeedProgramGlobal) — never shown
    // as user variables. "Me" is handled as the root, not a plain local.
    //
    // This list has to grow with SeedProgramGlobal. A class instance's env is CLONED from its class template,
    // so a newly seeded global appears inside every object too: adding App made a field-less instance report
    // HasChildren, i.e. an expander that opens to one thing the user never declared.
    private static readonly HashSet<string> Hidden =
        new(StringComparer.OrdinalIgnoreCase) { "Debug", "Err", "App", "Me" };

    public static DebugScope Build(ExecutionEnvironment env, ModuleInfo module, BasicInterpreter interp, string? procName)
    {
        var ctx = interp.ExecutionContext;

        // Locate Me + the base scope. Me is bound as a variable named "Me" only for instance (class) methods.
        Vb6Value? me = null;
        Control? formWindow = null;   // a form's Me is the live form window (a Control) — its own property surface (D7)
        ExecutionEnvironment baseEnv = module.ModuleEnv;
        if (ctx.TryGetVariable(env, "Me", out var meVal))
        {
            if (meVal.Value is VbObject mo) { me = meVal; baseEnv = mo.InstanceEnv; }
            else if (meVal.Value is Control fw) formWindow = fw;   // VBLoader binds Me = the form window
        }

        var nodes = new List<DebugNode>();
        // The module's own name is its self-reference in scope (a form seeds its own name into module scope); hide
        // it under the root — Me IS the form, VB6 never lists it as a field.
        string selfName = module.Name;
        bool baseHasVars = HasVisibleVars(baseEnv, selfName);

        // 1) The root: the Me / module / instance whose fields sit under it. Class method -> "Me" (class name);
        //    the primary (form) module -> synthetic "Me" (D8); another standard (.bas) module -> the module name.
        //    Only expandable when it actually has inspectable state (no empty expander — review finding).
        if (me is { Value: VbObject rootObj })
        {
            Func<IReadOnlyList<DebugNode>>? expand = null;
            if (baseHasVars)
            {
                var ancestors = NewAncestorSet();
                ancestors.Add(rootObj);
                expand = () => EnvChildren(baseEnv, ctx, ancestors, 1, selfName);
            }
            nodes.Add(new DebugNode("Me", rootObj.ClassName, rootObj.ClassName, expand));
        }
        else if (formWindow is { } fw)
        {
            // A form's Me is the live form window: its own VB6 properties (Caption / Width / BackColor / …) FIRST,
            // then the form module's variables + child controls (P8 D7 — completes the form property surface; the
            // synthetic-Me fallback below no longer applies to a form).
            nodes.Add(new DebugNode("Me", module.Name, module.Name, () =>
            {
                var kids = new List<DebugNode>();
                foreach (var (propName, propValue) in AvaloniaInteroperability.ReadProperties(fw))
                    kids.Add(new DebugNode(propName, FormatValue(propValue), VB6BuiltIns.DebugTypeName(propValue)));
                kids.AddRange(EnvChildren(baseEnv, ctx, NewAncestorSet(), 1, selfName));
                return kids;
            }));
        }
        else if (baseHasVars)
        {
            string label = ReferenceEquals(module, interp.PrimaryModule) ? "Me" : module.Name;
            nodes.Add(new DebugNode(
                label, module.Name, module.Name,
                () => EnvChildren(baseEnv, ctx, NewAncestorSet(), 1, selfName)));
        }

        // 2) The procedure's own params + locals — a currentEnv entry whose SLOT differs from the base scope's slot
        //    for that name, or that has no base counterpart. Comparing by SLOT (not name) is what keeps a local /
        //    ByVal param that SHADOWS a module var or instance field visible: AllocVariable overwrote the cloned
        //    base entry with a fresh slot, while the base env keeps the outer slot (shown under the root). A
        //    name-only subtraction dropped the shadowing local entirely (and mis-dropped across case, since the env
        //    dictionaries are case-sensitive).
        foreach (var (name, slot) in env.variableToLocation)
        {
            if (Hidden.Contains(name))
                continue;
            if (baseEnv.variableToLocation.TryGetValue(name, out var baseSlot) && baseSlot == slot)
                continue;   // identical to the base scope's binding — shown under the Me/module root instead
            if (ctx.TryGetVariable(env, name, out var value))
                nodes.Add(ValueNode(name, value, ctx, NewAncestorSet(), 1));
        }

        string context = procName is { Length: > 0 } ? $"{module.Name}.{procName}" : module.Name;
        return new DebugScope(context, nodes);
    }

    /// <summary>Build a single Locals-style node for an arbitrary evaluated expression <paramref name="value"/> (a
    /// Watch expression's result). Named <paramref name="name"/> (the watch text); expandable for object/array/UDT
    /// results, a leaf otherwise — the same rendering the Locals tree uses.</summary>
    public static DebugNode NodeFor(string name, Vb6Value value, ModuleExecutionContext ctx)
        => ValueNode(name, value, ctx, NewAncestorSet(), 1);

    /// <summary>VB6 Boolean coercion of a watch result — a "Break When Value Is True" watch breaks when this is true.
    /// Booleans pass through; any numeric coerces (non-zero → true); anything else is false (a non-coercible watch
    /// simply never trips, rather than erroring the gate).</summary>
    public static bool IsTruthy(Vb6Value value)
        => value.Value switch
        {
            bool b => b,
            _ => Vb6Value.TryNumericToDouble(value, out var d) && d != 0,
        };

    // Children of an environment (a module/instance scope) — every non-hidden variable as a row, in declaration
    // order. The ancestor set is shared with the parent so a field referring back up the graph is caught.
    // <paramref name="selfName"/> (the module's own name for the Me/module root) hides a form's self-reference
    // binding — HexIDE seeds the form's own name into its module scope, but VB6 never shows that under Me.
    private static IReadOnlyList<DebugNode> EnvChildren(
        ExecutionEnvironment env, ModuleExecutionContext ctx, HashSet<VbObject> ancestors, int depth, string? selfName = null)
    {
        var list = new List<DebugNode>();
        foreach (var name in env.variableToLocation.Keys)
        {
            if (Hidden.Contains(name) || IsSelfRef(name, selfName))
                continue;
            if (ctx.TryGetVariable(env, name, out var value))
                list.Add(ValueNode(name, value, ctx, ancestors, depth));
        }
        return list;
    }

    // One row for a named value — a leaf for scalars/Nothing/controls, or an expandable node for arrays, UDTs and
    // live class instances. `name` is the bare variable name (array element names are derived from it).
    private static DebugNode ValueNode(
        string name, Vb6Value value, ModuleExecutionContext ctx, HashSet<VbObject> ancestors, int depth)
    {
        string type = VB6BuiltIns.DebugTypeName(value);

        // Array — expand to elements by real subscript (capped). An undimensioned OR zero-length array (any
        // dimension with UBound < LBound, e.g. Split("") or ReDim a(1 To 0)) is a leaf — expanding it would index
        // an empty backing store and throw (review finding).
        if (value.Type.IsArray && value.Value is VBArray arr)
        {
            if (arr.Rank == 0 || IsEmptyArray(arr) || depth >= MaxDepth)
                return new DebugNode(name + "()", string.Empty, type);
            return new DebugNode(name + "()", string.Empty, type,
                () => ArrayChildren(name, arr, ctx, ancestors, depth + 1));
        }

        // UDT — expand to fields (a UDT is a value type; no cycle possible, but honour the depth backstop and don't
        // show an expander for a (defensively) field-less UDT).
        if (value.Value is VbUdt udt)
        {
            if (depth >= MaxDepth || !udt.EnumerateFields().Any())
                return new DebugNode(name, string.Empty, type);
            return new DebugNode(name, string.Empty, type,
                () => UdtChildren(udt, ctx, ancestors, depth + 1));
        }

        // Class instance — expand to its fields, unless it is Nothing, already on the ancestor path (cycle), or at
        // the depth cap.
        if (value.Type == Vb6Value.ValueType.Object)
        {
            if (value.Value is not VbObject obj)
                return new DebugNode(name, "Nothing", "Nothing");
            // Leaf when: a cycle back to an ancestor, at the depth cap, or the instance has no inspectable fields
            // (a field-less class would otherwise show an expander that opens to nothing — review finding).
            if (depth >= MaxDepth || ancestors.Contains(obj) || !HasVisibleVars(obj.InstanceEnv))
                return new DebugNode(name, obj.ClassName, obj.ClassName);   // cycle / too deep / no fields: leaf
            return new DebugNode(name, string.Empty, obj.ClassName, () =>
            {
                // VbObject uses default (reference) equality, so a plain HashSet keys instances by identity.
                var next = new HashSet<VbObject>(ancestors) { obj };
                return EnvChildren(obj.InstanceEnv, ctx, next, depth + 1);
            });
        }

        // Live control — expand to its readable VB6 properties (Name/Caption/Left/Top/Width/Height/…), name-sorted
        // (D7, P8). A control has no cyclic object graph, so no ancestor guard is needed; capped by depth. Children
        // are read lazily on Expand (every built-in control has properties, so the expander is never empty in
        // practice). ICSharpProxy wrappers and a form's synthetic Me root (D8) keep no property surface — they fall
        // through to the leaf below.
        if (value.Type == Vb6Value.ValueType.Control && value.Value is Control control && depth < MaxDepth)
            return new DebugNode(name, FormatValue(value), type, () =>
            {
                var kids = new List<DebugNode>();
                foreach (var (propName, propValue) in AvaloniaInteroperability.ReadProperties(control))
                    kids.Add(new DebugNode(propName, FormatValue(propValue), VB6BuiltIns.DebugTypeName(propValue)));
                return kids;
            });

        // A control array (Command1) — expand to its elements by index (Command1(0), Command1(1), …), each a live
        // control that further expands to its own properties (D7). The group is a CSharpProxyObject wrapping a
        // ControlArrayGroup; label the node with the element control type, not the internal proxy type name.
        if (value.Value is ControlArrayGroup group)
        {
            string elemType = "Object";
            foreach (var (_, el) in group.Elements)
            {
                elemType = VB6BuiltIns.DebugTypeName(new Vb6Value(el));
                break;
            }
            if (depth >= MaxDepth || group.Count == 0)
                return new DebugNode(name, string.Empty, elemType);
            return new DebugNode(name, string.Empty, elemType, () =>
            {
                var kids = new List<DebugNode>();
                foreach (var (index, element) in group.Elements)
                    kids.Add(ValueNode($"{name}({index})", new Vb6Value(element), ctx, ancestors, depth + 1));
                return kids;
            });
        }

        // Everything else is a leaf: scalars, Nothing, proxies (no property surface — D7).
        return new DebugNode(name, FormatValue(value), type);
    }

    private static IReadOnlyList<DebugNode> UdtChildren(
        VbUdt udt, ModuleExecutionContext ctx, HashSet<VbObject> ancestors, int depth)
    {
        var list = new List<DebugNode>();
        foreach (var (fieldName, fieldValue) in udt.EnumerateFields())
            list.Add(ValueNode(fieldName, fieldValue, ctx, ancestors, depth));
        return list;
    }

    private static IReadOnlyList<DebugNode> ArrayChildren(
        string baseName, VBArray arr, ModuleExecutionContext ctx, HashSet<VbObject> ancestors, int depth)
    {
        var list = new List<DebugNode>();
        int shown = 0;
        foreach (var (index, value) in arr.EnumerateIndexed())
        {
            if (shown >= MaxArrayElements)
            {
                int total = 1;
                for (int d = 1; d <= arr.Rank; d++)
                    total *= arr.Length(d);
                list.Add(DebugNode.Truncation(total - shown));
                break;
            }
            string elemName = $"{baseName}({string.Join(", ", index)})";
            list.Add(ValueNode(elemName, value, ctx, ancestors, depth));
            shown++;
        }
        return list;
    }

    // The Value-column text for a leaf value, following VB6 Locals conventions (approximation — see D9).
    private static string FormatValue(Vb6Value v)
    {
        if (v.Type == Vb6Value.ValueType.Null) return "Null";
        if (v.Type == Vb6Value.ValueType.EmptyVariant) return "Empty";
        switch (v.Value)
        {
            case null: return "Nothing";
            case string s: return "\"" + s + "\"";
            case bool b: return b ? "True" : "False";
            case DateTime dt: return "#" + dt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + "#";
            case Control: return VB6BuiltIns.DebugTypeName(v);
            case ICSharpProxy: return VB6BuiltIns.DebugTypeName(v);
            case IFormattable f: return f.ToString(null, CultureInfo.InvariantCulture);
            default: return v.Value.ToString() ?? string.Empty;
        }
    }

    // VbObject uses default (reference) equality — no override — so a plain HashSet already keys by identity.
    private static HashSet<VbObject> NewAncestorSet() => new();

    // True if an environment holds at least one variable that would actually be shown (not hidden, not the module
    // self-reference) — i.e. there is something to expand.
    private static bool HasVisibleVars(ExecutionEnvironment env, string? selfName = null)
        => env.variableToLocation.Keys.Any(n => !Hidden.Contains(n) && !IsSelfRef(n, selfName));

    private static bool IsSelfRef(string name, string? selfName)
        => selfName != null && string.Equals(name, selfName, StringComparison.OrdinalIgnoreCase);

    // True if a dimensioned array has zero elements (any dimension with UBound < LBound). Such an array must not be
    // expanded — its backing store is empty and indexing it throws.
    private static bool IsEmptyArray(VBArray arr)
    {
        for (int d = 1; d <= arr.Rank; d++)
            if (arr.Length(d) <= 0)
                return true;
        return false;
    }
}
