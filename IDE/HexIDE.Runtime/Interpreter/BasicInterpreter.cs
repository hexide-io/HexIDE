using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Antlr4.Runtime;
using HexIDE.IDE;

namespace HexIDE.Runtime.Interpreter;

public interface IBasicStandardLibrary
{
    Task<MessageBoxResult> MsgBox(string text, string? caption, MessageBoxButtons buttons, MessageBoxIcon icon);
    Task<string?> InputBox(string prompt, string? title, string defaultText);

    /// <summary>Sink for <c>Debug.Print</c>. Receives the typed value (live → Immediate window; test → capture).</summary>
    void DebugPrint(Vb6Value value);
}

public partial class BasicInterpreter : IAntlrErrorListener<IToken>, IAntlrErrorListener<int>
{
    public ModuleExecutionContext ExecutionContext { get; }
    public VB6BuiltIns BuiltIns { get; }
    private readonly IBasicStandardLibrary stdLib;
    private readonly ExecutionEnvironment rootEnv;
    private readonly string code;
    private PrePass prepass;

    /// <summary>The primary/startup module's prepass. Per-executing-module data (Option Base, procedures) lives
    /// on <see cref="ModuleInfo.PrePass"/>; resolve against the executing module via <c>CurrentModule</c> at the
    /// call site, not this property.</summary>
    public PrePass PrePass => prepass;

    /// <summary>The project-wide module registry this interpreter resolves names against <em>at runtime</em>.
    /// Phase 1 scopes it to one execution context (the test harness, or one running form + its modules);
    /// cross-form shared standard-module state (limitation E1 in the interpreter-advanced spec) is deferred to a
    /// real-path project-scope hoist.</summary>
    public ModuleRegistry Modules { get; } = new();

    /// <summary>The debug session driving step-through, or null for a non-debugged / headless run (tests, the
    /// Standalone runner). Set by the IDE right after construction (the shared per-session controller); read by the
    /// StatementExecutor's pause-gate. Null ⇒ zero debug overhead (the gate is skipped entirely).</summary>
    public Debugging.IDebugController? DebugController { get; set; }

    /// <summary>While set (AND the debugger is Paused), <see cref="RunProcedure"/> and <see cref="NewObject"/> refuse
    /// to run USER code (any Sub/Function/Property-Get body, or <c>New</c> construction) — used by Immediate-window
    /// evaluation, where running user statements would hit the paused gate on this (UI) thread (deadlock) and mutate
    /// program state. Intrinsic/builtin functions are unaffected (they never reach those sinks). The evaluator sets
    /// it around the eval and resets it in a <c>finally</c>; the extra <c>State==Paused</c> condition means it can
    /// never affect the RESUMED program walk even if it leaks across an async intrinsic's UI-thread yield.</summary>
    public bool SuppressUserProcedureCalls { get; set; }

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
    public VbUdt NewUdt(string typeName) => NewUdt(typeName, 0);

