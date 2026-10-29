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
public sealed record DebugEvalResult(bool Ok, string Display, string TypeName, bool Truthy, DebugNode Node)
{
    /// <summary>
    /// What "Break When Value Changes" compares, where it differs from <see cref="Display"/>: the value in full, with
    /// its type. VB6's text has 7 or 15 significant digits, so a change below them would not show in it (#772).
    /// </summary>
    public string? ChangeKey { get; init; }

    internal bool UndefinedName { get; init; }
}

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

    /// <summary>Evaluate a stored context; legacy frames retain their current-frame evaluation.</summary>
    System.Threading.Tasks.Task<DebugEvalResult> EvaluateScopedWatchAsync(string expression, string context)
        => EvaluateTypedAsync(expression);

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

        // Locate Me + the base scope. Me is bound as a variable named "Me" only in an instance's procedures: a class's, and
        // a form's (#822).
        Vb6Value? me = null;
        ExecutionEnvironment baseEnv = module.ModuleEnv;
        if (ctx.TryInspectVariable(env, "Me", out var meVal) && meVal.Value is VbObject mo)
        {
            me = meVal;
            baseEnv = mo.InstanceEnv;
        }

        var nodes = new List<DebugNode>();
        // The module's own name is its self-reference in scope (a form seeds its own name into module scope); hide
        // it under the root — Me IS the form, VB6 never lists it as a field.
        string selfName = module.Name;
        bool baseHasVars = HasVisibleVars(baseEnv, ctx, selfName);

        // 1) The root: the Me / module / instance whose fields sit under it. Class method -> "Me" (class name);
        //    the primary (form) module -> synthetic "Me" (D8); another standard (.bas) module -> the module name.
        //    Only expandable when it actually has inspectable state (no empty expander — review finding).
        if (me is { Value: VbObject { FormWindow: { } instanceWindow } formObj })
        {
            // A form instance (#822): its window's own VB6 properties (Caption / Width / BackColor / …) first, then the
            // instance's variables and controls (P8 D7, the form property surface). Read even before the form has loaded,
            // since Locals only looks.
            nodes.Add(FormNode("Me", formObj.ClassName, formObj, instanceWindow, baseEnv, ctx, selfName, 0));
        }
        else if (me is { Value: VbObject rootObj })
        {
            Func<IReadOnlyList<DebugNode>>? expand = null;
            if (baseHasVars)
            {
                var ancestors = NewAncestorSet();
                ancestors.Add(rootObj);
                expand = () => SortedByName(EnvChildren(baseEnv, ctx, ancestors, 1, selfName));
            }
            nodes.Add(new DebugNode("Me", rootObj.ClassName, rootObj.ClassName, expand));
        }
        else
        {
            // VB6 lists a standard module's row even with nothing in it, typed by its name twice, and opens it to one
            // <No Variables> row (measured, #2700).
            nodes.Add(new DebugNode(
                module.Name, string.Empty, $"{module.Name}/{module.Name}",
                () => baseHasVars
                    ? EnvChildren(baseEnv, ctx, NewAncestorSet(), 1, selfName)
                    : [new DebugNode("<No Variables>", string.Empty, string.Empty)]));
        }

        // 2) The procedure's own params + locals. Procedure scopes no longer copy base bindings (#2120).
        // Keep the slot comparison for module-level execution, where env itself is the base scope.
        foreach (var (name, slot) in env.variableToLocation)
        {
            if (Hidden.Contains(name) || IsFormName(ctx, env, name))
                continue;
            if (baseEnv.variableToLocation.TryGetValue(name, out var baseSlot) && baseSlot == slot)
                continue;   // identical to the base scope's binding — shown under the Me/module root instead
            if (ctx.TryInspectVariable(env, name, out var value))
                nodes.Add(ValueNode(name, value, ctx, NewAncestorSet(), 1, Declared.Of(ctx, env, name)));
        }

        string context = procName is { Length: > 0 } ? $"{module.Name}.{procName}" : module.Name;
        return new DebugScope(context, nodes);
    }

    /// <summary>
    /// A running form, as <c>Me</c> or a Timer's <c>Parent</c>, as VB6's Locals window lists it (measured, #2329 and the
    /// oracle's #2698): its default member's row first; then its properties, its controls and its own variables as one
    /// list, sorted by name with case ignored, Public and Private alike and a control array as one node; then an
    /// <c>Item</c> row for each control, in the file's order with each array element where its block is. VB6 opens a form
    /// however it was reached, Parent under Me included, so only the depth bounds it.
    /// </summary>
    internal static DebugNode FormNode(string name, string value, VbObject formObj, Control window,
        ExecutionEnvironment baseEnv, ModuleExecutionContext ctx, string selfName, int depth,
        IReadOnlySet<VbObject>? above = null, string? type = null) =>
        new(name, value, type ?? formObj.ClassName, depth + 1 >= MaxDepth ? null : () =>
        {
            // VB6's first row under a form is its default member, which can't be read without an argument.
            var kids = new List<DebugNode>
            {
                new("[_Default]", $"<{VBStandardError.WrongNumberOfArgumentsOrInvalidPropertyAssignment.Description}>",
                    "Object"),
            };
            var ancestors = NewAncestorSet();
            ancestors.Add(formObj);
            var members = new List<DebugNode>(PropertyNodes(window));
            foreach (var (varName, varValue) in VisibleVariables(baseEnv, ctx, selfName))
            {
                // A form's control is a member declared by its class, Timer/Timer, though its slot records no class.
                var declared = Declared.Of(ctx, baseEnv, varName);
                if (declared is { Class: null, Scalar: null, IsVariant: false } && ControlClassOf(varValue) is { } controlClass)
                    declared = new Declared(controlClass, null, false);
                members.Add(ValueNode(varName, varValue, ctx, ancestors, depth + 1, declared));
            }
            kids.AddRange(SortedByName(members));
            kids.AddRange(ItemNodes(formObj.Controls?.All ?? [], ctx, ancestors, depth + 1));
            return kids;
        }) { Repeats = above?.Contains(formObj) == true };

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
        ExecutionEnvironment env, ModuleExecutionContext ctx, HashSet<VbObject> ancestors, int depth, string? selfName = null) =>
        [.. VisibleVariables(env, ctx, selfName).Select(v =>
            ValueNode(v.Name, v.Value, ctx, ancestors, depth, Declared.Of(ctx, env, v.Name)))];

    // An object's members as VB6's Locals lists them, sorted by name with case ignored: a form's and a class instance's
    // alike, Private included (measured, #2329 and #2700). A procedure's own locals keep their declaration order.
    private static IReadOnlyList<DebugNode> SortedByName(IEnumerable<DebugNode> members) =>
        [.. members.OrderBy(n => n.Name.EndsWith("()", StringComparison.Ordinal) ? n.Name[..^2] : n.Name,
            StringComparer.OrdinalIgnoreCase)];

    /// <summary>
    /// What a row was declared as, for its Type column (#2700): a class or <c>Object</c>, a scalar type, or a Variant. A
    /// row with none of them, a UDT's, or with no declaration at all, as a watch's expression is, keeps its value's own
    /// type.
    /// </summary>
    private sealed record Declared(string? Class, Vb6Value.ValueType? Scalar, bool IsVariant)
    {
        public static readonly Declared Variant = new(null, null, true);

        public static Declared? Of(ModuleExecutionContext ctx, ExecutionEnvironment env, string name) =>
            ctx.DeclarationOf(env, name) is { } d ? new Declared(d.Class, d.Scalar, d.IsVariant) : null;
    }

    /// <summary>
    /// The Type column as VB6's Locals window writes it (measured by hexide-int on #2700): a live object as its declared
    /// type, a slash and its class, <c>Object/Class1</c>, with <c>Variant/</c> in front when a Variant holds it;
    /// <c>Nothing</c> as the declared type alone, and <c>Variant/Object</c> in a Variant; a Variant's scalar as
    /// <c>Variant/</c> and its type. A typed scalar is its type, and a row with no declaration its value's.
    /// </summary>
    private static string TypeText(Vb6Value value, string actual, Declared? declared)
    {
        if (declared is null)
            return actual;
        if (value.Type == Vb6Value.ValueType.Object && value.Value is null)
            return declared.Class ?? (declared.IsVariant ? "Variant/Object" : actual);
        if (value.Type == Vb6Value.ValueType.Object || value.Type == Vb6Value.ValueType.Control
            || value.Value is ControlArrayGroup or VbCollection or ICSharpProxy)
            return declared.Class is { } declaredClass ? $"{declaredClass}/{actual}"
                : declared.IsVariant ? $"Variant/Object/{actual}" : actual;
        return declared.IsVariant ? $"Variant/{actual}" : actual;
    }

    // A control's class, or a control array's elements', which a form declares it by.
    private static string? ControlClassOf(Vb6Value value) => value.Value switch
    {
        Control control when value.Type == Vb6Value.ValueType.Control => VB6BuiltIns.DebugTypeName(value),
        ControlArrayGroup group => group.Elements.Values.FirstOrDefault() is { } first
            ? VB6BuiltIns.DebugTypeName(new Vb6Value(first)) : null,
        _ => null,
    };

    // An array's Type as VB6 writes it: its element type and bounds, Class1(1 to 2) (measured, #2329 and #2700).
    private static string ArrayType(VBArray arr, Declared declared, string elementType) =>
        arr.Rank == 0
            ? $"{elementType}()"
            : $"{elementType}({string.Join(", ", Enumerable.Range(1, arr.Rank).Select(d =>
                $"{arr.LowerBound(d).ToString(CultureInfo.InvariantCulture)} to {arr.UpperBound(d).ToString(CultureInfo.InvariantCulture)}"))})";

    // What an array's elements were declared as: its class, or Object, or its scalar type, or Variant.
    private static (Declared Element, string TypeName) ElementOf(VBArray arr, Declared declared)
    {
        if ((declared.Class ?? arr.DeclaredClass) is { } elementClass)
            return (new Declared(elementClass, null, false), elementClass);
        if (arr.ElementType is { } scalar && scalar != Vb6Value.ValueType.EmptyVariant)
            return scalar == Vb6Value.ValueType.Object
                ? (new Declared("Object", null, false), "Object")
                : (new Declared(null, scalar, false), VB6BuiltIns.DebugTypeName(new Vb6Value(scalar)));
        return (Declared.Variant, "Variant");
    }

    // Every variable of an environment that Locals shows, in declaration order.
    private static IEnumerable<(string Name, Vb6Value Value)> VisibleVariables(
        ExecutionEnvironment env, ModuleExecutionContext ctx, string? selfName)
    {
        foreach (var name in env.variableToLocation.Keys)
        {
            if (Hidden.Contains(name) || IsSelfRef(name, selfName) || IsFormName(ctx, env, name))
                continue;
            if (ctx.TryInspectVariable(env, name, out var value))
                yield return (name, value);
        }
    }



    // One row for a named value — a leaf for scalars/Nothing/controls, or an expandable node for arrays, UDTs and
    // live class instances. `name` is the bare variable name (array element names are derived from it).
    private static DebugNode ValueNode(
        string name, Vb6Value value, ModuleExecutionContext ctx, HashSet<VbObject> ancestors, int depth,
        Declared? declared = null)
    {
        string type = TypeText(value, VB6BuiltIns.DebugTypeName(value), declared);

        // Array — expand to elements by real subscript (capped). An undimensioned OR zero-length array (any
        // dimension with UBound < LBound, e.g. Split("") or ReDim a(1 To 0)) is a leaf — expanding it would index
        // an empty backing store and throw (review finding). A declared array is named and typed as VB6 writes it,
        // Grid of type Long(1 to 2) (measured, #2329 and #2700). A watch's array result, and a Variant holding an array,
        // which nothing has measured, keep their name and value's type.
        if (value.Type.IsArray && value.Value is VBArray arr)
        {
            var (element, elementType) = declared is null || declared.IsVariant ? (null, null) : ElementOf(arr, declared);
            var header = element is null ? name + "()" : name;
            if (element is not null)
                type = ArrayType(arr, declared!, elementType!);
            if (arr.Rank == 0 || IsEmptyArray(arr) || depth >= MaxDepth)
                return new DebugNode(header, string.Empty, type);
            return new DebugNode(header, string.Empty, type,
                () => ArrayChildren(name, arr, ctx, ancestors, depth + 1, element));
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
                return new DebugNode(name, "Nothing", type);
            // Leaf when: a cycle back to an ancestor, at the depth cap, or the instance has no inspectable fields
            // (a field-less class would otherwise show an expander that opens to nothing — review finding).
            if (depth >= MaxDepth || ancestors.Contains(obj) || !HasVisibleVars(obj.InstanceEnv, ctx))
                return new DebugNode(name, obj.ClassName, type);   // cycle / too deep / no fields: leaf
            return new DebugNode(name, string.Empty, type, () =>
            {
                // VbObject uses default (reference) equality, so a plain HashSet keys instances by identity.
                var next = new HashSet<VbObject>(ancestors) { obj };
                return SortedByName(EnvChildren(obj.InstanceEnv, ctx, next, depth + 1));
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
                kids.AddRange(control is VBTimer ? TimerNodes(control, ctx, ancestors, depth) : PropertyNodes(control));
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
            var groupType = TypeText(value, elemType, declared);
            if (depth >= MaxDepth || group.Count == 0)
                return new DebugNode(name, string.Empty, groupType);
            return new DebugNode(name, string.Empty, groupType,
                () => [.. ItemNodes([.. group.Elements.Values], ctx, ancestors, depth)]);
        }

        // A Collection (#1471) — expand to its items in order, "Item 1", "Item 2", …, each a node of its own, so an object
        // item opens to its fields. Capped as an array is; an empty one is a leaf.
        if (value.Value is VbCollection collection)
        {
            if (depth >= MaxDepth || collection.ItemCount == 0)
                return new DebugNode(name, VB6BuiltIns.DebugTypeName(value), type);   // like a field-less class: its class, no expander
            return new DebugNode(name, string.Empty, type, () =>
            {
                var values = collection.Snapshot();
                var kids = new List<DebugNode>();
                for (int i = 0; i < values.Count && i < MaxArrayElements; i++)
                    kids.Add(ValueNode($"Item {i + 1}", values[i], ctx, ancestors, depth + 1, Declared.Variant));
                if (values.Count > MaxArrayElements)
                    kids.Add(DebugNode.Truncation(values.Count - MaxArrayElements));
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
        string baseName, VBArray arr, ModuleExecutionContext ctx, HashSet<VbObject> ancestors, int depth,
        Declared? element = null)
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
            list.Add(ValueNode(elemName, value, ctx, ancestors, depth, element));
            shown++;
        }
        return list;
    }

    // An enumerable object's members as VB6's Locals lists them after its own rows: Item 1, Item 2 and on, in its order and
    // counted from 1 whatever an element's Index. A control array's elements, and a form's controls under Me (measured on
    // #2289, the table on #2329; Me's measured with two Timers only).
    private static IEnumerable<DebugNode> ItemNodes(
        IReadOnlyList<Control> items, ModuleExecutionContext ctx, HashSet<VbObject> ancestors, int depth) =>
        items.Select((item, i) => ValueNode($"Item {i + 1}", new Vb6Value(item), ctx, ancestors, depth + 1, Declared.Variant));

    // The seven rows VB6's Locals window lists under a Timer, in its order, each with the member's declared type (#2329).
    // Parent opens to the form, as VB6's does.
    private static IEnumerable<DebugNode> TimerNodes(
        Control timer, ModuleExecutionContext ctx, HashSet<VbObject> ancestors, int depth) =>
        TimerMembers.LocalsRows(timer).Select(row =>
            row.Error is { } error ? new DebugNode(row.Name, $"<{error}>", row.DeclaredType)
            : row.Value.Value is VbObject { FormWindow: { } window } form
                ? FormNode(row.Name, string.Empty, form, window, form.InstanceEnv, ctx, form.ClassName, depth + 1, ancestors,
                    $"{row.DeclaredType}/{form.ClassName}")
            : new DebugNode(row.Name, FormatValue(row.Value), row.DeclaredType));

    // A live control's or form's properties, name-sorted. One whose read raises shows the raised error's own description
    // in angle brackets, with no type, and the rest are still listed (#2289). The angle brackets are VB6's own, for a member
    // its Locals lists whose read raises (measured on #2289). Which members are listed, and an error row's type, are #2329.
    private static IEnumerable<DebugNode> PropertyNodes(Control control) =>
        AvaloniaInteroperability.ReadProperties(control).Select(p => p.Error is { } error
            ? new DebugNode(p.Name, $"<{error}>", string.Empty)
            : new DebugNode(p.Name, FormatValue(p.Value), VB6BuiltIns.DebugTypeName(p.Value)));

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
            case VbDate dt: return "#" + dt.Instant.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + "#";
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
    private static bool HasVisibleVars(ExecutionEnvironment env, ModuleExecutionContext ctx, string? selfName = null)
        => env.variableToLocation.Keys.Any(n => !Hidden.Contains(n) && !IsSelfRef(n, selfName) && !IsFormName(ctx, env, n));

    // The name a form is reached by, seeded into every module's env as Debug and Err are (#822). VB6 doesn't list it
    // as a module's variable, and it is not one.
    private static bool IsFormName(ModuleExecutionContext ctx, ExecutionEnvironment env, string name)
        => env.TryGetVariableLocation(name, out var location)
           && ctx.State.TryRead(location, out var value)
           && value.Value is VbObject { ClassDef.Kind: InterpreterModuleKind.Form } form
           && string.Equals(form.ClassName, name, StringComparison.OrdinalIgnoreCase);

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
