using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Antlr4.Runtime;
using Antlr4.Runtime.Tree;
using Avalonia.Controls;
using HexIDE.Runtime.AvaloniaInterop;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.Components;

#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously

namespace HexIDE.Runtime.Interpreter;

// Per-procedure error-handling mode (a StatementExecutor is created per invocation).
public enum ErrorMode { None, ResumeNext, GoToLabel }

public partial class StatementExecutor : VB6Visitor<Task<ControlFlow>>, Debugging.IDebugFrame
{
    private readonly BasicInterpreter interpreter;
    private readonly ExecutionEnvironment currentEnv;

    // The module whose code this activation is executing — threaded PER-ACTIVATION (never an ambient interpreter
    // field), so name resolution stays correct under async re-entrancy (a Timer event firing during a MsgBox
    // await must not clobber the suspended activation's module). currentEnv and errorMode are already per-activation
    // for the same reason.
    private readonly ModuleInfo currentModule;

    // Active `With` targets for THIS activation (innermost on top). Per-instance — a new StatementExecutor is
    // created per procedure call, so a callee starts with an empty With scope (VB6: a `With` does not extend into
    // a called procedure; a leading `.X` in the callee is Error 91) and re-entrancy can't cross-contaminate.
    private readonly Stack<Vb6Value> withTargets = new();

    // Active On Error mode for this procedure body (and its nested blocks, which share this executor).
    private ErrorMode errorMode = ErrorMode.None;
    private string? handlerLabel;   // target when errorMode == GoToLabel

    // Object-holding slots this activation OWNS (its own `Dim` locals, appended in declaration order), released by
    // RunProcedure at scope-exit for reference counting (Phase 4.2). Non-null only for a PROCEDURE-body executor —
    // module-init and class-field-init executors pass null, so their `Dim`s (module globals / instance fields) are
    // never scope-exit-released. See BasicInterpreter.RunProcedure.
    private readonly List<int>? ownedSlots;

    // Per-activation statement-temporary frame stack (Phase 4.2b) — like currentEnv/withTargets, owned by this
    // activation (NOT the interpreter), so overlapping awaits from re-entrant event dispatch can't pop each
    // other's frames. Each statement pushes/pops (LIFO) a frame holding its call-result / `New` object temps.
    private readonly Stack<List<Vb6Value>> stmtFrames;

    // The procedure this activation is running, for the debugger's Locals header ("Module.Procedure"). Null for a
    // module-init / class-field-init executor (no enclosing proc) — those show just the module name.
    private readonly string? procName;

    private ExpressionExecutor expressionExecutor => new ExpressionExecutor(interpreter, currentEnv, currentModule, withTargets, stmtFrames);

    public StatementExecutor(BasicInterpreter interpreter,
        ExecutionEnvironment currentEnv,
        ModuleInfo currentModule,
        List<int>? ownedSlots = null,
        Stack<List<Vb6Value>>? stmtFrames = null,
        string? procName = null)
    {
        this.interpreter = interpreter;
        this.currentEnv = currentEnv;
        this.currentModule = currentModule;
        this.ownedSlots = ownedSlots;
        this.stmtFrames = stmtFrames ?? new Stack<List<Vb6Value>>();
        this.procName = procName;
    }

    /// <summary>The debugger's view of THIS activation's Locals (see <see cref="Debugging.DebugInspector"/>) —
    /// called by the controller only while this frame is the one paused at a break.</summary>
    Debugging.DebugScope Debugging.IDebugFrame.GetLocals()
        => Debugging.DebugInspector.Build(currentEnv, currentModule, interpreter, procName);

