using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Antlr4.Runtime;
using Antlr4.Runtime.Atn;
using Antlr4.Runtime.Misc;
using HexIDE.IDE;
using HexIDE.Runtime.Interpreter.ConditionalCompilation;

namespace HexIDE.Runtime.Interpreter;

public interface IBasicStandardLibrary
{
    ITextClipboard Clipboard => throw new NotImplementedException("This host does not implement the clipboard (#1156)");

    /// <summary>Where SaveSetting and GetSetting keep a program's settings (#1562): see <see cref="VbSettingsStores"/>.</summary>
    IVbSettingsStore Settings => throw new NotImplementedException("This host does not keep a program's settings (#1562)");

    /// <summary>The screen the <c>Screen</c> object describes (#2431): see <see cref="IVbDisplay"/>.</summary>
    IVbDisplay Display => throw new NotImplementedException("This host has no screen for the Screen object to describe (#2431)");
    Task<MessageBoxResult> MsgBox(string text, string? caption, MessageBoxButtons buttons, MessageBoxIcon icon);
    Task<string?> InputBox(string prompt, string? title, string defaultText);

    /// <summary>
    /// The typed channel for <c>Debug.Print</c>: each item it prints, in order, as its value, which the test suites assert
    /// on. A bare <c>Debug.Print</c> gives an empty String. A host that shows the output takes it from
    /// <see cref="DebugWrite"/> instead.
    /// </summary>
    void DebugPrint(Vb6Value value);

    /// <summary>
    /// What <c>Debug.Print</c> writes, as the Immediate window shows it, formatted as Print formats it: a line's end comes
    /// as CR LF, and none comes after a statement that ends in a semicolon or a comma, so its line stays open (#1380).
    /// </summary>
    void DebugWrite(string text) { }

    /// <summary>
    /// The column <see cref="DebugWrite"/>'s text has reached: the characters since its last line break, which Print's
    /// zones and <c>Tab</c> count from. VB6's Immediate window keeps it from one run to the next, as it keeps a line a run
    /// left open (measured, #1380). A host that keeps no text has no column, and gives 0.
    /// </summary>
    int DebugColumn => 0;

    /// <summary>A CommonDialog's <c>ShowOpen</c> or <c>ShowSave</c> (#1084): the file chosen, or null for Cancel.</summary>
    Task<VbFileDialogAnswer?> ShowFileDialog(VbFileDialogRequest request) =>
        throw new NotImplementedException("A CommonDialog's file dialogs are not implemented in this host (#1084)");

    Task<bool> ShowPrintDialog() =>
        throw new NotImplementedException("This host does not implement the print dialog (#2299)");

    Task<int?> ShowColorDialog(int initial) =>
        throw new NotImplementedException("This host does not implement the color dialog (#2299)");

    /// <summary>A CommonDialog's <c>ShowFont</c> (#1084): the font chosen, or null for Cancel.</summary>
    Task<VbFontChoice?> ShowFontDialog(VbFontChoice initial) =>
        throw new NotImplementedException("A CommonDialog's font dialog is not implemented in this host (#1084)");
}

public partial class BasicInterpreter : IAntlrErrorListener<IToken>, IAntlrErrorListener<int>
{
    public ModuleExecutionContext ExecutionContext { get; }
    public VB6BuiltIns BuiltIns { get; }
    private readonly IBasicStandardLibrary stdLib;
    private readonly ExecutionEnvironment rootEnv;
    private readonly string code;
    private PrePass prepass;

    /// <summary>Classes whose <c>Implements</c> conformance has already been verified — the check is per class,
    /// not per instance.</summary>
    private readonly HashSet<string> verifiedImplementers = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The primary/startup module's prepass. Per-executing-module data (Option Base, procedures) lives
    /// on <see cref="ModuleInfo.PrePass"/>; resolve against the executing module via <c>CurrentModule</c> at the
    /// call site, not this property.</summary>
    public PrePass PrePass => prepass;

    /// <summary>The project-wide module registry this interpreter resolves names against <em>at runtime</em>.
    /// Phase 1 scopes it to one execution context (the test harness, or one running form + its modules);
    /// cross-form shared standard-module state (limitation E1 in the interpreter-advanced spec) is deferred to a
    /// real-path project-scope hoist.</summary>
    public ModuleRegistry Modules { get; } = new();

    private Debugging.IDebugController? debugController;

    /// <summary>The debug session driving step-through, or null for a non-debugged / headless run (tests, the
    /// Standalone runner). Set by the IDE right after construction (the shared per-session controller); read by the
    /// StatementExecutor's pause-gate. Null ⇒ zero debug overhead (the gate is skipped entirely).</summary>
    public Debugging.IDebugController? DebugController
    {
        get => debugController;
        set
        {
            debugController = value;
            if (value is not null)
                RunSession ??= (value, value.Session);
        }
    }

    // Origin survives a controller reset or detachment: static run events must still name the run that made us (#761).
    private (Debugging.IDebugController Controller, int Session)? RunSession { get; set; }

    /// <summary>Whether this interpreter belongs to the named run, even after its debugger has detached.</summary>
    public bool IsInDebugSession(Debugging.IDebugController controller, int session) => RunSession == (controller, session);

    /// <summary>
    /// The run's event pump, when the program runs in a window: what keeps its handlers one at a time (#766). Null for code
    /// run with no window, as a module test's is, where nothing can interleave.
    /// </summary>
    public EventPump? Events { get; set; }

    /// <summary>
    /// A built-in call. With a pump, a built-in that waits (MsgBox, InputBox) is a suspension the pump is told of, and
    /// <c>DoEvents</c> runs what is waiting, as VB6's does (#766). Without one, DoEvents does nothing, as before.
    /// </summary>
    internal async Task<Vb6Value?> CallBuiltIn(string name, List<Vb6Value> args, bool compareText)
    {
        if (name.Equals("CreateObject", StringComparison.OrdinalIgnoreCase) && args.Count == 1
            && args[0].Value is string progId && progId.Contains('.') && LibraryClassFor(progId) is { } cls)
            return NewLibraryObject(cls);
        if (Events is not { } events)
            return await BuiltIns.EvaluateBuiltInFunction(name, args, compareText);
        if (args.Count == 0 && string.Equals(name, "DoEvents", StringComparison.OrdinalIgnoreCase))
        {
            await events.DoEvents();
            return new Vb6Value(0);
        }
        return await events.WhileWaitingOnWindow(BuiltIns.EvaluateBuiltInFunction(name, args, compareText));
    }

    internal async Task<Vb6Value?> CallProxy(ICSharpProxy proxy, string name, List<Vb6Value> args)
    {
        if (proxy is not ICSharpAsyncProxy asyncProxy)
        {
            proxy.Call(name, args);
            return null;
        }
        var pending = asyncProxy.CallAsync(name, args);
        return Events is { } events ? await events.WhileSuspended(pending) : await pending;
    }

    // Resolved after project names, rather than seeded over user variables or functions (#1156).
    private VbClipboard? clipboard;
    internal VbClipboard Clipboard => clipboard ??= new VbClipboard(stdLib);

    /// <summary>
    /// Set by <c>End</c>, and never cleared: the program is over. Every walk of this interpreter stops at its next
    /// statement, a procedure started later (a Timer tick or a click already queued) runs none of its own, and no
    /// <c>Class_Terminate</c> runs (#775).
    /// </summary>
    /// <remarks>
    /// Checked here as well as through the debugger's abort, because a host with no debugger (a test, the Standalone
    /// runner) has nothing else to stop the walks. Measured on vb6.exe: after <c>End</c> nothing else runs, no form
    /// raises QueryUnload, Unload or Terminate, and no Class_Terminate runs, whether it was reached from an event
    /// procedure, from Form_Load or from Sub Main.
    /// </remarks>
    public bool Ending { get; private set; }

    private bool unimplementedFault;

    // A filter runs before this try's cleanup, even across async boundaries. Never handle the fault here.
    internal bool MarkUnimplementedFault(Exception fault)
    {
        if (fault is NotImplementedException)
            unimplementedFault = true;
        return false;
    }

    /// <summary>
    /// Raised when a program reaches <c>End</c>, so whoever owns the run ends it: closes its forms, and stops the walks
    /// of any other interpreter in the run. Static, as <see cref="VBWindowContext.Faulted"/> is, because a run's
    /// interpreters are built in more than one place; an owner listens only while its run is live.
    /// </summary>
    public static event Action<BasicInterpreter>? EndReached;

    /// <summary>
    /// <c>End</c>: mark the program over, tell the host, and unwind this walk. The unwind is
    /// <see cref="Debugging.StopExecutionSignal"/>, which every entry point already swallows as a clean stop.
    /// </summary>
    internal Debugging.StopExecutionSignal End()
    {
        Ending = true;
        // End closes every file, as the end of the program does (#1145).
        BuiltIns.Files.CloseAll();
        EndReached?.Invoke(this);
        return new Debugging.StopExecutionSignal();
    }

    /// <summary>While set (AND the debugger is Paused), <see cref="RunProcedure"/> and <see cref="NewObject"/> refuse
    /// to run USER code (any Sub/Function/Property-Get body, or <c>New</c> construction) — used by Immediate-window
    /// evaluation, where running user statements would hit the paused gate on this (UI) thread (deadlock) and mutate
    /// program state. Intrinsic/builtin functions are unaffected (they never reach those sinks). The evaluator sets
    /// it around the eval and resets it in a <c>finally</c>; the extra <c>State==Paused</c> condition means it can
    /// never affect the RESUMED program walk even if it leaks across an async intrinsic's UI-thread yield.</summary>
    public bool SuppressUserProcedureCalls { get; set; }

    /// <summary>
    /// While set, an unresolvable name is an ERROR rather than an implicitly-declared Empty.
    ///
    /// Implicit declaration is a property of the running PROGRAM, not of anything looking at it. A watch or
    /// an Immediate expression naming something out of scope must report that it cannot be evaluated — if it
    /// reports Empty instead, the debugger has invented a variable, and a Break-When-Changed watch then takes
    /// a baseline from a name the program never had and trips on the real one appearing. Sibling of
    /// <see cref="SuppressUserProcedureCalls"/>, and the same principle: observing must not alter.
    /// </summary>
    public bool SuppressImplicitDeclaration { get; set; }

    private ModuleInfo primaryModule = null!;

    /// <summary>The startup module — where top-level statements (or a future <c>Sub Main</c>) run.</summary>
    public ModuleInfo PrimaryModule => primaryModule;

    // The live call stack of running procedure activations (bottom = outermost, top = deepest/current), maintained
    // by RunProcedure. Its Count is the current call DEPTH (used by Step Over/Out); the debugger reads it via
    // IDebugFrame.GetCallStack for the Call Stack window. Not a strict LIFO under overlapping async re-entrancy —
    // frames are removed by identity — but it mirrors the physical stack, matching VB6's Call Stack semantics.
    private readonly List<StatementExecutor> activationStack = new();

    /// <summary>The live procedure-activation call stack (outermost first). Read by the debugger only.</summary>
    public IReadOnlyList<StatementExecutor> ActivationStack => activationStack;