    private VbUdt NewUdt(string typeName, int depth)
    {
        if (depth > 64)
            throw new VBCompileErrorException("Recursive Type definition: " + typeName);
        if (!Types.TryGetValue(typeName, out var def))
            throw new VBCompileErrorException("User-defined type not defined: " + typeName);
        var fields = new Dictionary<string, Vb6Value>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in def.Fields)
        {
            if (f.ScalarType is { } scalar)
                fields[f.Name] = new Vb6Value(scalar);
            else if (f.UdtTypeName is { } nested)
                fields[f.Name] = Types.ContainsKey(nested) ? Vb6Value.NewUdt(NewUdt(nested, depth + 1))
                    : Enums.ContainsKey(nested) ? new Vb6Value(0L)   // an Enum-typed field is a Long
                    : throw new VBCompileErrorException($"Type '{typeName}' field '{f.Name}' references undefined type '{nested}'");
            else
                fields[f.Name] = Vb6Value.Variant;
        }
        return new VbUdt(typeName, fields);
    }

    /// <summary>Deep-copy a UDT value (VB6 value semantics); pass everything else through unchanged. Applied at
    /// the value-assignment boundaries: <c>Let</c>, <c>ByVal</c> param, whole-UDT field write, function return.
    /// A <c>VbObject</c> is deliberately NOT copied — objects are reference types, so <c>Set b = a</c> shares.</summary>
    public static Vb6Value CopyIfValueType(Vb6Value v)
        => v.Value is VbUdt udt ? Vb6Value.NewUdt(udt.DeepClone()) : v;

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
        if (!Modules.TryGet(className, out var classDef) || classDef.Kind != InterpreterModuleKind.Class)
            throw new VBCompileErrorException("User-defined type not defined: " + className);
        var instanceEnv = classDef.ModuleEnv.Clone();
        foreach (var block in classDef.PrePass.topLevelBlocks)
            await new StatementExecutor(this, instanceEnv, classDef).Execute(block);
        var instance = new VbObject(classDef, instanceEnv);
        if (classDef.PrePass.Procedures.TryGetValue("Class_Initialize", out var init))
            await RunProcedure(classDef, init, [], instanceEnv, Vb6Value.NewObject(instance));
        return instance;
    }

    // ---- Class_Terminate reference counting (interpreter-advanced Phase 4.2) ----
    // Real slot-based refcounting: named-storage + transient holders are counted on the VbObject; Terminate fires
    // at zero. Runtime execution machinery (in-bounds), not a scan of ExecutionState (unsound under E2). A `New`
    // object starts at 0 and is AddRef'd by whatever first stores it (Set / ByVal param); there is no construction
    // guard because Class_Initialize's Me is NOT counted (a lifecycle hook — see RunProcedure), so the hook can't
    // terminate the half-built instance.

    /// <summary>Increment the reference count if <paramref name="v"/> is a live class instance (no-op for
    /// Nothing / controls / proxies / value types). Callers do AddRef(new) BEFORE Release(old) at every store, so
    /// <c>Set x = x</c> and aliased assignment can't transiently hit zero and self-terminate.</summary>
    public void AddRef(Vb6Value v)
    {
        if (v.Value is VbObject o && !o.Terminated)
            o.RefCount++;
    }

    /// <summary>Decrement the reference count of a live class instance; fire <c>Class_Terminate</c> the instant it
    /// reaches zero. Async because the hook runs user code.</summary>
    public async Task ReleaseRef(Vb6Value v)
    {
        if (v.Value is VbObject o && !o.Terminated)
        {
            o.RefCount--;
            if (o.RefCount <= 0)
                await FireTerminate(o);
        }
    }

    /// <summary>Run the instance's <c>Private Sub Class_Terminate</c> (if any), one-shot. Sets <see
    /// cref="VbObject.Terminated"/> BEFORE dispatching so the hook's own <c>Me</c> AddRef/Release are no-ops and
    /// re-entry / double-terminate is impossible. Symmetric to the <c>Class_Initialize</c> dispatch in
    /// <see cref="NewObject"/>.</summary>
    public async Task FireTerminate(VbObject o)
    {
        // A debugger reset (Stop) unwinds the walk via StopExecutionSignal, which runs this refcount teardown in
        // RunProcedure's finally — but VB6 `End` fires NO Class_Terminate (oracle-verified), so suppress it while
        // the debugger is aborting.
        if (o.Terminated || DebugController?.IsAborting == true)
            return;
        o.Terminated = true;
        if (o.ClassDef.PrePass.Procedures.TryGetValue("Class_Terminate", out var term))
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
    // the safe direction).
    private static void AdoptReturn(Vb6Value? ret, Stack<List<Vb6Value>>? callerFrames)
    {
        if (ret is { Value: VbObject } r && callerFrames is { Count: > 0 })
            callerFrames.Peek().Add(r);
    }

    /// <summary>Adopt a freshly-created <c>New</c> object into the current statement frame with a held reference,
    /// so a transient that is never stored (e.g. <c>Debug.Print (New Foo).Name</c>, or a <c>New</c> passed to a
    /// built-in) still terminates at statement-end. The <c>AddRef</c> here nets against the frame flush; any store
    /// (<c>Set</c>) or ByVal-param bind AddRefs again, leaving exactly the destination's hold after the flush. If
    /// there is no active frame the object simply isn't tracked (leaks — the safe direction).</summary>
    public void AdoptNewTemp(Vb6Value v, Stack<List<Vb6Value>> frames)
    {
        if (v.Value is VbObject && frames.Count > 0)
        {
            AddRef(v);
            frames.Peek().Add(v);
        }
    }

    public BasicInterpreter(IBasicStandardLibrary stdLib,
        ModuleExecutionContext executionContext,
        ExecutionEnvironment rootEnv,
        string code,
        string primaryName = "Module1",
        IReadOnlyList<(string Name, string Code)>? additionalModules = null,
        IReadOnlyList<(string Name, string Code)>? classModules = null)
    {
        ExecutionContext = executionContext;
        BuiltIns = new VB6BuiltIns(stdLib) { AppTitle = () => App.TitleOrNull };
        this.stdLib = stdLib;
        this.rootEnv = rootEnv;
        this.code = code;

        // Build the primary (startup) module over the caller-provided rootEnv, then any additional modules —
        // each with its own env, ALL sharing one ExecutionState so slots are program-global (this is why ByRef
        // aliasing across modules works, and why cross-form sharing needs a project-scope hoist — see E1).
        primaryModule = BuildModule(primaryName, InterpreterModuleKind.Standard, code, rootEnv);
        prepass = primaryModule.PrePass;
        Modules.Add(primaryModule);

        if (additionalModules != null)
            foreach (var (name, modCode) in additionalModules)
                Modules.Add(BuildModule(name, InterpreterModuleKind.Standard, modCode, new ExecutionEnvironment()));

        // Class modules are TEMPLATES, not singletons — registered so `New`/`Dim As`/`TypeOf` resolve the name,
        // but instantiated only by New (each instance gets its own field env), never run at program start.
        if (classModules != null)
            foreach (var (name, modCode) in classModules)
                Modules.Add(BuildModule(name, InterpreterModuleKind.Class, modCode, new ExecutionEnvironment()));

        // Aggregate every module's UDT/Enum definitions into one program-wide table — Types are Public and
        // cross-module (a `Dim e As Employee` anywhere resolves a `Type Employee` declared in any module).
        foreach (var m in Modules.All)
        {
            foreach (var (tn, def) in m.PrePass.Types) Types[tn] = def;
            foreach (var (en, members) in m.PrePass.Enums) Enums[en] = members;
        }

        // Seed the program-global Debug and Err objects into EVERY module env, each at a SINGLE shared slot
        // (never a per-env alloc, which would mint N distinct objects). Sharing the slot also removes the old
        // Err field/slot divergence across a SetCode recompile.
        SeedProgramGlobal("Debug", () => new Vb6Value(new DebugProxy(stdLib)));
        SeedProgramGlobal("Err", () => new Vb6Value(Err));
        SeedProgramGlobal("App", () => new Vb6Value(App));
    }

    /// <summary>The program-global <c>App</c> object, describing the project this program came from.</summary>
    public VbApp App { get; private set; } = new(AppInfo.None);

    /// <summary>
    /// Tell the running program which project it is. Called by the host before <c>Execute</c>; a program
    /// with no project behind it (the test harness, the bare interpreter) keeps <see cref="AppInfo.None"/>
    /// and reports empty strings rather than inventing an identity.
    /// </summary>
    public void SetAppInfo(AppInfo info)
    {
        App = new VbApp(info);
        SeedProgramGlobal("App", () => new Vb6Value(App));
    }

    /// <summary>The program-global <c>Err</c> object. Shared with the <c>Err</c> slot in every module env (see
    /// <see cref="SeedProgramGlobal"/>), so runtime error traps and user <c>Err.Number</c> reads see one state.</summary>
    public readonly VbErr Err = new();

    private ModuleInfo BuildModule(string name, InterpreterModuleKind kind, string moduleCode, ExecutionEnvironment env)
    {
        var pp = new PrePass(env, ExecutionContext.State);
        pp.Visit(Parse(moduleCode));
        return new ModuleInfo(name, kind, pp, env);
    }

    private Antlr4.Runtime.Tree.IParseTree Parse(string source)
    {
        var lexer = new VB6Lexer(new AntlrInputStream(new StringReader(source)));
        var parser = new VB6Parser(new CommonTokenStream(lexer));
        lexer.RemoveErrorListeners();
        parser.RemoveErrorListeners();
        lexer.AddErrorListener(this);
        parser.AddErrorListener(this);
        parser.AddParseListener(new ParseDepthGuard());   // reject pathological nesting as a compile error, not a crash
        return parser.startRule();
    }

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
        parser.AddParseListener(new ParseDepthGuard());   // reject pathological nesting as a compile error, not a crash
        var tree = parser.valueStmt();
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
        parser.AddParseListener(new ParseDepthGuard());
        parser.ErrorHandler = new Antlr4.Runtime.BailErrorStrategy();   // any syntax error → bail; we return null
        try
        {
            var stmt = parser.blockStmt();
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

    public async Task Execute()
    {
        // Initialise every NON-primary module's module-level declarations first (fills Public Const/Dim values
        // in that module's own env) so a cross-module read sees a value, not Empty. Modules can't have executable
        // top-level statements in real VB6, so this is declaration hoisting; the primary (test/startup) body runs
        // after.
        // Class modules are templates — they must NOT run at program start (a class body runs only per-instance,
        // via New). Only Standard modules are initialised here.
        try
        {
            foreach (var m in Modules.All)
                if (!ReferenceEquals(m, primaryModule) && m.Kind == InterpreterModuleKind.Standard)
                    foreach (var block in m.PrePass.topLevelBlocks)
                        await new StatementExecutor(this, m.ModuleEnv, m).Execute(block);

            var statementExecutor = new StatementExecutor(this, rootEnv, primaryModule);
            foreach (var block in primaryModule.PrePass.topLevelBlocks)
                await statementExecutor.Execute(block);
        }
        catch (Debugging.StopExecutionSignal)
        {
            // A debugger reset (Stop) unwound the walk cleanly (refcount teardown ran, suppressed) — swallow it.
        }
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
            if (owner.Kind == InterpreterModuleKind.Class)
            {
                if (callerEnv != null && ExecutionContext.TryGetVariable(callerEnv, "Me", out var me) && me.Value is VbObject self)
                    return await RunProcedure(owner, proc, args, self.InstanceEnv, me, callerFrames);
                throw new VBRunTimeException((Antlr4.Runtime.ParserRuleContext?)null, VBStandardError.ObjectVariableOrWithBlockVariableNotSet);
            }
            return await RunProcedure(owner, proc, args, callerFrames: callerFrames);
        }

        var flat = new List<Vb6Value>(args.Count);
        foreach (var a in args)
            flat.Add(a.Value);
        if (await BuiltIns.EvaluateBuiltInFunction(name, flat) is { } result)
            return result;
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
    public async Task<Vb6Value?> RunProcedure(ModuleInfo owner, ProcedureInfo proc, IReadOnlyList<CallArg> args,
        ExecutionEnvironment? baseEnv = null, Vb6Value? me = null, Stack<List<Vb6Value>>? callerFrames = null)
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

        var callee = (baseEnv ?? owner.ModuleEnv).Clone();
        // Object-holding slots this activation OWNS and must Release at scope-exit: ByVal object params (added by
        // BindParameters) then the body's own Dim locals (appended by the StatementExecutor in declaration order).
        // NOT the return-name (its hold transfers to the caller) and NOT ByRef params (they alias the caller slot).
        var ownedSlots = new List<int>();
        bool countMe = me is not null && !IsLifecycleHook(proc.Name);
        if (me is { } m)
        {
            ExecutionContext.AllocVariable(callee, "Me", m);
            if (countMe) AddRef(m);   // a running (non-lifecycle) method holds Me → defers a last-ref drop to return
        }
        await BindParameters(proc, args, callee, owner, ownedSlots);
        if (proc.IsFunction)
            ExecutionContext.AllocVariable(callee, proc.Name, SeedReturnValue(proc));
        Vb6Value? ret = null;
        bool completed = false;
        try
        {
            if (proc.Body != null)
            {
                // The callee runs with its OWN per-activation statement-frame stack (re-entrancy isolation).
                var executor = new StatementExecutor(this, callee, owner, ownedSlots, new Stack<List<Vb6Value>>(), proc.Name);
                // Debugger call stack + depth: push before running, remove THIS executor after (Remove, not
                // RemoveAt(Count-1), so overlapping async re-entrancy pops the right frame). Reflects the physical
                // call stack — matching VB6's Call Stack, which shows a re-entrant handler's frames above the
                // suspended chain.
                activationStack.Add(executor);
                executor.SetFrameDepth(activationStack.Count);   // captured per-frame depth (1 = outermost proc)
                try { await executor.Execute(proc.Body); }
                finally { activationStack.Remove(executor); }
            }
            if (proc.IsFunction)
                ret = ExecutionContext.TryGetVariable(callee, proc.Name, out var rv)
                    // Coerce the function's value to its declared return type (banker's round + range-check).
                    ? (proc.Body is { } body && VbNumeric.IsCoreNumeric(proc.ReturnType) ? VbNumeric.Narrow(rv, proc.ReturnType, body) : rv)
                    : SeedReturnValue(proc);
            completed = true;
        }
        finally
        {
            // Scope-exit release: this activation's owned holds (ByVal params + Dim locals, Dim locals among
            // themselves in declaration order), then Me. In a `finally` so an unhandled-error unwind still
            // terminates locals (faithful stack teardown). The return object is deliberately NOT released here on
            // the NORMAL path — its hold transfers to the caller. But on an ERROR exit (the body threw, so no
            // caller adopts it), release the return-name slot too, or a `Set F = New T` before a fault leaks.
            foreach (var slot in ownedSlots)
                await ReleaseRef(ExecutionContext.State[slot]);
            if (countMe && me is { } m2)
                await ReleaseRef(m2);
            if (!completed && proc.IsFunction && ExecutionContext.TryGetVariable(callee, proc.Name, out var orphan))
                await ReleaseRef(orphan);
        }
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

    // ---- namespace qualifiers: Module.Member, VBA.X, VBA.Math.X ----

    public enum QualifierKind { Module, Library, Enum }

    public readonly record struct QualifierTarget(QualifierKind Kind, ModuleInfo? Module, Dictionary<string, long>? EnumMembers);

    /// <summary>Resolve a leading identifier as a namespace qualifier — a user module, an <c>Enum</c> type, or
    /// the <c>VBA</c>/<c>VB</c> libraries. The caller MUST check for a same-named variable first (it shadows the
    /// qualifier).</summary>
    public bool TryResolveQualifier(string name, out QualifierTarget target)
    {
        // Only Standard modules are namespace qualifiers — a class name is not usable as a static call qualifier
        // (`ClassName.Method` needs an instance), so class modules are excluded here.
        if (Modules.TryGet(name, out var m) && m.Kind == InterpreterModuleKind.Standard) { target = new QualifierTarget(QualifierKind.Module, m, null); return true; }
        if (Enums.TryGetValue(name, out var members)) { target = new QualifierTarget(QualifierKind.Enum, null, members); return true; }
        if (IsLibraryName(name)) { target = new QualifierTarget(QualifierKind.Library, null, null); return true; }
        target = default;
        return false;
    }

    private static bool IsLibraryName(string n)
        => n.Equals("VBA", StringComparison.OrdinalIgnoreCase) || n.Equals("VB", StringComparison.OrdinalIgnoreCase);

    // The intrinsic "modules" (Math/Strings/…) inside the VBA library are transparent scoping — members promote
    // to the library level, so `VBA.Math.Abs` ≡ `VBA.Abs`. We recognise them only to skip past them; membership
    // is NEVER validated (that would be pre-execution analysis — see the spec's scope boundary).
    public static bool IsIntrinsicModuleSegment(string n) => n.ToLowerInvariant() switch
    {
        "math" or "strings" or "conversion" or "datetime" or "information"
            or "interaction" or "filesystem" or "financial" => true,
        _ => false,
    };

    private async Task BindParameters(ProcedureInfo proc, IReadOnlyList<CallArg> args, ExecutionEnvironment callee, ModuleInfo owner, List<int> ownedSlots)
    {
        for (int i = 0; i < proc.Parameters.Count; i++)
        {
            var p = proc.Parameters[i];
            if (p.ParamArray)
                throw new NotImplementedException("ParamArray parameters are not yet supported");

            // A blank slot at this position (`Foo 1, , 3`) occupies it only so the arguments after it bind
            // correctly. It is NOT a value: fall through to the Optional branch so the parameter takes its
            // declared default, exactly as if the caller had stopped short.
            if (i < args.Count && !args[i].Value.IsMissing)
            {
                var arg = args[i];
                bool aliasable = p.ByRef && arg.Location is not null
                    && (p.DeclaredType is null || p.DeclaredType == arg.Value.Type);
                if (aliasable)
                    callee.DefineVariable(p.Name, arg.Location!.Value);            // ByRef: share the caller's slot
                                                                                   // (NOT counted — the caller's slot already holds the ref)
                else
                {
                    // ByVal / rvalue: copy, coerced to the parameter's declared type (banker's round + range-check).
                    // A UDT arg is deep-copied so the callee's mutations don't touch the caller's value.
                    var stored = p.DeclaredType is { } dt && VbNumeric.IsCoreNumeric(dt) && proc.Body is { } body
                        ? VbNumeric.Narrow(arg.Value, dt, body)
                        : CopyIfValueType(arg.Value);
                    ExecutionContext.AllocVariable(callee, p.Name, stored);
                    // A ByVal object param is a counted hold for the call's duration (so a `New T` passed straight
                    // into a call terminates right after the call, per the oracle); release it at scope-exit.
                    if (stored.Value is VbObject && callee.TryGetVariableLocation(p.Name, out var ploc))
                    {
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
                // docs/vb6-fidelity-oracle.md):
                //   a default    → that value, and IsMissing is False
                //   a declared type → that type's zero, and IsMissing is False; there is no Missing to hold
                //   neither      → Missing, the only case where IsMissing is True
                var value = p.Default != null
                    ? await new ExpressionExecutor(this, callee, owner, new Stack<Vb6Value>()).EvaluateValue(p.Default.valueStmt())
                    : p.DeclaredType is { } declared ? new Vb6Value(declared)
                    : Vb6Value.Missing;
                ExecutionContext.AllocVariable(callee, p.Name, value);
            }
            else
            {
                throw new VBCompileErrorException($"Argument not optional: '{p.Name}' in '{proc.Name}'");
            }
        }
    }

    public void SyntaxError(TextWriter output, IRecognizer recognizer, IToken offendingSymbol, int line, int charPositionInLine,
        string msg, RecognitionException e)
    {
        throw new VBCompileErrorException($"{msg} in line {line}:{charPositionInLine}\nOffending symbol: {offendingSymbol.Text}");
    }

    public void SyntaxError(TextWriter output, IRecognizer recognizer, int offendingSymbol, int line, int charPositionInLine,
        string msg, RecognitionException e)
    {
        throw new VBCompileErrorException($"{msg} in line {line}:{charPositionInLine}");
    }
}