    /// <summary>Handle an Immediate-window line against THIS paused frame. A leading <c>?</c>/<c>Print</c>/
    /// <c>Debug.Print</c> forces EXPRESSION evaluation (print the result). A BARE assignment or <c>Set</c> is
    /// EXECUTED against the frame (P7c) — `count = 5` assigns, mirroring VB6 (whereas `?count = 5` compares) — and
    /// mutates the paused state. Any other bare input falls back to being evaluated + printed. User Sub/Function
    /// calls are still rejected in break mode (they would deadlock the paused gate — D14–D15), so a bare user call,
    /// or a user call in an assignment's right-hand side, returns the rejection message and mutates nothing. Returns
    /// the formatted result / empty (for a statement) / a VB6-style error message.</summary>
    async Task<string> Debugging.IDebugFrame.EvaluateAsync(string input)
    {
        string raw = (input ?? string.Empty).TrimStart();
        // ?/Print/Debug.Print → EXPRESSION context; bare input → STATEMENT context (VB6 semantics).
        bool forcedExpression = raw.StartsWith("?", StringComparison.Ordinal)
            || raw.StartsWith("Print ", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("Debug.Print ", StringComparison.OrdinalIgnoreCase);
        string expr = StripImmediatePrefix(input ?? string.Empty);
        if (string.IsNullOrWhiteSpace(expr))
            return string.Empty;

        interpreter.SuppressUserProcedureCalls = true;   // reject user Sub/Function calls in break mode (D14–D15)
        interpreter.SuppressImplicitDeclaration = true;  // an out-of-scope name is unevaluable, not Empty
        try
        {
            // Bare assignment / Set → EXECUTE against the paused frame (mutates currentEnv). A user call in the RHS
            // hits the wall and is rejected (nothing mutated). Everything else bare falls through to expression eval,
            // so `count` still prints its value and `Foo` (a user Sub) is rejected there.
            if (!forcedExpression && interpreter.TryParseStatement(expr) is { } stmt
                && ((Antlr4.Runtime.ParserRuleContext?)stmt.letStmt() ?? stmt.setStmt()) is { } assign)
            {
                // Bracket with its own statement-temp frame like the statement loop does (the pause-gate runs BEFORE
                // that push, so the paused statement has no temp frame open here to clobber).
                stmtFrames.Push(new List<Vb6Value>());
                try { await Visit(assign); }
                finally { await interpreter.FlushFrame(stmtFrames.Pop()); }
                return string.Empty;
            }

            var tree = interpreter.ParseValueStmt(expr);
            // A FRESH statement-temp frame (omit stmtFrames — it defaults to a new stack), so an eval temp is never
            // adopted into / terminated by the PAUSED statement's frame.
            var evaluator = new ExpressionExecutor(interpreter, currentEnv, currentModule, withTargets);
            var result = await evaluator.EvaluateValue(tree);
            return FormatEvalResult(result);
        }
        catch (Debugging.ImmediateEvalException ex) { return ex.Message; }        // user-call / New rejection
        catch (VBRunTimeException ex) { return FlattenError(ex.Message); }        // VB6 runtime error (e.g. Error 91)
        catch (VBCompileErrorException) { return "Syntax error"; }               // parse failure
        catch (Exception ex) { return FlattenError(ex.Message); }               // defensive (e.g. unsupported expr)
        finally
        {
            interpreter.SuppressUserProcedureCalls = false;
            interpreter.SuppressImplicitDeclaration = false;
        }
    }

    /// <summary>Typed Watch evaluation against THIS frame — same read-only rules as <see cref="EvaluateAsync"/>, but
    /// returns display / type / VB6 truthiness / an expandable node (for the Watches tree), or <c>Ok = false</c> with
    /// a VB6-style error message.</summary>
    async Task<Debugging.DebugEvalResult> Debugging.IDebugFrame.EvaluateTypedAsync(string expression)
    {
        string expr = StripImmediatePrefix(expression);
        if (string.IsNullOrWhiteSpace(expr))
            return EvalErr(expression, string.Empty);

        interpreter.SuppressUserProcedureCalls = true;
        interpreter.SuppressImplicitDeclaration = true;
        try
        {
            var tree = interpreter.ParseValueStmt(expr);
            var evaluator = new ExpressionExecutor(interpreter, currentEnv, currentModule, withTargets);
            var result = await evaluator.EvaluateValue(tree);
            return new Debugging.DebugEvalResult(
                true, FormatEvalResult(result), VB6BuiltIns.DebugTypeName(result),
                Debugging.DebugInspector.IsTruthy(result),
                Debugging.DebugInspector.NodeFor(expr, result, interpreter.ExecutionContext));
        }
        catch (Debugging.ImmediateEvalException ex) { return EvalErr(expr, ex.Message); }
        catch (VBRunTimeException ex) { return EvalErr(expr, FlattenError(ex.Message)); }
        catch (VBCompileErrorException) { return EvalErr(expr, "Syntax error"); }
        catch (Exception ex) { return EvalErr(expr, FlattenError(ex.Message)); }
        finally
        {
            interpreter.SuppressUserProcedureCalls = false;
            interpreter.SuppressImplicitDeclaration = false;
        }

        // Error result: the message sits in the Value column, Ok=false so a condition tells this from a real False.
        static Debugging.DebugEvalResult EvalErr(string name, string message)
            => new(false, message, string.Empty, false, new Debugging.DebugNode(name, message, string.Empty));
    }

    // The 1-based source line this activation is currently at (set at each pause-gate) — its line in the Call Stack.
    private int currentLine;

    // Set Next Statement (P7b) — TOP-LEVEL-BODY granularity only. `_bodyStmts` is this frame's procedure body's
    // top-level statements (set by ExecuteProcedureBody); `_setNextTargetPc` is a pending pc repoint the loop applies
    // after its gate; `_suspendedAtTopLevel` is true only while paused AT the top-level gate (false at a nested
    // If/For/Do block gate), so a move is refused when paused inside a nested construct.
    private VB6Parser.BlockStmtContext[]? _bodyStmts;
    private int? _setNextTargetPc;
    private bool _suspendedAtTopLevel;

    // Names Dim'd in THIS activation. VB6 allocates a local ONCE per proc call; re-executing its Dim (e.g. a Dim
    // inside a loop) is a no-op — the variable keeps its value, it is NOT reset (oracle: Dim total in a 1..3 loop
    // that does `total = total + i` → 1,3,6, not 1,2,3). Case-insensitive, like VB6 identifiers.
    private readonly HashSet<string> declaredLocals = new(System.StringComparer.OrdinalIgnoreCase);

    // This activation's call depth, CAPTURED when it was pushed onto the activation stack (1 = outermost procedure;
    // 0 = module top-level code, which never runs through RunProcedure). Captured — NOT the live ActivationStack.Count
    // — so an unrelated re-entrant event frame (one frozen while paused, or one that starts mid-step) can't inflate
    // this frame's depth and desync Step Over/Out. Set by RunProcedure at push time (SetFrameDepth).
    private int frameDepth;

    /// <summary>Records this activation's call depth at push time. Called by <c>BasicInterpreter.RunProcedure</c>
    /// immediately after adding this executor to the activation stack.</summary>
    internal void SetFrameDepth(int depth) => frameDepth = depth;

    /// <summary>This activation's captured call depth (1 = outermost procedure; 0 = module top-level). Read by the
    /// controller at the gate for Step Over/Out. Per-frame (not the live stack size), so it is immune to overlapping
    /// re-entrant event frames on the activation stack.</summary>
    int Debugging.IDebugFrame.Depth => frameDepth;

    /// <summary>The Call Stack — this (paused) activation first, then its callers. Anchored at THIS frame's position
    /// in the activation stack, NOT the global top: a newcomer event frozen while paused is pushed ABOVE us and must
    /// not masquerade as the current frame (nor push the real paused frame down the list). Falls back to just this
    /// frame for a break in module top-level code, which isn't on the activation stack.</summary>
    IReadOnlyList<Debugging.CallStackFrame> Debugging.IDebugFrame.GetCallStack()
    {
        var stack = interpreter.ActivationStack;
        int idx = -1;
        for (int i = stack.Count - 1; i >= 0; i--)
            if (ReferenceEquals(stack[i], this)) { idx = i; break; }
        if (idx < 0)
            return new[] { ToCallStackFrame() };   // module top-level break — not on the activation stack
        var frames = new List<Debugging.CallStackFrame>(idx + 1);
        for (int i = idx; i >= 0; i--)
            frames.Add(stack[i].ToCallStackFrame());
        return frames;
    }

    // This activation as a Call Stack row: a Sub/Function uses its own name; a proc-less frame (module top-level)
    // uses the module name. Called on peer StatementExecutors from GetCallStack.
    internal Debugging.CallStackFrame ToCallStackFrame()
        => new(procName ?? currentModule.Name, currentModule.Name, currentLine);

    /// <summary>Set Next Statement (P7b) — repoint execution to <paramref name="line"/>, TOP-LEVEL-BODY granularity
    /// only. Returns false (refused) when this frame isn't paused at a top-level statement (e.g. inside a nested
    /// If/For/Do block), or the target line isn't a top-level statement of this procedure (nested, blank, End Sub, or
    /// elsewhere). On success the pending repoint is applied by <c>ExecuteProcedureBody</c>'s loop when the walk
    /// resumes; the next Step/Continue executes from there.</summary>
    bool Debugging.IDebugFrame.SetNextStatement(int line)
    {
        if (!_suspendedAtTopLevel || _bodyStmts is null)
            return false;
        for (int i = 0; i < _bodyStmts.Length; i++)
            if (_bodyStmts[i].Start.Line == line)
            {
                _setNextTargetPc = i;
                currentLine = line;   // the Call Stack + the moved arrow reflect the new next-statement line
                return true;
            }
        return false;
    }

    // Format an Immediate result the VB6 way where it's cheap: strings verbatim, True/False, numbers; Nothing/Empty/
    // Null explicitly; objects/arrays/UDTs/controls by their type name (VB6 `?obj` without a default property is an
    // error, so a type name is a sane, non-crashing approximation rather than the raw CLR type).
    private static string FormatEvalResult(Vb6Value v)
    {
        if (v.Type == Vb6Value.ValueType.Null) return "Null";
        if (v.Type == Vb6Value.ValueType.EmptyVariant) return "Empty";
        if (v.Type == Vb6Value.ValueType.Object) return v.Value is VbObject o ? o.ClassName : "Nothing";
        if (v.Value is VbUdt || v.Type.IsArray || v.Value is Avalonia.Controls.Control || v.Value is ICSharpProxy)
            return VB6BuiltIns.DebugTypeName(v);
        return VBDebugConsole.Format(v);
    }

    private static string FlattenError(string message) => message.Replace("\r", " ").Replace("\n", " ").Trim();

    // Strip an Immediate print prefix: "?" / "Print " / "Debug.Print " (case-insensitive). A bare expression is
    // evaluated as-is (so `count` and `?count` both print the value).
    private static string StripImmediatePrefix(string input)
    {
        string s = input.TrimStart();
        if (s.StartsWith("?", StringComparison.Ordinal))
            return s.Substring(1).TrimStart();
        if (s.StartsWith("Debug.Print ", StringComparison.OrdinalIgnoreCase))
            return s.Substring("Debug.Print ".Length);
        if (s.StartsWith("Print ", StringComparison.OrdinalIgnoreCase))
            return s.Substring("Print ".Length);
        return s;
    }

    public override async Task<ControlFlow> VisitBlock(VB6Parser.BlockContext context)
    {
        foreach (var stmt in context.blockStmt())
        {
            // Debugger pause-gate (before the temp-frame push, so a suspended break / reset never parks a
            // half-open frame). Null controller ⇒ skipped entirely (zero overhead headless/tests).
            currentLine = stmt.Start.Line;   // track for the Call Stack (this frame's current line)
            if (interpreter.DebugController is { } dbg)
            {
                _suspendedAtTopLevel = false;   // a pause here is inside a nested block — Set Next Statement is refused
                await dbg.OnStatementAsync(currentLine, currentModule.Name, this);
            }

            ControlFlow ret;
            try
            {
                // A statement-temporary frame brackets each statement (Phase 4.2b): function-call results and `New`
                // objects adopted during the statement are released here, so a discarded object terminates at
                // statement end. Pop before flush so a Terminate the flush triggers can't re-enter this frame. In a
                // finally so it drains on the error/Resume-Next path too.
                stmtFrames.Push(new List<Vb6Value>());
                try
                {
                    ret = await Visit(stmt);
                }
                finally
                {
                    await interpreter.FlushFrame(stmtFrames.Pop());
                }
            }
            catch (VBRunTimeException ex) when (errorMode == ErrorMode.ResumeNext)
            {
                // On Error Resume Next: a runtime error unwinds the C# stack to the innermost enclosing block;
                // record it in the global Err and continue with the next statement. Only VBRunTimeException is
                // trapped — engine-limit NotImplementedExceptions propagate as real gaps. This per-block trap
                // gives correct nested-block and cross-procedure semantics for free.
                interpreter.Err.Capture(ex);
                continue;
            }

            if (ret != ControlFlow.Nothing)
                return ret;
        }

        return ControlFlow.Nothing;
    }

    public override async Task<ControlFlow> VisitAppActivateStmt(VB6Parser.AppActivateStmtContext context)
    {
        throw new NotImplementedException("AppActivate not implemented");
    }

    public override async Task<ControlFlow> VisitAttributeStmt(VB6Parser.AttributeStmtContext context)
    {
        throw new NotImplementedException("Attribute not implemented");
    }

    public override async Task<ControlFlow> VisitBeepStmt(VB6Parser.BeepStmtContext context)
    {
        // VB6 sounds the system bell. This used to be a flat no-op justified as a cross-platform limit, which
        // measurement did not support: `Console.Beep()` is attributed UnsupportedOSPlatform for android, browser,
        // ios and tvos only — every desktop platform HexIDE ships to is fine. (It is the frequency/duration
        // OVERLOAD that is Windows-only. VB6's Beep takes no arguments, so we never need it.)
        //
        // The platform guard is FORWARD-LOOKING, not currently required, and that is worth stating precisely
        // because the obvious guess is wrong. Measured both ways: CA1416 does NOT fire for this call in a plain
        // net10.0 library, which is what HexIDE.Runtime targets today; it fires only when the project itself
        // targets browser, where it is an ERROR under TreatWarningsAsErrors. So the guard costs nothing now and
        // is what keeps this compiling if the Browser aspiration ever multi-targets the runtime. It also spares
        // WASM an exception per call. The analyzer recognises this guard form; the bare call it does not.
        //
        // Audible on Windows (Win32 Beep, 800 Hz for 200 ms). On Linux and macOS this resolves to the terminal
        // bell, so it sounds from a terminal launch and is silent from a desktop launcher. Silent is an acceptable
        // outcome here; it withholds a sound rather than misleading anyone, which is why this is safe to degrade
        // where a file-writing statement would not be.
        //
        // It BLOCKS for that 200 ms on the interpreter's thread, which is faithful — VB6 blocks too — but means
        // `For i = 1 To 100: Beep: Next` stops the world for twenty seconds. Recorded so that is recognised as
        // fidelity rather than filed as a hang.
        try
        {
            if (OperatingSystem.IsWindows() || OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
                Console.Beep();
        }
        catch (Exception)
        {
            // Fail-safe, deliberately broad: a missing audio device, a locked-down container or a redirected
            // console must not take a user's program down over a bell. Every failure here degrades to silence,
            // which is what a muted speaker gives anyway — and VB6 raises no error for an inaudible Beep either.
        }
        return ControlFlow.Nothing;
    }

    public override async Task<ControlFlow> VisitChDirStmt(VB6Parser.ChDirStmtContext context)
    {
        // VB6 coerces the arg to a String path (a number → its text form), then changes directory; any failure
        // (bad/empty/nonexistent path, incl. a coerced number that isn't a real path) is Path Not Found (76) —
        // oracle-verified: valid→ok, bad→76, `ChDir 5`→76, `ChDir ""`→76. (VB6 changes the current dir without
        // changing the default DRIVE — .NET CurrentDirectory changes both; a minor, documented divergence.)
        var dir = await expressionExecutor.EvaluateValue(context.valueStmt());
        var path = dir.Value?.ToString() ?? "";
        try { Environment.CurrentDirectory = path; }
        catch (Exception ex) when (ex is System.IO.DirectoryNotFoundException or System.IO.FileNotFoundException or ArgumentException or System.IO.IOException)
        { throw new VBRunTimeException(context, VBStandardError.PathNotFound); }
        return ControlFlow.Nothing;
    }

    public override async Task<ControlFlow> VisitChDriveStmt(VB6Parser.ChDriveStmtContext context)
    {
        // VB6 ChDrive uses the FIRST character of the (string-coerced) arg as the drive letter. Oracle-verified:
        // `ChDrive ""`→no-op, `ChDrive "C"`→ok, a non-drive-letter first char (e.g. `ChDrive 5`→'5')→Invalid
        // Procedure Call (5), a valid letter whose drive is unavailable (`ChDrive "Q"`)→Device Unavailable (68).
        // (The old code stored the bare arg as a relative path — always broke.)
        var drive = await expressionExecutor.EvaluateValue(context.valueStmt());
        var s = drive.Value?.ToString() ?? "";
        if (s.Length == 0)
            return ControlFlow.Nothing;                         // no-op, not an error
        var c = char.ToUpperInvariant(s[0]);
        if (c < 'A' || c > 'Z')
            throw new VBRunTimeException(context, VBStandardError.InvalidProcedureCall);   // Error 5
        try { Environment.CurrentDirectory = c + ":\\"; }
        catch (Exception ex) when (ex is System.IO.DirectoryNotFoundException or System.IO.FileNotFoundException or ArgumentException or System.IO.IOException)
        { throw new VBRunTimeException(context, VBStandardError.DeviceUnavailable); }      // Error 68
        return ControlFlow.Nothing;
    }

    public override async Task<ControlFlow> VisitCloseStmt(VB6Parser.CloseStmtContext context)
    {
        throw new NotImplementedException("Close not implemented");
    }

    public override async Task<ControlFlow> VisitConstStmt(VB6Parser.ConstStmtContext context)
    {
        // A Const is just a slot — the value model has no read-only concept, so reassignment isn't caught
        // (VB6 rejects it at compile time; deferred). Module-level const names are hoisted by PrePass so a Sub
        // declared after the Const can see them; here we evaluate the initializer and fill the slot.
        foreach (var sub in context.constSubStmt())
        {
            if (sub.typeHint() != null)
                throw new NotImplementedException("Const type hints not supported");
            var name = sub.ambiguousIdentifier().GetText();
            var value = await expressionExecutor.EvaluateValue(sub.valueStmt());
            if (!interpreter.ExecutionContext.TryUpdateVariable(currentEnv, name, value))
                interpreter.ExecutionContext.AllocVariable(currentEnv, name, value);
        }
        return ControlFlow.Nothing;
    }

    public override async Task<ControlFlow> VisitDateStmt(VB6Parser.DateStmtContext context)
    {
        throw new NotImplementedException("Date not implemented");
    }

    public override async Task<ControlFlow> VisitDeleteSettingStmt(VB6Parser.DeleteSettingStmtContext context)
    {
        throw new NotImplementedException("DeleteSetting not implemented");
    }

    public override async Task<ControlFlow> VisitDeftypeStmt(VB6Parser.DeftypeStmtContext context)
    {
        throw new NotImplementedException("Deftype not implemented");
    }

    public override async Task<ControlFlow> VisitDoBlockLoop(VB6Parser.DoBlockLoopContext context)
    {
        var block = context.block();   // optional in the grammar — null for an empty body (VB6: Do/Loop with no body)
        while (true)
        {
            if (block == null)
                continue;
            var result = await Visit(block);
            if (result == ControlFlow.ExitDo)
                return ControlFlow.Nothing;
            if (result == ControlFlow.ContinueDo)
                continue;
            if (result != ControlFlow.Nothing)
                return result;
        }
    }

    public override async Task<ControlFlow> VisitDoBlockWhileLoop(VB6Parser.DoBlockWhileLoopContext context)
    {
        var until = context.UNTIL() != null;
        var block = context.block();   // optional in the grammar — null for an empty body
        while (true)
        {
            if (block != null)
            {
                var result = await Visit(block);
                if (result == ControlFlow.ExitDo)
                    return ControlFlow.Nothing;
                if (result == ControlFlow.ContinueDo)
                    continue;
                if (result != ControlFlow.Nothing)
                    return result;
            }

            var condition = await expressionExecutor.EvaluateValue(context.valueStmt());
            bool conditionMet;
            if (TryUnpack(condition, out bool b))
                conditionMet = b;
            else if (TryUnpack(condition, out int i))
                conditionMet = i != 0;
            else if (condition.IsNull)
                conditionMet = false;
            else
                throw new VBRunTimeException(context, VBStandardError.TypeMismatch);

            if (until && conditionMet)
                return ControlFlow.Nothing;

            if (!until && !conditionMet)
                return ControlFlow.Nothing;
        }
    }

    public override async Task<ControlFlow> VisitDoWhileBlockLoop(VB6Parser.DoWhileBlockLoopContext context)
    {
        var until = context.UNTIL() != null;
        while (true)
        {
            var condition = await expressionExecutor.EvaluateValue(context.valueStmt());
            bool conditionMet;
            if (TryUnpack(condition, out bool b))
                conditionMet = b;
            else if (TryUnpack(condition, out int i))
                conditionMet = i != 0;
            else if (condition.IsNull)
                conditionMet = false;
            else
                throw new VBRunTimeException(context, VBStandardError.TypeMismatch);

            if (until && conditionMet)
                return ControlFlow.Nothing;

            if (!until && !conditionMet)
                return ControlFlow.Nothing;

            var block = context.block();   // optional in the grammar — null for an empty body
            if (block == null)
                continue;
            var result = await Visit(block);
            if (result == ControlFlow.ExitDo)
                return ControlFlow.Nothing;
            if (result == ControlFlow.ContinueDo)
                continue;
            if (result != ControlFlow.Nothing)
                return result;

        }
    }

    public override async Task<ControlFlow> VisitEndStmt(VB6Parser.EndStmtContext context)
    {
        throw new NotImplementedException("End not implemented");
    }

    public override async Task<ControlFlow> VisitEraseStmt(VB6Parser.EraseStmtContext context)
    {
        // `Erase a[, b, ...]` — a DYNAMIC array is freed (undimensioned: later LBound/UBound/index → Err 9), a FIXED
        // array keeps its bounds with every element reset to default. Both mutate the array in place (oracle-verified).
        foreach (var vs in context.valueStmt())
        {
            var name = vs.GetText();
            if (!interpreter.ExecutionContext.TryGetVariable(currentEnv, name, out var v))
                throw new VBRunTimeException(context, VBStandardError.ObjectRequired, "Variable " + name + " is not declared");
            if (!v.Type.IsArray || v.Value is not VBArray arr)
                throw new VBCompileErrorException("Array required in Erase");
            if (arr.IsDynamic)
                arr.Free();
            else
                arr.ResetElements();
        }
        return ControlFlow.Nothing;
    }

    public override async Task<ControlFlow> VisitErrorStmt(VB6Parser.ErrorStmtContext context)
    {
        // Legacy `Error n` statement — equivalent to Err.Raise n.
        var number = await expressionExecutor.EvaluateValue(context.valueStmt());
        if (!Vb6Value.TryNumericToDouble(number, out var d))
            throw new VBRunTimeException(context, VBStandardError.TypeMismatch);
        interpreter.Err.Raise((long)d);
        return ControlFlow.Nothing;   // unreachable — Raise throws
    }

    public override async Task<ControlFlow> VisitContinueStmt(VB6Parser.ContinueStmtContext context)
    {
        if (context.CONTINUE_DO() != null)
            return ControlFlow.ContinueDo;
        throw new NotImplementedException("Unexpected " + context);
    }

    public override async Task<ControlFlow> VisitExitStmt(VB6Parser.ExitStmtContext context)
    {
        if (context.EXIT_DO() != null)
            return ControlFlow.ExitDo;
        if (context.EXIT_FOR() != null)
            return ControlFlow.ExitFor;
        if (context.EXIT_FUNCTION() != null)
            return ControlFlow.ExitFunction;
        if (context.EXIT_PROPERTY() != null)
            return ControlFlow.ExitProperty;
        if (context.EXIT_SUB() != null)
            return ControlFlow.ExitSub;
        throw new NotImplementedException("Unexpected " + context);
    }

    private void ThrowIfTypeHint(VB6Parser.TypeHintContext? typeHintContext)
    {
        if (typeHintContext != null)
            throw new NotImplementedException("Type hints not supported");
    }

    public override async Task<ControlFlow> VisitECS_ProcedureCall(VB6Parser.ECS_ProcedureCallContext context)
    {
        var subName = context.ambiguousIdentifier().GetText();
        ThrowIfTypeHint(context.typeHint());
        var callArgs = await expressionExecutor.ResolveCallArgs(context.argsCall());
        await interpreter.CallProcedure(subName, callArgs, currentModule, callerEnv: currentEnv, callerFrames: stmtFrames);
        return ControlFlow.Nothing;
    }

    public override async Task<ControlFlow> VisitECS_MemberProcedureCall(VB6Parser.ECS_MemberProcedureCallContext context)
    {
        // The `Call`-keyword form of a member call: `Call Module1.Foo(args)` / `Call obj.Method(args)`.
        if (await TryQualifiedStatementCall(context.implicitCallStmt_InStmt(), context.ambiguousIdentifier().GetText(), context.argsCall()))
            return ControlFlow.Nothing;

        var value = context.implicitCallStmt_InStmt() is { } baseExpr
            ? await expressionExecutor.EvaluateValue(baseExpr)
            : throw new VBRunTimeException(context, VBStandardError.ObjectVariableOrWithBlockVariableNotSet);
        var identifier = context.ambiguousIdentifier().GetText();

        // A user class-instance method call (`Call obj.Method(args)`) — dispatch into the interpreter on the instance
        // (Me bound, args by-ref-capable), exactly as the bare-call path (VisitICS_B_MemberProcedureCall) does. Without
        // this, `Call obj.Method(args)` threw "Unknown method" while `obj.Method args` worked (bug-hunt MED).
        if (value.Type == Vb6Value.ValueType.Object)
        {
            if (value.Value is not VbObject vobj)
                throw new VBRunTimeException(context, VBStandardError.ObjectVariableOrWithBlockVariableNotSet);
            if (VbInterface.ResolveMember(value, vobj, identifier) is not { } callName
                || !vobj.ClassDef.PrePass.Procedures.TryGetValue(callName, out var method))
                throw new VBRunTimeException(context, VBStandardError.MethodOrDataMemberNotFound, identifier);
            var objCallArgs = await expressionExecutor.ResolveCallArgs(context.argsCall());
            await interpreter.RunProcedure(vobj.ClassDef, method, objCallArgs, vobj.InstanceEnv, value, stmtFrames);
            return ControlFlow.Nothing;
        }

        var callArgs = await expressionExecutor.EvaluateCallArgs(context.argsCall());

        if (value.Type == Vb6Value.ValueType.CSharpProxyObject)
            ((ICSharpProxy)value.Value!).Call(identifier, callArgs);
        else if (value.Type == Vb6Value.ValueType.Control)
            ((Control)value.Value!).Call(identifier, callArgs);
        else
            throw new VBRunTimeException(context, $"Unknown method {identifier} on {value}");

        return ControlFlow.Nothing;
    }

    public override async Task<ControlFlow> VisitExplicitCallStmt(VB6Parser.ExplicitCallStmtContext context) => await base.VisitExplicitCallStmt(context);

    public override async Task<ControlFlow> VisitFilecopyStmt(VB6Parser.FilecopyStmtContext context)
    {
        throw new NotImplementedException("Filecopy not implemented");
    }

    public override async Task<ControlFlow> VisitForEachStmt(VB6Parser.ForEachStmtContext context)
    {
        if (context.typeHint() != null)
            throw new NotImplementedException("For Each type hints not supported");

        var varName = context.ambiguousIdentifier(0).GetText();
        var collection = await expressionExecutor.EvaluateValue(context.valueStmt());

        // Arrays only for now — Collections are blocked on the object model (the same visitor gets a
        // Collection branch later). A non-array collection is a clear TypeMismatch, not an NRE.
        if (collection.Value is not VBArray array)
            throw new VBRunTimeException(context, VBStandardError.TypeMismatch,
                "For Each requires an array — Collections are not yet implemented");

        if (!interpreter.ExecutionContext.TryGetVariable(currentEnv, varName, out _))
            interpreter.ExecutionContext.AllocVariable(currentEnv, varName, Vb6Value.Variant);

        var block = context.block();
        foreach (var element in array.EnumerateElements())
        {
            // VB6 binds each element as a read-only Variant copy (Vb6Value is a value type, so this is a copy).
            interpreter.ExecutionContext.TryUpdateVariable(currentEnv, varName, element);

            if (block == null)
                continue;
            var result = await Visit(block);
            if (result == ControlFlow.ExitFor)
                return ControlFlow.Nothing;
            if (result != ControlFlow.Nothing)
                return result;
        }
        return ControlFlow.Nothing;
    }

    public override async Task<ControlFlow> VisitForNextStmt(VB6Parser.ForNextStmtContext context)
    {
        var variable = context.iCS_S_VariableOrProcedureCall().GetText();
        if (context.typeHint().Length != 0)
            throw new NotImplementedException("TypeHints not implemented");
        if (context.asTypeClause() != null)
            throw new NotImplementedException("asTypeClause not implemented");
        var from = await expressionExecutor.EvaluateValue(context.valueStmt(0));
        var to = await expressionExecutor.EvaluateValue(context.valueStmt(1));
        var step = context.valueStmt(2) is { } stepStmt ?
            await expressionExecutor.EvaluateValue(stepStmt)
            : new Vb6Value(1);

        // Read the bounds through the catch-all numeric rung (double) so a Long limit like `To 50000` is accepted —
        // the old TryUnpack<int> rejected ANY Long, throwing a spurious type-mismatch on a perfectly valid loop.
        // Integral counters only for now: a fractional Step (`Step 0.5`) is a documented gap, not a supported case.
        if (!expressionExecutor.TryUnpack(from, out double fromD) ||
            !expressionExecutor.TryUnpack(to, out double toD) ||
            !expressionExecutor.TryUnpack(step, out double stepD))
            throw new VBRunTimeException(context, VBStandardError.TypeMismatch);
        if (fromD != Math.Truncate(fromD) || toD != Math.Truncate(toD) || stepD != Math.Truncate(stepD))
            throw new VBRunTimeException(context, VBStandardError.TypeMismatch,
                "fractional For counters/steps are not yet supported");

        long fromL = (long)fromD, toL = (long)toD, stepL = (long)stepD;

        if (!interpreter.ExecutionContext.TryGetVariable(currentEnv, variable, out _))
            interpreter.ExecutionContext.AllocVariable(currentEnv, variable, Vb6Value.Variant);

        // VB6 runs the body while (step >= 0 ? counter <= limit : counter >= limit) — a `<=`/`>=` test, NOT `i == to`
        // (equality never terminates when the step doesn't land exactly on `to`, e.g. `0 To 10 Step 3` → 0,3,6,9,…
        // forever). The counter is left one step PAST the limit — the value that failed the test — matching the
        // oracle (0 To 10 Step 3 → 12; empty 1 To 3 → 4; 1 To 50000 → 50001|Long). An empty body still counts.
        var block = context.block();
        long i = fromL;
        for (; stepL >= 0 ? i <= toL : i >= toL; i += stepL)
        {
            interpreter.ExecutionContext.TryUpdateVariable(currentEnv, variable, ForCounterValue(i));
            if (block == null)
                continue;
            var ret = await Visit(block);
            if (ret == ControlFlow.ExitFor)
                return ControlFlow.Nothing;
            if (ret != ControlFlow.Nothing)
                return ret;
        }
        interpreter.ExecutionContext.TryUpdateVariable(currentEnv, variable, ForCounterValue(i));
        return default;
    }

    // A For counter value with VB6-faithful typing: an integer magnitude picks Integer vs Long via Vb6Value's
    // magnitude rule (oracle: 12 → Integer, 50001 → Long); a value beyond Long range is a Double, matching VB6's
    // promotion of an out-of-Long-range loop to a Double counter.
    private static Vb6Value ForCounterValue(long v)
        => v is >= int.MinValue and <= int.MaxValue ? new Vb6Value((int)v) : new Vb6Value((double)v);

    public override async Task<ControlFlow> VisitGetStmt(VB6Parser.GetStmtContext context)
    {
        throw new NotImplementedException("Get not implemented");
    }

    public override async Task<ControlFlow> VisitGoSubStmt(VB6Parser.GoSubStmtContext context)
    {
        throw new NotImplementedException("GoSub not implemented");
    }

    public override async Task<ControlFlow> VisitGoToStmt(VB6Parser.GoToStmtContext context)
    {
        // A control-signal exception so it unwinds arbitrary nesting up to the procedure-body pc-driver, which
        // repositions to the target label. (Labels are top-level statements of the body.)
        throw new GoToSignal(context.valueStmt().GetText(), context);
    }

    public override async Task<ControlFlow> VisitBlockIfThenElse(VB6Parser.BlockIfThenElseContext context)
    {
        var val = await expressionExecutor.EvaluateValue(context.ifBlockStmt().ifConditionStmt().valueStmt());
        if (val.Type != Vb6Value.ValueType.Boolean)
            throw new VBRunTimeException(context, "IF doesn't contain a bool expression");
        if (val.Value is true)
            return await Visit(context.ifBlockStmt().block());
        else
        {
            bool matched = false;
            foreach (var elseIf in context.ifElseIfBlockStmt())
            {
                val = await expressionExecutor.EvaluateValue(elseIf.ifConditionStmt().valueStmt());
                if (val.Type != Vb6Value.ValueType.Boolean)
                    throw new VBRunTimeException(context, "IF doesn't contain a bool expression");
                if (val.Value is true)
                {
                    return await Visit(elseIf.block());
                }
            }

            if (!matched)
            {
                if (context.ifElseBlockStmt() != null)
                {
                    return await Visit(context.ifElseBlockStmt().block());
                }
            }
        }

        return default;
    }

    public override async Task<ControlFlow> VisitInlineIfThenElse(VB6Parser.InlineIfThenElseContext context)
    {
        var condition = await expressionExecutor.EvaluateValue(context.ifConditionStmt());
        if (!TryUnpack(condition, out bool conditionMet))
            throw new VBRunTimeException(context, VBStandardError.TypeMismatch);

        // A branch is a colon-joined RUN of statements, not one statement. Measured: `If False Then A : B`
        // runs neither — the whole tail belongs to the Then. Running only the first and letting the rest
        // fall through would silently execute code the program said not to.
        var branch = conditionMet ? context.inlineIfBody(0) : context.inlineIfBody(1);
        return branch is null ? ControlFlow.Nothing : await RunInlineBranch(branch);
    }

    /// <summary>Run every statement of one single-line-If branch, stopping the moment control leaves it —
    /// an `Exit For` or a `GoTo` inside a joined tail must not let the statements after it run.</summary>
    private async Task<ControlFlow> RunInlineBranch(VB6Parser.InlineIfBodyContext body)
    {
        foreach (var stmt in body.blockStmt())
        {
            var flow = await Visit(stmt);
            if (flow != ControlFlow.Nothing)
                return flow;
        }
        return ControlFlow.Nothing;
    }

    public override Task<ControlFlow> VisitImplementsStmt(VB6Parser.ImplementsStmtContext context)
        // A declaration, not an executable statement: the PrePass recorded the claim, and the conformance
        // check runs at first instantiation. Reaching it again while re-running a class's top-level blocks
        // per instance must therefore do nothing at all.
        => Task.FromResult(ControlFlow.Nothing);

    public override async Task<ControlFlow> VisitInputStmt(VB6Parser.InputStmtContext context)
    {
        throw new NotImplementedException("Input not implemented");
    }

    public override async Task<ControlFlow> VisitKillStmt(VB6Parser.KillStmtContext context)
    {
        throw new NotImplementedException("Kill not implemented");
    }

    public override async Task<ControlFlow> VisitLetStmt(VB6Parser.LetStmtContext context)
    {
        var value = await expressionExecutor.EvaluateValue(context.valueStmt());

        if (context.PLUS_EQ() != null || context.MINUS_EQ() != null)
            throw new NotImplementedException("+-/-= not implemented in variable assignment");

        // Objects are reference types: a plain `x = obj` (Let, not Set) is an error — object assignment requires
        // Set. (Default-property assignment is deferred; the exact Err number is oracle-pending.)
        if (value.Type == Vb6Value.ValueType.Object)
            throw new VBRunTimeException(context, VBStandardError.ObjectDoesntSupportThisPropertyOrMethod);

        if (context.implicitCallStmt_InStmt().iCS_S_VariableOrProcedureCall() is { } varOrProcCall)
        {
            var identifier = varOrProcCall.GetText() ?? throw new VBRunTimeException(context, VBStandardError.ObjectRequired, "Null variable name");
            // A UDT is a value type: `b = a` stores an independent deep copy so mutating b never touches a.
            if (!interpreter.ExecutionContext.TryGetVariable(currentEnv, identifier, out var oldLet))
            {
                // VB6 creates an undeclared variable on first use — a procedure-local Variant, Empty, fresh
                // per call (all measured). "Require Variable Declaration" is OFF by default in VB6, so a
                // module without Option Explicit is the ordinary case rather than a legacy one, and this
                // used to raise Err 424 on it. (#171)
                if (currentModule.PrePass.RequireVariableDefinitions)
                    throw new VBVariableNotDefinedException(identifier);
                interpreter.ExecutionContext.AllocVariable(currentEnv, identifier, Vb6Value.Variant);
                interpreter.ExecutionContext.TryGetVariable(currentEnv, identifier, out oldLet);
            }
            interpreter.ExecutionContext.TryUpdateVariable(currentEnv, identifier, BasicInterpreter.CopyIfValueType(value));
            // Refcount (Phase 4.2): a Variant slot that held an object and is now overwritten by a scalar drops
            // that reference — release it (no-op unless the old value was an object). The new value is non-object
            // (guarded above), so no AddRef is needed.
            await interpreter.ReleaseRef(oldLet);
            return ControlFlow.Nothing;
        }
        else if (context.implicitCallStmt_InStmt().iCS_S_MembersCall() is { } membersCall)
        {
            if (membersCall.dictionaryCallStmt() != null)
                throw new NotImplementedException("dict not supported yet");

            // A leading dot (`.Member = value`) has no leading part — resolve against the innermost With target.
            Vb6Value variable;
            string identifier;
            if (membersCall.iCS_S_VariableOrProcedureCall() is { } leadPart)
            {
                identifier = leadPart.GetText();
                if (!interpreter.ExecutionContext.TryGetVariable(currentEnv, identifier, out variable))
                    throw new VBRunTimeException(context, VBStandardError.ObjectRequired, "Can't find variable " + identifier);
            }
            else if (membersCall.iCS_S_ProcedureOrArrayCall() is { } leadProcOrArray)
            {
                // `Command1(i).Caption = x` — the lead is an indexed call (a control-array element); resolve it to
                // the element control, then set the member on it below (Err 340 if the element doesn't exist).
                identifier = leadProcOrArray.ambiguousIdentifier()?.GetText() ?? "<indexed>";
                variable = (Vb6Value)(await expressionExecutor.EvaluateProcedureOrArrayCall(leadProcOrArray))!;
            }
            else
            {
                identifier = "<With>";
                variable = WithTargetOrError(context);
            }

            // UDT field assignment (`e.City = x`, nested `e.Address.City = x`) — navigate to the innermost owned
            // bag and set the field in place (the slot holds the same VbUdt reference, so the write persists).
            // Lifts the single-member restriction for UDT field chains (object chains stay single-dot).
            if (variable.Value is VbUdt)
            {
                SetUdtField(variable, membersCall.iCS_S_MemberCall(), BasicInterpreter.CopyIfValueType(value), context);
                return default;
            }

            // A class instance member value-assign (`obj.Member = x`, no `Set`). A Property Let accessor wins
            // over a raw field write (the value coerces to the Let parameter's declared type inside the call);
            // otherwise write into the instance's own env slot (a UDT value is copied so the object owns it).
            // Nothing → Error 91.
            if (variable.Type == Vb6Value.ValueType.Object)
            {
                if (variable.Value is not VbObject vobj)
                    throw new VBRunTimeException(context, VBStandardError.ObjectVariableOrWithBlockVariableNotSet);
                if (membersCall.iCS_S_MemberCall().Length != 1)
                    throw new NotImplementedException("Object member chains are single-dot only");
                if (MemberHasArgs(membersCall.iCS_S_MemberCall()[0]))
                    throw new NotImplementedException("Parameterized/indexed object member assignment is not supported");
                var fieldName = VbInterface.ResolveMember(variable, vobj, UdtFieldName(membersCall.iCS_S_MemberCall()[0]))
                    ?? throw new VBRunTimeException(context, VBStandardError.MethodOrDataMemberNotFound,
                        UdtFieldName(membersCall.iCS_S_MemberCall()[0]));
                if (vobj.ClassDef.PrePass.Properties.TryGetValue(fieldName, out var prop) && prop.Let is { } letter)
                {
                    await interpreter.RunProcedure(vobj.ClassDef, letter, [new CallArg(value, null)], vobj.InstanceEnv, variable, stmtFrames);
                    return default;
                }
                if (!interpreter.ExecutionContext.TryGetVariable(vobj.InstanceEnv, fieldName, out var oldFieldLet))
                    throw new VBRunTimeException(context, VBStandardError.MethodOrDataMemberNotFound, fieldName);
                interpreter.ExecutionContext.TryUpdateVariable(vobj.InstanceEnv, fieldName, BasicInterpreter.CopyIfValueType(value));
                // A field that held an object, now overwritten by a scalar via Let, drops that reference.
                await interpreter.ReleaseRef(oldFieldLet);
                return default;
            }

            if (membersCall.iCS_S_MemberCall().Length != 1)
                throw new NotImplementedException("Only single member call (single dot) is supported as of now");

            var memberIdentifier = membersCall.iCS_S_MemberCall()[0].GetText().TrimStart('.') ?? throw new VBRunTimeException(context, VBStandardError.ObjectRequired, "Null member name");

            // A CSharp property bag (e.g. Err.Number = …) handles its own get/set by name.
            if (variable.Value is ICSharpPropertyBag propertyBag && propertyBag.TrySetProperty(memberIdentifier, value))
                return default;

            if (variable.Type != Vb6Value.ValueType.Control ||
                variable.Value is not Control control)
                throw new VBRunTimeException(context, VBStandardError.MethodOrDataMemberNotFound, $"Variable {identifier} type {variable.Type} doesn't have member {memberIdentifier}");

            var props = VBProperties.PropertiesByName.GetValueOrDefault(memberIdentifier, []);

            foreach (var prop in props)
            {
                if (AvaloniaInteroperability.TrySet(control, prop, value))
                    return default;
            }

            throw new VBRunTimeException(context, VBStandardError.MethodOrDataMemberNotFound, $"Variable {identifier} type {variable.Type} doesn't have member {memberIdentifier}");
        }
        if (context.implicitCallStmt_InStmt().iCS_S_ProcedureOrArrayCall() is { } procedureOrArrayCall)
        {
            if (procedureOrArrayCall.baseType() != null||
                procedureOrArrayCall.iCS_S_NestedProcedureCall() != null ||
                procedureOrArrayCall.typeHint() != null ||
                procedureOrArrayCall.dictionaryCallStmt() != null)
                throw new NotImplementedException();

            if (procedureOrArrayCall.argsCall().Length != 1)
                throw new NotImplementedException();

            var identifier = procedureOrArrayCall.ambiguousIdentifier().GetText() ?? throw new VBRunTimeException(context, VBStandardError.ObjectRequired, "Null variable name");

            // Mid STATEMENT: `Mid(target, start[, length]) = replacement`. It routes HERE (not VisitMidStmt) because
            // the grammar's letStmt swallows the trailing `= value`, so `Mid(...)` looks like an array-element write.
            // Mid is reserved, so it can't be a user array name — an identifier of "Mid" is unambiguously the statement.
            if (string.Equals(identifier, "Mid", StringComparison.OrdinalIgnoreCase))
            {
                var midArgs = procedureOrArrayCall.argsCall(0).argCall();
                if (midArgs.Length < 2)
                    throw new VBRunTimeException(context, VBStandardError.ArgumentNotOptionalOrInvalidPropertyAssignment);
                var targetName = midArgs[0].valueStmt().GetText();
                if (!interpreter.ExecutionContext.TryGetVariable(currentEnv, targetName, out var midTarget))
                    throw new VBRunTimeException(context, VBStandardError.ObjectRequired, "Variable " + targetName + " is not declared");
                var startVal = await expressionExecutor.EvaluateValue(midArgs[1].valueStmt());
                Vb6Value? lenVal = midArgs.Length >= 3 ? (Vb6Value?)await expressionExecutor.EvaluateValue(midArgs[2].valueStmt()) : null;
                interpreter.ExecutionContext.TryUpdateVariable(currentEnv, targetName,
                    VB6BuiltIns.MidStatementReplace(midTarget, startVal, lenVal, value));
                return default;
            }

            var indexes = await expressionExecutor.EvaluateCallArgs(procedureOrArrayCall.argsCall(0));
            var indexesAsInt = AsType<int>(indexes);

            if (!interpreter.ExecutionContext.TryGetVariable(currentEnv, identifier, out var array))
                throw new VBRunTimeException(context, VBStandardError.ObjectRequired, "Variable " + identifier + " is not declared");

            if (!array.Type.IsArray || array.Value is not VBArray arr)
                throw new VBCompileErrorException("Expected array");

            try
            {
                arr.SetValue(indexesAsInt, value);
            }
            catch (IndexOutOfRangeException)
            {
                throw new VBRunTimeException(procedureOrArrayCall, VBStandardError.SubscriptOutOfRange);
            }

            return ControlFlow.Nothing;
        }
        else
        {
            throw new NotImplementedException($"{context.implicitCallStmt_InStmt()} is not supported");
        }
    }

    public override async Task<ControlFlow> VisitLineInputStmt(VB6Parser.LineInputStmtContext context)
    {
        throw new NotImplementedException("LineInput not implemented");
    }

    public override async Task<ControlFlow> VisitLineLabel(VB6Parser.LineLabelContext context)
    {
        // A label is just a jump target; the pc-driver maps its position. Executing it is a no-op.
        return ControlFlow.Nothing;
    }

    public override async Task<ControlFlow> VisitLoadStmt(VB6Parser.LoadStmtContext context)
    {
        // Only control-array element loading is modelled (Load Command1(i)); a bare form Load isn't.
        var (group, index) = await ResolveControlArrayTarget(context.valueStmt(), "Load");
        group.Load(index);   // Err 360 if the element already exists
        return ControlFlow.Nothing;
    }

    // Resolve `Load/Unload Command1(i)` to its control-array group + index WITHOUT evaluating the element itself
    // (evaluating Command1(i) would throw Err 340 when the element doesn't exist yet — the normal Load case).
    private async Task<(ControlArrayGroup group, int index)> ResolveControlArrayTarget(
        VB6Parser.ValueStmtContext valueStmt, string verb)
    {
        var poac = FindProcedureOrArrayCall(valueStmt);
        if (poac?.ambiguousIdentifier() == null || poac.argsCall().Length != 1)
            throw new NotImplementedException($"{verb} is only supported for control array elements (e.g. {verb} Command1(i))");
        var name = poac.ambiguousIdentifier().GetText();
        if (!interpreter.ExecutionContext.TryGetVariable(currentEnv, name, out var target) || target.Value is not ControlArrayGroup group)
            throw new NotImplementedException($"{verb} is only supported for control arrays; '{name}' is not one");
        var idxArgs = await expressionExecutor.EvaluateCallArgs(poac.argsCall(0));
        return (group, AsType<int>(idxArgs[0]));
    }

    // First iCS_S_ProcedureOrArrayCall in pre-order (the outer Command1(i), not a nested call inside the index).
    private static VB6Parser.ICS_S_ProcedureOrArrayCallContext? FindProcedureOrArrayCall(IParseTree node)
    {
        if (node is VB6Parser.ICS_S_ProcedureOrArrayCallContext poac)
            return poac;
        for (int i = 0; i < node.ChildCount; i++)
            if (FindProcedureOrArrayCall(node.GetChild(i)) is { } found)
                return found;
        return null;
    }

    public override async Task<ControlFlow> VisitLockStmt(VB6Parser.LockStmtContext context)
    {
        throw new NotImplementedException("Lock not implemented");
    }

    public override async Task<ControlFlow> VisitLsetStmt(VB6Parser.LsetStmtContext context)
    {
        throw new NotImplementedException("Lset not implemented");
    }

    public override async Task<ControlFlow> VisitMacroIfThenElseStmt(VB6Parser.MacroIfThenElseStmtContext context)
    {
        throw new NotImplementedException("MacroIfThenElse not implemented");
    }

    public override async Task<ControlFlow> VisitMidStmt(VB6Parser.MidStmtContext context)
    {
        throw new NotImplementedException("Mid not implemented");
    }

    public override async Task<ControlFlow> VisitMkdirStmt(VB6Parser.MkdirStmtContext context)
    {
        throw new NotImplementedException("Mkdir not implemented");
    }

    public override async Task<ControlFlow> VisitNameStmt(VB6Parser.NameStmtContext context)
    {
        throw new NotImplementedException("Name not implemented");
    }

    public override async Task<ControlFlow> VisitOnErrorStmt(VB6Parser.OnErrorStmtContext context)
    {
        if (context.RESUME() != null)
        {
            errorMode = ErrorMode.ResumeNext;   // On Error Resume Next
            handlerLabel = null;
        }
        else
        {
            // On Error GoTo <target>. `GoTo 0` disables handling; a real label installs a handler.
            var target = context.valueStmt().GetText();
            if (target == "0")
            {
                errorMode = ErrorMode.None;
                handlerLabel = null;
            }
            else
            {
                errorMode = ErrorMode.GoToLabel;
                handlerLabel = target;
            }
        }
        return ControlFlow.Nothing;
    }

    public override async Task<ControlFlow> VisitOnGoToStmt(VB6Parser.OnGoToStmtContext context)
    {
        throw new NotImplementedException("OnGoTo not implemented");
    }

    public override async Task<ControlFlow> VisitOnGoSubStmt(VB6Parser.OnGoSubStmtContext context)
    {
        throw new NotImplementedException("OnGoSub not implemented");
    }

    public override async Task<ControlFlow> VisitOpenStmt(VB6Parser.OpenStmtContext context)
    {
        throw new NotImplementedException("Open not implemented");
    }

    public override async Task<ControlFlow> VisitPrintStmt(VB6Parser.PrintStmtContext context)
    {
        throw new NotImplementedException("Print not implemented");
    }

    public override async Task<ControlFlow> VisitPutStmt(VB6Parser.PutStmtContext context)
    {
        throw new NotImplementedException("Put not implemented");
    }

    public override async Task<ControlFlow> VisitRaiseEventStmt(VB6Parser.RaiseEventStmtContext context)
    {
        var eventName = context.ambiguousIdentifier().GetText();
        // RaiseEvent is only valid inside the event source's own class, so Me is the source instance. No Me / not
        // an object → silent no-op.
        if (!interpreter.ExecutionContext.TryGetVariable(currentEnv, "Me", out var meVal)
            || meVal.Value is not VbObject source)
            return ControlFlow.Nothing;

        // Evaluate the args ONCE, with Locations, so a ByRef param aliases the raiser's local and the SAME args
        // flow to every sink — a later handler sees an earlier one's write-back (multicast shares ByRef args).
        // Evaluated even when no sink is bound: VB6 evaluates RaiseEvent arg expressions regardless (oracle-
        // verified — a side-effecting arg still runs), then the no-sink case is a dispatch no-op.
        var args = context.argsCall() is { } ac ? await expressionExecutor.ResolveCallArgs(ac) : new List<CallArg>();
        if (source.Sinks.Count == 0)
            return ControlFlow.Nothing;

        // Dispatch synchronously, in ATTACH order, to {varName}_{eventName} on each bound listener instance.
        // Snapshot first — a handler may attach/detach mid-dispatch. Skip a Terminated listener or a missing
        // handler (an event with no matching handler is a silent no-op).
        foreach (var sink in source.Sinks.ToList())
        {
            if (sink.Listener.Value is not VbObject listener || listener.Terminated)
                continue;
            if (listener.ClassDef.PrePass.Procedures.TryGetValue(sink.VarName + "_" + eventName, out var handler))
                await interpreter.RunProcedure(listener.ClassDef, handler, args, listener.InstanceEnv, sink.Listener, stmtFrames);
        }
        return ControlFlow.Nothing;
    }

    public override async Task<ControlFlow> VisitRandomizeStmt(VB6Parser.RandomizeStmtContext context)
    {
        double? seed = null;
        if (context.valueStmt() is { } vs)
        {
            var v = await expressionExecutor.EvaluateValue(vs);
            seed = Vb6Value.TryNumericToDouble(v, out var d) ? d : 0;
        }
        interpreter.BuiltIns.Reseed(seed);
        return ControlFlow.Nothing;
    }

    public override async Task<ControlFlow> VisitResetStmt(VB6Parser.ResetStmtContext context)
    {
        throw new NotImplementedException("Reset not implemented");
    }

    public override async Task<ControlFlow> VisitResumeStmt(VB6Parser.ResumeStmtContext context)
    {
        // Resume / Resume Next / Resume <label> — a control signal the pc-driver turns into a pc reposition.
        if (context.NEXT() != null)
            throw new ResumeSignal(ResumeKind.Next, null, context);
        if (context.ambiguousIdentifier() is { } label)
            throw new ResumeSignal(ResumeKind.Label, label.GetText(), context);
        // A NUMERIC label target (`Resume 10`) is the same thing spelled differently. Without this the
        // statement still parses and falls through to ResumeKind.Same — silently retrying the faulting
        // statement instead of jumping, which is a wrong answer with no error, the failure mode #191 was
        // about. Bare `Resume` has no target at all, hence the token check rather than an else.
        if (context.INTEGERLITERAL() is { } lineNo)
            throw new ResumeSignal(ResumeKind.Label, lineNo.GetText(), context);
        throw new ResumeSignal(ResumeKind.Same, null, context);
    }

    public override async Task<ControlFlow> VisitReturnStmt(VB6Parser.ReturnStmtContext context)
    {
        throw new NotImplementedException("Return not implemented");
    }

    public override async Task<ControlFlow> VisitRmdirStmt(VB6Parser.RmdirStmtContext context)
    {
        throw new NotImplementedException("Rmdir not implemented");
    }

    public override async Task<ControlFlow> VisitRsetStmt(VB6Parser.RsetStmtContext context)
    {
        throw new NotImplementedException("Rset not implemented");
    }

    public override async Task<ControlFlow> VisitSavepictureStmt(VB6Parser.SavepictureStmtContext context)
    {
        throw new NotImplementedException("Savepicture not implemented");
    }

    public override async Task<ControlFlow> VisitSaveSettingStmt(VB6Parser.SaveSettingStmtContext context)
    {
        throw new NotImplementedException("SaveSetting not implemented");
    }

    public override async Task<ControlFlow> VisitSeekStmt(VB6Parser.SeekStmtContext context)
    {
        throw new NotImplementedException("Seek not implemented");
    }

    public override async Task<ControlFlow> VisitSelectCaseStmt(VB6Parser.SelectCaseStmtContext context)
    {
        var value = await expressionExecutor.EvaluateValue(context.valueStmt());
        foreach (var @case in context.sC_Case())
        {
            var cond = @case.sC_Cond();
            if (cond is VB6Parser.CaseCondElseContext)
            {
                return await RunCaseBody(@case);
            }
            else if (cond is VB6Parser.CaseCondExprContext condExpr)
            {
                foreach (var subCond in condExpr.sC_CondExpr())
                {
                    if (subCond is VB6Parser.CaseCondExprIsContext caseIs)
                    {
                        var val = await expressionExecutor.EvaluateValue(caseIs.valueStmt());
                        if (caseIs.comparisonOperator().LT() != null)
                        {
                            if (value.TryCompareTo(val) is < 0)
                                return await RunCaseBody(@case);
                        }
                        else if (caseIs.comparisonOperator().LEQ() != null)
                        {
                            if (value.TryCompareTo(val) is <= 0)
                                return await RunCaseBody(@case);
                        }
                        else if (caseIs.comparisonOperator().GT() != null)
                        {
                            if (value.TryCompareTo(val) is > 0)
                                return await RunCaseBody(@case);
                        }
                        else if (caseIs.comparisonOperator().GEQ() != null)
                        {
                            if (value.TryCompareTo(val) is >= 0)
                                return await RunCaseBody(@case);
                        }
                        else if (caseIs.comparisonOperator().EQ() != null)
                        {
                            if (CaseMatches(value, val, caseIs))
                                return await RunCaseBody(@case);
                        }
                        else if (caseIs.comparisonOperator().NEQ() != null)
                        {
                            if (!CaseMatches(value, val, caseIs))
                                return await RunCaseBody(@case);
                        }
                        else if (caseIs.comparisonOperator().IS() != null)
                        {
                            throw new NotImplementedException("Operator " + caseIs.comparisonOperator().GetText() + " not impleemented");
                        }
                        else if (caseIs.comparisonOperator().LIKE() != null)
                        {
                            throw new NotImplementedException("Operator " + caseIs.comparisonOperator().GetText() + " not impleemented");
                        }
                        else
                            throw new NotImplementedException("Operator " + caseIs.comparisonOperator().GetText() + " not impleemented");
                    }
                    else if (subCond is VB6Parser.CaseCondExprValueContext caseCondExpr)
                    {
                        var val = await expressionExecutor.EvaluateValue(caseCondExpr.valueStmt());
                        if (CaseMatches(value, val, caseCondExpr))
                            return await RunCaseBody(@case);
                    }
                    else if (subCond is VB6Parser.CaseCondExprToContext caseTo)
                    {
                        var from = await expressionExecutor.EvaluateValue(caseTo.valueStmt(0));
                        var to = await expressionExecutor.EvaluateValue(caseTo.valueStmt(1));
                        if (value.TryCompareTo(from) is >= 0 && value.TryCompareTo(to) is <= 0)
                        {
                            return await RunCaseBody(@case);
                        }
                    }
                    else
                    {
                        throw new NotImplementedException($"Unepexected Select-Case");
                    }
                }
            }
            else
                throw new NotImplementedException($"Unepexected Select-Case");
        }

        return ControlFlow.Nothing;
    }

    /// <summary>Run a case arm's body, which the grammar makes OPTIONAL.
    ///
    /// <para>
    /// `Case 1` with nothing underneath is legal VB6: the arm matches, does nothing, and — measured — does
    /// NOT fall through to a later arm or to <c>Case Else</c>. So <c>block()</c> is null there and visiting
    /// it throws. This was unreachable while the comparison below never matched anything; widening the
    /// comparison is what exposes it, which is why the two changes belong in one commit.
    /// </para></summary>
    private async Task<ControlFlow> RunCaseBody(VB6Parser.SC_CaseContext @case)
        => @case.block() is { } body ? await Visit(body) : ControlFlow.Nothing;

    /// <summary>Does a case expression match the selector?
    ///
    /// <para>
    /// VB6 coerces the CASE VALUE to the SELECTOR's runtime type and then compares — it does not compare
    /// numerically, and it is not symmetric. Both halves are measured (see <i>Select Case matching</i> in
    /// docs/vb6-fidelity-oracle.md): a <c>String</c> selector holding <c>"1.0"</c> does NOT match
    /// <c>Case 1</c>, because <c>CStr(1)</c> is <c>"1"</c>; a <c>Long</c> selector holding 2 DOES match
    /// <c>Case 1.7</c>, because <c>CLng(1.7)</c> is 2. Comparing numerically would get both backwards.
    /// </para>
    ///
    /// <para>
    /// That rule is <see cref="VbNumeric.CoerceOnStore"/> exactly — VB6's coercion-on-store and its
    /// Select Case comparison are one rule — so this reuses it rather than restating it, and inherits its
    /// already-pinned edges: a failed coercion RAISES rather than failing to match (<c>Case 40000</c>
    /// against an <c>Integer</c> selector is Err 6, <c>Case "abc"</c> against a <c>Long</c> one is Err 13),
    /// while coercion toward a <c>String</c> selector cannot fail and so never raises.
    /// </para>
    ///
    /// <para>
    /// NB the <c>=</c> operator is a DIFFERENT rule: <c>"1.0" = 1</c> is True in VB6 while the
    /// corresponding Select Case is not. Sharing a comparison helper between the two would be wrong, and
    /// was the obvious thing to do.
    /// </para></summary>
    private static bool CaseMatches(Vb6Value selector, Vb6Value caseValue, ParserRuleContext ctx)
    {
        // Empty has not decided what it is yet, so it takes its partner's zero — the same rule
        // GetTwoValuesSameTypesOrNull applies for `=`. Measured: an unassigned Variant matches `Case 0`.
        if (selector.Type == Vb6Value.ValueType.EmptyVariant)
            return VbNumeric.IsDeclarableScalar(caseValue.Type)
                && new Vb6Value(caseValue.Type).Equals(caseValue);

        // Object, UDT, Null, arrays: nothing to coerce toward, so compare as before rather than invent a
        // rule for a case the oracle has not been asked about.
        if (!VbNumeric.IsDeclarableScalar(selector.Type))
            return selector.Equals(caseValue);

        return VbNumeric.CoerceOnStore(caseValue, selector.Type, ctx).Equals(selector);
    }

    public override async Task<ControlFlow> VisitSendkeysStmt(VB6Parser.SendkeysStmtContext context)
    {
        throw new NotImplementedException("Sendkeys not implemented");
    }

    public override async Task<ControlFlow> VisitSetattrStmt(VB6Parser.SetattrStmtContext context)
    {
        throw new NotImplementedException("Setattr not implemented");
    }

    public override async Task<ControlFlow> VisitSetStmt(VB6Parser.SetStmtContext context)
    {
        var value = await expressionExecutor.EvaluateValue(context.valueStmt());
        // Set requires an object (a class instance, control, or proxy) or Nothing on the RHS. (Object type
        // covers both a live instance and Nothing, whose Value is null.)
        if (value.Type != Vb6Value.ValueType.Object && value.Type != Vb6Value.ValueType.Control
            && value.Type != Vb6Value.ValueType.CSharpProxyObject)
            throw new VBRunTimeException(context, VBStandardError.ObjectRequired);

        // Store the REFERENCE (never a copy — objects are reference types, so `Set b = a` shares).
        if (context.implicitCallStmt_InStmt().iCS_S_VariableOrProcedureCall() is { } varCall)
        {
            var id = varCall.GetText();
            if (!interpreter.ExecutionContext.TryGetVariable(currentEnv, id, out var oldVal))
                throw new VBRunTimeException(context, VBStandardError.ObjectRequired, "Variable " + id + " is not declared");
            // Phase-5 event-sink bind detection: `id` is a `WithEvents` field of the current class AND resolves to
            // the actual instance field slot — NOT a procedure-local of the same name shadowing it (the shadowing
            // Dim re-allocs a distinct slot, so the locations differ). `Me` is the listener instance (WithEvents is
            // class-only). Precomputed so the unadvise can run before the release.
            Vb6Value evtMe = default;
            VbObject? evtListener = null;
            if (currentModule.PrePass.WithEventsNames.Contains(id)
                && interpreter.ExecutionContext.TryGetVariable(currentEnv, "Me", out evtMe)
                && evtMe.Value is VbObject lst
                && currentEnv.TryGetVariableLocation(id, out var curLoc)
                && lst.InstanceEnv.TryGetVariableLocation(id, out var fieldLoc)
                && curLoc == fieldLoc)
                evtListener = lst;

            // Refcount (Phase 4.2): AddRef the new referent BEFORE releasing the slot's old one — this ordering
            // protects `Set x = x` / aliased assignment, and (since a `New` RHS already ran its Initialize during
            // eval) makes the new object's Initialize precede the old object's Terminate.
            interpreter.AddRef(value);
            // Unadvise the OLD source's event connection BEFORE releasing it (VB6 unadvise-then-release): a
            // Class_Terminate triggered by the release must not still see the sink this Set is removing (else a
            // RaiseEvent during the old source's teardown would reach the just-detached listener).
            if (evtListener != null && oldVal.Value is VbObject oldSrc)
                oldSrc.RemoveSink(evtListener, id);
            // Assign the new reference to the slot BEFORE releasing the old one (oracle-verified): a Class_Terminate
            // fired by the release observes the NEW value — `g` points to the new object, not the dying old one — and
            // a reassignment of the slot inside that Terminate is not clobbered by a late slot-write.
            interpreter.ExecutionContext.TryUpdateVariable(currentEnv, id, value);
            // Advise the new source (RaiseEvent on it now reaches this listener).
            if (evtListener != null && value.Value is VbObject newSrc)
                newSrc.AddSink(new EventSink(evtMe, id));
            await interpreter.ReleaseRef(oldVal);
            return ControlFlow.Nothing;
        }

        // `Set obj.Member = New Foo` — an object-assign to another instance's member. A Property Set accessor
        // wins over a raw object-field write; otherwise store the reference in the field's slot.
        // NB `Set .Member = o` against a With target can't reach here: `Set .x` parses as a member-Let on a
        // variable named "Set" (VB6 keyword-as-identifier ambiguity resolves to letStmt), so leading-dot Set is
        // a grammar-level wall — use `Set obj.Member = o` with an explicit reference. (Property Let via With
        // works — no Set keyword to alias.)
        if (context.implicitCallStmt_InStmt().iCS_S_MembersCall() is { } membersCall
            && membersCall.iCS_S_VariableOrProcedureCall() is { } leadPart)
        {
            if (!interpreter.ExecutionContext.TryGetVariable(currentEnv, leadPart.GetText(), out var target)
                || target.Value is not VbObject targetObj)
                throw new VBRunTimeException(context, VBStandardError.ObjectVariableOrWithBlockVariableNotSet);
            if (membersCall.iCS_S_MemberCall().Length != 1)
                throw new NotImplementedException("Object member chains are single-dot only");
            if (MemberHasArgs(membersCall.iCS_S_MemberCall()[0]))
                throw new NotImplementedException("Parameterized/indexed object member assignment is not supported");
            var fieldName = VbInterface.ResolveMember(target, targetObj, UdtFieldName(membersCall.iCS_S_MemberCall()[0]))
                ?? throw new VBRunTimeException(context, VBStandardError.MethodOrDataMemberNotFound,
                    UdtFieldName(membersCall.iCS_S_MemberCall()[0]));
            if (targetObj.ClassDef.PrePass.Properties.TryGetValue(fieldName, out var prop) && prop.Set is { } setter)
            {
                // The reference flows through the setter's ByVal param (counted on bind, released at its scope-exit)
                // and its inner `Set mField = o` (counted there) — no direct AddRef/Release here would double-count.
                await interpreter.RunProcedure(targetObj.ClassDef, setter, [new CallArg(value, null)], targetObj.InstanceEnv, target, stmtFrames);
                return ControlFlow.Nothing;
            }
            // Raw object-field write: refcount the field slot (AddRef new before Release old, as for a plain var).
            if (!interpreter.ExecutionContext.TryGetVariable(targetObj.InstanceEnv, fieldName, out var oldField))
                throw new VBRunTimeException(context, VBStandardError.MethodOrDataMemberNotFound, fieldName);
            interpreter.AddRef(value);
            interpreter.ExecutionContext.TryUpdateVariable(targetObj.InstanceEnv, fieldName, value);   // slot first (see above)
            await interpreter.ReleaseRef(oldField);
            return ControlFlow.Nothing;
        }

        // `Set arr(i) = obj` — object-assign into an ARRAY ELEMENT (an object array, e.g. a fleet of Ship). Mirror
        // the Let-path array locate; AddRef the new referent and Release the element's old occupant — the AddRef is
        // load-bearing: a ship held ONLY by the array would otherwise drop to RefCount 0 (and Terminate) the moment
        // the local that created it goes out of scope. (Array elements are not scope-released, so their objects
        // leak at program end — a documented divergence, like module globals; VB6 would terminate them.)
        if (context.implicitCallStmt_InStmt().iCS_S_ProcedureOrArrayCall() is { } arrCall
            && arrCall.baseType() == null && arrCall.iCS_S_NestedProcedureCall() == null
            && arrCall.typeHint() == null && arrCall.dictionaryCallStmt() == null
            && arrCall.argsCall().Length == 1)
        {
            var identifier = arrCall.ambiguousIdentifier().GetText()
                ?? throw new VBRunTimeException(context, VBStandardError.ObjectRequired, "Null array name");
            var indexes = AsType<int>(await expressionExecutor.EvaluateCallArgs(arrCall.argsCall(0)));
            if (!interpreter.ExecutionContext.TryGetVariable(currentEnv, identifier, out var arrayVal))
                throw new VBRunTimeException(context, VBStandardError.ObjectRequired, "Variable " + identifier + " is not declared");
            if (!arrayVal.Type.IsArray || arrayVal.Value is not VBArray arr)
                throw new VBCompileErrorException("Expected array");
            try
            {
                var oldElem = arr.GetValue(indexes);   // release the element's previous occupant (refcount)
                interpreter.AddRef(value);
                arr.SetValue(indexes, value);          // slot first, then release the old occupant (see above)
                await interpreter.ReleaseRef(oldElem);
            }
            catch (IndexOutOfRangeException)
            {
                throw new VBRunTimeException(arrCall, VBStandardError.SubscriptOutOfRange);
            }
            return ControlFlow.Nothing;
        }

        throw new NotImplementedException("Set to this target is not yet supported");
    }

    public override async Task<ControlFlow> VisitStopStmt(VB6Parser.StopStmtContext context)
    {
        // VB6 `Stop`: in the IDE debugger it enters Break mode (like a breakpoint on this line); in a compiled exe
        // (here: no controller / headless) it ends the program. Either way, no longer a NotImplementedException.
        currentLine = context.Start.Line;
        if (interpreter.DebugController is { } dbg)
            await dbg.EnterBreakFromStopStatementAsync(currentLine, currentModule.Name, this);
        else
            throw new Debugging.StopExecutionSignal();
        return ControlFlow.Nothing;
    }

    public override async Task<ControlFlow> VisitTimeStmt(VB6Parser.TimeStmtContext context)
    {
        throw new NotImplementedException("Time not implemented");
    }

    public override async Task<ControlFlow> VisitUnloadStmt(VB6Parser.UnloadStmtContext context)
    {
        // Only control-array element unloading is modelled (Unload Command1(i)); a bare form Unload isn't.
        var (group, index) = await ResolveControlArrayTarget(context.valueStmt(), "Unload");
        group.Unload(index);   // Err 340 if missing, Err 362 if a design-time element
        return ControlFlow.Nothing;
    }

    public override async Task<ControlFlow> VisitUnlockStmt(VB6Parser.UnlockStmtContext context)
    {
        throw new NotImplementedException("Unlock not implemented");
    }

    public override async Task<ControlFlow> VisitRedimStmt(VB6Parser.RedimStmtContext context)
    {
        if (context.PRESERVE() != null)
            throw new NotImplementedException("PRESERVE not implemented");

        foreach (var redim in context.redimSubStmt())
        {
            if (redim.implicitCallStmt_InStmt().iCS_S_VariableOrProcedureCall() is not { } varOrProcCall)
                throw new NotImplementedException();

            if (varOrProcCall.dictionaryCallStmt() != null)
                throw new NotImplementedException();

            var variableName = varOrProcCall.ambiguousIdentifier().GetText();

            if (!interpreter.ExecutionContext.TryGetVariable(currentEnv, variableName, out var value))
                throw new VBCompileErrorException("Unknown variable " + variableName);

            List<(int, int)>? dimensions = await ExtractDimensions(redim.subscripts());
            Vb6Value.ValueType type = redim.asTypeClause() != null ? ExtractType(redim.asTypeClause(), true) : value.Type;

            if (dimensions == null)
                throw new VBCompileErrorException("Dimensions required");

            interpreter.ExecutionContext.TryUpdateVariable(currentEnv, variableName, new Vb6Value(type, dimensions));
        }

        return ControlFlow.Nothing;
    }

    public override async Task<ControlFlow> VisitVariableStmt(VB6Parser.VariableStmtContext context)
    {
        if (context.WITHEVENTS() != null)
        {
            // `[Private] WithEvents src As Clock` re-run per-instance at New (a class field): seed each slot to
            // Nothing (a class-typed object ref). The sink NAME was recorded in the class's PrePass; the sink is
            // registered when the var is Set. Not added to ownedSlots — fields aren't scope-released.
            foreach (var sub in context.variableListStmt().variableSubStmt())
                interpreter.ExecutionContext.AllocVariable(currentEnv, sub.ambiguousIdentifier().GetText(), Vb6Value.Nothing);
            return default;
        }

        // `Dim`, `Private`, and `Public` module/local declarations all allocate the same way (visibility isn't
        // tracked). Class fields are usually declared `Private`/`Public`.
        if (context.DIM() != null || context.visibility() != null)
        {
            foreach (var subStmt in context.variableListStmt().variableSubStmt())
                await DeclareLocal(subStmt);
        }
        else
            throw new NotImplementedException("non dim variables not supported");

        return default;
    }

    /// <summary>
    /// Allocate one declared variable. Called from the Dim statement itself for module-level and
    /// field-initialiser executors, and from <see cref="HoistDeclaredLocals"/> at procedure entry for a
    /// procedure body — where VB6 says the local already exists.
    ///
    /// Idempotent per activation via <c>declaredLocals</c>, which is what lets a hoisted local make the Dim
    /// statement a no-op when execution reaches it, and what already made a Dim inside a loop allocate once.
    /// </summary>
    private async Task DeclareLocal(VB6Parser.VariableSubStmtContext subStmt)
    {
        {
            {
                if (subStmt.typeHint() != null)
                    throw new NotImplementedException("DIM type hints not implemented");
                // A Dim allocates its local exactly once per activation; a re-executed Dim (inside a loop) keeps the
                // existing value. The first Dim rebinds the name to a fresh slot, so it still shadows a module var.
                if (!declaredLocals.Add(subStmt.ambiguousIdentifier().GetText()))
                    return;
                bool isArray = false;
                List<(int, int)>? dimensions = null;
                if (subStmt.LPAREN() != null && subStmt.RPAREN() != null) // array
                {
                    isArray = true;
                    dimensions = await ExtractDimensions(subStmt.subscripts());
                }

                var asType = subStmt.asTypeClause();
                Vb6Value value;
                // The class or interface name a class-typed Dim was declared with, kept because an object slot
                // has nothing to coerce and the NAME is the whole of its declared type (see ExecutionState).
                string? declaredClass = null;
                if (!isArray && asType?.type()?.complexType() is { } ct && asType.NEW() == null && asType.fieldLength() == null)
                {
                    // `Dim e As Employee` — a fresh UDT instance; `Dim x As MyEnum` — a Long; `Dim c As Clock`
                    // (a class) — Nothing (a null object reference, so `c Is Nothing` is True until Set).
                    // A type name may be QUALIFIED, and the two kinds differ — measured. `Module2.Point`
                    // names a UDT in that module and overrides any local one; `Module2.MyEnum` is not a
                    // type name at all, because an Enum's identity is project-scoped rather than
                    // module-scoped. The project name qualifies both.
                    var (qualifier, typeName) = SplitTypeName(ct.GetText());
                    if (interpreter.TryResolveType(typeName, currentModule, qualifier, out _))
                        value = Vb6Value.NewUdt(interpreter.NewUdt(typeName, qualifier: qualifier, from: currentModule));
                    else if (qualifier is null && interpreter.TryResolveEnum(typeName, currentModule, out _))
                        value = new Vb6Value(0L);
                    else if (interpreter.Modules.TryGet(typeName, out var classMod) && classMod.Kind == InterpreterModuleKind.Class)
                    {
                        value = Vb6Value.Nothing;
                        declaredClass = classMod.Name;   // the module's own casing, not the Dim's
                    }
                    else
                        throw new VBCompileErrorException("User-defined type not defined: " + typeName);
                }
                else
                {
                    var type = ExtractType(asType, isArray);
                    value = dimensions != null ? new Vb6Value(type, dimensions) : new Vb6Value(type);
                    if (dimensions != null && value.Value is VBArray fixedArr) fixedArr.IsDynamic = false;   // `Dim a(N)` = fixed
                }

                var localName = subStmt.ambiguousIdentifier().GetText();
                // Record the DECLARED type, so later assignments coerce to it instead of replacing it. Only
                // for a coercible scalar: an array, an object, a UDT and a Variant all take their value
                // wholesale, and `Dim v` / `Dim v As Variant` must stay exactly as untyped as they read.
                var declared = VbNumeric.IsDeclarableScalar(value.Type) ? value.Type : null;
                interpreter.ExecutionContext.AllocVariable(currentEnv, localName, value, declared, declaredClass);
                // Track this local so RunProcedure releases its reference (fires Class_Terminate) at scope-exit,
                // in declaration order. Every Dim slot is tracked (a Variant can later hold an object via Set);
                // ReleaseRef no-ops non-objects. Only proc-body executors have ownedSlots (module/field-init = null).
                if (ownedSlots != null && currentEnv.TryGetVariableLocation(localName, out var localLoc))
                    ownedSlots.Add(localLoc);
            }
        }
    }

    /// <summary>
    /// Allocate every local a procedure declares, before its body runs — because in VB6 a Dim is a
    /// declaration, not an executable statement. Declaration order, which is also the order scope-exit
    /// releases them in; previously that was EXECUTION order, so a Dim in an untaken branch was released
    /// never and one in a loop was ordered by when it first ran.
    /// </summary>
    internal async Task HoistDeclaredLocals(ProcedureInfo proc)
    {
        foreach (var subStmt in proc.DeclaredLocals)
            await DeclareLocal(subStmt);
    }

    private async Task<List<(int, int)>?> ExtractDimensions(VB6Parser.SubscriptsContext? subscripts)
    {
        List<(int, int)>? dimensions = null;
        if (subscripts != null)
        {
            dimensions = new List<(int, int)>();
            int arrayLowerBound;
            int arrayUpperBound;
            foreach (var dimension in subscripts.subscript())
            {
                var size = dimension.valueStmt();
                if (size.Length == 2)
                {
                    arrayLowerBound = AsType<int>(await expressionExecutor.EvaluateValue(size[0]));
                    arrayUpperBound = AsType<int>(await expressionExecutor.EvaluateValue(size[1]));
                }
                else if (size.Length == 1)
                {
                    arrayLowerBound = currentModule.PrePass.ArrayBase;   // Option Base is per-module
                    arrayUpperBound = AsType<int>(await expressionExecutor.EvaluateValue(size[0]));
                }
                else
                    throw new VBCompileErrorException("Either specify upper bound or lower and upper bound");
                dimensions.Add((arrayLowerBound, arrayUpperBound));
            }
        }

        return dimensions;
    }


    public override async Task<ControlFlow> VisitWhileWendStmt(VB6Parser.WhileWendStmtContext context)
    {
        // While…Wend is a pre-tested loop. VB6 has no `Exit While`/`Continue While`, so any non-Nothing
        // control flow (Exit Sub/Function/Property) propagates straight out. The grammar exposes `block*`.
        while (true)
        {
            var condition = await expressionExecutor.EvaluateValue(context.valueStmt());
            bool conditionMet;
            if (TryUnpack(condition, out bool b))
                conditionMet = b;
            else if (TryUnpack(condition, out int i))
                conditionMet = i != 0;
            else if (condition.IsNull)
                conditionMet = false;
            else
                throw new VBRunTimeException(context, VBStandardError.TypeMismatch);

            if (!conditionMet)
                return ControlFlow.Nothing;

            foreach (var block in context.block())
            {
                var result = await Visit(block);
                if (result != ControlFlow.Nothing)
                    return result;
            }
        }
    }

    public override async Task<ControlFlow> VisitWidthStmt(VB6Parser.WidthStmtContext context)
    {
        throw new NotImplementedException("Width not implemented");
    }

    public override async Task<ControlFlow> VisitWithStmt(VB6Parser.WithStmtContext context)
    {
        // `With New …` and `With <userObject>` need the object model — deferred. A `With` over a Control or
        // CSharpProxy target works now: push it so leading-dot members inside resolve against it, pop in finally.
        if (context.NEW() != null)
            throw new NotImplementedException("With New is not supported yet (needs the object model)");

        var target = await expressionExecutor.EvaluateValue(context.implicitCallStmt_InStmt());
        withTargets.Push(target);
        try
        {
            if (context.block() is { } block)
            {
                var result = await Visit(block);
                if (result != ControlFlow.Nothing)
                    return result;
            }
        }
        finally
        {
            withTargets.Pop();
        }
        return ControlFlow.Nothing;
    }

    // The innermost With target for a leading-dot member reference, or Error 91 when there is no active With.
    // `withTargets` is this activation's own stack, so a leading dot in a callee (which never inherits the
    // caller's With) correctly raises Error 91.
    private Vb6Value WithTargetOrError(Antlr4.Runtime.ParserRuleContext context) =>
        withTargets.Count > 0
            ? withTargets.Peek()
            : throw new VBRunTimeException(context, VBStandardError.ObjectVariableOrWithBlockVariableNotSet);

    // Navigate a UDT field-write chain (`e.City`, `e.Address.City`), mutating the innermost owned bag in place.
    private static void SetUdtField(Vb6Value root, VB6Parser.ICS_S_MemberCallContext[] members, Vb6Value value, Antlr4.Runtime.ParserRuleContext ctx)
    {
        var current = root;
        for (int i = 0; i < members.Length; i++)
        {
            if (current.Value is not VbUdt bag)
                throw new VBRunTimeException(ctx, VBStandardError.TypeMismatch, "Member access on a non-UDT value");
            var fieldName = UdtFieldName(members[i]);
            if (i == members.Length - 1)
            {
                if (!bag.TrySet(fieldName, value))
                    throw new VBRunTimeException(ctx, VBStandardError.MethodOrDataMemberNotFound, fieldName);
                return;
            }
            if (!bag.TryGet(fieldName, out current))
                throw new VBRunTimeException(ctx, VBStandardError.MethodOrDataMemberNotFound, fieldName);
        }
    }

    // The field name of a member-call segment (a UDT field access is a bare `.Field`).
    private static string UdtFieldName(VB6Parser.ICS_S_MemberCallContext m)
        => m.iCS_S_VariableOrProcedureCall()?.ambiguousIdentifier()?.GetText()
           ?? m.iCS_S_ProcedureOrArrayCall()?.ambiguousIdentifier()?.GetText()
           ?? throw new VBCompileErrorException("Malformed UDT field access");

    // True when a member carries an argument list (`.P(i)`) — a parameterized/indexed member access, a wall for
    // both scalar properties and object fields (mirrors the READ-site guard). `UdtFieldName` discards the index,
    // so without this guard `x.P(i) = v` would silently drop `(i)`. Empty parens `.P()` yield no argsCall (the
    // grammar makes argsCall optional), so this is false — a plain member access.
    private static bool MemberHasArgs(VB6Parser.ICS_S_MemberCallContext m)
        => m.iCS_S_ProcedureOrArrayCall()?.argsCall().Length > 0;

    public override async Task<ControlFlow> VisitWriteStmt(VB6Parser.WriteStmtContext context)
    {
        throw new NotImplementedException("Write not implemented");
    }

    public override async Task<ControlFlow> VisitICS_B_MemberProcedureCall(VB6Parser.ICS_B_MemberProcedureCallContext context)
    {
        // A namespace-qualified statement call (`Module1.Foo args`, `VBA.MsgBox "x"`) — base is a module/library
        // qualifier, not a value. Object member calls fall through to the value path below.
        if (await TryQualifiedStatementCall(context.implicitCallStmt_InStmt(), context.ambiguousIdentifier().GetText(), context.argsCall()))
            return ControlFlow.Nothing;

        // A leading dot (`.Method args`) has no base — resolve it against the innermost With target.
        var value = context.implicitCallStmt_InStmt() is { } baseExpr
            ? await expressionExecutor.EvaluateValue(baseExpr)
            : WithTargetOrError(context);
        var identifier = context.ambiguousIdentifier().GetText() ?? throw new VBRunTimeException(context, VBStandardError.ObjectRequired, "Empty method identifier");

        // A user class-instance method call (`obj.Method args`) — dispatch into the interpreter on the instance
        // (Me bound, args by-ref-capable). Nothing → Error 91.
        if (value.Type == Vb6Value.ValueType.Object)
        {
            if (value.Value is not VbObject vobj)
                throw new VBRunTimeException(context, VBStandardError.ObjectVariableOrWithBlockVariableNotSet);
            if (VbInterface.ResolveMember(value, vobj, identifier) is not { } callName
                || !vobj.ClassDef.PrePass.Procedures.TryGetValue(callName, out var method))
                throw new VBRunTimeException(context, VBStandardError.MethodOrDataMemberNotFound, identifier);
            var objCallArgs = await expressionExecutor.ResolveCallArgs(context.argsCall());
            await interpreter.RunProcedure(vobj.ClassDef, method, objCallArgs, vobj.InstanceEnv, value, stmtFrames);
            return ControlFlow.Nothing;
        }

        var callArgs = await expressionExecutor.EvaluateCallArgs(context.argsCall());

        if (value.Type == Vb6Value.ValueType.CSharpProxyObject)
        {
            ((ICSharpProxy)value.Value!).Call(identifier, callArgs);
        }
        else if (value.Type == Vb6Value.ValueType.Control)
        {
            ((Control)value.Value!).Call(identifier, callArgs);
        }
        else
            throw new VBRunTimeException(context, $"Unknown method {identifier} on {value}");

        return default;
    }

    // Handle a namespace-qualified statement call whose base is a module/library qualifier (`Module1.Foo args`,
    // `Call Module1.Foo(args)`, `VBA.MsgBox "x"`). Returns false when the base is not a bare qualifier (a variable
    // shadows it, or it's an object member call) so the caller resolves the base as a value instead.
    private async Task<bool> TryQualifiedStatementCall(
        VB6Parser.ImplicitCallStmt_InStmtContext? baseCtx, string memberName, VB6Parser.ArgsCallContext? argsCall)
    {
        if (baseCtx?.iCS_S_VariableOrProcedureCall()?.ambiguousIdentifier() is not { } leadId)
            return false;
        var lead = leadId.GetText();
        if (interpreter.ExecutionContext.TryGetVariable(currentEnv, lead, out _))
            return false;   // a same-named variable shadows the qualifier
        if (!interpreter.TryResolveQualifier(lead, out var qualifier))
            return false;

        if (qualifier.Kind == BasicInterpreter.QualifierKind.Library)
        {
            // A library-qualified intrinsic invoked as a statement — evaluate and discard the result. An unknown
            // member errors (mirroring the expression path), rather than silently no-op'ing; a known constant
            // used as a bare statement is a harmless no-op.
            var args = await expressionExecutor.EvaluateCallArgs(argsCall);
            if (await expressionExecutor.EvaluateFunction(memberName, args) is null
                && !(args.Count == 0 && interpreter.BuiltIns.TryGetBuiltInConstant(memberName, out _)))
                throw new VBRunTimeException((Antlr4.Runtime.ParserRuleContext?)null, VBStandardError.MethodOrDataMemberNotFound, memberName);
            return true;
        }

        // An enum member (`MyEnum.Member`) is a value, not a callable statement — not handled here.
        if (qualifier.Kind == BasicInterpreter.QualifierKind.Enum)
            return false;

        var module = qualifier.Module!;
        if (module.PrePass.Procedures.TryGetValue(memberName, out var proc)
            && (!proc.IsPrivate || ReferenceEquals(module, currentModule)))
        {
            var callArgs = await expressionExecutor.ResolveCallArgs(argsCall);
            await interpreter.RunProcedure(module, proc, callArgs, callerFrames: stmtFrames);
            return true;
        }
        throw new VBRunTimeException((Antlr4.Runtime.ParserRuleContext?)null, VBStandardError.MethodOrDataMemberNotFound,
            $"{module.Name}.{memberName}");
    }

    public override async Task<ControlFlow> VisitICS_B_ProcedureCall(VB6Parser.ICS_B_ProcedureCallContext context)
    {
        var subName = context.certainIdentifier().GetText();
        // A bare procedure statement (Foo a, b): a user Sub/Function (this module, or another module's Public)
        // takes precedence over a builtin.
        if (interpreter.TryResolveProcedure(subName, currentModule, out _, out _))
        {
            var callArgs = await expressionExecutor.ResolveCallArgs(context.argsCall());
            await interpreter.CallProcedure(subName, callArgs, currentModule, callerEnv: currentEnv, callerFrames: stmtFrames);
        }
        else
        {
            List<Vb6Value> builtinArgs = await expressionExecutor.EvaluateCallArgs(context.argsCall());
            // EvaluateFunction answers null for a name the registry does not have. Discarding that made an
            // unimplemented statement-form intrinsic a silent no-op: `Shell "notepad.exe", vbNormalFocus`
            // ran, launched nothing, and continued as though it had. (#191)
            if (await expressionExecutor.EvaluateFunction(subName, builtinArgs) is null)
                throw VbIntrinsicNames.IsIntrinsic(subName)
                    ? new NotImplementedException(subName + " is a VB6 intrinsic that HexIDE does not implement yet")
                    : new VBCompileErrorException("Sub or Function not defined (" + subName + ')');
        }
        return ControlFlow.Nothing;
    }

    public override async Task<ControlFlow> VisitImplicitCallStmt_InBlock(VB6Parser.ImplicitCallStmt_InBlockContext context) => await base.VisitImplicitCallStmt_InBlock(context);

    public async Task Execute(VB6Parser.BlockContext block)
    {
        await ExecuteProcedureBody(block);
    }

    // Runs a procedure body's TOP-LEVEL statements with an explicit program counter so On Error GoTo <label>,
    // GoTo, Resume, and labels work. Labels/handlers are always top-level statements of the body, so only the
    // top-level list is linearised; nested blocks keep going through VisitBlock (which carries the Resume Next
    // trap). GoTo/Resume are control-signal exceptions that unwind nested constructs up to here.
    //   Documented limitation: for a fault nested inside a top-level construct, faultPc is that construct's
    //   index, so `Resume Next` resumes after the whole construct — nested-granular resume needs a CFG rewrite
    //   (a real language engine's job). The Resume Next *mode* (5a, via VisitBlock) remains per-statement precise.
    /// <summary>
    /// Add this body's LINE-HEAD jump targets to the label table — numeric line numbers (`10 x = 1`) and
    /// named labels that share their line with a statement (`Skip: x = 1`).
    ///
    /// Neither can be read off the statement list the way a label standing alone on its line can, because
    /// the grammar carries them as a prefix on the block rather than as statements of their own. So
    /// <c>block.lineHead()</c> and <c>block.blockStmt()</c> are two arrays that do NOT line up: only some
    /// statements have a head.
    ///
    /// Walking the children in source order is what pairs them. A head applies to the next statement that
    /// follows it, which is what a reader assumes and what VB6 does.
    /// </summary>
    /// <summary>
    /// Split a possibly-qualified type name into (module qualifier, bare name).
    ///
    /// <para>
    /// A type name may carry the PROJECT and/or the declaring MODULE — `Project1.Module1.Point`. The
    /// project level is stripped and discarded: there is one project here, so naming it changes nothing,
    /// and refusing it would refuse legal VB6 for no gain. Whatever remains in front of the last segment
    /// is the module.
    /// </para>
    /// </summary>
    private (string? Qualifier, string Name) SplitTypeName(string text)
    {
        var parts = text.Split('.');
        if (parts.Length == 1) return (null, parts[0]);

        // Drop a leading project name if that is what it is.
        var i = 0;
        if (parts.Length > 1 && string.Equals(parts[0], interpreter.ProjectName, StringComparison.OrdinalIgnoreCase))
            i = 1;

        var name = parts[^1];
        return i < parts.Length - 1 ? (parts[^2], name) : (null, name);
    }

    private static void CollectLineHeads(VB6Parser.BlockContext block, VB6Parser.BlockStmtContext[] stmts,
        Dictionary<string, int> labels)
    {
        // Names awaiting a statement to label. A list rather than a single slot for two reasons: a head may
        // stand alone on its line, so several queue up before the next executable statement —
        //
        //     10
        //     20 Rem still nothing here
        //     30 x = 1        <- 10, 20 and 30 all name this statement
        //
        // — and one head may carry BOTH a number and a name, which is legal in that order only
        // (`10 Skip: x = 1`; `Skip: 10 x = 1` is a syntax error in VB6). Every one of them is a live GoTo
        // target landing on the same place, measured.
        var pending = new List<string>();
        var index = 0;
        for (var c = 0; c < block.ChildCount; c++)
        {
            switch (block.GetChild(c))
            {
                case VB6Parser.LineHeadContext head:
                    if (head.lineNumber() is { } number)
                        pending.Add(number.GetText());
                    if (head.lineLabel() is { } named)
                        pending.Add(named.labelName().GetText());
                    break;
                case VB6Parser.EmptyLineNumberContext eln:
                    // A number whose line carried no statement. It keeps its claim on the next one.
                    pending.Add(eln.lineNumber().GetText());
                    break;
                case VB6Parser.BlockStmtContext stmt:
                    // Defensive: only map while the walk and the statement array agree on position. They
                    // should always agree, but a label pointing at the wrong statement would be a silent
                    // mis-jump, which is worse than no label at all.
                    if (pending.Count > 0 && index < stmts.Length && ReferenceEquals(stmts[index], stmt))
                        foreach (var name in pending)
                            labels[name] = index;
                    pending.Clear();
                    index++;
                    break;
            }
        }
    }

    private async Task ExecuteProcedureBody(VB6Parser.BlockContext block)
    {
        var stmts = block.blockStmt();
        _bodyStmts = stmts;   // Set Next Statement targets this top-level list
        var labels = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < stmts.Length; i++)
            if (stmts[i].lineLabel() is { } lbl)
                labels[lbl.labelName().GetText()] = i;
        CollectLineHeads(block, stmts, labels);

        int pc = 0;
        int faultPc = -1;       // index of the statement that faulted (for Resume); -1 = no active error
        bool inHandler = false; // in an On Error GoTo handler — a further fault propagates (isn't re-trapped)

        while (pc < stmts.Length)
        {
            // Debugger pause-gate (before the temp-frame push). Sees every top-level statement; a StopExecutionSignal
            // it raises falls through all four catches below (none match) and unwinds via RunProcedure's finally.
            currentLine = stmts[pc].Start.Line;   // track for the Call Stack
            if (interpreter.DebugController is { } dbg)
            {
                _suspendedAtTopLevel = true;   // a pause here is at a top-level statement — Set Next Statement is valid
                await dbg.OnStatementAsync(currentLine, currentModule.Name, this);
            }
            // Set Next Statement (P7b): apply a pc repoint requested while paused at this top-level gate, so the
            // resumed walk executes from the target instead of the paused statement.
            if (_setNextTargetPc is { } snt) { pc = snt; _setNextTargetPc = null; currentLine = stmts[pc].Start.Line; }

            try
            {
                // Per-statement temporary frame (Phase 4.2b) — finally-bracketed so it drains on the normal path,
                // on Exit, and when a GoTo/Resume/error signal unwinds out of the statement.
                stmtFrames.Push(new List<Vb6Value>());
                try
                {
                    if (await Visit(stmts[pc]) != ControlFlow.Nothing)
                        return;   // Exit Sub/Function/Property — leave the body
                    pc++;
                }
                finally
                {
                    await interpreter.FlushFrame(stmtFrames.Pop());
                }
            }
            catch (GoToSignal g)
            {
                pc = ResolveLabel(labels, g.Label);
            }
            catch (ResumeSignal r)
            {
                if (faultPc < 0)
                    throw new VBRunTimeException(r.Context, VBStandardError.ResumeWithoutError);   // Error 20
                inHandler = false;
                pc = r.Kind switch
                {
                    ResumeKind.Same => faultPc,        // retry the faulting statement
                    ResumeKind.Next => faultPc + 1,    // continue after it
                    _ => ResolveLabel(labels, r.Label!),
                };
                faultPc = -1;
            }
            catch (VBRunTimeException ex) when (errorMode == ErrorMode.GoToLabel && !inHandler)
            {
                interpreter.Err.Capture(ex);
                faultPc = pc;
                inHandler = true;
                pc = ResolveLabel(labels, handlerLabel!);
            }
            catch (VBRunTimeException ex) when (errorMode == ErrorMode.ResumeNext)
            {
                interpreter.Err.Capture(ex);
                faultPc = pc;
                pc++;
            }
        }
    }

    private static int ResolveLabel(Dictionary<string, int> labels, string label) =>
        labels.TryGetValue(label, out var idx) ? idx : throw new VBCompileErrorException("Label not defined: " + label);

    // Internal control signals (not VB runtime errors — never trapped by On Error).
    private sealed class GoToSignal(string label, Antlr4.Runtime.ParserRuleContext context) : Exception
    {
        public string Label { get; } = label;
        public Antlr4.Runtime.ParserRuleContext Context { get; } = context;
    }

    private enum ResumeKind { Same, Next, Label }

    private sealed class ResumeSignal(ResumeKind kind, string? label, Antlr4.Runtime.ParserRuleContext context) : Exception
    {
        public ResumeKind Kind { get; } = kind;
        public string? Label { get; } = label;
        public Antlr4.Runtime.ParserRuleContext Context { get; } = context;
    }

    public override async Task<ControlFlow> VisitChildren(IRuleNode node)
    {
        ControlFlow result = default;
        int childCount = node.ChildCount;
        for (int i = 0; i < childCount; ++i) // && this.ShouldVisitNextChild(node, result)
        {
            ControlFlow nextResult = await node.GetChild(i).Accept<Task<ControlFlow>>((IParseTreeVisitor<Task<ControlFlow>>) this);
            result = nextResult; //this.AggregateResult(result, Task.FromResult(nextResult));
        }
        return result;
    }
}