    /// <summary>Program-wide user-defined <c>Type</c> definitions (aggregated from every module's PrePass).</summary>
    public Dictionary<string, UdtTypeDef> Types { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Program-wide <c>Enum</c> tables (enum name → member → Long value), for qualified <c>MyEnum.Member</c>.</summary>
    public Dictionary<string, Dictionary<string, long>> Enums { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Construct a zero-initialised instance of user-defined type <paramref name="typeName"/> — each
    /// scalar field to its type's default, each nested-UDT field recursively, each Enum-typed field to Long 0.</summary>
    /// <summary>
    /// The project's own name, which is a QUALIFIER level in VB6 — `Project1.Module1.MyEnum.Foo` and
    /// `Project1.MyPublicEnum` are both legal, and the project level is valid in type position even where
    /// the module level is not.
    ///
    /// <para>
    /// Defaulted to VB6's own default project name so the common case works without wiring; a host that
    /// knows better (the IDE, reading the .vbp) should set it. There is only one project in scope here, so
    /// naming it changes nothing about which declaration is found — the level exists to be ACCEPTED, since
    /// refusing it would refuse legal VB6 for no gain.
    /// </para>
    /// </summary>
    public string ProjectName { get; set; } = "Project1";

    /// <summary>
    /// The names of the project's forms, which a declaration may name as a type (<c>Private mOwner As Form2</c>) though
    /// only the running form is a module here. Set by the host that knows the project; empty otherwise.
    /// </summary>
    public IReadOnlySet<string> ProjectFormNames { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Whether the project references a type library beyond the in-box four, whose types the interpreter can't read:
    /// an unqualified type name nothing here declares may then be that library's. Set by the host; false otherwise.
    /// </summary>
    public bool ReferencesOtherLibraries { get; set; }

    public VbUdt NewUdt(string typeName) => NewUdt(typeName, 0);

    /// <summary>Instantiate a UDT resolved from a specific module's point of view — so `Module2.Point`
    /// gets Module2's Point rather than whichever one loaded last into the program-wide table.</summary>
    public VbUdt NewUdt(string typeName, string? qualifier, ModuleInfo? from)
    {
        if (!TryResolveType(typeName, from, qualifier, out var def))
            throw new VBCompileErrorException("User-defined type not defined: " + typeName);
        return NewUdt(def, 0);
    }

    internal Vb6Value NewUdtArray(UdtTypeDef def, IReadOnlyList<(int, int)>? bounds, bool dynamic = true)
        => NewUdtArray(def, bounds, dynamic, 0);

    private Vb6Value NewUdtArray(UdtTypeDef def, IReadOnlyList<(int, int)>? bounds, bool dynamic, int depth)
    {
        Vb6Value MakeElement() => Vb6Value.NewUdt(NewUdt(def, depth));
        var type = new Vb6Value.ValueType(Vb6Value.ValueType.UserDefinedType, true);
        var array = bounds is null ? VBArray.Unallocated(Vb6Value.ValueType.UserDefinedType, MakeElement)
            : new VBArray(Vb6Value.ValueType.UserDefinedType, bounds, MakeElement);
        array.IsDynamic = dynamic;
        return new Vb6Value(type).WithArray(array);
    }

    private VbUdt NewUdt(string typeName, int depth)
    {
        if (!Types.TryGetValue(typeName, out var def))
            throw new VBCompileErrorException("User-defined type not defined: " + typeName);
        return NewUdt(def, depth);
    }

    private VbUdt NewUdt(UdtTypeDef def, int depth)
    {
        var typeName = def.Name;
        if (depth > 64)
            throw new VBCompileErrorException("Recursive Type definition: " + typeName);
        var fields = new Dictionary<string, Vb6Value>(StringComparer.OrdinalIgnoreCase);
        var typedFields = new Dictionary<string, Vb6Value.ValueType>(StringComparer.OrdinalIgnoreCase);
        var variantFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var autoNewFields = new Dictionary<string, AutoNew>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in def.Fields)
        {
            if (f.IsArray)
            {
                if (f.UdtTypeName is { } nestedType && Types.TryGetValue(nestedType, out var nestedDef))
                    fields[f.Name] = NewUdtArray(nestedDef, f.Bounds, f.Bounds is null, depth + 1);
                else
                {
                    var elementType = f.ScalarType ?? (f.UdtTypeName is { } enumType && Enums.ContainsKey(enumType)
                        ? Vb6Value.ValueType.Long : throw new NotImplementedException($"Array field '{f.Name}' has an unsupported element type (#178)"));
                    var arrayType = new Vb6Value.ValueType(elementType, true);
                    fields[f.Name] = f.Bounds is null ? new Vb6Value(arrayType) : new Vb6Value(arrayType, f.Bounds);
                    ((VBArray)fields[f.Name].Value!).IsDynamic = f.Bounds is null;
                }
                continue;
            }
            if (f.AutoNewClass is { } made)
            {
                if (!Modules.TryGet(made, out var classModule) || classModule.Kind != InterpreterModuleKind.Class || classModule.HostedControl)
                    throw new NotImplementedException($"Field '{f.Name}' declared As New inside Type '{typeName}' is not implemented (#1488)");
                fields[f.Name] = Vb6Value.Nothing;
                autoNewFields[f.Name] = new AutoNew(made, f.Name);
                continue;
            }
            // An Enum field is a typed Long, as an Enum variable is (measured, #1005).
            if (f.ScalarType is { } declared && VbNumeric.IsDeclarableScalar(declared))
                typedFields[f.Name] = declared;
            else if (f.UdtTypeName is { } enumName && !Types.ContainsKey(enumName) && Enums.ContainsKey(enumName))
                typedFields[f.Name] = Vb6Value.ValueType.Long;
            else if (f.UdtTypeName is null && (f.ScalarType is null || f.ScalarType == Vb6Value.ValueType.EmptyVariant))
                variantFields.Add(f.Name);
            if (f.ScalarType is { } scalar)
                fields[f.Name] = new Vb6Value(scalar);
            else if (f.UdtTypeName is { } nested)
                fields[f.Name] = Types.ContainsKey(nested) ? Vb6Value.NewUdt(NewUdt(nested, depth + 1))
                    : Enums.ContainsKey(nested) ? new Vb6Value(0L)   // an Enum-typed field is a Long
                    : throw new VBCompileErrorException($"Type '{typeName}' field '{f.Name}' references undefined type '{nested}'");
            else
                fields[f.Name] = Vb6Value.Variant;
        }
        return new VbUdt(typeName, fields, typedFields, variantFields, autoNewFields);
    }

    /// <summary>
    /// The value a store keeps, copied as VB6 copies it: a UDT deeply, an array level by level
    /// (<see cref="VBArray.CopyForLet"/>), everything else as it is. Applied wherever VB6 copies: a Let into a variable, a
    /// field or an element, a ByVal argument and a For Each variable, and so a Function's return. A <c>VbObject</c> is
    /// never copied: objects are references, so <c>Set b = a</c> shares, and a copied array shares its objects with the
    /// original (measured, #970: <c>b(0) Is a(0)</c>). Each is held once more, so the copy AddRefs it, and every array then
    /// has exactly one holder, which is what lets <see cref="ReleaseRef"/> descend into one. A ByRef argument never comes
    /// here: it shares the caller's slot.
    /// </summary>
    public Vb6Value CopyForStore(Vb6Value v) => CopyForStore(v, 0);

    private Vb6Value CopyForStore(Vb6Value v, int depth) => v.Value switch
    {
        VbUdt udt => CopyUdt(udt),
        VBArray array => v.WithArray(array.CopyForLet(element => CopyElement(element, depth + 1))),
        _ => v,
    };

    private Vb6Value CopyUdt(VbUdt udt)
    {
        var copy = udt.DeepClone();
        HoldFields(copy);
        return Vb6Value.NewUdt(copy);

        void HoldFields(VbUdt bag)
        {
            foreach (var field in bag.EnumerateFields())
                if (field.Value.Value is VbUdt nested) HoldFields(nested);
                else if (field.Value.Value is VBArray array) HoldArray(array);
                else AddRef(field.Value);
        }

        void HoldArray(VBArray array)
        {
            foreach (var element in array.EnumerateElements())
                if (element.Value is VbUdt nested) HoldFields(nested);
                else if (element.Value is VBArray inner) HoldArray(inner);
                else AddRef(element);
        }
    }

    // Arrays nested in arrays are copied recursively. A store into an element copies too, so no array can come to hold
    // itself; the bound refuses where the alternative would be a stack overflow taking the host down.
    private const int MaxArrayNesting = 1000;

    private Vb6Value CopyElement(Vb6Value element, int depth)
    {
        if (depth > MaxArrayNesting)
            throw new NotImplementedException(
                $"An array nested more than {MaxArrayNesting} deep is not copied: HexIDE refuses rather than overflow its stack (#970).");
        AddRef(element);
        return CopyForStore(element, depth);
    }

    /// <summary>Instantiate a class (<c>New ClassName</c>): build this instance's OWN field env by cloning the
    /// class template env (so the shared <c>Debug</c>/<c>Err</c>/global slots carry over) then re-running the
    /// class's top-level <c>Dim</c> declarations against it — each <c>Dim</c> allocates a FRESH slot, giving true
    /// per-instance field storage. Finally, if the class declares <c>Private Sub Class_Initialize()</c>, fire it
    /// on the new instance (after the field Dims, so it can set fields) — VB6's construction hook.</summary>
    public async Task<VbObject> NewObject(string className)
    {
        // `New` in the Immediate window would run field-init + Class_Initialize (statements → the paused gate →
        // deadlock) and mutate state. Blocked here, before construction. Scoped to Paused (see RunProcedure).
        if (SuppressUserProcedureCalls && DebugController?.State == Debugging.DebugState.Paused)
            throw new Debugging.ImmediateEvalException("Creating objects is not available in the Immediate window.");
        if (!Modules.TryGet(className, out var classDef) || classDef.Kind != InterpreterModuleKind.Class || classDef.HostedControl)
            throw new VBCompileErrorException("User-defined type not defined: " + className);
        // A class that claims an interface must supply all of it. VB6 refuses to BUILD such a program; with no
        // compile step to refuse at, the check runs here — once per class, before the first instance exists, so
        // no half-built object escapes. Recorded only on success, so a failing class raises on every attempt.
        if (classDef.PrePass.Implemented.Count > 0 && !verifiedImplementers.Contains(classDef.Name))
        {
            VbInterface.VerifyConformance(classDef, Modules);
            verifiedImplementers.Add(classDef.Name);
        }
        var instanceEnv = classDef.ModuleEnv.Clone();
        foreach (var block in classDef.PrePass.topLevelBlocks)
            await new StatementExecutor(this, instanceEnv, classDef).Execute(block);
        var instance = new VbObject(classDef, instanceEnv);
        if (classDef.PrePass.Procedures.TryGetValue("Class_Initialize", out var init))
        {
            // The object under construction is held while Class_Initialize runs, as the New expression holds it in
            // VB6. Without the hold it stood at 0, so a call from Class_Initialize to one of the object's own
            // methods, which counts Me, took it to 1 and back to 0 on return and fired Class_Terminate before
            // construction had finished (measured: VB6 runs Initialize, then both siblings, then nothing until the
            // last reference goes). The hold is dropped without the zero check, so the object is handed back at
            // whatever Initialize left it: 0 for the caller's Set to count, or more if Initialize stored Me.
            instance.RefCount++;
            try
            {
                await RunProcedure(classDef, init, [], instanceEnv, Vb6Value.NewObject(instance));
            }
            finally
            {
                instance.RefCount--;
            }
        }
        return instance;
    }

    // ---- Class_Terminate reference counting (interpreter-advanced Phase 4.2) ----
    // Real slot-based refcounting: named-storage + transient holders are counted on the VbObject; Terminate fires
    // at zero. Runtime execution machinery (in-bounds), not a scan of ExecutionState (unsound under E2). A `New`
    // object is handed back at 0 and is AddRef'd by whatever first stores it (Set / ByVal param). While it is being
    // built, NewObject holds it, so a method Class_Initialize calls on it cannot take it to 0. Class_Initialize's
    // own Me is not counted either (a lifecycle hook — see RunProcedure).

    /// <summary>Increment the reference count if <paramref name="v"/> is a live class instance (no-op for
    /// Nothing / controls / proxies / value types). Callers do AddRef(new) BEFORE Release(old) at every store, so
    /// <c>Set x = x</c> and aliased assignment can't transiently hit zero and self-terminate.</summary>
    public void AddRef(Vb6Value v)
    {
        if (v.Value is LibraryObject host)
            host.References++;
        else if (v.Value is VbObject o && !o.Terminated)
            o.RefCount++;
        else if (v.Value is VbCollection c)
            c.RefCount++;
    }

    /// <summary>A new Collection (#1471), with no references yet: <c>New Collection</c> hands it back as <c>New</c> hands
    /// back a class instance, for whatever first stores it to count.</summary>
    public VbCollection NewCollection()
    {
        if (SuppressUserProcedureCalls && DebugController?.State == Debugging.DebugState.Paused)
            throw new Debugging.ImmediateEvalException("Creating objects is not available in the Immediate window.");
        return new VbCollection(this);
    }

    /// <summary>Decrement the reference count of a live class instance; fire <c>Class_Terminate</c> the instant it
    /// reaches zero. Async because the hook runs user code. An array releases each object it holds, nested arrays
    /// included: an array has one holder (<see cref="CopyForStore"/>), so releasing the holder releases its contents. That
    /// is what terminates a local object array's objects at <c>End Sub</c>, as VB6 does (measured, #970).</summary>
    public async Task ReleaseRef(Vb6Value v)
    {
        if (v.Value is LibraryObject host)
        {
            ReleaseLibraryObject(host);
            return;
        }
        if (v.Value is VbObject o && !o.Terminated)
        {
            o.RefCount--;
            if (o.RefCount <= 0)
                await FireTerminate(o);
        }
        else if (v.Value is VbUdt bag)
        {
            foreach (var field in bag.EnumerateFields())
                await ReleaseRef(field.Value);
        }
        // A ParamArray's elements are its caller's arguments, and the copies it made of the rest are released with the call
        // (#2462), so releasing the array itself, as `a = Array(7)` does, releases nothing.
        else if (v.Value is VBArray { IsParamArray: false } array)
        {
            foreach (var element in array.EnumerateElements())
                await ReleaseRef(element);
        }
        // A Collection whose last reference goes releases what it holds, first to last (measured, #1471).
        else if (v.Value is VbCollection c && --c.RefCount <= 0)
            await c.ReleaseItems();
    }

    /// <summary>Run the instance's <c>Private Sub Class_Terminate</c> (if any), one-shot. Sets <see
    /// cref="VbObject.Terminated"/> BEFORE dispatching so the hook's own <c>Me</c> AddRef/Release are no-ops and
    /// re-entry / double-terminate is impossible. Symmetric to the <c>Class_Initialize</c> dispatch in
    /// <see cref="NewObject"/>.</summary>
    public async Task FireTerminate(VbObject o)
    {
        // A debugger reset (Stop) unwinds the walk via StopExecutionSignal, which runs this refcount teardown in
        // RunProcedure's finally — but VB6 `End` fires NO Class_Terminate (oracle-verified), so suppress it while
        // the debugger is aborting, once the program itself has reached End, or while an unimplemented fault unwinds (#767).
        if (o.Terminated || Ending || unimplementedFault || DebugController?.IsAborting == true)
            return;
        o.Terminated = true;
        // A form's hook is its own Form_Terminate, raised once it has unloaded and is released (#973).
        var hook = o.ClassDef.Kind == InterpreterModuleKind.Form ? "Form_Terminate" : "Class_Terminate";
        if (o.ClassDef.PrePass.Procedures.TryGetValue(hook, out var term))
            await RunProcedure(o.ClassDef, term, [], o.InstanceEnv, Vb6Value.NewObject(o));
        // Auto-unadvise (Phase 5): a terminating listener drops its `WithEvents` connections from their sources
        // (VB6 releases the connection point on Terminate), so no source retains a sink into a dead instance —
        // the dispatch-time Terminated skip is a safety net, this is the real cleanup.
        foreach (var name in o.ClassDef.PrePass.WithEventsNames)
            if (ExecutionContext.TryGetVariable(o.InstanceEnv, name, out var srcVal) && srcVal.Value is VbObject src)
                src.RemoveSink(o, name);
        // Cascade: after the hook runs (VB6 tears members down after Class_Terminate), release this instance's own
        // field references, so a sole-owned sub-object (composition) terminates too. ReleaseRef no-ops the
        // inherited Debug/Err proxies and value-typed fields, so walking every field slot is safe. A cyclic field
        // never reaches this path (its count never hit zero); a shared field just decrements. (Inter-field
        // teardown order is unpinned — Dictionary order — no oracle constraint yet.)
        foreach (var slot in o.InstanceEnv.variableToLocation.Values)
            if (!predeclaredSlots.Contains(slot))   // shared program names are not this instance's fields (#2555)
                await ReleaseRef(ExecutionContext.State[slot]);
        // Then its procedures' Statics, in the order the module declares them (measured, #1491).
        if (o.Statics is { } statics)
            foreach (var slot in statics.InSourceOrder())
                await ReleaseRef(ExecutionContext.State[slot]);
    }

    // Class_Initialize / Class_Terminate are lifecycle HOOKS, not ordinary methods: their `Me` is not a counted
    // reference (counting it would terminate a half-built instance at Initialize's scope-exit, and Terminate's Me
    // is already neutralised by the Terminated flag). Ordinary methods DO count Me (so a running method defers a
    // last-ref drop until it returns — oracle-verified).
    private static bool IsLifecycleHook(string name)
        => name.Equals("Class_Initialize", StringComparison.OrdinalIgnoreCase)
           || name.Equals("Class_Terminate", StringComparison.OrdinalIgnoreCase);

    // ---- statement-temporary frames (Phase 4.2b: function-return / temporary transfer) ----
    // Each statement runs inside a temporary frame — a list holding the object RESULT of every user Function/Get
    // call made during that statement, plus any `New` object the statement creates. A returned object carries the
    // return-name's un-released hold (RunProcedure never releases the return-name); adopting it into the CALLER's
    // frame and releasing at statement-end means a DISCARDED result terminates right after its statement, while an
    // ASSIGNED one is kept alive by the store's AddRef (the store out-lives the flush by exactly the one net
    // reference). The frame STACK is per-activation (owned by each StatementExecutor — like currentEnv/withTargets
    // — NOT an interpreter field), so overlapping awaits from re-entrant event dispatch can't pop each other's
    // frames. Push/pop are strictly LIFO around each statement (finally-bracketed in the StatementExecutor).

    /// <summary>End a statement's temporary frame: release each adopted call-result / <c>New</c> temp — a
    /// discarded object terminates here. The StatementExecutor pops the frame first, then hands it here, so a
    /// Terminate triggered by the flush can't re-enter the frame being drained.</summary>
    public async Task FlushFrame(List<Vb6Value> frame)
    {
        foreach (var t in frame)
            await ReleaseRef(t);
    }

    // Adopt a call's object return into the CALLER's current statement frame — a transfer, no AddRef. Called by
    // RunProcedure with the caller's own frame stack (threaded from the calling executor), so it targets the
    // caller's activation, never a re-entrant one. No-op if the caller has no active frame (return then leaks —
    // the safe direction). An array return is adopted too: the return name holds its own copy (#970), so the objects in
    // it are released at the caller's statement end, after whatever stored the result has copied it.
    private static void AdoptReturn(Vb6Value? ret, Stack<List<Vb6Value>>? callerFrames)
    {
        if (ret is { Value: VbObject or VBArray or VbCollection or LibraryObject } r && callerFrames is { Count: > 0 })
            callerFrames.Peek().Add(r);
    }

    /// <summary>Adopt a freshly-created <c>New</c> object into the current statement frame with a held reference,
    /// so a transient that is never stored (e.g. <c>Debug.Print (New Foo).Name</c>, or a <c>New</c> passed to a
    /// built-in) still terminates at statement-end. The <c>AddRef</c> here nets against the frame flush; any store
    /// (<c>Set</c>) or ByVal-param bind AddRefs again, leaving exactly the destination's hold after the flush. If
    /// there is no active frame the object simply isn't tracked (leaks — the safe direction).</summary>
    public Vb6Value AdoptNewTemp(Vb6Value v, Stack<List<Vb6Value>> frames)
    {
        if (v.Value is VbObject or VbCollection or LibraryObject && frames.Count > 0)
        {
            AddRef(v);
            frames.Peek().Add(v);
        }
        return v;
    }

    public BasicInterpreter(IBasicStandardLibrary stdLib,
        ModuleExecutionContext executionContext,
        ExecutionEnvironment rootEnv,
        string code,
        string primaryName = "Module1",
        IReadOnlyList<(string Name, string Code)>? additionalModules = null,
        IReadOnlyList<(string Name, string Code)>? classModules = null,
        InterpreterModuleKind primaryKind = InterpreterModuleKind.Standard,
        IReadOnlyList<(string Name, string Code)>? formModules = null,
        CompilationConstants? compilationConstants = null)
    {
        Err = new VbErr(() => ProjectName);
        ExecutionContext = executionContext;
        this.compilationConstants = compilationConstants ?? CompilationConstants.Vb6;
        BuiltIns = new VB6BuiltIns(stdLib)
        {
            AppTitle = () => App.TitleOrNull,
            ErrObject = () => Err,
            WatchRunEnd = () =>
            {
                var session = DebugController?.Session;
                return () => Ending || DebugController is { } debug && (debug.IsAborting || debug.Session != session);
            },
        };
        this.stdLib = stdLib;
        this.rootEnv = rootEnv;
        this.code = code;
        // A relative path in a file statement is the project's folder's (#1145), read when the file is opened, since the
        // project's details arrive after the interpreter is built.
        BuiltIns.Files.ProjectFolder = () => App.Path;
        App = new VbApp(AppInfo.None, BuiltIns.Files);

        // Build the primary (startup) module over the caller-provided rootEnv, then any additional modules —
        // each with its own env, ALL sharing one ExecutionState so slots are program-global (this is why ByRef
        // aliasing across modules works, and why cross-form sharing needs a project-scope hoist — see E1).
        // A form's run has the form as its primary module, of kind Form: a template whose running form is an instance of
        // it (#822). Its env is the template's, which every instance is cloned from.
        primaryModule = BuildModule(primaryName, primaryKind, code, rootEnv);
        prepass = primaryModule.PrePass;
        Modules.Add(primaryModule);

        if (additionalModules != null)
            foreach (var (name, modCode) in additionalModules)
                Modules.Add(BuildModule(name, InterpreterModuleKind.Standard, modCode, new ExecutionEnvironment()));

        // Class modules are TEMPLATES, not singletons — registered so `New`/`Dim As`/`TypeOf` resolve the name,
        // but instantiated only on use (each instance gets its own field env), never run at program start.
        if (classModules != null)
            foreach (var (name, modCode) in classModules)
                Modules.Add(BuildModule(name, InterpreterModuleKind.Class, modCode, new ExecutionEnvironment()));

        // The project's other forms are templates too (#822): each running form is an instance of one, made where the
        // form's name is first used.
        if (formModules != null)
            foreach (var (name, modCode) in formModules)
                Modules.Add(BuildModule(name, InterpreterModuleKind.Form, modCode, new ExecutionEnvironment()));

        // Aggregate every module's UDT/Enum definitions into one program-wide table.
        //
        // This table is now a FALLBACK, not the authority. It is what a lookup with no module context can
        // still use — a nested UDT field resolved at instantiation, a Function's declared return type —
        // and last-writer-wins is tolerable there only because the module-aware path is tried first.
        // TryResolveType and TryResolveEnum are the authority, and they honour Private and report a
        // genuine clash instead of quietly keeping one of the two (#180).
        //
        // An Enum's identity is PROJECT-scoped, so two modules exporting the same Public Enum NAME is an
        // error in VB6 at the declaration — no use required. That one is raised here, at load, because
        // that is where VB6 raises it and there is nothing later to attach it to.
        foreach (var m in Modules.All)
        {
            foreach (var (tn, def) in m.PrePass.Types) Types[tn] = def;
            foreach (var (en, members) in m.PrePass.Enums)
            {
                if (Enums.ContainsKey(en) && !m.PrePass.PrivateEnums.Contains(en)
                    && Modules.All.Any(o => !ReferenceEquals(o, m)
                                            && o.PrePass.Enums.ContainsKey(en)
                                            && !o.PrePass.PrivateEnums.Contains(en)))
                    throw new VBCompileErrorException("Ambiguous name detected: " + en);
                Enums[en] = members;
            }
        }

        // Seed the program-global Debug and Err objects into EVERY module env, each at a SINGLE shared slot
        // (never a per-env alloc, which would mint N distinct objects). Sharing the slot also removes the old
        // Err field/slot divergence across a SetCode recompile.
        SeedProgramGlobal("Debug", () => new Vb6Value(new DebugProxy(stdLib)));
        SeedProgramGlobal("Err", () => new Vb6Value(Err));
        SeedProgramGlobal("App", () => new Vb6Value(App));

        // A predeclared class name is a shared As New slot (#2555), separate from every explicit New.
        foreach (var module in Modules.All)
            if (module.Kind == InterpreterModuleKind.Class && module.PrePass.PredeclaredId)
            {
                SeedProgramGlobal(module.Name, () => Vb6Value.Nothing);
                if (rootEnv.TryGetVariableLocation(module.Name, out var slot))
                {
                    ExecutionContext.State.MarkAutoNew(slot, module.Name, module.Name);
                    predeclaredSlots.Add(slot);
                }
            }

        // Each module sees the other standard modules' Public variables and Consts unqualified, behind its own names
        // (#880). Set after every module's pre-pass, which is what lists them.
        foreach (var m in Modules.All)
            m.ModuleEnv.ProjectNames = ProjectNames.For(m, Modules.All);
    }

    /// <summary>The program-global <c>App</c> object, describing the project this program came from.</summary>
    public VbApp App { get; private set; } = new(AppInfo.None);

    private VbScreen? screen;

    /// <summary>
    /// The program-global <c>Screen</c> object (#2431). Not a seeded variable, as <c>App</c> is: like <c>Clipboard</c>, the
    /// name is reached only where nothing the program declares claims it, so a user's <c>Function Screen</c> or
    /// <c>Dim Screen</c> still wins, and the Locals window does not list it.
    /// </summary>
    public VbScreen Screen => screen ??= new VbScreen(this, stdLib);

    private VbForms? formsCollection;

    /// <summary>The program-global <c>Forms</c> (#2496), reached as <see cref="Screen"/> is.</summary>
    public VbForms FormsCollection => formsCollection ??= new VbForms(this);

    /// <summary>The library object a name nothing else claims stands for, or null: <c>Screen</c> (#2431) and <c>Forms</c>
    /// (#2496).</summary>
    internal Vb6Value? UnclaimedLibraryObject(string name) =>
        name.Equals("Screen", StringComparison.OrdinalIgnoreCase) ? new Vb6Value(Screen)
        : name.Equals("Forms", StringComparison.OrdinalIgnoreCase) ? new Vb6Value(FormsCollection)
        : (Vb6Value?)null;

    /// <summary>
    /// Tell the running program which project it is. Called by the host before <c>Execute</c>; a program
    /// with no project behind it (the test harness, the bare interpreter) keeps <see cref="AppInfo.None"/>
    /// and reports empty strings rather than inventing an identity.
    /// </summary>
    public void SetAppInfo(AppInfo info)
    {
        if (info.ProjectName is { } name)
            ProjectName = name;
        App = new VbApp(info, BuiltIns.Files);
        SeedProgramGlobal("App", () => new Vb6Value(App));
        BuiltIns.CommonDialogReferenced = info.CommonDialogReferenced;
        BuiltIns.ControlLibraries = info.ControlLibraries ?? [];
        BuiltIns.CommandLine = info.CommandLine;
    }

    /// <summary>The program-global <c>Err</c> object. Shared with the <c>Err</c> slot in every module env (see
    /// <see cref="SeedProgramGlobal"/>), so runtime error traps and user <c>Err.Number</c> reads see one state.</summary>
    public readonly VbErr Err;

    // What a module's #If can name besides its own #Consts: the project's arguments and the built-ins (#1112).
    private readonly CompilationConstants compilationConstants;

    // Each module's conditional compilation is applied before it is parsed, so an inactive branch is never parsed, as in
    // vb6.exe, and a directive's error stops the program as it loads (#1112). The text is the parser's alone: the code
    // window and every save keep the module as written.
    private ModuleInfo BuildModule(string name, InterpreterModuleKind kind, string moduleCode, ExecutionEnvironment env)
    {
        var pp = new PrePass(env, ExecutionContext.State);
        pp.Visit(Parse(ConditionalCompiler.Preprocess(moduleCode, compilationConstants, name).Text, name));
        return new ModuleInfo(name, kind, pp, env);
    }

    // The module being parsed, named in a syntax error's message so the person fixes the right file (#759).
    private string? parsingModule;

    private Antlr4.Runtime.Tree.IParseTree Parse(string source, string moduleName)
    {
        var lexer = new VB6Lexer(new AntlrInputStream(new StringReader(source)));
        lexer.RemoveErrorListeners();
        lexer.AddErrorListener(this);
        parsingModule = moduleName;
        try
        {
            var parser = new VB6Parser(NextCounterList.Tokens(lexer));
            parser.RemoveErrorListeners();
            parser.AddErrorListener(this);
            return ParseWithFallback(parser, p => p.startRule(), moduleName: moduleName);
        }
        finally
        {
            parsingModule = null;
        }
    }

    private T ParseWithFallback<T>(VB6Parser parser, Func<VB6Parser, T> parse,
        bool bailOnError = false, bool requireEnd = false, string? moduleName = null)
    {
        parser.RemoveErrorListeners();
        parser.AddParseListener(new ParseDepthGuard(moduleName: moduleName));
        parser.Interpreter.PredictionMode = PredictionMode.SLL;
        parser.ErrorHandler = new BailErrorStrategy();
        try
        {
            var tree = parse(parser);
            // Standalone rules can accept a prefix under SLL; let LL decide the whole entry.
            if (requireEnd && parser.CurrentToken.Type != TokenConstants.EOF)
                throw new ParseCanceledException();
            return tree;
        }
        catch (ParseCanceledException)
        {
            // SLL can mispredict call-versus-array syntax. Retry with LL and the original error policy.
            parser.Reset();
            parser.RemoveParseListeners();
            parser.AddParseListener(new ParseDepthGuard(moduleName: moduleName));
            parser.Interpreter.PredictionMode = PredictionMode.LL;
            parser.ErrorHandler = bailOnError ? new BailErrorStrategy() : new DefaultErrorStrategy();
            if (!bailOnError)
                parser.AddErrorListener(this);
            return parse(parser);
        }
    }

    private string SyntaxErrorAt(int line, int column) =>
        $"in {(parsingModule is null ? "" : parsingModule + ", ")}line {line}:{column}";

    /// <summary>Parse a standalone VB6 expression (an Immediate-window entry) into a value-statement tree. Uses the
    /// interpreter's throwing error listener, so a syntax error surfaces as <c>VBCompileErrorException</c>. Requires
    /// the WHOLE input to be one expression — trailing tokens (e.g. <c>count +</c>) are a syntax error, not ignored.</summary>
    public VB6Parser.ValueStmtContext ParseValueStmt(string expression)
    {
        var lexer = new VB6Lexer(new AntlrInputStream(new StringReader(expression)));
        var parser = new VB6Parser(new CommonTokenStream(lexer));
        lexer.RemoveErrorListeners();
        parser.RemoveErrorListeners();
        lexer.AddErrorListener(this);
        parser.AddErrorListener(this);
        var tree = ParseWithFallback(parser, p => p.valueStmt(), requireEnd: true);
        if (parser.CurrentToken.Type != Antlr4.Runtime.TokenConstants.EOF)
            throw new VBCompileErrorException($"Unexpected token '{parser.CurrentToken.Text}'");
        return tree;
    }

    /// <summary>Try to parse a single Immediate-window line as a STATEMENT (<c>blockStmt</c>) — for statement
    /// execution in break mode (P7c). Returns null when the input isn't a clean, whole statement, so the caller can
    /// fall back to evaluating it as an expression. NON-throwing (unlike <see cref="ParseValueStmt"/>): a bail error
    /// strategy plus a whole-input (EOF) check mean a bare expression, a partial line, or a non-statement simply
    /// yields null. NB a bare identifier parses as an <c>implicitCallStmt</c> (a no-arg call) — the caller only
    /// executes <c>letStmt</c>/<c>setStmt</c>, so `count` still falls back to being evaluated.</summary>
    public VB6Parser.BlockStmtContext? TryParseStatement(string input)
    {
        var lexer = new VB6Lexer(new AntlrInputStream(new StringReader(input)));
        var parser = new VB6Parser(new CommonTokenStream(lexer));
        lexer.RemoveErrorListeners();
        parser.RemoveErrorListeners();
        parser.ErrorHandler = new Antlr4.Runtime.BailErrorStrategy();   // any syntax error → bail; we return null
        try
        {
            var stmt = ParseWithFallback(parser, p => p.blockStmt(), bailOnError: true, requireEnd: true);
            // Require the WHOLE line to be one statement — trailing tokens (`count + `) mean it isn't a clean one.
            return parser.CurrentToken.Type == Antlr4.Runtime.TokenConstants.EOF ? stmt : null;
        }
        catch (Exception)   // ParseCanceledException (bail) / ParseDepthGuard / etc. — not a parseable statement
        {
            return null;
        }
    }

    // Allocate a shared slot for `name` exactly once (reusing an existing one if any module env already has it —
    // the recompile-reuse case), then point every module env's `name` at that one slot. Distinct from
    // AllocVariable, which always allocs and would produce a separate slot per env.
    private void SeedProgramGlobal(string name, Func<Vb6Value> factory)
    {
        int slot = -1;
        foreach (var m in Modules.All)
            if (m.ModuleEnv.TryGetVariableLocation(name, out var existing)) { slot = existing; break; }
        if (slot < 0)
            slot = ExecutionContext.State.Alloc(factory());
        else
            // Recompile over a reused rootEnv (VBWindowContext.SetCode): refresh the reused slot to THIS
            // interpreter's object so the `Err` field the traps write IS the slot user code reads (a fresh run
            // also correctly resets Err). Without this, the field and slot would diverge post-recompile.
            ExecutionContext.State[slot] = factory();
        foreach (var m in Modules.All)
            if (!m.ModuleEnv.TryGetVariableLocation(name, out _))
                m.ModuleEnv.DefineVariable(name, slot);
    }

    private readonly List<int> predeclaredSlots = new();

    internal async Task ReleasePredeclaredInstances()
    {
        foreach (var slot in predeclaredSlots)
        {
            var value = ExecutionContext.State[slot];
            ExecutionContext.State[slot] = Vb6Value.Nothing;
            await ReleaseRef(value);
        }
    }

    public async Task Execute()
    {
        declarationsRun = true;
        try
        {
            foreach (var m in Modules.All)
                if (!ReferenceEquals(m, primaryModule) && m.Kind == InterpreterModuleKind.Standard)
                    await RunModuleDeclarations(m);
            await RunModuleDeclarations(primaryModule);

            var statementExecutor = new StatementExecutor(this, rootEnv, primaryModule);
            foreach (var block in primaryModule.PrePass.topLevelBlocks)
                await statementExecutor.Execute(block, moduleDeclarations: false);
        }
        catch (Debugging.StopExecutionSignal)
        {
            // A debugger reset (Stop) unwound the walk cleanly (refcount teardown ran, suppressed) — swallow it.
        }
        finally
        {
            if (Forms is null)
                await ReleasePredeclaredInstances();
        }
    }

    /// <summary>
    /// Run the project's <c>Sub Main</c> — the startup object a code-only Standard EXE begins at, which is
    /// what <c>Startup="Sub Main"</c> in the .vbp selects.
    /// </summary>
    ///
    /// <remarks>
    /// <para>
    /// The startup lookup is NOT ordinary procedure resolution, and reusing <see cref="CallProcedure"/>
    /// would have been wrong. Every rule here is measured
    /// (<c>corpus/conformance/sub-main-startup.json</c>):
    /// </para>
    /// <list type="bullet">
    /// <item><b>Project-wide.</b> A <c>Sub Main</c> in any standard module is found, not only the startup
    ///   module's own — measured with Main in Module2 and nothing in Module1.</item>
    /// <item><b>Visibility is ignored.</b> A <c>Private Sub Main</c> is a valid startup, including one in
    ///   a module other than the primary. That is the rule that makes this different from ordinary
    ///   resolution, which sees a foreign module's <c>Public</c> only — so a <c>Private</c> Main
    ///   elsewhere would have been invisible and the project wrongly refused.</item>
    /// <item><b>Exactly one.</b> Two modules declaring <c>Main</c> is "Ambiguous name detected: Main",
    ///   even when one is <c>Private</c> and the other <c>Public</c> — which ordinary resolution would
    ///   not treat as a clash at all.</item>
    /// <item><b>A parameterless Sub, not a Function.</b> A <c>Function Main</c> does not qualify, and
    ///   neither does a <c>Sub Main</c> with any parameter list — <i>including</i> one whose only
    ///   parameter is <c>Optional</c>. Both give "Must have startup form or Sub Main()".</item>
    /// </list>
    /// <para>
    /// Both errors are VB6 compile errors. <see cref="FindStartupSubMain"/> raises them from the pre-pass tables, so
    /// the IDE asks it before the run begins and reports either as a start that never became a run (#759). Class
    /// modules are excluded: a class member is not a project startup, and searching every module indiscriminately
    /// would find one.
    /// </para>
    /// </remarks>
    public async Task RunStartupSubMain()
    {
        var startup = FindStartupSubMain();

        try
        {
            // Hoist EVERY standard module's module-level declarations first, the primary's included.
            // Execute() does this for the non-primary modules and then runs the primary's top-level
            // blocks, which doubles as its own hoist; entering through Main skips that, so without this
            // the startup module's own `Const`/`Dim` are never initialised. Measured symptom: a module
            // with `Private Const vbCancel = 7` printed Empty instead of 7 — a wrong value introduced by
            // the new entry point rather than found by it.
            //
            // These are declarations, not statements: VB6 refuses an executable statement outside a
            // procedure ("Invalid outside procedure", measured), so nothing here can have an effect
            // beyond seeding values.
            await RunModuleDeclarations();

            await RunProcedure(startup.Module, startup.Proc, Array.Empty<CallArg>(), isEntry: true);
        }
        catch (Debugging.StopExecutionSignal)
        {
            // A debugger reset (Stop) unwound the walk cleanly — swallowed exactly as Execute() does.
        }
        finally
        {
            if (Forms is null)
                await ReleasePredeclaredInstances();
            ReleaseLibraryGlobals();
        }
    }

    // Set once the modules' declarations have run, by Execute, RunStartupSubMain or RunModuleDeclarations.
    private bool declarationsRun;

    /// <summary>
    /// Run every standard module's module-level declarations, once, in list order: the primary's too when it is a
    /// standard module. Sub Main's run does this before Main, and a form's run before the form is created (#256): a
    /// module's Consts are then filled and its arrays sized, so a form reads a module's Public Const as VB6 does, not as
    /// Empty. A form module's declarations are its instance's, and run when the instance is made
    /// (<see cref="RunFormDeclarations"/>); a class's run per instance at New.
    /// </summary>
    public async Task RunModuleDeclarations()
    {
        if (declarationsRun)
            return;
        declarationsRun = true;
        RunningDeclarations = true;
        try
        {
            foreach (var m in Modules.All)
                if (m.Kind == InterpreterModuleKind.Standard)
                    await RunModuleDeclarations(m);
        }
        finally
        {
            RunningDeclarations = false;
        }
    }

    internal static bool IsModuleDeclaration(VB6Parser.BlockStmtContext statement) =>
        statement.constStmt() != null || statement.variableStmt() != null;

    // Resolve dependencies only while executing declarations, never by relating the pre-pass tables up front.
    internal async Task RunModuleDeclarations(ModuleInfo module)
    {
        if (module.DeclarationsRunning)
            throw new VBCompileErrorException("Circular dependencies between modules");
        if (module.DeclarationsRun)
            return;
        module.DeclarationsRunning = true;
        var wasRunning = RunningDeclarations;
        RunningDeclarations = true;
        try
        {
            var executor = new StatementExecutor(this, module.ModuleEnv, module);
            foreach (var block in module.PrePass.topLevelBlocks)
                await executor.Execute(block, moduleDeclarations: true);
            module.DeclarationsRun = true;
        }
        finally
        {
            module.DeclarationsRunning = false;
            RunningDeclarations = wasRunning;
        }
    }

    /// <summary>
    /// True while the modules' or a form's declarations run (#1010 review). The debugger's gate is skipped then: VB6 never
    /// stops on a declaration, so F8 from design mode breaks on the first statement of the event that follows, and a
    /// breakpoint can't be hit there either. A class's field initialisers, run at New, still pass the gate (D13).
    /// </summary>
    internal bool RunningDeclarations { get; private set; }

    /// <summary>
    /// Make the running instance of a form module (#822): its own env, cloned from the module's template env as a
    /// class's is at New, and the program-wide name the form is reached by, so <c>Form1.Caption</c> works from any
    /// module (measured). The program holds the instance through that name, so it is not terminated by the Me holds its
    /// own events take. Its declarations run separately (<see cref="RunFormDeclarations"/>), because the window's controls
    /// go into the instance env first.
    /// </summary>
    /// <remarks>
    /// In VB6 a form's name is a predeclared variable that creates its instance on first use, and a new one after
    /// <c>Set Form1 = Nothing</c> (measured, #822). Here the name is bound to the one instance the run starts with; that
    /// rule belongs to the slices that add Load, Show and a second form.
    /// </remarks>
    /// <summary>The run's forms, which make default instances and load them (#822). Null in a run with none.</summary>
    public IFormProgram? Forms { get; set; }

    /// <summary>
    /// Give every form module's name its program-wide slot, holding Nothing until its first read makes the form's default
    /// instance (#822). The program calls this once its forms can be made.
    /// </summary>
    public void SeedFormNames()
    {
        foreach (var form in Modules.All)
            if (form.Kind == InterpreterModuleKind.Form)
            {
                if (!rootEnv.TryGetVariableLocation(form.Name, out _))
                    SeedProgramGlobal(form.Name, () => Vb6Value.Nothing);
                if (primaryModule.ModuleEnv.TryGetVariableLocation(form.Name, out var slot)
                    || form.ModuleEnv.TryGetVariableLocation(form.Name, out slot))
                    ExecutionContext.State.MarkDefaultForm(slot, form);
            }
    }

    /// <summary>
    /// A variable read, the walk's hottest path, with a form's requests answered (#822): a form's name that holds no
    /// instance makes one, and a control of a form that isn't loaded loads it, then the read is made again.
    /// </summary>
    internal ValueTask<(bool Found, Vb6Value Value)> ReadVariable(ExecutionEnvironment env, string name) =>
        TryReadNow(env, name, out var found, out var value, out var need)
            ? new ValueTask<(bool, Vb6Value)>((found, value))
            : LoadingSlowly((Context: ExecutionContext, Env: env, Name: name),
                static s => (s.Context.TryGetVariable(s.Env, s.Name, out var v), v), need!);

    /// <summary>
    /// The variable read without awaiting anything: true with the read's answer, or false with the form request that
    /// <see cref="ReadVariable"/> must answer first (#822). The walk's hottest sites call this, and await
    /// <see cref="ReadVariable"/> only when it returns false, so an ordinary read costs what it did before forms could
    /// be made on use.
    /// </summary>
    internal bool TryReadNow(ExecutionEnvironment env, string name, out bool found, out Vb6Value value, out Exception? need)
    {
        try
        {
            if (SuppressImplicitDeclaration && env.TryGetVariableLocation(name, out var location)
                && !ExecutionContext.State.TryRead(location, out _))
                throw new Debugging.ImmediateEvalException($"Execution slot {location} is out of scope.");
            found = ExecutionContext.TryGetVariable(env, name, out value);
            need = null;
            return true;
        }
        catch (Exception request) when (IsReadRequest(request))
        {
            found = false;
            value = default;
            need = request;
            return false;
        }
    }

    /// <summary>The rest of a read <see cref="TryReadNow"/> could not finish: the form made or loaded, then read again.</summary>
    internal ValueTask<(bool Found, Vb6Value Value)> ReadVariableAfter(ExecutionEnvironment env, string name, Exception need) =>
        LoadingSlowly((Context: ExecutionContext, Env: env, Name: name),
            static s => (s.Context.TryGetVariable(s.Env, s.Name, out var v), v), need);

    /// <summary>
    /// A read that may meet a form not yet made or not loaded (#822): <paramref name="read"/> of
    /// <paramref name="state"/>, and when it asks for a form's default instance or its load, that is done and the read
    /// made again. A read has no side effect before the point it asks, so making it again is the same read. With no forms
    /// to reach, the request stays the refusal it is.
    /// </summary>
    /// <remarks>
    /// Synchronous until a form is asked for: the read is tried in place and its value returned as a completed
    /// <see cref="ValueTask{T}"/>, so an ordinary read costs no allocation and no state machine. Only the catch goes
    /// async. Pass a static lambda with its inputs in <paramref name="state"/>, which keeps the closure off that path too.
    /// </remarks>
    internal ValueTask<T> Loading<TState, T>(TState state, Func<TState, T> read)
    {
        try
        {
            return new ValueTask<T>(read(state));
        }
        catch (Exception need) when (IsReadRequest(need))
        {
            return LoadingSlowly(state, read, need);
        }
    }

    /// <summary>
    /// Whether a read asked for something the walk makes and then reads again: the object of a variable declared As New
    /// (#435), or a form's default instance or its load (#822), which only a run with forms can answer.
    /// </summary>
    private bool IsReadRequest(Exception request) =>
        request is AutoInstanceNeeded || (Forms is not null && request is FormInstanceNeeded or FormLoadNeeded);

    /// <summary>
    /// Make the object a variable declared As New is read without, as <c>Set v = New X</c> would, and store it there
    /// (#435). An Initialize that raises leaves the variable Nothing, so the next use tries again, as VB6's does
    /// (measured: three uses, three Initializes, three errors).
    /// </summary>
    private async Task MakeAutoInstance(AutoInstanceNeeded need)
    {
        var made = ExpressionExecutor.IsCollectionType(need.AutoNew.ClassName)
            ? new Vb6Value(NewCollection())
            : Vb6Value.NewObject(await NewObject(need.AutoNew.ClassName));
        AddRef(made);
        if (need.Field is { } field)
            field.Write(made);
        else if (ExecutionContext.State[need.Location] is { Value: null })
            ExecutionContext.State[need.Location] = made;
        else
            await ReleaseRef(made);   // Initialize filled the variable itself
    }

    private async ValueTask<T> LoadingSlowly<TState, T>(TState state, Func<TState, T> read, Exception need)
    {
        for (var attempt = 0; ; attempt++)
        {
            if (need is AutoInstanceNeeded autoInstance)
                await MakeAutoInstance(autoInstance);
            else if (need is FormInstanceNeeded instance)
                await Forms!.MakeDefaultInstance(instance.Form);
            else if (need is FormLoadNeeded load)
                await Forms!.Load(load.Form);
            try
            {
                return read(state);
            }
            catch (Exception again) when (attempt < 16 && IsReadRequest(again))
            {
                need = again;
            }
        }
    }

    /// <inheritdoc cref="Loading{TState, T}"/>
    internal ValueTask<bool> Loading<TState>(TState state, Action<TState> write) =>
        Loading((State: state, Write: write), static s =>
        {
            s.Write(s.State);
            return true;
        });

    /// <inheritdoc cref="Loading{TState, T}"/>
    internal ValueTask<T> Loading<T>(Func<T> read) => Loading(read, static r => r());

    /// <inheritdoc cref="Loading{TState, T}"/>
    internal ValueTask<bool> Loading(Action read) => Loading(read, static r =>
    {
        r();
        return true;
    });

    public VbObject NewFormInstance(ModuleInfo form)
    {
        if (form.Kind != InterpreterModuleKind.Form)
            throw new ArgumentException($"{form.Name} is not a form module", nameof(form));
        var instance = new VbObject(form, form.ModuleEnv.Clone());
        var reference = Vb6Value.NewObject(instance);
        SeedProgramGlobal(form.Name, () => reference);
        if (form.ModuleEnv.TryGetVariableLocation(form.Name, out var slot))
            instance.InstanceEnv.DefineVariable(form.Name, slot);
        AddRef(reference);
        return instance;
    }

    /// <summary>
    /// Run a form instance's module-level declarations into its own env, as <see cref="NewObject"/> does for a class.
    /// VB6 has them in place, sized and zeroed, before <c>Form_Initialize</c> (measured, #822: an array bounded by the
    /// form's own Const is <c>1 To 5</c> there).
    /// </summary>
    public async Task RunFormDeclarations(VbObject instance)
    {
        RunningDeclarations = true;
        try
        {
            foreach (var block in instance.ClassDef.PrePass.topLevelBlocks)
                await new StatementExecutor(this, instance.InstanceEnv, instance.ClassDef).Execute(block);
            instance.ClassDef.DeclarationsRun = true;
        }
        finally
        {
            RunningDeclarations = false;
        }
    }

    /// <summary>
    /// Raise one of a form's events on its instance: run the form module's own procedure of that name with <c>Me</c>
    /// bound to the instance, as a class's method runs (#822). A form with no handler for the event does nothing. Only
    /// the form's own procedures are looked at: an event is the form's, never another module's Sub of the same name.
    /// </summary>
    public async Task RaiseFormEvent(VbObject instance, string name, IReadOnlyList<Vb6Value>? args = null)
    {
        var callArgs = new List<CallArg>();
        if (args != null)
            foreach (var value in args)
                callArgs.Add(new CallArg(value, null));
        await RaiseFormEvent(instance, name, callArgs);
    }

    /// <summary>
    /// Raise a form's event with arguments that may be passed by reference, as QueryUnload's and Unload's Cancel are: an
    /// argument with a location is that slot, so what the handler stores there is read back (#973).
    /// </summary>
    public async Task RaiseFormEvent(VbObject instance, string name, IReadOnlyList<CallArg> callArgs)
    {
        if (!instance.ClassDef.PrePass.Procedures.TryGetValue(name, out var proc))
            return;
        try
        {
            await RunProcedure(instance.ClassDef, proc, callArgs, instance.InstanceEnv, Vb6Value.NewObject(instance), isEntry: true);
        }
        catch (Debugging.StopExecutionSignal)
        {
            // A debugger reset (Stop) during this event handler — swallowed, as ExecuteSub does.
        }
    }

    /// <summary>VB6's compile error for a project with no startup form and no usable <c>Sub Main</c>.</summary>
    public const string NoStartupObject = "Must have startup form or Sub Main()";

    /// <summary>
    /// The project's <c>Sub Main</c>, or the compile error VB6 gives for its absence: "Ambiguous name detected:
    /// Main" for two, and "Must have startup form or Sub Main()" for none or for one that is not a parameterless
    /// Sub. Read from the tables the pre-pass already collected, so it can be asked before the run begins.
    /// </summary>
    public (ModuleInfo Module, ProcedureInfo Proc) FindStartupSubMain()
    {
        var declared = new List<(ModuleInfo Module, ProcedureInfo Proc)>();
        foreach (var m in Modules.All)
        {
            if (m.Kind != InterpreterModuleKind.Standard) continue;
            if (m.PrePass.Procedures.TryGetValue("Main", out var proc))
                declared.Add((m, proc));
        }

        // Ambiguity is checked before suitability: VB6 reports two Mains as ambiguous rather than looking
        // past an unsuitable one to find a usable partner.
        if (declared.Count > 1)
            throw new VBCompileErrorException("Ambiguous name detected: Main");

        var startup = declared.Count == 1 && !declared[0].Proc.IsFunction
                      && declared[0].Proc.Parameters.Count == 0
            ? declared[0]
            : default;

        if (startup.Proc is null)
            throw new VBCompileErrorException(NoStartupObject);
        return startup;
    }

    /// <summary>True if this project has a usable <c>Sub Main</c> startup object — the same rules as
    /// <see cref="RunStartupSubMain"/>, without running or throwing. For the IDE, which must offer
    /// <c>Sub Main</c> in the Startup Object list only when one is actually available.</summary>
    public bool HasStartupSubMain()
    {
        ProcedureInfo? only = null;
        foreach (var m in Modules.All)
        {
            if (m.Kind != InterpreterModuleKind.Standard) continue;
            if (!m.PrePass.Procedures.TryGetValue("Main", out var proc)) continue;
            if (only is not null) return false;   // ambiguous is not usable
            only = proc;
        }
        return only is { IsFunction: false } && only.Parameters.Count == 0;
    }

    // Preserved entry point for event handlers / VBTimer / VBWindowContext (they pass positional by-value
    // args or none). Enters at the primary (form) module's scope. Delegates to CallProcedure; return discarded.
    public async Task ExecuteSub(string name, List<Vb6Value>? args = null, bool ignoreMissing = false)
    {
        var callArgs = new List<CallArg>();
        if (args != null)
            foreach (var value in args)
                callArgs.Add(new CallArg(value, null));
        try
        {
            // Entered without Execute, as a test's event does, the modules' declarations have not run yet (#256).
            await RunModuleDeclarations();
            await CallProcedure(name, callArgs, primaryModule, ignoreMissing);
        }
        catch (Debugging.StopExecutionSignal)
        {
            // A debugger reset (Stop) during this event handler / Sub — swallow the unwind signal (see Execute).
        }
    }

    /// <summary>
    /// Invoke a user Sub/Function by name from <paramref name="currentModule"/> (or fall through to a builtin).
    /// Resolution: the current module's own procedures, then other standard modules' Public procedures (ambiguous
    /// if 2+), then builtins. Returns the Function's value (null for a Sub, or when the name is not found and
    /// <paramref name="ignoreMissing"/> is set).
    /// </summary>
    public async Task<Vb6Value?> CallProcedure(string name, IReadOnlyList<CallArg> args, ModuleInfo currentModule, bool ignoreMissing = false,
        ExecutionEnvironment? callerEnv = null, Stack<List<Vb6Value>>? callerFrames = null)
    {
        if (TryResolveProcedure(name, currentModule, out var owner, out var proc))
        {
            // A bare call that resolves to a method OWNED by a class module (i.e. a sibling method called from
            // inside the same class — implicit Me) must run on the CURRENT instance, not a clone of the class
            // template. The instance is `Me` in the caller's env.
            if (owner.Kind is InterpreterModuleKind.Class or InterpreterModuleKind.Form)
            {
                if (callerEnv != null && ExecutionContext.TryGetVariable(callerEnv, "Me", out var me) && me.Value is VbObject self)
                    return await RunProcedure(owner, proc, args, self.InstanceEnv, me, callerFrames);
                throw new VBRunTimeException((Antlr4.Runtime.ParserRuleContext?)null, VBStandardError.ObjectVariableOrWithBlockVariableNotSet);
            }
            return await RunProcedure(owner, proc, args, callerFrames: callerFrames);
        }

        if (GlobalMember(name) is { } global)
        {
            var values = new List<Vb6Value>();
            foreach (var arg in args) values.Add(arg.Value);
            return await CallLibraryMember(global, name, values);
        }
        if (name.Equals("CallByName", StringComparison.OrdinalIgnoreCase))
            return await CallByName(args, callerFrames);

        var flat = await VB6BuiltIns.ArrangeIntrinsicArguments(name, args);
        if (await CallBuiltIn(name, flat, currentModule.PrePass.OptionCompareText) is { } result)
            return result.Value is LibraryObject && callerFrames is not null ? AdoptNewTemp(result, callerFrames) : result;
        if (!ignoreMissing)
            throw new VBCompileErrorException("Sub or Function not defined (" + name + ')');
        return null;
    }

    /// <summary>Run an already-resolved procedure in its owning module. Clones a base scope at call time
    /// (recursion-safe), binds parameters (ByRef aliases the caller slot across the shared ExecutionState;
    /// ByVal/rvalue copies), and executes the body with CurrentModule = owner. Shared by name-resolved calls,
    /// qualified <c>Module.Foo</c> calls, and instance methods.
    ///
    /// For an INSTANCE METHOD, pass <paramref name="baseEnv"/> = the object's field env and <paramref name="me"/>
    /// = the object value: the callee then clones the instance env (so field writes persist through the shared
    /// slots while params/locals stay per-call) with <c>Me</c> bound. Defaults reproduce the module-procedure
    /// behaviour exactly (base = owner's ModuleEnv, no Me).</summary>
    /// <param name="isEntry">Whether the program is entered here: an event's procedure, a <c>RaiseEvent</c> sink or
    /// <c>Sub Main</c>. A handler below an entry never sees an error raised above it (#1011).</param>
    public async Task<Vb6Value?> RunProcedure(ModuleInfo owner, ProcedureInfo proc, IReadOnlyList<CallArg> args,
        ExecutionEnvironment? baseEnv = null, Vb6Value? me = null, Stack<List<Vb6Value>>? callerFrames = null,
        bool isEntry = false)
    {
        // Immediate-window evaluation forbids running USER code — its statements would hit the paused pause-gate on
        // this (UI) thread and deadlock, and mutate program state. RunProcedure is the single sink every user
        // proc/method/Property-Get body flows through, so guarding here covers bare calls, instance methods,
        // Property Get, and Module-qualified calls (intrinsics never reach here). Scoped to State==Paused so the
        // flag can never affect the RESUMED program walk if it leaks across an async intrinsic's UI-thread yield.
        if (SuppressUserProcedureCalls && DebugController?.State == Debugging.DebugState.Paused)
            throw new Debugging.ImmediateEvalException("Sub or Function calls are not available in the Immediate window.");

        // VB6's Error 28, raised while there is still stack left to raise it with.
        //
        // A StackOverflowException on .NET cannot be caught — the runtime tears the process down where it
        // stands — so `Sub A(): Call A(): End Sub` used to take the whole IDE with it, and every unsaved form
        // in it, for a mistake a beginner makes in their first hour (#80).
        //
        // This is a REAL stack probe, deliberately, not a depth counter. VB6's stack limit is a physically
        // real constraint and reproducing the error is fidelity, where a fixed recursion ceiling would be
        // reintroducing an artificial limit this project does not impose. The measurement settles it more
        // firmly than the principle does: real vb6.exe reaches 258,825 frames before raising 28, so any
        // number a person would have picked is two orders of magnitude low — and low in the direction that
        // breaks working programs rather than the one that catches broken ones.
        //
        // TryEnsureSufficientExecutionStack reserves enough headroom for the unwind that follows, which is
        // what makes the error trappable and the program survivable afterwards, exactly as VB6 leaves it.
        //
        // RunProcedure is the single sink every user proc, instance method, Property accessor and lifecycle
        // hook flows through, so one guard here covers call depth from all of them. It is NOT the guard for
        // pathological NESTING — that is ParseDepthGuard, at parse time, on a different axis.
        if (!RuntimeHelpers.TryEnsureSufficientExecutionStack())
            throw new VBRunTimeException(VBStandardError.OutOfStackSpace);

        // Named arguments go to their parameters, and are evaluated in parameter order (#1465), before anything of the call is
        // set up, so one that raises leaves nothing to undo.
        if (NamedArguments.Any(args))
        {
            var names = new List<string>(proc.Parameters.Count);
            foreach (var parameter in proc.Parameters)
                names.Add(parameter.Name);
            args = await NamedArguments.Arrange(args, names, i => proc.Parameters[i] is { Optional: false, ParamArray: false },
                i => proc.Parameters[i].ParamArray);
        }

        // Surplus arguments are VB6's compile error, raised at execution here (#741). Optional slots count;
        // ParamArray has no maximum. #1476 callers already index parameterless Variant/object results.
        if (proc.Parameters.Count > 0 && args.Count > proc.Parameters.Count && !proc.Parameters.Any(p => p.ParamArray))
            throw new VBCompileErrorException("Wrong number of arguments or invalid property assignment");
        if (proc.Parameters.Count == 0 && args.Count > 0 && !proc.ReturnsVariant && !proc.ReturnsObject)
            throw new VBCompileErrorException("Wrong number of arguments or invalid property assignment");

        var callee = (baseEnv ?? owner.ModuleEnv).CreateProcedureScope();
        callee.AllocatedSlots = new List<int>();
        // Object-holding slots this activation OWNS and must Release at scope-exit: ByVal object params (added by
        // BindParameters) then the body's own Dim locals (appended by the StatementExecutor in declaration order).
        // NOT the return-name (its hold transfers to the caller) and NOT ByRef params (they alias the caller slot).
        var ownedSlots = new List<int>();
        callee.ReturnName = proc.IsFunction ? proc.Name : null;
        bool countMe = me is not null && !IsLifecycleHook(proc.Name);
        var refSlots = new List<int>();
        Vb6Value? ret = null;
        bool completed = false;
        bool holdsMe = false;
        bool hasReturnSlot = false;
        try
        {
            if (me is { } m)
            {
                ExecutionContext.AllocVariable(callee, "Me", m);
                if (countMe)
                {
                    AddRef(m);
                    holdsMe = true;
                }
            }
            await BindParameters(proc, args, callee, owner, ownedSlots, refSlots);
            // The return slot is typed as the declaration says, so a failed store is raised inside the Function,
            // where its own handler traps it (measured, #1444).
            if (proc.IsFunction)
            {
                ExecutionContext.AllocVariable(callee, proc.Name, SeedReturnValue(proc),
                    proc.ReturnUdtTypeName is null && VbNumeric.IsDeclarableScalar(proc.ReturnType) ? proc.ReturnType : null);
                hasReturnSlot = true;
            }
            if (proc.Body != null || proc.Declaration != null)
            {
                // The callee runs with its OWN per-activation statement-frame stack (re-entrancy isolation).
                // A class's or a form's Statics are its instance's, and a standard module's are the run's (#1491).
                var executor = new StatementExecutor(this, callee, owner, ownedSlots, new Stack<List<Vb6Value>>(), proc.Name,
                    proc.CheckedArrayReturn)
                {
                    IsEntry = isEntry,
                    VariantReturn = proc.IsFunction && proc.ReturnsVariant && !proc.ReturnsObject,
                    Statics = me?.Value is VbObject instance ? instance.Statics ??= new StaticLocals() : ExecutionContext.State.ModuleStatics,
                };
                // Debugger call stack + depth: push before running, remove THIS executor after (Remove, not
                // RemoveAt(Count-1), so overlapping async re-entrancy pops the right frame). Reflects the physical
                // call stack — matching VB6's Call Stack, which shows a re-entrant handler's frames above the
                // suspended chain.
                activationStack.Add(executor);
                executor.SetFrameDepth(activationStack.Count);   // captured per-frame depth (1 = outermost proc)
                // Every Dim in the body, allocated BEFORE the body runs: in VB6 a Dim is a declaration, not
                // an executable statement, so the local exists for the whole invocation. Reading one before
                // its Dim line is legal and yields the declared type's zero — where previously the name
                // resolved to a module-level procedure of the same name, and a watch on it saw a change the
                // program never made. Inside the try, so a fault while allocating still unwinds normally.
                try
                {
                    await executor.HoistDeclaredLocals(proc);
                    if (proc.Declaration is VB6Parser.SubStmtContext declaration)
                        await executor.ProcedureBoundary(declaration.Start.Line, breakpointOnly: true);
                    if (proc.Body is { } procedureBody)
                        await executor.Execute(procedureBody);
                    // A handler the last statement raised finishes inside the call, as it does in VB6, before the caller
                    // goes on with what the call returned (#766).
                    if (Events is { } events)
                        await events.WaitForRaised();
                    // Keep entry procedures' existing stepping positions while honouring their End breakpoints:
                    // a startup step carries on from Initialize into Load's first statement (#2172 review).
                    if ((proc.Body == null || executor.ReachedProcedureEnd) && proc.Declaration is { } ending)
                        await executor.ProcedureBoundary(ending.Stop.Line,
                            breakpointOnly: isEntry || ending is VB6Parser.PropertyGetStmtContext);
                }
                finally { activationStack.Remove(executor); }
            }
            if (proc.IsFunction)
                ret = ExecutionContext.TryGetVariable(callee, proc.Name, out var rv)
                    // Coerce the function's value to its declared return type (banker's round + range-check).
                    ? (proc.Body is { } body && VbNumeric.IsCoreNumeric(proc.ReturnType) ? VbNumeric.Narrow(rv, proc.ReturnType, body) : rv)
                    : SeedReturnValue(proc);
            // A Function or Property Get declared as a scalar or an Enum returns a typed value, and an untyped, Variant or
            // Object one a Variant's (measured: #987, `F() * 7000` with `F` As Integer returning 5 is 6, and `FV() * 7000`
            // is 35000; #1005, `GetColor()` As an Enum is a typed Long, and `GetObj().P` As Object is late-bound). A class,
            // UDT, array or Collection return is left as it is: a class's members are early-bound and typed already.
            if (ret is { } returned)
                ret = proc.ReturnUdtTypeName is null && VbNumeric.IsDeclarableScalar(proc.ReturnType) ? returned.AsFixedType()
                    : proc.ReturnUdtTypeName is { } named && !named.Contains('.') && !Types.ContainsKey(named)
                      && TryResolveEnum(named, owner, out _) ? returned.AsFixedType()
                    : proc.ReturnsVariant ? returned.AsVariant()
                    : returned;
            completed = true;
        }
        catch (Exception ex) when (MarkUnimplementedFault(ex)) { throw; }
        finally
        {
            try
            {
                foreach (var slot in refSlots)
                    ExecutionContext.State.ReleaseRef(slot);
                // Scope-exit release: this activation's owned holds (ByVal params + Dim locals, Dim locals among
                // themselves in declaration order), then Me. In a `finally` so an unhandled-error unwind still
                // terminates locals (faithful stack teardown). The return object is deliberately NOT released here on
                // the NORMAL path — its hold transfers to the caller. But on an ERROR exit (the body threw, so no
                // caller adopts it), release the return-name slot too, or a `Set F = New T` before a fault leaks.
                // A procedure's scalar locals, Variants and object references, are released before its arrays, each in
                // declaration order (measured, #970: `Dim a(0) As Foo, o` terminates o's object first, and two arrays or
                // two scalars go in the order they were declared).
                foreach (var slot in ownedSlots)
                    if (!ExecutionContext.State.IsDeclaredArray(slot))
                        await ReleaseRef(ExecutionContext.State[slot]);
                foreach (var slot in ownedSlots)
                    if (ExecutionContext.State.IsDeclaredArray(slot))
                        await ReleaseRef(ExecutionContext.State[slot]);
                if (holdsMe && me is { } m2)
                    await ReleaseRef(m2);
                if (!completed && hasReturnSlot && ExecutionContext.TryGetVariable(callee, proc.Name, out var orphan))
                    await ReleaseRef(orphan);
            }
            finally
            {
                foreach (var slot in callee.AllocatedSlots)
                    ExecutionContext.State.Free(slot);
            }
        }
        // A procedure whose last statement raised an event that reached End, or faulted, has no statement left for
        // a gate to stop: the event's boundary took the unwind as a clean stop, so the statement simply returned.
        // Without this the caller went on with the result: `Debug.Print "got " & Fn()` printed after End (#775),
        // and after a fault that had ended the run (#762).
        if (Ending || DebugController?.IsAborting == true)
            throw new Debugging.StopExecutionSignal();
        // Transfer the return object's hold to the CALLER's current statement frame (flushed at the caller's
        // statement end): a discarded result terminates then, an assigned one is out-lived by the store's AddRef.
        AdoptReturn(ret, callerFrames);
        return ret;
    }

    /// <summary>The zero-value a Function's return slot starts at, honouring its declared return type. A
    /// UDT-returning Function needs a real zero-initialised instance so <c>F.Field = x</c> works inside the body;
    /// an Enum return is a Long (default 0); a <b>class</b>-typed return starts at <c>Nothing</c> — an
    /// object-returning Function that never <c>Set</c>s its name yields <c>Nothing</c> in VB6 (matching a
    /// <c>Dim … As Class</c>), not a UDT-typed null, so <c>Set x = obj.Fn()</c> assigns Nothing rather than
    /// raising 424. Everything else uses the primitive default. (An Enum/class return name isn't in
    /// <c>Types</c>, so each must be checked before the fallback.)</summary>
    private Vb6Value SeedReturnValue(ProcedureInfo proc)
        => proc.ReturnUdtTypeName is { } rn
            ? (Types.ContainsKey(rn) ? Vb6Value.NewUdt(NewUdt(rn))
                : Enums.ContainsKey(rn) ? new Vb6Value(0L)
                : Modules.TryGet(rn, out var rc) && rc.Kind == InterpreterModuleKind.Class ? Vb6Value.Nothing
                : new Vb6Value(proc.ReturnType))
            // An `As Object` return, DefObj's included, and an `As Collection` one start at Nothing too, so a Let into the
            // first before a Set is 91 (measured, #169). They started Empty, which took the value.
            : proc.ReturnsObject ? Vb6Value.Nothing
            : new Vb6Value(proc.ReturnType);

    /// <summary>Resolve a procedure name as seen from <paramref name="currentModule"/>: the current module's own
    /// procedures (any visibility) win first, then other standard modules' <c>Public</c> procedures — 2+ matches
    /// raise "Ambiguous name detected" (VB6 catches this at compile time; being CST-only we surface it at the
    /// call). Returns false when no user procedure matches, so the caller falls through to builtins.</summary>
    public bool TryResolveProcedure(string name, ModuleInfo currentModule, out ModuleInfo owner, out ProcedureInfo proc)
    {
        if (currentModule.PrePass.Procedures.TryGetValue(name, out proc!))
        {
            owner = currentModule;
            return true;
        }

        owner = null!;
        proc = null!;
        ModuleInfo? found = null;
        ProcedureInfo? foundProc = null;
        foreach (var m in Modules.All)
        {
            if (ReferenceEquals(m, currentModule) || m.Kind != InterpreterModuleKind.Standard)
                continue;
            if (m.PrePass.Procedures.TryGetValue(name, out var p) && !p.IsPrivate)
            {
                if (found != null)
                    throw new VBCompileErrorException("Ambiguous name detected: " + name);
                found = m;
                foundProc = p;
            }
        }
        if (found != null) { owner = found; proc = foundProc!; return true; }
        return false;
    }

    /// <summary>
    /// Resolve a UDT name as seen from <paramref name="from"/>, honouring module scope.
    ///
    /// <para>
    /// The rule is the one <see cref="TryResolveProcedure"/> already implements, and it is measured: the
    /// declaring module's own types win at any visibility, then other modules' Public ones, and two foreign
    /// Publics are "Ambiguous name detected". Two modules may each declare a Private Type of one name —
    /// they are unrelated, which is why a clash cannot be reported without reading visibility first.
    /// </para>
    ///
    /// <para><paramref name="qualifier"/> names a module explicitly (<c>Module2.Point</c>) and OVERRIDES the
    /// local declaration — measured — but never defeats Private.</para>
    /// </summary>
    public bool TryResolveType(string name, ModuleInfo? from, string? qualifier, out UdtTypeDef def)
    {
        def = null!;
        if (qualifier is not null)
        {
            // Named its module. Private stays invisible unless that module IS the asking one.
            if (!Modules.TryGet(qualifier, out var owner) || !owner.PrePass.Types.TryGetValue(name, out var q))
                return false;
            if (q.IsPrivate && !ReferenceEquals(owner, from)) return false;
            def = q;
            return true;
        }

        if (from is not null && from.PrePass.Types.TryGetValue(name, out var mine)) { def = mine; return true; }

        UdtTypeDef? found = null;
        foreach (var m in Modules.All)
        {
            if (ReferenceEquals(m, from)) continue;
            if (m.PrePass.Types.TryGetValue(name, out var t) && !t.IsPrivate)
            {
                if (found != null) throw new VBCompileErrorException("Ambiguous name detected: " + name);
                found = t;
            }
        }
        if (found != null) { def = found; return true; }

        // The program-wide table is a fallback for lookups with NO module context — a nested UDT field
        // resolved at instantiation, a Function's declared return type. It must not be reached when a
        // module WAS supplied, or it hands back a Private type from somewhere else and undoes the whole
        // check above. That is exactly what it did on the first run of these tests.
        return from is null && Types.TryGetValue(name, out def!);
    }

    /// <summary>Resolve an Enum name as seen from <paramref name="from"/>. Same rule as
    /// <see cref="TryResolveType"/> minus the ambiguity arm — two modules cannot both export a Public Enum
    /// of one name, so that is refused at load and there is never a pair to choose between here.</summary>
    public bool TryResolveEnum(string name, ModuleInfo? from, out Dictionary<string, long> members)
    {
        members = null!;
        if (from is not null && from.PrePass.Enums.TryGetValue(name, out members!)) return true;
        foreach (var m in Modules.All)
        {
            if (ReferenceEquals(m, from)) continue;
            if (m.PrePass.Enums.TryGetValue(name, out members!) && !m.PrePass.PrivateEnums.Contains(name))
                return true;
        }
        return from is null && Enums.TryGetValue(name, out members!);
    }

    // ---- namespace qualifiers: Module.Member, VBA.X, VBA.Math.X ----

    // #1637: an Enum wins over its hoisted member of the same name. A real variable
    // still shadows a qualifier. Ask the environment first so project ambiguity is raised
    // at the use, before selecting an Enum; no expression is bound ahead of execution.
    internal bool EnumIsQualifier(string name, ExecutionEnvironment env, ModuleInfo? from)
    {
        var hasSlot = env.TryGetVariableLocation(name, out var slot);
        return (!hasSlot || ExecutionContext.State.IsReadOnly(slot))
            && TryResolveEnum(name, from, out _);
    }

    public enum QualifierKind { Module, Library, Enum, Project, InBoxContainer, RuntimeLibrary, ControlLibraryContainer }

    /// <param name="Name">For <see cref="QualifierKind.InBoxLibrary"/> and
    /// <see cref="QualifierKind.InBoxContainer"/>, the library or enum/module name as VB6 declares it —
    /// these levels SELECT rather than being stepped over, so the name has to survive classification.</param>
    /// <param name="RuntimeLibrary">For <see cref="QualifierKind.RuntimeLibrary"/> and
    /// <see cref="QualifierKind.ControlLibraryContainer"/>, the referenced control library (#1082).</param>
    public readonly record struct QualifierTarget(QualifierKind Kind, ModuleInfo? Module,
        Dictionary<string, long>? EnumMembers, string? Name = null, Components.InBox.RuntimeLibrary? RuntimeLibrary = null);

    /// <summary>Resolve a leading identifier as a namespace qualifier — a user module, an <c>Enum</c> type, or
    /// the <c>VBA</c>/<c>VB</c> libraries. The caller MUST check for a same-named variable first (it shadows the
    /// qualifier).</summary>
    public bool TryResolveQualifier(string name, out QualifierTarget target)
    {
        // Only Standard modules are namespace qualifiers — a class name is not usable as a static call qualifier
        // (`ClassName.Method` needs an instance), so class modules are excluded here.
        if (Modules.TryGet(name, out var m) && m.Kind == InterpreterModuleKind.Standard) { target = new QualifierTarget(QualifierKind.Module, m, null); return true; }
        if (Enums.TryGetValue(name, out var members)) { target = new QualifierTarget(QualifierKind.Enum, null, members); return true; }
        // The project's own declarations are checked FIRST, above: measured, a user Enum or Const named
        // after an in-box one wins (a `Private Enum VbMsgBoxResult` with `vbCancel = 42` makes a bare
        // `vbCancel` answer 42). So the in-box levels come after the user's, never before.
        //
        // The library level now carries its NAME, because it selects: VBRUN.vbCancel is 0 and
        // VBA.vbCancel is 2. It used to be nameless, which is exactly why it could only be transparent.
        if (IsLibraryName(name)) { target = new QualifierTarget(QualifierKind.Library, null, null, name); return true; }
        // An in-box enum or constant module used as the leading qualifier: `DragConstants.vbCancel`.
        // Measured: the container selects on its own, and `DragConstants.vbYes` is refused.
        if (VB6InBoxLibraries.IsContainer(name))
        { target = new QualifierTarget(QualifierKind.InBoxContainer, null, null, name); return true; }
        // A control library the project references, and its enums (#1082): `MSComctlLib.ccOrientationVertical` and
        // `ScrollingConstants.ccScrollingStandard`, measured. After the four in-box libraries, as an Object= reference comes
        // after them in VB6's order, so an enum both declare, BorderStyleConstants among them, is the in-box library's.
        if (ControlLibraryQualifier(name) is { } controlLibrary) { target = controlLibrary; return true; }
        // The PROJECT name is an outermost qualifier level — `Project1.Module1.MyEnum.Foo`, and a bare
        // `Project1.Foo`. Measured legal, to four segments. It is checked LAST so a module or enum of the
        // same name still wins, and it resolves to nothing in particular: there is one project in scope,
        // so the level exists to be accepted and stepped over.
        if (name.Equals(ProjectName, StringComparison.OrdinalIgnoreCase))
        { target = new QualifierTarget(QualifierKind.Project, null, null); return true; }
        target = default;
        return false;
    }

    private QualifierTarget? ControlLibraryQualifier(string name)
    {
        foreach (var library in BuiltIns.ControlLibraries)
            if (library.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                return new QualifierTarget(QualifierKind.RuntimeLibrary, null, null, library.Name, library);
        foreach (var library in BuiltIns.ControlLibraries)
            if (library.ContainerName(name) is { } container)
                return new QualifierTarget(QualifierKind.ControlLibraryContainer, null, null, container, library);
        return null;
    }

    /// <summary>The four libraries a Standard EXE references by default, from the measured inventory.
    /// This used to be a hand-written pair — VBA and VB — which recognised the one library that declares
    /// NO constants at all and missed VBRUN, which declares 590 of the 728. So `VBRUN.vbAlignBottom`
    /// raised "variable not defined" rather than answering 2.</summary>
    private static bool IsLibraryName(string n) => VB6InBoxLibraries.IsLibrary(n);

    // The intrinsic "modules" (Math/Strings/…) inside the VBA library are transparent scoping — members promote
    // to the library level, so `VBA.Math.Abs` ≡ `VBA.Abs`. We recognise them only to skip past them; membership
    // is NEVER validated (that would be pre-execution analysis — see the spec's scope boundary).
    public static bool IsIntrinsicModuleSegment(string n) => n.ToLowerInvariant() switch
    {
        "math" or "strings" or "conversion" or "datetime" or "information"
            or "interaction" or "filesystem" or "financial" => true,
        _ => false,
    };

    // The declared type a parameter's own slot records, as a Dim's does, so that a store into it converts and a For over
    // it is a typed counter (measured, #839: `Function P(ByVal i As Long)` counts as a Long; #874: `x = 5` in a String
    // parameter is "5", and `x = "abc"` in a Boolean one 13). Every type a variable can be declared as.
    private static Vb6Value.ValueType? DeclaredSlotType(ParamInfo p) =>
        p.DeclaredType is { } dt && VbNumeric.IsDeclarableScalar(dt) ? dt : null;

    private async Task BindParameters(ProcedureInfo proc, IReadOnlyList<CallArg> args, ExecutionEnvironment callee, ModuleInfo owner,
        List<int> ownedSlots, List<int> refSlots)
    {
        for (int i = 0; i < proc.Parameters.Count; i++)
        {
            var p = proc.Parameters[i];
            // A ParamArray is the last parameter, and takes every argument left (#2462).
            if (p.ParamArray)
            {
                BindParamArray(p, args, i, callee, ownedSlots, refSlots);
                return;
            }
            // An Enum parameter is a Long: converted into one, and typed (measured, #1005: `c * m` with `c As MyEnum`
            // given 5 and `m As Long` 500000000 overflows, and `Select Case c` converts `Case 4.6` into 5). Resolved here,
            // since the pre-pass that read the declaration had not yet seen every module's Enums.
            if (p is { DeclaredType: null, IsArray: false, TypeName: { } typeName } && !typeName.Contains('.')
                && TryResolveEnum(typeName, owner, out _))
                p = p with { DeclaredType = Vb6Value.ValueType.Long };
            callee.Parameters.Add(p.Name);
            if (p.ReadsAsVariant)
                callee.VariantParameters.Add(p.Name);
            if (p.DeclaredObject)
                callee.ObjectParameters.Add(p.Name);

            // A blank slot at this position (`Foo 1, , 3`) occupies it only so the arguments after it bind
            // correctly. It is NOT a value: fall through to the Optional branch so the parameter takes its
            // declared default, exactly as if the caller had stopped short.
            if (i < args.Count && !args[i].Value.IsMissing)
            {
                var arg = args[i];
                // A ParamArray passed whole to a ByRef parameter is VB6's compile error "Invalid ParamArray use" (measured, to
                // `x() As Variant` and to `v As Variant`), raised here as the call runs. A ByVal parameter takes a copy.
                if (p.ByRef && arg.Location is { } argSlot && ExecutionContext.State.IsParamArray(argSlot))
                    throw new VBCompileErrorException("Invalid ParamArray use");
                // A `v As Variant` shares its argument as an untyped `v` does, whatever the argument holds (measured:
                // #970 for an array, which is what lets a Variant sort routine sort its caller's array and a ReDim reach
                // the caller's; #1001 for a scalar, so `v = 5` reaches a caller's Variant, and a caller's `Integer`, which
                // the store converts into and which stays an Integer). It got a copy of a scalar, so the caller kept 1.
                bool aliasable = p.ByRef && arg.Location is not null
                    && (p.DeclaredType is null || p.DeclaredType == Vb6Value.ValueType.EmptyVariant
                        || p.DeclaredType == arg.Value.Type);
                // A UDT field (#1451) or an array element (#1455) is shared the same way, by a slot that reads and writes it
                // for the whole call: given to a ByRef parameter of its type, or an untyped or Variant one. One of another type
                // is VB6's compile error "ByRef argument type mismatch" (measured), and is copied here, as a variable is.
                bool refAliasable = !aliasable && p.ByRef && arg.Ref is { } target
                    && (p.DeclaredType is null || p.DeclaredType == Vb6Value.ValueType.EmptyVariant
                        || p.DeclaredType == target.DeclaredType);
                if (aliasable)
                    callee.DefineVariable(p.Name, arg.Location!.Value);            // ByRef: share the caller's slot
                                                                                   // (NOT counted — the caller's slot already holds the ref)
                else if (refAliasable)
                {
                    var slot = ExecutionContext.State.AllocRef(arg.Ref!, callee);
                    refSlots.Add(slot);
                    callee.DefineVariable(p.Name, slot);
                }
                else
                {
                    // ByVal / rvalue: copy, coerced to the parameter's declared type (banker's round + range-check).
                    // A UDT arg is deep-copied so the callee's mutations don't touch the caller's value. An object
                    // passed to a declared scalar parameter stands for its default member (ObjectAsValue); a Variant
                    // or object parameter takes the object itself (measured).
                    var passed = p.DeclaredType is { } declared && VbNumeric.IsDeclarableScalar(declared)
                        ? ObjectAsValue.Scalar(arg.Value, proc.Body as Antlr4.Runtime.ParserRuleContext)
                        : arg.Value;
                    // Converted as a store into a variable of the type converts, before the procedure runs, so a value
                    // that will not convert raises in the caller (measured, #874: `B "abc"` with `ByVal x As Boolean` is
                    // 13 at the call, `S 5` with `ByVal x As String` is "5", `D "1/2/2000"` As Date is that date, and
                    // `NI "12"` As Integer is 12, which Narrow read as 0).
                    var stored = DeclaredSlotType(p) is { } dt
                        ? VbNumeric.CoerceOnStore(passed, dt, proc.Body as Antlr4.Runtime.ParserRuleContext)
                        : CopyForStore(passed);
                    ExecutionContext.AllocVariable(callee, p.Name, stored, DeclaredSlotType(p));
                    MarkParameterSlot(p, callee);
                    // A ByVal object param is a counted hold for the call's duration (so a `New T` passed straight
                    // into a call terminates right after the call, per the oracle); release it at scope-exit. A ByVal
                    // array is the callee's own copy, whose objects the copy holds, so it is released there too.
                    if (stored.Value is VbObject or VBArray or VbCollection or LibraryObject && callee.TryGetVariableLocation(p.Name, out var ploc))
                    {
                        if (stored.Value is VbObject or VbCollection or LibraryObject)
                            AddRef(stored);
                        ownedSlots.Add(ploc);
                    }
                }
            }
            else if (p.Optional)
            {
                // Optional defaults evaluate in the OWNER module's context (a default may name an owner-module
                // helper); an empty With scope (a leading `.X` in a default is Error 91).
                // Three cases, and VB6 distinguishes all three (measured — see the IsMissing section of
                // docs/vb6-fidelity-oracle/ismissing-and-the-shape-of-an-omitted-argument.md):
                //   a default    → that value, and IsMissing is False
                //   a non-Variant declared type → that type's zero, and IsMissing is False
                //   an untyped or As Variant parameter → Missing (#866, native and in the IDE)
                var value = p.Default != null
                    ? await new ExpressionExecutor(this, callee, owner, new Stack<Vb6Value>()).EvaluateValue(p.Default.valueStmt())
                    : p.DeclaredType is { } declared && declared != Vb6Value.ValueType.EmptyVariant ? new Vb6Value(declared)
                    : Vb6Value.Missing;
                // A default converts into the parameter's type too (measured, #874: `Optional x As String = 5` is "5",
                // `As Date = "1/2/2003"` that date, `As Boolean = 1` True, and `As Integer = 2.5` 2).
                if (DeclaredSlotType(p) is { } declaredSlot)
                    value = VbNumeric.CoerceOnStore(value, declaredSlot, proc.Body as Antlr4.Runtime.ParserRuleContext);
                ExecutionContext.AllocVariable(callee, p.Name, value, DeclaredSlotType(p));
                MarkParameterSlot(p, callee);
            }
            else
            {
                throw new VBCompileErrorException($"Argument not optional: '{p.Name}' in '{proc.Name}'");
            }
        }
    }

    // A ParamArray (#2462): the arguments from `first` on, as a Variant array from 0, empty when there are none. Measured, native
    // and p-code: `IsMissing` of it is True only when it is empty, Option Base 1 leaves it from 0, and each element is its
    // argument, ByRef, so `a(i) = a(i) + 1` on `Bump i, n` leaves i 2 and n 11.
    private void BindParamArray(ParamInfo p, IReadOnlyList<CallArg> args, int first, ExecutionEnvironment callee,
        List<int> ownedSlots, List<int> refSlots)
    {
        var elements = new IByRefTarget[Math.Max(0, args.Count - first)];
        for (int e = 0; e < elements.Length; e++)
            elements[e] = ParamArrayElement(args[first + e], callee, ownedSlots, refSlots);
        var value = new Vb6Value(new Vb6Value.ValueType(Vb6Value.ValueType.EmptyVariant, true), [(0, elements.Length - 1)])
            .WithArray(VBArray.ParamArray(elements));
        callee.Parameters.Add(p.Name);
        ExecutionContext.AllocVariable(callee, p.Name, value, null);
        if (callee.TryGetVariableLocation(p.Name, out var location))
        {
            ExecutionContext.State.MarkDeclaredArray(location);
            ExecutionContext.State.MarkParamArray(location);
        }
    }

    // One argument of a ParamArray (#2462), as a ByRef argument binds (measured): a variable is shared, and a store through the
    // element converts to its type, so 5.7 into an Integer is 6 and "abc" 13; a field or an array element is reached through a
    // slot that stands for it, so `Bump arr(2)` leaves 21. Anything else is the call's own copy: an expression, a variable in
    // parentheses (`Bump (i)` leaves i), a constant, a blank, which is Missing (`Show 1, , 3`), and a ParamArray passed on,
    // which arrives as one element holding a copy of the array.
    private IByRefTarget ParamArrayElement(CallArg arg, ExecutionEnvironment callee, List<int> ownedSlots, List<int> refSlots)
    {
        var state = ExecutionContext.State;
        if (arg.Location is { } location && !state.IsConstant(location) && !state.IsReadOnly(location) && !state.IsParamArray(location))
            return new SlotRef(state, location);
        if (arg.Ref is { } target)
        {
            var refSlot = state.AllocRef(target, callee);
            refSlots.Add(refSlot);
            return new SlotRef(state, refSlot);
        }
        var copy = arg.Omitted ? Vb6Value.Missing : CopyForStore(arg.Value);
        var own = state.Alloc(copy);
        callee.AllocatedSlots?.Add(own);
        // Held for the call, as a ByVal argument is, and released when it returns.
        if (copy.Value is VbObject or VBArray or VbCollection or LibraryObject)
        {
            if (copy.Value is not VBArray)
                AddRef(copy);
            ownedSlots.Add(own);
        }
        return new SlotRef(state, own);
    }

    // A parameter's own slot says what it was declared as, for ReDim: `b()` an array, `v` or `v As Variant` a Variant.
    // A ByRef parameter that shares its caller's slot keeps the caller's marks. That is right for fixedness (a fixed
    // array is 10 through it, measured), but not for the inverted-range rule: an untyped `v` sharing a declared `Long()`
    // takes the Variant rule (measured, #970), which ReDim reads from ExecutionEnvironment.VariantParameters.
    private void MarkParameterSlot(ParamInfo p, ExecutionEnvironment callee)
    {
        if (!callee.TryGetVariableLocation(p.Name, out var location))
            return;
        if (p.IsArray)
            ExecutionContext.State.MarkDeclaredArray(location);
        else if (p.ReadsAsVariant)
            ExecutionContext.State.MarkVariant(location);
    }

    public void SyntaxError(TextWriter output, IRecognizer recognizer, IToken offendingSymbol, int line, int charPositionInLine,
        string msg, RecognitionException e)
    {
        throw new VBCompileErrorException($"{msg} {SyntaxErrorAt(line, charPositionInLine)}\nOffending symbol: {offendingSymbol.Text}")
            { Line = line };
    }

    public void SyntaxError(TextWriter output, IRecognizer recognizer, int offendingSymbol, int line, int charPositionInLine,
        string msg, RecognitionException e)
    {
        throw new VBCompileErrorException($"{msg} {SyntaxErrorAt(line, charPositionInLine)}") { Line = line };
    }
}
