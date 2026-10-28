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

    // Each For statement's limit and step as this activation last evaluated them: VB6's loop state, kept per call and zero
    // at its start. Only a For whose header raised under Resume Next ever reads them (see RunUninitialisedFor).
    private readonly Dictionary<VB6Parser.ForNextStmtContext, (double Limit, double Step)> forState = new();

    // Active On Error mode for this procedure body (and its nested blocks, which share this executor).
    private ErrorMode errorMode = ErrorMode.None;
    private string? handlerLabel;   // target when errorMode == GoToLabel
    // In an On Error GoTo handler, where a further fault propagates rather than being trapped again. A field, not a local
    // of the body's loop, because a halt asks every caller's activation whether it would trap (#1011).
    private bool inHandler;
    // The source line this activation is at, for Erl (#2528): the statement running, or the condition, Case or Next being
    // run on a line of its own. Erl is the line number at or above it in this body, from erlNumbers, kept by source line.
    private int erlLine;
    private (int Line, long Number)[] erlNumbers = [];

    /// <summary>Whether the program was entered at this activation: an event's procedure, a RaiseEvent sink or Sub Main.
    /// A handler further down the stack never sees an error raised above an entry (measured, #1011).</summary>
    internal bool IsEntry { get; init; }

    // Whether a run-time error raised in this activation now would be trapped by it. Nothing is while its handler runs,
    // whatever On Error the handler has said since: the error goes to the caller (measured, #877).
    private bool Traps => errorMode != ErrorMode.None && !inHandler;

    // Whether an error raised now continues with the next statement: On Error Resume Next, and no handler running.
    private bool ResumesNext => errorMode == ErrorMode.ResumeNext && !inHandler;

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
    private readonly Vb6Value.ValueType? checkedArrayReturn;

    /// <summary>
    /// Where this activation's <c>Static</c> locals keep their slots between calls: its instance's, or its module's for
    /// the run (#1491). Null for a module-init or class-field-init executor, where <c>Static</c> does not compile.
    /// </summary>
    internal StaticLocals? Statics { get; init; }

    /// <summary>
    /// Whether this activation is a Function or Property Get whose return is a Variant's, untyped or <c>As Variant</c>,
    /// so its name takes a Let as a Variant does. Its slot carries no Variant mark (#1493).
    /// </summary>
    internal bool VariantReturn { get; init; }

    private ExpressionExecutor expressionExecutor => new ExpressionExecutor(interpreter, currentEnv, currentModule, withTargets, stmtFrames);

    public StatementExecutor(BasicInterpreter interpreter,
        ExecutionEnvironment currentEnv,
        ModuleInfo currentModule,
        List<int>? ownedSlots = null,
        Stack<List<Vb6Value>>? stmtFrames = null,
        string? procName = null,
        Vb6Value.ValueType? checkedArrayReturn = null)
    {
        this.interpreter = interpreter;
        this.currentEnv = currentEnv;
        this.currentModule = currentModule;
        this.ownedSlots = ownedSlots;
        this.stmtFrames = stmtFrames ?? new Stack<List<Vb6Value>>();
        this.procName = procName;
        this.checkedArrayReturn = checkedArrayReturn;
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
                catch (Exception ex) when (interpreter.MarkUnimplementedFault(ex)) { throw; }
                finally { await interpreter.FlushFrame(stmtFrames.Pop()); }
                return string.Empty;
            }

            // Several items, `? a; b`, are Print's output list, which no expression parses as (#1386).
            if (forcedExpression && PrintListOf(expr) is { } printList)
                return await PrintToText(printList);

            VB6Parser.ValueStmtContext tree;
            try { tree = interpreter.ParseValueStmt(expr); }
            catch (VBCompileErrorException) { return "Syntax error"; }             // the line doesn't parse
            // A FRESH statement-temp frame (omit stmtFrames — it defaults to a new stack), so an eval temp is never
            // adopted into / terminated by the PAUSED statement's frame.
            var evaluator = new ExpressionExecutor(interpreter, currentEnv, currentModule, withTargets);
            var result = ImmediateValue(await evaluator.EvaluateValue(tree));
            return forcedExpression && IsPrintable(result) ? VbPrint.ItemText(result) : FormatEvalResult(result);
        }
        catch (Debugging.ImmediateEvalException ex) { return ex.Message; }        // user-call / New rejection
        catch (VBRunTimeException ex) { return FlattenError(ex.Message); }        // VB6 runtime error (e.g. Error 91)
        // A compile error raised while evaluating, "Method or data member not found" among them, says so: answered as a
        // syntax error, it sent #1386 to look at the parser.
        catch (VBCompileErrorException ex) { return FlattenError(ex.Message); }
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
                Debugging.DebugInspector.NodeFor(expr, result, interpreter.ExecutionContext))
            {
                ChangeKey = ChangeKeyOf(result),
            };
        }
        catch (Debugging.ImmediateEvalException ex) { return EvalErr(expr, ex.Message); }
        catch (VBRunTimeException ex) { return EvalErr(expr, FlattenError(ex.Message)); }
        catch (VBVariableNotDefinedException) { return EvalErr(expr, "Syntax error") with { UndefinedName = true }; }
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

    async Task<Debugging.DebugEvalResult> Debugging.IDebugFrame.EvaluateScopedWatchAsync(string expression, string context)
    {
        var dot = context.LastIndexOf('.');
        var module = dot < 0 ? context : context[..dot];
        var procedure = dot < 0 ? null : context[(dot + 1)..];
        // Cross-module contexts and recursive-frame selection have not been measured (#2290).
        if (module != "(All Procedures)" && !string.Equals(module, currentModule.Name, StringComparison.OrdinalIgnoreCase))
            return await ((Debugging.IDebugFrame)this).EvaluateTypedAsync(expression);
        StatementExecutor target = this;
        if (procedure is null)
            target = new StatementExecutor(interpreter, currentEnv.ModuleScope, currentModule);
        else
        {
            var stack = interpreter.ActivationStack;
            var index = -1;
            for (var i = stack.Count - 1; i >= 0; i--)
                if (ReferenceEquals(stack[i], this)) { index = i; break; }
            var matches = new List<StatementExecutor>();
            for (var i = index; i >= 0; i--)
                if (string.Equals(stack[i].currentModule.Name, module, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(stack[i].procName, procedure, StringComparison.OrdinalIgnoreCase))
                    matches.Add(stack[i]);
            if (matches.Count == 0)
                return ContextError("<Out of context>");
            if (matches.Count > 1)
                return await ((Debugging.IDebugFrame)this).EvaluateTypedAsync(expression);
            target = matches[0];
        }
        return await target.EvaluateStoredWatch(expression);
    }

    private async Task<Debugging.DebugEvalResult> EvaluateStoredWatch(string expression)
    {
        var result = await ((Debugging.IDebugFrame)this).EvaluateTypedAsync(expression);
        return result.UndefinedName ? ContextError("<Expression not defined in context>") : result;
    }

    private static Debugging.DebugEvalResult ContextError(string message)
        => new(false, message, "Empty", false, new Debugging.DebugNode(string.Empty, message, "Empty"));

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

    // The local names in declaredLocals that are Consts, which a typed-array return's check reads as typed (#986).
    private readonly HashSet<string> localConsts = new(System.StringComparer.OrdinalIgnoreCase);

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
    /// If/For/Do block), or the target line isn't a top-level statement of this procedure (nested, blank, End Sub, an
    /// Attribute line, or elsewhere). On success the pending repoint is applied by <c>ExecuteProcedureBody</c>'s loop when the walk
    /// resumes; the next Step/Continue executes from there.</summary>
    bool Debugging.IDebugFrame.SetNextStatement(int line)
    {
        if (!_suspendedAtTopLevel || _bodyStmts is null)
            return false;
        for (int i = 0; i < _bodyStmts.Length; i++)
            if (_bodyStmts[i].Start.Line == line && _bodyStmts[i].attributeStmt() == null)
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
    /// <summary>
    /// What <c>? x</c> prints for an object: its default member, as Print does everywhere. Reading a class's is a call
    /// into the program, refused here as D15 refuses <c>? obj.Prop</c>; one with none is 438, and Err is its Number
    /// (ObjectAsValue). A control, a control array and Nothing print as they did.
    /// </summary>
    private static Vb6Value ImmediateValue(Vb6Value v) => v.Value switch
    {
        VbObject obj when obj.ClassDef.PrePass.DeclaresDefaultMember => throw new Debugging.ImmediateEvalException(
            "Reading an object's default member calls into the program, which is not available in the Immediate window."),
        VbObject or VbErr or VbApp or VbScreen or VbForms => ObjectAsValue.Scalar(v, null),
        _ => v,
    };

    /// <summary>
    /// The output list of a <c>?</c> line with more than one item, or separators, as <c>Debug.Print</c> would read it;
    /// null for a single expression, which <see cref="Debugging.IDebugFrame.EvaluateAsync"/> evaluates itself.
    /// <para>
    /// A <c>?</c> line is Print to the Immediate window: the VB6 IDE prints <c>? 5</c> as <c> 5 </c>, and <c>? 1; "a"; 2</c>
    /// as <c> 1 a 2 </c>, exactly as <c>Debug.Print</c> prints them (measured, #1386).
    /// </para>
    /// </summary>
    private IList<IParseTree>? PrintListOf(string expr)
    {
        if (interpreter.TryParseStatement("Debug.Print " + expr) is not { } statement
            || FirstOfType<VB6Parser.ICS_B_MemberProcedureCallContext>(statement) is not { } call)
            return null;
        var children = call.argsCall() is { } list ? list.children : call.PRINT() is not null ? call.children : null;
        var items = children?.Count(c => c is VB6Parser.ArgCallContext or VB6Parser.OutputList_ExpressionContext) ?? 0;
        var separated = children?.Any(c => c is ITerminalNode { Symbol.Type: VB6Parser.SEMICOLON or VB6Parser.COMMA }) ?? false;
        return items > 1 || separated ? children : null;
    }

    private static T? FirstOfType<T>(IParseTree tree) where T : class, IParseTree
    {
        if (tree is T found)
            return found;
        for (var i = 0; i < tree.ChildCount; i++)
            if (FirstOfType<T>(tree.GetChild(i)) is { } inChild)
                return inChild;
        return null;
    }

    /// <summary>Prints an output list as text, from column 1, without the line break it ends with.</summary>
    private async Task<string> PrintToText(IList<IParseTree> list)
    {
        var target = new TextTarget();
        stmtFrames.Push(new List<Vb6Value>());
        try { await PrintList(list, target, _ => { }); }
        catch (Exception ex) when (interpreter.MarkUnimplementedFault(ex)) { throw; }
        finally { await interpreter.FlushFrame(stmtFrames.Pop()); }
        var text = target.Text;
        return (text.EndsWith("\r\n", StringComparison.Ordinal) ? text[..^2] : text).Replace("\r\n", "\n");
    }

    private sealed class TextTarget : VbPrint.ITarget
    {
        private readonly System.Text.StringBuilder text = new();
        public int Column { get; private set; }
        public string Text => text.ToString();

        public void Write(string written)
        {
            Column = VbPrint.ColumnAfter(Column, written);
            text.Append(written);
        }
    }

    // A value Print formats itself. An object, an array and a UDT keep the name FormatEvalResult gives them.
    private static bool IsPrintable(Vb6Value v) =>
        v.Type != Vb6Value.ValueType.Object && v.Value is not (VbUdt or Avalonia.Controls.Control or ICSharpProxy) && !v.Type.IsArray;

    private static string FormatEvalResult(Vb6Value v)
    {
        if (v.Type == Vb6Value.ValueType.Null) return "Null";
        if (v.Type == Vb6Value.ValueType.EmptyVariant) return "Empty";
        if (v.Type == Vb6Value.ValueType.Object) return v.Value is VbObject o ? o.ClassName : "Nothing";
        if (v.Value is VbUdt || v.Type.IsArray || v.Value is Avalonia.Controls.Control || v.Value is ICSharpProxy)
            return VB6BuiltIns.DebugTypeName(v);
        return VBDebugConsole.Format(v);
    }

    // The value in full, for "Break When Value Changes": its display rounds a Single or Double to VB6's 7 or 15
    // significant digits, so comparing displays would miss a change below them. Null where the display is already
    // the whole value.
    private static string? ChangeKeyOf(Vb6Value v)
    {
        var full = v.Value switch
        {
            double d => d.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
            float f => f.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
            decimal m => m.ToString(System.Globalization.CultureInfo.InvariantCulture),
            VbDate dt => dt.Serial.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
            _ => null,
        };
        return full is null ? null : v.Type + ":" + full;
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

    // The debugger's gate. A pause or a time slice there gives up the thread, which the pump has to be told of, or it would
    // take the suspended handler for one still running and start the next event inside it (#766).
    private ValueTask DebugGate(Debugging.IDebugController dbg)
    {
        var gate = dbg.OnStatementAsync(currentLine, currentModule.Name, this);
        return interpreter.Events is { } events ? events.WhileSuspended(gate) : gate;
    }

    public override async Task<ControlFlow> VisitBlock(VB6Parser.BlockContext context)
    {
        var stmts = context.blockStmt();
        var retry = false;
        for (var i = 0; i < stmts.Length; i++)
        {
            var stmt = stmts[i];
            // Debugger pause-gate (before the temp-frame push, so a suspended break / reset never parks a
            // half-open frame). Null controller ⇒ skipped entirely (zero overhead headless/tests).
            currentLine = stmt.Start.Line;   // track for the Call Stack (this frame's current line)
            // A handler the last statement raised finishes before this one starts (#766).
            if (interpreter.Events is { } events)
                await events.WaitForRaised();
            // After End nothing more runs, with or without a debugger (#775). Ahead of the debugger's gate, so a
            // breakpoint on a statement that would run after End is never hit.
            if (interpreter.Ending)
                throw new Debugging.StopExecutionSignal();
            // An Attribute line is metadata, so the debugger never stops on one, nested or not (see VisitAttributeStmt).
            // Nor again on a statement a halt is retrying: its break has just been resumed, and a step armed there runs it.
            if (!retry && interpreter.DebugController is { } dbg && stmt.attributeStmt() == null && !interpreter.RunningDeclarations)
            {
                _suspendedAtTopLevel = false;   // a pause here is inside a nested block — Set Next Statement is refused
                await DebugGate(dbg);
            }
            retry = false;
            erlLine = stmt.Start.Line;

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
                catch (Exception ex) when (interpreter.MarkUnimplementedFault(ex)) { throw; }
                finally
                {
                    await interpreter.FlushFrame(stmtFrames.Pop());
                }
            }
            catch (Exception ex) when (Locate(ex, stmt)) { throw; }
            catch (VBRunTimeException ex) when (ResumesNext)
            {
                // On Error Resume Next: a runtime error unwinds the C# stack to the innermost enclosing block;
                // record it in the global Err and continue with the next statement. Only VBRunTimeException is
                // trapped — engine-limit NotImplementedExceptions propagate as real gaps. This per-block trap
                // gives correct nested-block and cross-procedure semantics for free.
                CaptureError(ex);
                continue;
            }
            catch (VBRunTimeException ex) when (HaltsHere())
            {
                _suspendedAtTopLevel = false;
                if (!await Halt(ex, stmt.Start.Line))
                    throw;
                retry = true;
                i--;
                continue;
            }

            if (ret != ControlFlow.Nothing)
                return ret;
        }

        // And at the end of the block, so a loop's condition, read next, sees what a handler its last statement raised
        // has set: `Do: Text1.Text = "x": Loop Until done`, with Text1_Change setting done (#766).
        if (interpreter.Events is { } raised)
            await raised.WaitForRaised();
        return ControlFlow.Nothing;
    }

    public override async Task<ControlFlow> VisitAppActivateStmt(VB6Parser.AppActivateStmtContext context)
    {
        throw new NotImplementedException("AppActivate not implemented");
    }

    // An Attribute line inside a procedure (VB_UserMemId, VB_Description, VB_ProcData, VB_HelpID, VB_MemberFlags) is
    // metadata about the procedure, not a statement, and running it does nothing. It threw "Attribute not
    // implemented", so calling any procedure that carried one stopped the run (#832). Measured: a Function with
    // VB_UserMemId = 0 returns its value, a Property Get with VB_Description and VB_ProcData lines and its Let both
    // work, and so does a Function with VB_Description, VB_HelpID and VB_MemberFlags.
    //
    // The debugger treats the line as it treats a blank one. A step never stops on it: ExecuteProcedureBody, which also
    // runs a module's declarations where `Attribute Name.VB_VarDescription` follows a Public variable, and VisitBlock,
    // for one nested in a block, both skip it. Set Next Statement onto it is refused. A breakpoint or Run To Cursor on
    // it is accepted and never reached, as on a blank or comment line, because neither checks its line. VB6's code
    // window hides these lines, so none of that can be asked of it there. HexIDE's shows them, read-only and folded
    // into the line each run describes, and refuses a breakpoint on one (#273).
    public override Task<ControlFlow> VisitAttributeStmt(VB6Parser.AttributeStmtContext context) =>
        Task.FromResult(ControlFlow.Nothing);

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
        // changing the default drive; HexIDE keeps one interpreter-local directory, shared with Open.)
        var path = VbDisplay.ToText(await expressionExecutor.EvaluateScalar(context.valueStmt()));
        try { interpreter.BuiltIns.Files.ChangeDirectory(path); }
        catch (Exception ex) when (ex is System.IO.DirectoryNotFoundException or System.IO.FileNotFoundException or ArgumentException or System.IO.IOException)
        { throw new VBRunTimeException(context, VBStandardError.PathNotFound); }
        // The program's files resolve a relative path here from now on (#1145).

        return ControlFlow.Nothing;
    }

    public override async Task<ControlFlow> VisitChDriveStmt(VB6Parser.ChDriveStmtContext context)
    {
        // VB6 ChDrive uses the FIRST character of the (string-coerced) arg as the drive letter. Oracle-verified:
        // `ChDrive ""`→no-op, `ChDrive "C"`→ok, a non-drive-letter first char (e.g. `ChDrive 5`→'5')→Invalid
        // Procedure Call (5), a valid letter whose drive is unavailable (`ChDrive "Q"`)→Device Unavailable (68).
        // (The old code stored the bare arg as a relative path — always broke.)
        var s = VbDisplay.ToText(await expressionExecutor.EvaluateScalar(context.valueStmt()));
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

    // `Close` with no number closes every open file, and `Close #f` one that isn't open is no error (measured, #1145).
    public override async Task<ControlFlow> VisitCloseStmt(VB6Parser.CloseStmtContext context)
    {
        var numbers = context.valueStmt();
        if (numbers.Length == 0)
            interpreter.BuiltIns.Files.CloseAll();
        foreach (var number in numbers)
            interpreter.BuiltIns.Files.Close(await FileNumber(number));
        return ControlFlow.Nothing;
    }

    /// <summary>A file number, <c>#f</c> or an expression, as <see cref="ExpressionExecutor.FileNumber"/> reads it.</summary>
    private Task<int> FileNumber(VB6Parser.ValueStmtContext valueStmt) => expressionExecutor.FileNumber(valueStmt);

    /// <summary>A variable a statement stores into, as a Let stores into it: created where Option Explicit allows.</summary>
    private async Task StoreVariable(string name, Vb6Value value)
    {
        if (!interpreter.TryReadNow(currentEnv, name, out var found, out var old, out var need))
            (found, old) = await interpreter.ReadVariableAfter(currentEnv, name, need!);
        if (!found)
        {
            if (currentModule.PrePass.RequireVariableDefinitions)
                throw new VBVariableNotDefinedException(name);
            AllocImplicitVariant(name);
        }
        interpreter.ExecutionContext.TryUpdateVariable(currentEnv, name, interpreter.CopyForStore(value));
        await interpreter.ReleaseRef(old);
    }

    public override async Task<ControlFlow> VisitConstStmt(VB6Parser.ConstStmtContext context)
    {
        // Module-level const names are hoisted by PrePass so a Sub declared after the Const can see them; here we evaluate
        // the initializer and fill the slot. The slot is marked a constant, so any other store into it is refused (#1003).
        foreach (var sub in context.constSubStmt())
        {
            // Inside a procedure the Const was declared at entry (HoistDeclaredLocals), so the statement has nothing
            // left to do when execution reaches it.
            if (ownedSlots != null)
            {
                await DeclareLocalConst(sub);
                continue;
            }

            var name = sub.ambiguousIdentifier().GetText();
            // What the value reads is checked against VB6's rules for a constant expression first (measured).
            await ModuleLevelBounds.CheckConst(interpreter, currentModule, currentEnv, sub);
            var value = AsDeclared(sub, await expressionExecutor.EvaluateValue(sub.valueStmt()));
            // At module level the name was hoisted by the pre-pass, so this fills that slot.
            if (!interpreter.ExecutionContext.TryUpdateVariable(currentEnv, name, value, fillingConstant: true))
            {
                interpreter.ExecutionContext.AllocVariable(currentEnv, name, value);
                if (currentEnv.TryGetVariableLocation(name, out var constant))
                {
                    interpreter.ExecutionContext.State.MarkConstant(constant);
                    if (PrePass.IsVariantConst(sub))
                        interpreter.ExecutionContext.State.MarkVariant(constant);
                }
            }
        }
        return ControlFlow.Nothing;
    }

    public override async Task<ControlFlow> VisitDateStmt(VB6Parser.DateStmtContext context)
    {
        throw new NotImplementedException("Date not implemented");
    }

    public override async Task<ControlFlow> VisitDeleteSettingStmt(VB6Parser.DeleteSettingStmtContext context)
    {
        await RunSettingStatement("DeleteSetting", context.valueStmt());
        return ControlFlow.Nothing;
    }

    // SaveSetting and DeleteSetting are statements in the grammar and intrinsics in VB6, which runs `Call SaveSetting(...)` and
    // `VBA.SaveSetting ...` as well (measured, #1562), so each runs as the intrinsic. A named argument, `AppName:="x"`, is
    // placed by the intrinsic's parameter names, as a call's is.
    private async Task RunSettingStatement(string name, IReadOnlyList<VB6Parser.ValueStmtContext> arguments)
    {
        var args = new List<CallArg>(arguments.Count);
        foreach (var argument in arguments)
        {
            if (argument is VB6Parser.VsAssignContext assign)
            {
                var value = assign.valueStmt();
                args.Add(new CallArg(Vb6Value.Missing, null, Name: assign.implicitCallStmt_InStmt().GetText(),
                    Deferred: async () => new CallArg(await expressionExecutor.EvaluateValue(value), null)));
                continue;
            }
            if (args.Count > 0 && args[^1].Name is not null)
                throw new VBCompileErrorException("Syntax error");
            args.Add(new CallArg(await expressionExecutor.EvaluateValue(argument), null));
        }
        await interpreter.BuiltIns.EvaluateBuiltInFunction(name, await VB6BuiltIns.ArrangeIntrinsicArguments(name, args), currentModule.PrePass.OptionCompareText);
    }

    // The pre-pass collected the module's DefType statements (#169); running one does nothing. Inside a procedure it does not
    // compile (measured).
    public override Task<ControlFlow> VisitDeftypeStmt(VB6Parser.DeftypeStmtContext context) =>
        ownedSlots != null ? throw new VBCompileErrorException("Invalid inside procedure") : Task.FromResult(ControlFlow.Nothing);

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

            // A condition that raised under Resume Next ends the loop: the next statement is the one after Loop.
            if (await ConditionMet(context.valueStmt(), context) is not { } conditionMet)
                return ControlFlow.Nothing;

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
            // A condition that raised under Resume Next runs the body, and is asked again next time round.
            if (await ConditionMet(context.valueStmt(), context) is { } conditionMet)
            {
                if (until && conditionMet)
                    return ControlFlow.Nothing;

                if (!until && !conditionMet)
                    return ControlFlow.Nothing;
            }

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

    public override Task<ControlFlow> VisitEndStmt(VB6Parser.EndStmtContext context)
        // The program is over: no further statement, no Class_Terminate, no form events (#775).
        => throw interpreter.End();

    private const string VariableRequired = "Variable required - can't assign to this expression";

    /// <summary>
    /// Refuse a statement whose target is a Const or an Enum member, with the message VB6 gives for that statement
    /// (measured, #1003, a Const and an Enum member alike): a For counter, a For Each variable or the target of a Mid
    /// statement is "Variable required - can't assign to this expression", and ReDim or Erase of one is "Expected array".
    /// LSet and Line Input refuse one the same way as Mid; neither is implemented yet, so neither stores anything. VB6 refuses at compile time; this
    /// raises it as the statement runs, before anything is evaluated. A Let or Set is refused by
    /// <see cref="ModuleExecutionContext.TryUpdateVariable"/>, with "Assignment to constant not permitted".
    /// </summary>
    private void RefuseConstantTarget(string name, string message)
    {
        if (currentEnv.TryGetVariableLocation(name, out var location)
            && (interpreter.ExecutionContext.State.IsConstant(location) || interpreter.ExecutionContext.State.IsReadOnly(location)))
            throw new VBCompileErrorException($"{message} ({name})");
    }

    // ReDim or Erase of a procedure's ParamArray is VB6's compile error "Invalid ParamArray use" (measured, #2462), raised here as
    // the statement runs.
    private void RefuseParamArray(string name)
    {
        if (currentEnv.TryGetVariableLocation(name, out var location) && interpreter.ExecutionContext.State.IsParamArray(location))
            throw new VBCompileErrorException($"Invalid ParamArray use ({name})");
    }

    public override async Task<ControlFlow> VisitEraseStmt(VB6Parser.EraseStmtContext context)
    {
        // `Erase a[, b, ...]` — a DYNAMIC array is freed (undimensioned: later LBound/UBound/index → Err 9), a FIXED
        // array keeps its bounds with every element reset to default. Both mutate the array in place (oracle-verified).
        foreach (var vs in context.valueStmt())
        {
            var name = vs.GetText();
            RefuseConstantTarget(name, "Expected array");
            RefuseParamArray(name);
            Vb6Value v;
            if (vs is VB6Parser.VsICSContext field && await UdtArrayField(field.implicitCallStmt_InStmt()) is { } arrayField)
                v = arrayField.Read();
            else if (!interpreter.ExecutionContext.TryGetVariable(currentEnv, name, out v))
                throw new VBRunTimeException(context, VBStandardError.ObjectRequired, "Variable " + name + " is not declared");
            if (!v.Type.IsArray || v.Value is not VBArray arr)
                throw new VBCompileErrorException("Array required in Erase");
            // An array one of whose elements a running call holds ByRef is locked (measured, #1455).
            if (arr.IsLocked)
                throw new VBRunTimeException(context, VBStandardError.ThisArrayIsFixedOrTemporarilyLocked);
            // Clear the slots before releasing their objects, as a scalar store does (measured, #1482).
            var released = arr.IsDynamic || arr.ElementType == Vb6Value.ValueType.Object || arr.ElementType == Vb6Value.ValueType.UserDefinedType
                ? arr.EnumerateElements().ToList() : null;
            if (arr.IsDynamic)
                arr.Free();
            else
                arr.ResetElements();
            if (released is not null)
                foreach (var element in released)
                    await interpreter.ReleaseRef(element);
        }
        return ControlFlow.Nothing;
    }

    public override async Task<ControlFlow> VisitErrorStmt(VB6Parser.ErrorStmtContext context)
    {
        // Legacy `Error n` statement — equivalent to Err.Raise n.
        var number = await expressionExecutor.EvaluateScalar(context.valueStmt());
        if (!Vb6Value.TryNumericToDouble(number, out var d))
            throw new VBRunTimeException(context, VBStandardError.TypeMismatch);
        interpreter.Err.Raise((long)d);
        return ControlFlow.Nothing;   // unreachable — Raise throws
    }

    public override Task<ControlFlow> VisitContinueStmt(VB6Parser.ContinueStmtContext context)
        => throw new VBCompileErrorException("Syntax error");

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
            if (VbInterface.ResolveMember(value, vobj, identifier) is { } callName
                && vobj.ClassDef.PrePass.Procedures.TryGetValue(callName, out var method))
            {
                var objCallArgs = await expressionExecutor.ResolveCallArgs(context.argsCall());
                await interpreter.RunProcedure(vobj.ClassDef, method, objCallArgs, vobj.InstanceEnv, value, stmtFrames);
                return ControlFlow.Nothing;
            }
            // A form's built-in method is its window's: `Call Me.Move 0, 0` (#822).
            value = await interpreter.Loading((Form: vobj, Member: identifier), static s => s.Form.BuiltInMembersFor(s.Member)) is { } formWindow
                ? new Vb6Value(formWindow)
                : throw new VBRunTimeException(context, VBStandardError.MethodOrDataMemberNotFound, identifier);
        }

        if (value.Value is VbCollection)
        {
            await ExpressionExecutor.AsBound(value, identifier, async () =>
                await interpreter.CallProxy((ICSharpProxy)value.Value!, identifier,
                    await expressionExecutor.EvaluateMemberArgs(value.Value, identifier, context.argsCall())));
            return ControlFlow.Nothing;
        }
        var callArgs = await expressionExecutor.EvaluateMemberArgs(value.Value, identifier, context.argsCall());

        if (value.Type == Vb6Value.ValueType.CSharpProxyObject)
            await interpreter.CallProxy((ICSharpProxy)value.Value!, identifier, callArgs);
        else if (value.Type == Vb6Value.ValueType.Control)
        {
            UserControlMembers.Check(value, identifier);
            await ((Control)value.Value!).CallAsync(identifier, callArgs);
        }
        else
            throw new VBRunTimeException(context, $"Unknown method {identifier} on {value}");

        return ControlFlow.Nothing;
    }

    public override async Task<ControlFlow> VisitExplicitCallStmt(VB6Parser.ExplicitCallStmtContext context) => await base.VisitExplicitCallStmt(context);

    public override async Task<ControlFlow> VisitFilecopyStmt(VB6Parser.FilecopyStmtContext context)
    {
        interpreter.BuiltIns.Files.Copy(VbDisplay.ToText(await expressionExecutor.EvaluateScalar(context.valueStmt(0))), VbDisplay.ToText(await expressionExecutor.EvaluateScalar(context.valueStmt(1))));
        return ControlFlow.Nothing;
    }

    public override async Task<ControlFlow> VisitForEachStmt(VB6Parser.ForEachStmtContext context)
    {
        if (context.typeHint() != null)
            throw new NotImplementedException("For Each type hints not supported");

        var varName = context.ambiguousIdentifier(0).GetText();
        RefuseConstantTarget(varName, VariableRequired);
        if (!interpreter.ExecutionContext.TryInspectVariable(currentEnv, varName, out _))
            AllocImplicitVariant(varName);

        Vb6Value collection;
        try
        {
            collection = await expressionExecutor.EvaluateValue(context.valueStmt());
        }
        catch (VBRunTimeException ex) when (ResumesNext)
        {
            var empties = DeclaredOtherThanAnArray(context.valueStmt())
                ?? throw new NotImplementedException(
                    "This For Each's collection raised under On Error Resume Next. VB6 then runs the body once and empties "
                    + "the loop variable unless the collection is declared as an array, and this expression's "
                    + "declaration is unavailable (#969): " + context.valueStmt().GetText() + ".");
            CaptureError(ex);
            return await RunUninitialisedForEach(context, varName, empties);
        }

        if (collection.Value is VBArray { ElementType: var elementType } && elementType == Vb6Value.ValueType.UserDefinedType)
            throw new VBCompileErrorException("For Each may not be used on array of user-defined type or fixed-length strings");

        if (collection.Value is Components.InBox.TabStripTabs tabs)
        {
            var snapshot = tabs.Owner.Model.Tabs.ToArray();
            var at = 0;
            return await ForEachItem(context, varName, (out Vb6Value item) =>
            {
                if (!snapshot.SequenceEqual(tabs.Owner.Model.Tabs))
                    throw TabStripMembers.Refused("collection mutation during For Each");
                item = at < snapshot.Length ? new Vb6Value(tabs.Wrap(snapshot[at++])) : default;
                return at <= snapshot.Length && item.Value is not null;
            }, () => { });
        }
        if (collection.Value is VbCollection items)
        {
            var cursor = items.StartEnumeration();
            return await ForEachItem(context, varName, (out Vb6Value item) => items.TryNext(cursor, out item),
                () => items.EndEnumeration(cursor));
        }
        if (collection.Value is ImageListImages images)
        {
            var iterator = images.Values.GetEnumerator();
            return await ForEachItem(context, varName, (out Vb6Value item) =>
            {
                var next = iterator.MoveNext();
                item = next ? iterator.Current : default;
                return next;
            }, iterator.Dispose);
        }
        if (collection.Value is FormControls controls)
        {
            var cursor = controls.StartEnumeration();
            return await ForEachItem(context, varName, (out Vb6Value item) => controls.TryNext(cursor, out item), () => { });
        }
        if (collection.Value is VbForms forms)
        {
            var cursor = forms.StartEnumeration();
            return await ForEachItem(context, varName, (out Vb6Value item) => VbForms.TryNext(cursor, out item), () => { });
        }

        if (collection.Value is not VBArray { IsDefined: true } array)
        {
            // Measured (#839): an unallocated array raises 92 here, Nothing raises 424, and any other value 13. Arrays are
            // all For Each walks so far, so an object is refused rather than raised: a trappable error let On Error Resume
            // Next step over the whole loop.
            var refusal = collection.Value switch
            {
                VBArray => new VBRunTimeException(context, VBStandardError.ForLoopNotInitialized),
                null when collection.Type == Vb6Value.ValueType.Object => new VBRunTimeException(context, VBStandardError.ObjectRequired),
                VbObject or Avalonia.Controls.Control or ICSharpProxy => throw new NotImplementedException(
                    "For Each over this object, such as a control array, is not supported yet: only arrays, a Collection, a form's Controls and Forms are."),
                _ => new VBRunTimeException(context, VBStandardError.TypeMismatch),
            };
            if (!ResumesNext)
                throw refusal;
            CaptureError(refusal);
            // P-code and the IDE skip Next with a failed For Each over Nothing when it directly follows
            // on the same line (#1486). The grammar gives this form no block; token lines distinguish
            // it from an empty multi-line loop. Returning consumes the loop, leaving any following
            // statement in the enclosing block to run, without touching the variable.
            if (collection.Type == Vb6Value.ValueType.Object && collection.Value is null
                && context.block() is null && context.FOR().Symbol.Line == context.NEXT().Symbol.Line)
                return ControlFlow.Nothing;
            return await RunUninitialisedForEach(context, varName, emptiesElement: collection.Value is not VBArray);
        }

        // Share the ByRef element lock (#1455): every exit, including a propagated jump or error, must release it.
        array.LockCount++;
        try
        {
            var block = context.block();
            foreach (var element in array.EnumerateElements())
            {
                // VB6 binds each element as a copy: an element that is an array is copied, so a write through the loop
                // variable does not reach the array walked (measured, #970). An object element is held by the variable while
                // it holds it, and what the variable held before is released.
                interpreter.ExecutionContext.TryInspectVariable(currentEnv, varName, out var previous);
                var bound = interpreter.CopyForStore(element);
                interpreter.AddRef(bound);
                interpreter.ExecutionContext.TryUpdateVariable(currentEnv, varName, bound);
                await interpreter.ReleaseRef(previous);

                if (block == null)
                    continue;
                var result = await Visit(block);
                if (result == ControlFlow.ExitFor)
                    return ControlFlow.Nothing;
                if (result != ControlFlow.Nothing)
                    return result;
                erlLine = context.NEXT().Symbol.Line;
            }
            // A loop that runs out leaves its variable Empty, releasing the last element it held, over an object array and a
            // Long() alike, and even when there was nothing to walk (measured, #970: a variable holding 9 before a For Each
            // over an empty Split is Empty after it). Exit For leaves it holding the element it reached.
            interpreter.ExecutionContext.TryInspectVariable(currentEnv, varName, out var last);
            interpreter.ExecutionContext.TryUpdateVariable(currentEnv, varName, Vb6Value.Variant);
            await interpreter.ReleaseRef(last);
            return ControlFlow.Nothing;
        }
        finally
        {
            array.LockCount--;
        }
    }

    /// <summary>The next item of a For Each over an object, or false once there are no more.</summary>
    private delegate bool NextItem(out Vb6Value item);

    /// <summary>
    /// For Each over a Collection (#1471) or a form's Controls (#1060), measured. The loop reaches the items in order, as
    /// they are when it reaches them: from a Collection, an item removed before it is reached is not, and one added after
    /// the item it is on is (VbCollection.Cursor); from Controls, one unloaded before it is reached is not, and one loaded
    /// while it runs is not either (FormControls.Cursor). Each is bound as an array's element is. A loop that runs out
    /// leaves a Variant Empty and an object variable Nothing; Exit For leaves it holding the item it reached. The variable
    /// must be a Variant or an object: a Long or a String is VB6's compile error, and an item that is no object, given to
    /// an object variable, is 424. Under Resume Next, that 424 on the first item goes on into the body, and on a later one,
    /// raised by Next, ends the loop (measured).
    /// </summary>
    private async Task<ControlFlow> ForEachItem(VB6Parser.ForEachStmtContext context, string varName, NextItem next, Action end)
    {
        currentEnv.TryGetVariableLocation(varName, out var location);
        var state = interpreter.ExecutionContext.State;
        if (state.DeclaredTypeOf(location) is not null)
            throw new VBCompileErrorException("For Each control variable must be Variant or Object");
        // The loop reads its variable before it stores the first item, so one declared As New makes its object here and
        // releases it at that store, or when an empty loop leaves the variable Nothing (measured, #435).
        var (_, initial) = await interpreter.ReadVariable(currentEnv, varName);
        var objectVariable = !state.IsVariant(location) && !currentEnv.VariantParameters.Contains(varName)
            && (state.DeclaredClassOf(location) is not null || IsObjectReference(initial));

        var block = context.block();
        try
        {
            var first = true;
            while (next(out var item))
            {
                try
                {
                    if (objectVariable && !IsObjectReference(item))
                        throw new VBRunTimeException(context, VBStandardError.ObjectRequired);
                    interpreter.ExecutionContext.TryInspectVariable(currentEnv, varName, out var previous);
                    var bound = interpreter.CopyForStore(item);
                    interpreter.AddRef(bound);
                    try
                    {
                        interpreter.ExecutionContext.TryUpdateVariable(currentEnv, varName, bound, context);
                    }
                    catch
                    {
                        await interpreter.ReleaseRef(bound);
                        throw;
                    }
                    await interpreter.ReleaseRef(previous);
                }
                catch (VBRunTimeException ex) when (ResumesNext)
                {
                    CaptureError(ex);
                    if (!first)
                        return ControlFlow.Nothing;
                }
                first = false;

                if (block == null)
                    continue;
                var result = await Visit(block);
                if (result == ControlFlow.ExitFor)
                    return ControlFlow.Nothing;
                if (result != ControlFlow.Nothing)
                    return result;
                erlLine = context.NEXT().Symbol.Line;
            }
        }
        finally
        {
            end();
        }
        interpreter.ExecutionContext.TryInspectVariable(currentEnv, varName, out var last);
        interpreter.ExecutionContext.TryUpdateVariable(currentEnv, varName, objectVariable ? Vb6Value.Nothing : Vb6Value.Variant);
        await interpreter.ReleaseRef(last);
        return ControlFlow.Nothing;
    }

    /// <summary>
    /// Whether a Let into this name is a Let into an object variable: one declared as a class, <c>As Object</c> or
    /// <c>As Collection</c>, or any other slot that holds an object and is not a Variant's. A Variant, an untyped or
    /// <c>As Variant</c> parameter, and a Variant Function's own name take the value instead (measured, #1493).
    /// </summary>
    private bool IsObjectVariable(ExecutionEnvironment env, string name, int slot) =>
        env.ObjectParameters.Contains(name)
        || !interpreter.ExecutionContext.State.IsVariant(slot) && !env.VariantParameters.Contains(name)
           && !(VariantReturn && string.Equals(name, procName, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// A Let into an object variable assigns the default member of the object it holds, and the object stays (measured,
    /// #1493): 438 for a class instance with none, as for a Collection, and 91 for Nothing. It released the object and
    /// stored the value. A class's own default member (#174) and a control's (#790) are not implemented, so those are
    /// refused. Anything else the variable holds is left to the store.
    /// </summary>
    private static void RefuseLetIntoObject(Vb6Value held, string target, ParserRuleContext context)
    {
        if (!IsObjectReference(held))
            return;
        throw held.Value switch
        {
            null => new VBRunTimeException(context, VBStandardError.ObjectVariableOrWithBlockVariableNotSet),
            VbObject { ClassDef.PrePass.DeclaresDefaultMember: true } withDefault => new NotImplementedException(
                $"`{target} = …` assigns the default member of the {withDefault.ClassName} it holds, which is not implemented "
                + "for a class yet (#174). Name the member instead."),
            VbObject or VbCollection => new VBRunTimeException(context, VBStandardError.ObjectDoesntSupportThisPropertyOrMethod),
            _ => new NotImplementedException(
                $"`{target} = …` assigns the default member of the object it holds, which is not implemented (#790). "
                + "Name the property instead."),
        };
    }

    // An object reference, or Nothing: what an object variable can hold.
    private static bool IsObjectReference(Vb6Value v) =>
        v.Type == Vb6Value.ValueType.Object || v.Type == Vb6Value.ValueType.Control || v.Type == Vb6Value.ValueType.CSharpProxyObject;

    public override async Task<ControlFlow> VisitForNextStmt(VB6Parser.ForNextStmtContext context)
    {
        var counter = context.iCS_S_VariableOrProcedureCall();
        var variable = counter.GetText();
        // `For i% = …` and `Next i%` name the variable `i`, a local, a module's or another module's Public, checked against
        // its declared type; an undeclared one is declared with the character's type (measured for % & ! # @, #739).
        if (counter.typeHint() is { } counterHint)
            variable = TypedCounter(counter.ambiguousIdentifier().GetText(), counterHint, declare: true);
        RefuseConstantTarget(variable, VariableRequired);
        foreach (var hint in context.typeHint())
        {
            if (context.NEXT().Symbol.TokenIndex > hint.Start.TokenIndex || context.ambiguousIdentifier() is not { } next)
                throw new NotImplementedException("TypeHints not implemented");
            TypedCounter(next.GetText(), hint, declare: false);
        }
        if (context.asTypeClause() != null)
            throw new NotImplementedException("asTypeClause not implemented");
        // Start, limit and step, in that order, each converted into the counter's type as soon as it is evaluated, so one
        // that raises, an overflow among them, stops the rest (measured: a Byte counter's `Step -1` is 6, and `1 To 2.5`
        // is `1 To 2` for an Integer one). The limit and step are kept as this activation's loop state (forState). With
        // no Step clause the step is 1, and is kept as if it had been evaluated.
        var counterType = currentEnv.TryGetVariableLocation(variable, out var counterLoc)
            ? interpreter.ExecutionContext.State.DeclaredTypeOf(counterLoc)
            : null;
        var state = forState.GetValueOrDefault(context);
        Vb6Value from, to, step;
        try
        {
            from = await ForBound(context.valueStmt(0), context, counterType);
            to = await ForBound(context.valueStmt(1), context, counterType);
            state.Limit = ForNumber(to);
            forState[context] = state;
            step = context.valueStmt(2) is { } stepStmt ? await ForBound(stepStmt, context, counterType) : new Vb6Value(1);
            state.Step = ForNumber(step);
            forState[context] = state;
        }
        catch (VBRunTimeException ex) when (ResumesNext)
        {
            CaptureError(ex);
            // In p-code a header that raised still steps by 1 when there is no Step clause, but only for an Integer or
            // Long counter. Every other type steps by the step it last evaluated, 0 before it has (measured).
            var impliedStep = context.valueStmt(2) is null
                && (counterType == Vb6Value.ValueType.Integer || counterType == Vb6Value.ValueType.Long);
            return await RunUninitialisedFor(context, variable, counterType, state.Limit, impliedStep ? 1 : state.Step);
        }

        if (!interpreter.ExecutionContext.TryInspectVariable(currentEnv, variable, out _))
            AllocImplicitVariant(variable);

        if (counterType is null || SteppedInItsType(counterType))
        {
            var type = counterType ?? ForVariantType(from, to, context.valueStmt(2) is null ? null : (Vb6Value?)step, context);
            return await RunForInType(context, variable, type, from, to, step, variant: counterType is null);
        }

        // A Byte, Integer or Long counter: its bounds were rounded into it as they were converted, so they are whole. A
        // Boolean or String counter keeps what its bounds were, and only whole numbers are taken for one (none was measured).
        double fromD = ForNumber(from), toD = ForNumber(to), stepD = ForNumber(step);
        if (fromD != Math.Truncate(fromD) || toD != Math.Truncate(toD) || stepD != Math.Truncate(stepD))
            throw FractionalForRefused();
        long fromL = (long)fromD, toL = (long)toD, stepL = (long)stepD;

        // VB6 runs the body while (step >= 0 ? counter <= limit : counter >= limit) — a `<=`/`>=` test, NOT `i == to`
        // (equality never terminates when the step doesn't land exactly on `to`, e.g. `0 To 10 Step 3` → 0,3,6,9,…
        // forever). The counter is left one step PAST the limit — the value that failed the test — matching the
        // oracle (0 To 10 Step 3 → 12; empty 1 To 3 → 4). An empty body still counts.
        var block = context.block();
        long i = fromL;
        interpreter.ExecutionContext.TryUpdateVariable(currentEnv, variable, ForCounterValue(i));
        while (stepL >= 0 ? i <= toL : i >= toL)
        {
            if (await RunLoopBodyOnce(block, context.NEXT()) is { } left)
                return left;
            i = AdvanceForCounter(context, variable, stepL, i);
        }
        return default;
    }

    /// <summary>
    /// A For counter, or the name on its Next, written with a type character: the variable of the bare name, measured on
    /// vb6.exe for #739, native and p-code. A character its declared type does not match, a Variant's included, is VB6's
    /// compile error, as is a String counter's `$` ("Type mismatch"). An undeclared name is "Variable not defined" under
    /// Option Explicit, and otherwise the counter declares it with the character's type.
    /// </summary>
    private string TypedCounter(string name, VB6Parser.TypeHintContext hint, bool declare)
    {
        var type = BaseTypeMapper.Map(hint);
        if (currentEnv.TryGetVariableLocation(name, out var location))
        {
            if (interpreter.ExecutionContext.State.DeclaredTypeOf(location) != type)
                throw new VBCompileErrorException("Type-declaration character does not match declared data type");
            if (type == Vb6Value.ValueType.String)
                throw new VBCompileErrorException("Type mismatch");
            return name;
        }
        if (currentModule.PrePass.RequireVariableDefinitions)
            throw new VBVariableNotDefinedException(name);
        // An undeclared `$` counter, and a Next naming an undeclared variable, were not measured.
        if (!declare || type == Vb6Value.ValueType.String)
            throw new NotImplementedException($"`{name}{hint.GetText()}` on a For or Next it does not declare is not implemented (#739)");
        interpreter.ExecutionContext.AllocVariable(currentEnv, name, new Vb6Value(type), type);
        return name;
    }

    // A Single, Double, Currency or Date counter steps in its own type, and so may take a fraction (#983).
    private static bool SteppedInItsType(Vb6Value.ValueType t) =>
        t == Vb6Value.ValueType.Single || t == Vb6Value.ValueType.Double || t == Vb6Value.ValueType.Currency || t == Vb6Value.ValueType.Date;

    /// <summary>
    /// A For whose counter is a Variant, or a Single, Double, Currency or Date, measured on vb6.exe for #983, native and p-code.
    /// <para>
    /// The counter starts at the start and the step is added to it each time round, in the loop's type, so a Double's
    /// tenths accumulate: <c>For x = 1 To 2 Step 0.1</c> runs 10 passes, its eleventh value being just over 2. The body runs
    /// while the counter is at or below the limit (at or above it for a negative step), and the counter is left at the
    /// value that failed. A Step of 0 runs for ever while the counter is within the limit.
    /// </para>
    /// <para>
    /// A Single adds in Double: the step and the limit are Singles, but the sum is tested against the limit before it is
    /// rounded into the counter. That is p-code's answer, and HexIDE's. Native code rounds the sum into a Single first, so
    /// <c>For x = 0.1 To 0.5 Step 0.1</c> runs 5 passes built native and 4 as p-code (and in the IDE).
    /// </para>
    /// </summary>
    private async Task<ControlFlow> RunForInType(VB6Parser.ForNextStmtContext context, string variable,
        Vb6Value.ValueType type, Vb6Value from, Vb6Value to, Vb6Value step, bool variant)
    {
        // A Variant's counter widens when a step overflows its type, as Variant arithmetic does: a Byte counter goes on past
        // 255 as an Integer, and an Integer one past 32767 as a Long (measured, #983, p-code).
        Vb6Value InLoop(Vb6Value v) => variant ? VbNumeric.Narrow(v, type, context).AsVariantSubtype() : VbNumeric.Narrow(v, type, context);
        var counter = InLoop(from);
        var limit = InLoop(to);
        var by = InLoop(step);
        var up = VbNumeric.CompareNumbers(by, new Vb6Value(0), context) >= 0;
        var tested = counter;
        var block = context.block();
        while (up ? VbNumeric.CompareNumbers(tested, limit, context) <= 0 : VbNumeric.CompareNumbers(tested, limit, context) >= 0)
        {
            interpreter.ExecutionContext.TryUpdateVariable(currentEnv, variable, counter);
            if (block == null)
            {
                // An empty body has no statement of its own at which an abort can reach a Step 0 loop.
                if (interpreter.Ending || interpreter.DebugController is { IsAborting: true })
                    throw new Debugging.StopExecutionSignal();
            }
            else
            {
                var ret = await Visit(block);
                if (ret == ControlFlow.ExitFor)
                    return ControlFlow.Nothing;
                if (ret != ControlFlow.Nothing)
                    return ret;
            }
            erlLine = context.NEXT().Symbol.Line;
            // Next reads a Variant counter back as the body left it (measured, #841). Single, Double, Currency and Date
            // counters, and a Variant the body left holding something that isn't a number, are unmeasured and keep the step.
            if (variant && interpreter.ExecutionContext.TryGetVariable(currentEnv, variable, out var held)
                && VbNumeric.IsCoreNumeric(held.Type))
                counter = held;
            (counter, tested) = ForNextValue(counter, by, context);
        }
        interpreter.ExecutionContext.TryUpdateVariable(currentEnv, variable, counter);
        return default;
    }

    /// <summary>The counter plus the step, in the counter's type, and the value the limit is tested against: the same,
    /// except for a Single, whose sum is tested in Double before it is rounded into the counter (p-code, measured, #983).</summary>
    private static (Vb6Value Counter, Vb6Value Tested) ForNextValue(Vb6Value counter, Vb6Value step, ParserRuleContext context)
    {
        if (counter.Type == Vb6Value.ValueType.Single && step.Type == Vb6Value.ValueType.Single)
        {
            var sum = (double)(float)counter.Value! + (float)step.Value!;
            return (new Vb6Value((float)sum), new Vb6Value(sum));
        }
        var next = VbNumeric.Add(counter, step, context);
        return (next, next);
    }

    /// <summary>
    /// The type a Variant counter takes, from the start, the limit and the step together, in the result table of <c>+</c>
    /// (measured, #983, native and p-code): <c>0 To 1 Step 0.1</c> is a Double from the first pass, <c>1 To 50000 Step 24999</c>
    /// a Long, <c>0@ To 1 Step 0.25</c> a Currency, <c>0& To 1 Step 0.25!</c> a Double, and a Date beside any number a Date.
    /// A String is read as a Double, and a Boolean or Empty as an Integer. With no Step clause, p-code takes the type from the
    /// start and the limit alone, so <c>CByte(1) To CByte(3)</c> counts in Bytes, where native code counts in Integers.
    /// <para>
    /// One p-code answer is not followed: a Decimal start with no Step clause gives p-code a counter that reads as a Date
    /// (<c>For x = CDec(0) To 2</c> counts 00:00:00, 31/12/1899, 01/01/1900). Native code counts Decimals, as it does with any
    /// Step clause, and so does HexIDE, as VB6 plainly meant (the Fidelity Principle).
    /// </para>
    /// </summary>
    private static Vb6Value.ValueType ForVariantType(Vb6Value from, Vb6Value to, Vb6Value? step, ParserRuleContext context)
    {
        var type = ForSumType(ForOperandType(from.Type), ForOperandType(to.Type), context);
        return step is { } s ? ForSumType(type, ForOperandType(s.Type), context) : type;
    }

    // A String bound reaches here already read as a Double (ForBound).
    private static Vb6Value.ValueType ForOperandType(Vb6Value.ValueType t) =>
        t == Vb6Value.ValueType.Boolean || t == Vb6Value.ValueType.EmptyVariant ? Vb6Value.ValueType.Integer : t;

    // The result table of + for two numeric types (docs/vb6-fidelity-oracle/verified-findings.md, "+ and -"): the wider of the two, except that
    // a Long beside a Single is a Double. A Date beside any number is a Date. A Date beside a Decimal was not measured.
    private static Vb6Value.ValueType ForSumType(Vb6Value.ValueType a, Vb6Value.ValueType b, ParserRuleContext context)
    {
        if (a == b)
            return a;
        if (a == Vb6Value.ValueType.Date || b == Vb6Value.ValueType.Date)
            return a == Vb6Value.ValueType.Decimal || b == Vb6Value.ValueType.Decimal
                ? throw new NotImplementedException("A For loop that mixes a Date and a Decimal is not supported: what VB6 does "
                    + "with that pair was not measured.")
                : Vb6Value.ValueType.Date;
        if (a == Vb6Value.ValueType.Long && b == Vb6Value.ValueType.Single || a == Vb6Value.ValueType.Single && b == Vb6Value.ValueType.Long)
            return Vb6Value.ValueType.Double;
        return ForRank(a) >= ForRank(b) ? a : b;
    }

    private static int ForRank(Vb6Value.ValueType t) =>
        t == Vb6Value.ValueType.Byte ? 0
        : t == Vb6Value.ValueType.Integer ? 1
        : t == Vb6Value.ValueType.Long ? 2
        : t == Vb6Value.ValueType.Single ? 3
        : t == Vb6Value.ValueType.Double ? 4
        : t == Vb6Value.ValueType.Currency ? 5
        : 6;

    /// <summary>A For loop's start, limit or step. A numeric or Date counter's is converted into the counter's declared type
    /// as a store into it would convert (banker's rounding, 6 on overflow, 13 for text that is not a number). A Variant
    /// counter's keeps its own type, a String being read as the Double it spells (measured: <c>"1" To "2" Step "0.5"</c>
    /// counts 1, 1.5 and 2 as Doubles). Anything that is not a number is 13.</summary>
    private async Task<Vb6Value> ForBound(VB6Parser.ValueStmtContext bound, VB6Parser.ForNextStmtContext context, Vb6Value.ValueType? counterType)
    {
        var value = await expressionExecutor.EvaluateScalar(bound);
        if (counterType is { } type && (VbNumeric.IsCoreNumeric(type) || type == Vb6Value.ValueType.Date))
            return VbNumeric.CoerceOnStore(value, type, context);
        if (value.Type == Vb6Value.ValueType.String)
        {
            try
            {
                return new Vb6Value(VB6BuiltIns.ToNum(value));
            }
            catch (VBRunTimeException e)
            {
                throw new VBRunTimeException(context, e.Error);
            }
        }
        if (value.Type == Vb6Value.ValueType.Boolean || value.Type == Vb6Value.ValueType.EmptyVariant)
            return value;
        return Vb6Value.TryNumericToDouble(value, out _)
            ? value
            : throw new VBRunTimeException(context, VBStandardError.TypeMismatch);
    }

    // A bound as a number, for the loop state a header that raised steps by: a Boolean as -1 or 0, Empty as 0, a Date as
    // its serial.
    private static double ForNumber(Vb6Value v) =>
        v.Type == Vb6Value.ValueType.EmptyVariant ? 0
        : v.Value is bool b ? (b ? -1 : 0)
        : Vb6Value.TryNumericToDouble(v, out var d) ? d : 0;

    private static NotImplementedException FractionalForRefused() =>
        new("A For loop with a fractional start, limit or step is not supported for a Boolean or String counter: only "
            + "whole numbers are.");

    /// <summary>
    /// A For whose start, limit or step raised under Resume Next (measured, #839). VB6 goes on at the body's first
    /// statement, so the body runs once with the counter never assigned.
    /// <para>
    /// A Variant counter's Next then raises 92, "For loop not initialized", and the loop ends, in native code and p-code
    /// alike, leaving the counter as it was.
    /// </para>
    /// <para>
    /// A typed counter's Next carries on with loop state that was never set this time: the counter as the body left it,
    /// plus the step, tested against the limit. A limit or step not evaluated is the value this statement last had in
    /// this call, or 0 if it has not run before. So a raised step is a <c>Step 0</c>, which loops for ever while the
    /// counter is within the limit; both compiled forms do that, and so does this. Without a Step clause the step is 1 for
    /// an Integer or Long counter. A Byte, Single, Double, Currency or Date counter takes the step this statement last
    /// evaluated, which is 1 after an earlier pass and 0 before one, so `For b = 1 To Boom()` with a Byte `b` at 0 loops
    /// for ever (measured, p-code). A counter of any other type is refused: none was measured.
    /// </para>
    /// <para>
    /// That is p-code's behaviour. HexIDE follows it because it evaluates the three in p-code's order, and because the
    /// VB6 IDE runs a program as p-code. Native code evaluates the limit and step before the start, and steps by 0 for any
    /// counter whose step was not evaluated, so it differs where the start or limit raised. Wherever the two compiled
    /// forms agree, this agrees with both.
    /// </para>
    /// </summary>
    private async Task<ControlFlow> RunUninitialisedFor(VB6Parser.ForNextStmtContext context, string counter,
        Vb6Value.ValueType? counterType, double limit, double step)
    {
        var block = context.block();
        if (counterType is null)
        {
            if (await RunLoopBodyOnce(block, context.NEXT()) is { } left)
                return left;
            throw new VBRunTimeException(context, VBStandardError.ForLoopNotInitialized);
        }
        if (!VbNumeric.IsCoreNumeric(counterType) && counterType != Vb6Value.ValueType.Date)
        {
            // Only a String or a Boolean can be declared and reach here.
            var typeName = counterType == Vb6Value.ValueType.String ? "String" : "Boolean";
            throw new NotImplementedException(
                $"A For whose header raised under On Error Resume Next, with a {typeName} counter, is not supported: " +
                "what VB6 does then was measured for numeric and Date counters only.");
        }
        if (SteppedInItsType(counterType))
        {
            // A Single, Double, Currency or Date counter steps in its own type, as an evaluated header's does (#983).
            var type = counterType;
            var limitValue = VbNumeric.Narrow(new Vb6Value(limit), type, context);
            var stepValue = VbNumeric.Narrow(new Vb6Value(step), type, context);
            var up = step >= 0;
            while (true)
            {
                if (interpreter.Ending || interpreter.DebugController is { IsAborting: true })
                    throw new Debugging.StopExecutionSignal();
                if (await RunLoopBodyOnce(block, context.NEXT()) is { } left)
                    return left;
                interpreter.ExecutionContext.TryGetVariable(currentEnv, counter, out var held);
                var (stored, tested) = ForNextValue(VbNumeric.Narrow(held, type, context), stepValue, context);
                interpreter.ExecutionContext.TryUpdateVariable(currentEnv, counter, stored, context);
                var order = VbNumeric.CompareNumbers(tested, limitValue, context);
                if (up ? order > 0 : order < 0)
                    return ControlFlow.Nothing;
            }
        }

        while (true)
        {
            // The body may be empty, and a Step 0 loop then has no statement of its own at which an abort can reach it.
            if (interpreter.Ending || interpreter.DebugController is { IsAborting: true })
                throw new Debugging.StopExecutionSignal();
            if (await RunLoopBodyOnce(block, context.NEXT()) is { } left)
                return left;

            var next = AdvanceForCounter(context, counter, (long)step);
            if (!(step >= 0 ? next <= limit : next >= limit))
                return ControlFlow.Nothing;
        }
    }

    // Next reads the counter as the body left it (#841). Unmeasured non-integral/nonnumeric body stores
    // keep the normal loop's previous advancement; a raised-header loop keeps its existing refusal.
    private long AdvanceForCounter(VB6Parser.ForNextStmtContext context, string counter, long step, long? previous = null)
    {
        interpreter.ExecutionContext.TryGetVariable(currentEnv, counter, out var current);
        long value;
        if ((previous is null || VbNumeric.IsCoreNumeric(current.Type)) && expressionExecutor.TryUnpack(current, out double c)
            && c == Math.Truncate(c))
            value = (long)c;
        else if (previous is { } fallback)
            value = fallback;
        else
            throw FractionalForRefused();
        var next = value + step;
        interpreter.ExecutionContext.TryUpdateVariable(currentEnv, counter, ForCounterValue(next), context);
        return next;
    }

    /// <summary>
    /// A For Each whose collection raised, or could not be walked, under Resume Next (measured, #839, native and p-code
    /// alike). The body runs once with the element as it was; then Next raises 92 and the loop ends. Next empties the
    /// element first unless the collection is declared as an array.
    /// </summary>
    private async Task<ControlFlow> RunUninitialisedForEach(VB6Parser.ForEachStmtContext context, string element, bool emptiesElement)
    {
        if (await RunLoopBodyOnce(context.block(), context.NEXT()) is { } left)
            return left;
        if (emptiesElement)
            interpreter.ExecutionContext.TryUpdateVariable(currentEnv, element, Vb6Value.Variant);
        throw new VBRunTimeException(context, VBStandardError.ForLoopNotInitialized);
    }

    /// <summary>Run a loop body once. Null when it reaches its <c>Next</c>; otherwise
    /// the flow that left it, with an <c>Exit For</c> spent here.</summary>
    private async Task<ControlFlow?> RunLoopBodyOnce(VB6Parser.BlockContext? block, ITerminalNode next)
    {
        var ret = block == null ? ControlFlow.Nothing : await Visit(block);
        if (ret == ControlFlow.ExitFor)
            return ControlFlow.Nothing;
        if (ret != ControlFlow.Nothing)
            return ret;
        erlLine = next.Symbol.Line;
        return null;
    }

    /// <summary>
    /// Whether a raised collection's declaration is not an array (#969). Read only existing slots and member
    /// tables: evaluating the expression again could repeat the error or its side effects. Null means unavailable.
    /// </summary>
    private bool? DeclaredOtherThanAnArray(VB6Parser.ValueStmtContext collection)
    {
        while (collection is VB6Parser.VsStructContext parentheses && parentheses.valueStmt().Length == 1)
            collection = parentheses.valueStmt(0);
        var call = (collection as VB6Parser.VsICSContext)?.implicitCallStmt_InStmt();
        var name = call?.iCS_S_VariableOrProcedureCall()?.ambiguousIdentifier()?.GetText()
                   ?? call?.iCS_S_ProcedureOrArrayCall()?.ambiguousIdentifier()?.GetText();
        if (name is not null)
        {
            if (interpreter.ExecutionContext.TryInspectVariable(currentEnv, name, out var slot))
                return call?.iCS_S_ProcedureOrArrayCall() is not null && slot.Value is VBArray
                    ? true : null; // An element is not declared as an array, even when a Variant element holds one.
            return interpreter.TryResolveProcedure(name, currentModule, out _, out var proc)
                ? ProcedureIsNotArray(proc) : null;
        }
        if (call?.iCS_S_MembersCall() is not { } members
            || members.dictionaryCallStmt() is not null
            || members.iCS_S_VariableOrProcedureCall() is not { } lead
            || lead.typeHint() is not null || lead.dictionaryCallStmt() is not null)
            return null;
        var chain = members.iCS_S_MemberCall();
        if (chain.Length != 1)
            return null;
        var member = chain[0];
        if (member.iCS_S_ProcedureOrArrayCall() is { } indexed
            && (indexed.argsCall().Length > 1 || indexed.dictionaryCallStmt() is not null)
            || member.iCS_S_VariableOrProcedureCall()?.dictionaryCallStmt() is not null)
            return null;
        var memberName = member.iCS_S_VariableOrProcedureCall()?.ambiguousIdentifier()?.GetText()
                         ?? member.iCS_S_ProcedureOrArrayCall()?.ambiguousIdentifier()?.GetText();
        if (memberName is null)
            return null;
        var leadName = lead.ambiguousIdentifier().GetText();
        if (interpreter.ExecutionContext.TryInspectVariable(currentEnv, leadName, out var target))
        {
            if (target.Value is not VbObject instance)
                return null;
            memberName = VbInterface.ResolveMember(target, instance, memberName);
            if (memberName is null)
                return null;
            var table = instance.ClassDef.PrePass;
            if (table.Procedures.TryGetValue(memberName, out var method))
                return ProcedureIsNotArray(method);
            if (table.Properties.TryGetValue(memberName, out var property) && property.Get is { } getter)
                return ProcedureIsNotArray(getter);
            if (instance.InstanceEnv.TryGetVariableLocation(memberName, out var location))
                return member.iCS_S_ProcedureOrArrayCall() is not null
                    || !interpreter.ExecutionContext.State.IsDeclaredArray(location);
            return null;
        }
        if (interpreter.TryResolveQualifier(leadName, out var qualifier) && qualifier.Module is { } module
            && module.PrePass.Procedures.TryGetValue(memberName, out var qualified)
            && (!qualified.IsPrivate || ReferenceEquals(module, currentModule)))
            return ProcedureIsNotArray(qualified);
        return null;
    }

    private static bool? ProcedureIsNotArray(ProcedureInfo procedure) => procedure.Body?.Parent switch
    {
        VB6Parser.FunctionStmtContext function => function.asTypeClause()?.type().LPAREN() == null,
        VB6Parser.PropertyGetStmtContext getter => getter.asTypeClause()?.type().LPAREN() == null,
        _ => null
    };

    // A For counter value with VB6-faithful typing: an integer magnitude picks Integer vs Long via Vb6Value's
    // magnitude rule (oracle: 12 → Integer, 50001 → Long); a value beyond Long range is a Double, matching VB6's
    // promotion of an out-of-Long-range loop to a Double counter.
    private static Vb6Value ForCounterValue(long v)
        => v is >= int.MinValue and <= int.MaxValue ? new Vb6Value((int)v) : new Vb6Value((double)v);

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

    /// <summary>
    /// Whether the condition of an <c>If</c>, <c>ElseIf</c>, <c>While</c> or <c>Do</c> holds. Null and Empty do not; any
    /// other value is read as <c>CBool</c> reads it, so a number holds when it is not 0, a string when it reads as True or
    /// as a number that is not 0, and any other string, or an Error value, raises 13. Measured for every form, native and
    /// in the IDE (#847): <c>If Len(s) Then</c>, <c>If InStr(s, "x") Then</c> and <c>If flags And 4 Then</c> are ordinary
    /// VB6, and HexIDE refused all of them. An object is read as an operand is (<see cref="ObjectAsValue"/>), so
    /// <c>If Err Then</c> is <c>If Err.Number Then</c> (measured: Then after an error, Else after <c>Err.Clear</c>).
    /// </summary>
    private async Task<bool> ConditionHolds(Antlr4.Runtime.Tree.IParseTree condition, Antlr4.Runtime.ParserRuleContext statement)
    {
        var operand = await expressionExecutor.EvaluateValue(condition);
        try
        {
            // An object is read here rather than by EvaluateScalar, so that its 438 names the statement as the 13 and
            // 91 below do, not the operand.
            var value = ObjectAsValue.Scalar(operand, null);
            // Null is False, where CBool raises 94 (measured). Empty is False to CBool already; it is named here because
            // it is half of the measured rule.
            if (value.IsNull || value.Type == Vb6Value.ValueType.EmptyVariant)
                return false;
            if (value.Type == Vb6Value.ValueType.Error)
                throw new VBRunTimeException(VBStandardError.TypeMismatch);
            return VB6BuiltIns.CBool(value).Value is true;
        }
        catch (VBRunTimeException ex)
        {
            // CBool and Scalar have no statement to name; the condition's does, for the error dialog and
            // get_last_runtime_error.
            throw ex.At(statement);
        }
    }

    public override async Task<ControlFlow> VisitBlockIfThenElse(VB6Parser.BlockIfThenElseContext context)
    {
        // A condition that raised under Resume Next enters its own branch, the Then or the ElseIf it was testing (null; see
        // ConditionMet).
        if (await ConditionMet(context.ifBlockStmt().ifConditionStmt().valueStmt(), context) is not false)
            return await VisitBranch(context.ifBlockStmt().ifBlockBody()?.block());

        foreach (var elseIf in context.ifElseIfBlockStmt())
        {
            if (await ConditionMet(elseIf.ifConditionStmt().valueStmt(), elseIf) is not false)
                return await VisitBranch(elseIf.ifBlockBody()?.block());
        }

        if (context.ifElseBlockStmt() is { } elseBlock)
            return await VisitBranch(elseBlock.ifBlockBody()?.block());

        return default;
    }

    // A branch may be empty, and the grammar then gives it no block. A condition that raised enters even an empty one, and
    // runs nothing (measured: `If Boom() > 0 Then` with an empty Then and an Else runs neither, #839).
    private async Task<ControlFlow> VisitBranch(VB6Parser.BlockContext? block) =>
        block is null ? ControlFlow.Nothing : await Visit(block);

    /// <summary>
    /// <see cref="ConditionHolds"/>, or null when the condition raised under <c>On Error Resume Next</c>. VB6 goes on at
    /// the next statement, and for a block statement that is the first one inside it: the branch the condition guarded,
    /// or a pre-tested loop's body, with the condition asked again next time round. A post-tested loop's next statement
    /// is the one after <c>Loop</c>. Measured in native code and p-code alike, which agree on all of it (#839). Under any
    /// other handling the error propagates as before, and so does a refusal, which is not a run-time error.
    /// </summary>
    private async Task<bool?> ConditionMet(Antlr4.Runtime.Tree.IParseTree condition, Antlr4.Runtime.ParserRuleContext statement)
    {
        // A pre-tested loop's condition, asked again, is on the Do or While line; a Loop Until's and an ElseIf's on their own.
        erlLine = (condition as Antlr4.Runtime.ParserRuleContext ?? statement).Start.Line;
        while (true)
        {
            try
            {
                return await ConditionHolds(condition, statement);
            }
            // Located at the condition, where a halt stops (#955): the statement loop around it would say the statement's line.
            catch (Exception ex) when (ErrorLocation.Note(ex, currentModule.Name, (condition as Antlr4.Runtime.ParserRuleContext ?? statement).Start.Line))
            {
                throw;
            }
            catch (VBRunTimeException ex) when (ResumesNext)
            {
                CaptureError(ex);
                return null;
            }
            catch (VBRunTimeException ex) when (HaltsHere())
            {
                // The condition alone runs again: an ElseIf's without the conditions before it, a Loop Until's without
                // another pass through the body.
                _suspendedAtTopLevel = false;
                if (!await Halt(ex, (condition as Antlr4.Runtime.ParserRuleContext ?? statement).Start.Line))
                    throw;
            }
        }
    }

    public override async Task<ControlFlow> VisitInlineIfThenElse(VB6Parser.InlineIfThenElseContext context)
    {
        // A condition that raised under Resume Next runs the Then part (null; see ConditionMet).
        var conditionMet = await ConditionMet(context.ifConditionStmt(), context) is not false;

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
            // Nor a wait for what the statement before raised, so it is here too: Text1_Change finishes before the rest
            // of the tail runs (#766), and its fault or End then stops the tail below.
            if (interpreter.Events is { } events)
                await events.WaitForRaised();
            // The statements of a joined tail get no pause-gate of their own, so this is the only point at which
            // an abort can reach them. Without it, `If True Then Text1.Text = "x": Label1.Caption = "Saved"` set the
            // caption after Text1_Change had faulted and ended the run.
            if (interpreter.Ending || interpreter.DebugController is { IsAborting: true })
                throw new Debugging.StopExecutionSignal();
            ControlFlow flow;
            while (true)
            {
                try
                {
                    flow = await Visit(stmt);
                    break;
                }
                catch (Exception ex) when (Locate(ex, stmt)) { throw; }
                catch (VBRunTimeException ex) when (!ResumesNext && HaltsHere())
                {
                    // The statement alone runs again, not the statements before it on the line (#1011).
                    _suspendedAtTopLevel = false;
                    if (!await Halt(ex, stmt.Start.Line))
                        throw;
                }
            }
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

    /// <summary>
    /// <c>Input #</c> (#1521): each variable in turn takes the next field, read and converted for its type as
    /// <see cref="VbInput"/> says. At the end of the file it is 62, and a Variant is emptied first, where a typed variable
    /// keeps what it had (measured). Only a plain variable is taken, as for <c>Line Input #</c>.
    /// </summary>
    public override async Task<ControlFlow> VisitInputStmt(VB6Parser.InputStmtContext context)
    {
        RequireHash(context.valueStmt(0), "Input");
        var number = await FileNumber(context.valueStmt(0));
        var files = interpreter.BuiltIns.Files;
        foreach (var target in context.valueStmt().Skip(1))
        {
            var name = target.GetText();
            if (!System.Text.RegularExpressions.Regex.IsMatch(name, "^[A-Za-z][A-Za-z0-9_]*$"))
                throw new NotImplementedException($"Input # into {name} is not implemented: only a plain variable is (#1521)");
            var type = await InputTypeOf(name);
            if (files.ReadField(number, VbInput.ReadingFor(type)) is not { } field)
            {
                if (type is null)
                    await StoreVariable(name, Vb6Value.Variant);
                throw new VBRunTimeException(target, VBStandardError.InputPastEndOfFile);
            }
            await StoreVariable(name, type is { } typed ? VbInput.ToTyped(field, typed, target) : VbInput.ToVariant(field));
        }
        return ControlFlow.Nothing;
    }

    // The type `Input #` reads a variable's field for, or null for a Variant. An undeclared name is created here, as a store
    // would create it, since a DefType may type it.
    private async Task<Vb6Value.ValueType?> InputTypeOf(string name)
    {
        if (!interpreter.TryReadNow(currentEnv, name, out var found, out var current, out var need))
            (found, current) = await interpreter.ReadVariableAfter(currentEnv, name, need!);
        if (!found)
        {
            if (currentModule.PrePass.RequireVariableDefinitions)
                throw new VBVariableNotDefinedException(name);
            AllocImplicitVariant(name);
            interpreter.ExecutionContext.TryGetVariable(currentEnv, name, out current);
        }
        var declared = currentEnv.TryGetVariableLocation(name, out var location)
            ? interpreter.ExecutionContext.State.DeclaredTypeOf(location)
            : null;
        declared ??= current.HasFixedType ? current.Type : null;
        if (current.Value is VBArray || !VbInput.Reads(declared)
            || (declared is null && VbInput.IsOneOf(current.Type, Vb6Value.ValueType.Object, Vb6Value.ValueType.UserDefinedType)))
            throw new NotImplementedException($"Input # into {name}, {(current.Value is VBArray ? "an array" : $"a {declared ?? current.Type}")}, is not implemented (#1521)");
        return declared;
    }

    public override async Task<ControlFlow> VisitKillStmt(VB6Parser.KillStmtContext context)
    {
        interpreter.BuiltIns.Files.Kill(VbDisplay.ToText(await expressionExecutor.EvaluateScalar(context.valueStmt())));
        return ControlFlow.Nothing;
    }

    /// <summary>
    /// The value a Let of a whole array stores: into a dynamic array variable, a ByRef array parameter, or the name of a
    /// Function or Property Get declared to return an array, `As Integer()` or `As Variant()`. Each takes only an array of
    /// exactly its element type, whose bounds it keeps (measured, #986, #998 and #1588, native and p-code, for every element
    /// type, named and through a Variant). What VB6 can see is wrong while compiling is its compile error, raised here as the
    /// statement runs: "Can't assign to array" for an array variable declared with another element type or a scalar whose
    /// type the statement itself shows, and "Invalid use of object" for `Nothing`. Anything else is 13, and the target is
    /// left as it was: Empty, Null, a number, a String, Nothing or an array of another type held by a Variant, or a call
    /// that returns one, such as Array or Split.
    /// <para>
    /// The one conversion VB6 makes is a String into a Byte(), which takes the String's bytes, from a literal, a Const, a
    /// String variable or a Variant: "ab" is a Byte() 0 To 3 holding 97 0 98 0, and "" a Byte() 0 To -1 (measured).
    /// </para>
    /// </summary>
    private Vb6Value ArrayStore(Vb6Value.ValueType declared, Vb6Value value, VB6Parser.LetStmtContext context)
    {
        if (value.Type == declared)
            return value;
        if (declared == ByteArray && value.Type == Vb6Value.ValueType.String)
            return VB6BuiltIns.StringBytes((string?)value.Value ?? "");
        if (value.Type == Vb6Value.ValueType.Object && context.valueStmt() is VB6Parser.VsLiteralContext { } literal
            && literal.literal()?.NOTHING() != null)
            throw new VBCompileErrorException("Invalid use of object");
        var seenWhileCompiling = value.Type.IsArray ? IsDeclaredArrayVariable(context.valueStmt()) : IsTypedBySyntax(context.valueStmt());
        if (seenWhileCompiling)
            throw new VBCompileErrorException("Can't assign to array");
        throw new VBRunTimeException(context, VBStandardError.TypeMismatch);
    }

    // Whether a Let stores a whole array: into a variable declared as one, or into the name of a Function declared to
    // return one. Neither reads an object's default member: Nothing, or an object held by a Variant, is refused as the
    // wrong value rather than given 438 (measured, #1588).
    private bool IntoArray(VB6Parser.LetStmtContext context) =>
        context.implicitCallStmt_InStmt().iCS_S_VariableOrProcedureCall() is { } target && target.dictionaryCallStmt() is null
        && target.GetText() is { } name
        && ((checkedArrayReturn is not null && string.Equals(name, procName, StringComparison.OrdinalIgnoreCase))
            || (currentEnv.TryGetVariableLocation(name, out var location) && interpreter.ExecutionContext.State.IsDeclaredArray(location)));

    private static readonly Vb6Value.ValueType ByteArray = new(Vb6Value.ValueType.Byte, true);

    // Whether the statement shows a value's type, as VB6's compiler sees it: a literal other than Empty, Null or Nothing,
    // with or without a sign or parentheses, a bare name for a variable declared with a scalar type, or a Const. All
    // measured as "Can't assign to array" into a typed-array return. Read from the syntax rather than from the value's
    // fixed flag, which a Variant array element or a Variant field can carry from a literal stored into it (#987); there,
    // VB6 raises 13. Whatever this misses is 13, which can only be wrong on a program VB6 does not compile, such as
    // `F = G()` with G declared to return an Integer.
    private bool IsTypedBySyntax(VB6Parser.ValueStmtContext valueStmt) => valueStmt switch
    {
        VB6Parser.VsLiteralContext literal => literal.literal() is { } l && l.NULL() == null && l.EMPTY_() == null && l.NOTHING() == null,
        VB6Parser.VsNegationContext negation => IsTypedBySyntax(negation.valueStmt()),
        VB6Parser.VsPlusContext plus => IsTypedBySyntax(plus.valueStmt()),
        VB6Parser.VsStructContext parenthesised => parenthesised.valueStmt().Length == 1 && IsTypedBySyntax(parenthesised.valueStmt(0)),
        VB6Parser.VsICSContext ics => ics.implicitCallStmt_InStmt().iCS_S_VariableOrProcedureCall() is { } bare
            && bare.dictionaryCallStmt() == null && NamesATypedScalarOrConst(bare.ambiguousIdentifier().GetText()),
        _ => false,
    };

    private bool NamesATypedScalarOrConst(string name)
    {
        if (!IsOwnDeclaration(name, out var location))
            return false;
        if (interpreter.ExecutionContext.State.DeclaredTypeOf(location) != null || localConsts.Contains(name))
            return true;
        // The module's own Const, unless a local of the same name hides it.
        return !declaredLocals.Contains(name) && currentModule.PrePass.ModuleConsts.ContainsKey(name);
    }

    // A bare name for an array variable declared as one (`Dim a(1) As Long`, `Dim a() As Long`), whose type VB6 knows
    // while compiling, as against a Variant that happens to hold an array.
    private bool IsDeclaredArrayVariable(VB6Parser.ValueStmtContext valueStmt) =>
        valueStmt is VB6Parser.VsICSContext ics
        && ics.implicitCallStmt_InStmt().iCS_S_VariableOrProcedureCall() is { } bare
        && IsOwnDeclaration(bare.ambiguousIdentifier().GetText(), out var location)
        && interpreter.ExecutionContext.State.IsDeclaredArray(location);

    // A name this procedure declared itself, or one the module declared, whose slot therefore says how it was declared. A
    // parameter is neither: a ByRef one reads its caller's slot, whose marks are the caller's declaration. So an untyped
    // `p` given a declared Integer, or a declared Long(), is 13 here, as in VB6 (measured, #997's review), and a typed
    // parameter is 13 too, where VB6 refuses to compile: wrong only on a program VB6 does not compile.
    private bool IsOwnDeclaration(string name, out int location)
    {
        if (!currentEnv.TryGetVariableLocation(name, out location) || currentEnv.Parameters.Contains(name))
            return false;
        return declaredLocals.Contains(name)
            || currentModule.ModuleEnv.TryGetVariableLocation(name, out var moduleLocation) && moduleLocation == location;
    }

    // The value a Let stores, with the refusals every Let makes of it.
    private async Task<Vb6Value> LetValue(VB6Parser.LetStmtContext context, bool intoArray = false)
    {
        var value = await expressionExecutor.EvaluateValue(context.valueStmt());

        if (context.PLUS_EQ() != null || context.MINUS_EQ() != null)
            throw new NotImplementedException("+-/-= not implemented in variable assignment");
        if (value.Type == Vb6Value.ValueType.Object && (intoArray || IsBareUdtArrayField(context.implicitCallStmt_InStmt())))
            return value;   // ArrayStore refuses it

        // BuddyControl is a Variant which accepts a control reference even through Let (#1093).
        if (value.Value is Control
            && context.implicitCallStmt_InStmt().iCS_S_MembersCall() is { } buddyCall
            && buddyCall.iCS_S_VariableOrProcedureCall() is { } buddyLead
            && buddyCall.iCS_S_MemberCall() is [var buddyMember]
            && UdtFieldName(buddyMember).Equals("BuddyControl", StringComparison.OrdinalIgnoreCase)
            && interpreter.ExecutionContext.TryGetVariable(currentEnv, buddyLead.GetText(), out var buddyTarget)
            && buddyTarget.Value is VBUpDown)
            return value;

        if (context.implicitCallStmt_InStmt().iCS_S_MembersCall() is { } selectionTarget
            && selectionTarget.iCS_S_MemberCall().LastOrDefault() is { } lastSelection
            && UdtFieldName(lastSelection).Equals("SelectedItem", StringComparison.OrdinalIgnoreCase)
            && value.Value is Components.InBox.TabStripTab)
            return value;
        // #1485/#878: only the measured direct temporary / App forms clear a Variant on a failed
        // default-value read. Keep the original exception, including its number and message.
        try
        {
            value = ObjectAsValue.Scalar(value, context);
            if (value.Type == Vb6Value.ValueType.Object)
                throw new VBRunTimeException(context, VBStandardError.ObjectDoesntSupportThisPropertyOrMethod);
        }
        catch (VBRunTimeException e) when (e.Error.ErrNo is 438 or 450 // the measured errors; 91 is not measured
                                          && IsMeasuredTemporaryOrApp(context.valueStmt(), value))
        {
            await EmptyMeasuredLetTarget(context);
            throw;
        }

        // A control on the right of a Let stands for its default member in VB6: `s = Text1` reads Text1.Text,
        // `v = Command1` reads its Value, `n = Check1` its Value (all measured). HexIDE has no default members for
        // controls yet (#790), and storing the reference gave a wrong value: a String took the .NET type name, a
        // Variant kept the control. So a Let of one is refused until #790, whatever the target. A Let never stores a
        // reference in VB6, even into an object variable, where it assigns that object's default member. Set, For
        // Each and argument passing do not come here. The in-box objects (Err, App, Debug, a control array) were
        // settled by ObjectAsValue above, and controls are the one kind it leaves alone.
        if (value.Type == Vb6Value.ValueType.Control && value.Value is not null)
            throw new NotImplementedException(
                $"`{context.implicitCallStmt_InStmt().GetText()} = {context.valueStmt().GetText()}` reads a default "
                + "member, which is not implemented (#790). Name the property, or use Set to keep the reference.");
        return value;
    }

    private bool IsMeasuredTemporaryOrApp(VB6Parser.ValueStmtContext rhs, Vb6Value value)
    {
        if (rhs is VB6Parser.VsICSContext ics)
        {
            var call = ics.implicitCallStmt_InStmt();
            if (call.iCS_S_VariableOrProcedureCall() is { } bare && bare.dictionaryCallStmt() is null
                && bare.GetText().Equals("App", StringComparison.OrdinalIgnoreCase) && value.Value is VbApp)
                return true;
            // Exclude member/property reads, Collection items, Me and With reads even if their result
            // happens to be held in the frame. A direct call must have no variable slot of its own.
            var name = call.iCS_S_ProcedureOrArrayCall()?.ambiguousIdentifier()?.GetText()
                ?? call.iCS_S_VariableOrProcedureCall()?.ambiguousIdentifier()?.GetText();
            if (name is null || name.Equals("Me", StringComparison.OrdinalIgnoreCase)
                || call.iCS_S_ProcedureOrArrayCall()?.dictionaryCallStmt() is not null
                || call.iCS_S_VariableOrProcedureCall()?.dictionaryCallStmt() is not null
                || currentEnv.TryGetVariableLocation(name, out _)
                || call.iCS_S_MembersCall() is not null)
                return false;
        }
        else if (rhs is not VB6Parser.VsNewContext)
            return false;

        if (stmtFrames.Count > 0)
            foreach (var temp in stmtFrames.Peek())
                if (value.Value is not null && ReferenceEquals(temp.Value, value.Value))
                    return true;
        return false;
    }

    private async Task EmptyMeasuredLetTarget(VB6Parser.LetStmtContext context)
    {
        var target = context.implicitCallStmt_InStmt();
        if (target.iCS_S_VariableOrProcedureCall() is { } bare && bare.dictionaryCallStmt() is null)
        {
            var name = bare.GetText();
            if (currentEnv.TryGetVariableLocation(name, out var slot)
                && !currentEnv.Parameters.Contains(name)
                && (declaredLocals.Contains(name) || currentModule.Kind == InterpreterModuleKind.Standard)
                && interpreter.ExecutionContext.State.IsVariant(slot)
                && interpreter.ExecutionContext.TryGetVariable(currentEnv, name, out var old))
            {
                interpreter.ExecutionContext.TryUpdateVariable(currentEnv, name, Vb6Value.Variant);
                await interpreter.ReleaseRef(old);
            }
        }
        else if (target.iCS_S_ProcedureOrArrayCall() is { } indexed
                 && indexed.dictionaryCallStmt() is null && indexed.argsCall().Length == 1
                 && !currentEnv.Parameters.Contains(indexed.ambiguousIdentifier().GetText())
                 && (declaredLocals.Contains(indexed.ambiguousIdentifier().GetText())
                     || currentModule.Kind == InterpreterModuleKind.Standard)
                 && interpreter.ExecutionContext.TryGetVariable(currentEnv, indexed.ambiguousIdentifier().GetText(), out var array)
                 && array.Value is VBArray elements
                 && (elements.ElementType is null || elements.ElementType == Vb6Value.ValueType.EmptyVariant))
        {
            var indexes = Subscripts(await expressionExecutor.EvaluateCallArgs(indexed.argsCall(0)));
            var old = elements.GetValue(indexes);
            elements.SetValue(indexes, Vb6Value.Variant);
            await interpreter.ReleaseRef(old);
        }
        // ByRef parameters, class/UDT fields and other destinations remain unmeasured (#1485).
    }

    // Resolve only the measured standard-module targets (#1006, #965), at the statement's point of use.
    // Keep the existing variable-first precedence; other qualifiers and unmeasured targets use the old path.
    private async Task<bool> TryStoreModuleMember(VB6Parser.ICS_S_MembersCallContext target, Vb6Value value,
        bool reference, Antlr4.Runtime.ParserRuleContext context)
    {
        // An enclosing With changes nothing here: only a leading-dot member reads the With object (measured, #1006).
        if (target.iCS_S_VariableOrProcedureCall() is not { } lead
            || interpreter.ExecutionContext.TryInspectVariable(currentEnv, lead.GetText(), out _)
            || !interpreter.TryResolveQualifier(lead.GetText(), out var qualifier)
            || qualifier.Kind != BasicInterpreter.QualifierKind.Module
            || target.dictionaryCallStmt() != null
            || target.iCS_S_MemberCall() is not { Length: 1 } members)
            return false;
        var module = qualifier.Module!;
        var member = members[0];
        var name = UdtFieldName(member);
        if (!module.PrePass.PublicVariables.Contains(name)
            && !(module.PrePass.ModuleConsts.TryGetValue(name, out var constant)
                 && (constant.Public || ReferenceEquals(module, currentModule))))
            return false;
        if (!module.ModuleEnv.TryGetOwnVariableLocation(name, out _))
            return false;
        Vb6Value old;
        if (reference)
        {
            if (!interpreter.ExecutionContext.TryInspectVariable(module.ModuleEnv, name, out old))
                return false;
        }
        else
        {
            var (found, previous) = await interpreter.ReadVariable(module.ModuleEnv, name);
            if (!found) return false;
            old = previous;
        }
        if (MemberHasArgs(member))
        {
            // Only the single index group of a Public array is measured here; leave other targets alone.
            if (reference || old.Value is not VBArray array
                || member.iCS_S_ProcedureOrArrayCall() is not { } call || call.argsCall().Length != 1)
                return false;
            var indices = Subscripts(await expressionExecutor.EvaluateCallArgs(call.argsCall(0)));
            try
            {
                var previous = array.GetValue(indices);
                array.SetValue(indices, interpreter.CopyForStore(value));
                await interpreter.ReleaseRef(previous);
            }
            catch (IndexOutOfRangeException)
            {
                throw new VBRunTimeException(context, VBStandardError.SubscriptOutOfRange);
            }
            return true;
        }
        if (old.Value is VBArray)
            return false; // Whole-array qualified stores are outside these measurements.
        if (!reference && IsObjectReference(old))
            return false; // Let into a qualified object variable is not measured by these issues.
        if (reference)
        {
            // Publish before releasing: Class_Terminate observes the new slot, as for a bare Set.
            interpreter.AddRef(value);
            try
            {
                interpreter.ExecutionContext.TryUpdateVariable(module.ModuleEnv, name, value);
            }
            catch
            {
                await interpreter.ReleaseRef(value);
                throw;
            }
        }
        else
            interpreter.ExecutionContext.TryUpdateVariable(module.ModuleEnv, name, interpreter.CopyForStore(value));
        await interpreter.ReleaseRef(old);
        return true;
    }

    public override async Task<ControlFlow> VisitLetStmt(VB6Parser.LetStmtContext context)
    {
        // `a(1)(0) = 9` and `F()(1) = 7`, whose last index group VB6 evaluates before the value (#1476).
        if (context.implicitCallStmt_InStmt().iCS_S_ProcedureOrArrayCall() is { } chain
            && ExpressionExecutor.ParenGroups(chain) is { Count: > 1 } chainGroups
            && chain.ambiguousIdentifier() is not null && chain.typeHint() is null && chain.dictionaryCallStmt() is null)
            return await LetIntoIndexChain(context, chain, chainGroups);

        var value = await LetValue(context, IntoArray(context));

        if (context.implicitCallStmt_InStmt().iCS_S_VariableOrProcedureCall() is { } varOrProcCall)
        {
            // `c!key = x`: a Collection's item is read-only (measured, #1471, #1492). No other default member takes it yet.
            if (varOrProcCall.dictionaryCallStmt() is { } bang)
            {
                var (_, bangTarget) = await interpreter.ReadVariable(currentEnv, varOrProcCall.ambiguousIdentifier().GetText());
                throw bangTarget.Value is VbCollection bangItems
                    ? bangItems.ItemAssigned(bangTarget, [new Vb6Value(ExpressionExecutor.BangKey(bang))], VbCollection.ItemAssignment.Member)
                    : new NotImplementedException($"`{varOrProcCall.GetText()} = …` is supported only on a Collection, which refuses it");
            }
            var identifier = varOrProcCall.ambiguousIdentifier().GetText();
            // A UDT is a value type: `b = a` stores an independent deep copy so mutating b never touches a.
            if (!interpreter.TryReadNow(currentEnv, identifier, out var letFound, out var oldLet, out var letFoundNeed))
                (letFound, oldLet) = await interpreter.ReadVariableAfter(currentEnv, identifier, letFoundNeed!);
            if (!letFound)
            {
                // In a form's code, a property of the form: `Caption = "Ready"` sets the caption (#995, measured), with or
                // without Option Explicit. It used to make a new variable and leave the caption as it was.
                if (FormMembers.TryBare(interpreter.ExecutionContext, currentEnv, identifier, out var form, out var formMember)
                    && !formMember.IsMethod)
                {
                    await interpreter.Loading((Form: form, Name: identifier, Member: formMember, Value: value),
                        static s => FormMembers.Write(s.Form, s.Name, s.Member, s.Value));
                    return default;
                }
                // VB6 creates an undeclared variable on first use — a procedure-local Variant, Empty, fresh
                // per call (all measured). "Require Variable Declaration" is OFF by default in VB6, so a
                // module without Option Explicit is the ordinary case rather than a legacy one, and this
                // used to raise Err 424 on it. (#171)
                if (currentModule.PrePass.RequireVariableDefinitions)
                    throw new VBVariableNotDefinedException(identifier);
                if (varOrProcCall.typeHint() is { } implicitHint)
                {
                    var type = BaseTypeMapper.Map(implicitHint);
                    interpreter.ExecutionContext.AllocVariable(currentEnv, identifier, new Vb6Value(type), type);
                }
                else
                    AllocImplicitVariant(identifier);
                interpreter.ExecutionContext.TryGetVariable(currentEnv, identifier, out oldLet);
            }
            if (varOrProcCall.typeHint() is { } hint
                && currentEnv.TryGetVariableLocation(identifier, out var hintedSlot)
                && interpreter.ExecutionContext.State.DeclaredTypeOf(hintedSlot) != BaseTypeMapper.Map(hint))
                throw new VBCompileErrorException("Type-declaration character does not match declared data type");
            if (oldLet.Value is VBSSTab)
                return default;

            // A Let into a variable declared As Collection assigns its default member, which needs an index: VB6's "Argument
            // not optional" (measured, #1471). Into any other object variable it is the held object's default member, and a
            // Variant simply takes the value (#1493).
            if (currentEnv.TryGetVariableLocation(identifier, out var letSlot) && IsObjectVariable(currentEnv, identifier, letSlot))
            {
                if (interpreter.ExecutionContext.State.DeclaredClassOf(letSlot) is { } letClass && ExpressionExecutor.IsCollectionType(letClass))
                    throw new VBCompileErrorException("Argument not optional");
                if (oldLet.Value is VBRichTextBox richTextBox)
                {
                    VBProps.RefuseIfRemoved(richTextBox);
                    RichTextBoxMembers.TrySet(richTextBox, "TextRTF", value);
                    return default;
                }
                if (oldLet.Value is ToolbarItem toolbarItem)
                {
                    toolbarItem.SetDefault(value);
                    return default;
                }
                if (oldLet.Value is StatusBarPanel panel)
                {
                    panel.SetText(value);
                    return default;
                }
                if (oldLet.Value is VBUpDown upDown)
                {
                    upDown.TrySetProperty("Value", value);
                    return default;
                }
                RefuseLetIntoObject(oldLet, identifier, context);
            }
            if (checkedArrayReturn is { } declaredReturn && string.Equals(identifier, procName, StringComparison.OrdinalIgnoreCase))
                value = ArrayStore(declaredReturn, value, context);
            else if (currentEnv.TryGetVariableLocation(identifier, out var letLocation)
                     && interpreter.ExecutionContext.State.IsDeclaredArray(letLocation) && oldLet.Type.IsArray)
            {
                // A fixed array cannot be assigned at all (measured, #970, #1588). Named directly it does not compile, whatever
                // the value, a Variant's included: "Can't assign to array", raised here as the statement runs. Through a ByRef
                // parameter sharing it, it is 13, and the array is left as it was. Fixedness is the declaring slot's
                // (ExecutionState.MarkFixedArray), as for ReDim.
                if (interpreter.ExecutionContext.State.IsFixedArray(letLocation))
                {
                    if (currentEnv.Parameters.Contains(identifier))
                        throw new VBRunTimeException(context, VBStandardError.TypeMismatch);
                    throw new VBCompileErrorException("Can't assign to array");
                }
                // A dynamic one takes what a Function declared with its type takes. The slot's array carries the declared
                // type, since nothing else is ever stored there, and a ByRef parameter's is its caller's.
                value = ArrayStore(oldLet.Type, value, context);
            }
            interpreter.ExecutionContext.TryUpdateVariable(currentEnv, identifier, interpreter.CopyForStore(value));
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

            // Enum members are constants, including the shared-name E.E (#1637).
            if (membersCall.iCS_S_VariableOrProcedureCall() is { } enumLead
                && interpreter.EnumIsQualifier(enumLead.GetText(), currentEnv, currentModule)
                && interpreter.TryResolveEnum(enumLead.GetText(), currentModule, out var enumMembers)
                && membersCall.iCS_S_MemberCall() is { Length: 1 } enumTail
                && enumMembers.ContainsKey(ExpressionExecutor.MemberName(enumTail[0])))
                throw new VBCompileErrorException("Assignment to constant not permitted");

            if (await TryStoreModuleMember(membersCall, value, false, context))
                return default;

            // A leading dot (`.Member = value`) has no leading part — resolve against the innermost With target.
            Vb6Value variable;
            string identifier;
            if (membersCall.iCS_S_VariableOrProcedureCall() is { } leadPart)
            {
                identifier = leadPart.GetText();
                bool leadFound;
                if (!interpreter.TryReadNow(currentEnv, identifier, out leadFound, out variable, out var leadNeed))
                    (leadFound, variable) = await interpreter.ReadVariableAfter(currentEnv, identifier, leadNeed!);
                // `Screen.MousePointer = x`, where nothing the program declares is named Screen (#2431).
                if (!leadFound && interpreter.UnclaimedLibraryObject(identifier) is { } libraryObject)
                    (leadFound, variable) = (true, libraryObject);
                if (!leadFound)
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
                await SetUdtField(variable, membersCall.iCS_S_MemberCall(), value, context);
                return default;
            }

            // A chain of objects, `Form2.Text1.Text = x` or `obj.Child.Name = x`: every member but the last is read as an
            // expression reads it, which loads Form2 as a read of its control does, and the last is the one assigned
            // (#822). Where the read ends at a UDT the write would land in a copy and be lost, so that is refused.
            var members = membersCall.iCS_S_MemberCall();
            // Me.mN = value cannot reach a Private class field (vb6.exe SP6, #2121).
            if (members.Length == 1 && !MemberHasArgs(members[0])
                && identifier.Equals("Me", StringComparison.OrdinalIgnoreCase)
                && variable.Value is VbObject { ClassDef.Kind: InterpreterModuleKind.Class } me
                && me.ClassDef.PrePass.PrivateVariables.Contains(UdtFieldName(members[0])))
                throw new VBCompileErrorException("Method or data member not found");
            if (members.Length > 1)
            {
                variable = await expressionExecutor.ResolveMemberChain(variable, members[..^1], 0, context);
                if (variable.Value is VbUdt)
                    throw new NotImplementedException(
                        "Assigning a field of a UDT reached through an object's member is not implemented");
                members = members[^1..];
            }

            // A class instance member value-assign (`obj.Member = x`, no `Set`). A Property Let accessor wins
            // over a raw field write (the value coerces to the Let parameter's declared type inside the call);
            // otherwise write into the instance's own env slot (a UDT value is copied so the object owns it).
            // Nothing → Error 91.
            if (variable.Type == Vb6Value.ValueType.Object)
            {
                if (variable.Value is not VbObject vobj)
                    throw new VBRunTimeException(context, VBStandardError.ObjectVariableOrWithBlockVariableNotSet);
                if (MemberHasArgs(members[0]))
                {
                    var indexedName = UdtFieldName(members[0]);
                    if (string.Equals(variable.DeclaredAs, vobj.ClassName, StringComparison.OrdinalIgnoreCase)
                        && members[0].iCS_S_ProcedureOrArrayCall() is { } indexed && indexed.argsCall().Length == 1
                        && await interpreter.ReadVariable(vobj.InstanceEnv, indexedName) is (true, var field)
                        && expressionExecutor.IsVariantArrayField(vobj, indexedName, field))
                    {
                        var indices = await expressionExecutor.EvaluateCallArgs(indexed.argsCall(0));
                        // VB6 writes into a copy of the field's array, leaving the field unchanged (#1435).
                        // Only an in-bounds store is measured; keep other stores refused.
                        if (field.Value is VBArray array && indices.Count == array.Rank)
                        {
                            try
                            {
                                array.GetValue(Subscripts(indices));
                                return default;
                            }
                            catch (IndexOutOfRangeException) { }
                            catch (VBRunTimeException) { }
                        }
                    }
                    throw new NotImplementedException("Parameterized/indexed object member assignment is not supported");
                }
                var fieldName = VbInterface.ResolveMember(variable, vobj, UdtFieldName(members[0]))
                    ?? throw new VBRunTimeException(context, VBStandardError.MethodOrDataMemberNotFound,
                        UdtFieldName(members[0]));
                if (vobj.ClassDef.PrePass.Properties.TryGetValue(fieldName, out var prop) && prop.Let is { } letter)
                {
                    await interpreter.RunProcedure(vobj.ClassDef, letter, [new CallArg(value, null)], vobj.InstanceEnv, variable, stmtFrames);
                    return default;
                }
                if (!interpreter.TryReadNow(vobj.InstanceEnv, fieldName, out var isField, out var oldFieldLet, out var isFieldNeed))
                    (isField, oldFieldLet) = await interpreter.ReadVariableAfter(vobj.InstanceEnv, fieldName, isFieldNeed!);
                if (isField)
                {
                    // An object field takes a Let as an object variable does (measured, #1493: `h.o = 8` is 438).
                    if (vobj.InstanceEnv.TryGetVariableLocation(fieldName, out var fieldSlot)
                        && !interpreter.ExecutionContext.State.IsVariant(fieldSlot))
                        RefuseLetIntoObject(oldFieldLet, context.implicitCallStmt_InStmt().GetText(), context);
                    interpreter.ExecutionContext.TryUpdateVariable(vobj.InstanceEnv, fieldName, interpreter.CopyForStore(value));
                    // A field that held an object, now overwritten by a scalar via Let, drops that reference.
                    await interpreter.ReleaseRef(oldFieldLet);
                    return default;
                }
                // A form's built-in property is its window's: `Me.Caption = "x"` (#822). One VB6's Form has is set as the bare
                // name is, so a read-only one is VB6's compile error and one HexIDE lacks is not implemented (#995). Any
                // other is set below, as a control's is.
                if (vobj.ClassDef.Kind == InterpreterModuleKind.Form && FormMembers.IsMember(fieldName, out var formMember)
                    && !formMember.IsMethod)
                {
                    await interpreter.Loading((Form: vobj, Name: fieldName, Member: formMember, Value: value),
                        static s => FormMembers.Write(s.Form, s.Name, s.Member, s.Value));
                    return default;
                }
                variable = await interpreter.Loading((Form: vobj, Member: fieldName), static s => s.Form.BuiltInMembersFor(s.Member)) is { } formWindow
                    ? new Vb6Value(formWindow)
                    : throw new VBRunTimeException(context, VBStandardError.MethodOrDataMemberNotFound, fieldName);
            }

            if (members[0].iCS_S_ProcedureOrArrayCall() is { } tabStripCall && tabStripCall.argsCall().Length == 1)
            {
                var tabName = UdtFieldName(members[0]);
                if (variable.Value is VBTabStrip tabStrip && tabName.Equals("Tabs", StringComparison.OrdinalIgnoreCase))
                {
                    var indexes = await expressionExecutor.EvaluateCallArgs(tabStripCall.argsCall(0));
                    if (indexes.Count != 1)
                        throw TabStripMembers.Refused("Tabs index arguments");
                    tabStrip.Model.Item(indexes[0]).Caption = Components.InBox.TabStripTabs.Text(value);
                    return default;
                }
                if (variable.Value is Components.InBox.TabStripTabs tabCollection && tabName.Equals("Item", StringComparison.OrdinalIgnoreCase))
                {
                    var indexes = await expressionExecutor.EvaluateCallArgs(tabStripCall.argsCall(0));
                    if (indexes.Count != 1)
                        throw TabStripMembers.Refused("Tabs index arguments");
                    tabCollection.Owner.Model.Item(indexes[0]).Caption = Components.InBox.TabStripTabs.Text(value);
                    return default;
                }
            }

            if (variable.Value is VBSSTab tab && SSTabMembers.IsIndexed(UdtFieldName(members[0]))
                && members[0].iCS_S_ProcedureOrArrayCall() is { } tabItem && tabItem.argsCall().Length == 1)
            {
                SSTabMembers.WriteIndexed(tab, UdtFieldName(members[0]),
                    await expressionExecutor.EvaluateCallArgs(tabItem.argsCall(0)), value);
                return default;
            }
            // A list's List(i) = text and a ListBox's Selected(i) = b, the control members here that take an index (#821,
            // #1395).
            if (variable.Value is Control listControl && ListMembers.IsList(listControl)
                && members[0].iCS_S_ProcedureOrArrayCall() is { } listItem && listItem.argsCall().Length == 1)
            {
                if (UdtFieldName(members[0]).Equals("List", StringComparison.OrdinalIgnoreCase))
                {
                    ListMembers.WriteItem(listControl, await expressionExecutor.EvaluateCallArgs(listItem.argsCall(0)), value);
                    return default;
                }
                if (listControl is VBListBox listBox && UdtFieldName(members[0]).Equals("Selected", StringComparison.OrdinalIgnoreCase))
                {
                    ListMembers.WriteSelected(listBox, await expressionExecutor.EvaluateCallArgs(listItem.argsCall(0)), value);
                    return default;
                }
            }

            var memberIdentifier = members[0].GetText().TrimStart('.') ?? throw new VBRunTimeException(context, VBStandardError.ObjectRequired, "Null member name");

            if (variable.Value is VBStatusBar statusBar && UdtFieldName(members[0]).Equals("Panels", StringComparison.OrdinalIgnoreCase)
                && members[0].iCS_S_ProcedureOrArrayCall() is { } panelCall && panelCall.argsCall().Length == 1)
            {
                statusBar.Panels.Item(await expressionExecutor.EvaluateCallArgs(panelCall.argsCall(0))).SetText(value);
                return default;
            }

            if (variable.Value is StatusBarPanels panelItems && UdtFieldName(members[0]).Equals("Item", StringComparison.OrdinalIgnoreCase)
                && members[0].iCS_S_ProcedureOrArrayCall() is { } panelItemCall && panelItemCall.argsCall().Length == 1)
            {
                panelItems.Item(await expressionExecutor.EvaluateCallArgs(panelItemCall.argsCall(0))).SetText(value);
                return default;
            }

            // A Collection's members can't be assigned (measured, #1471): `c.Item(1) = x` is 424 once its index is found
            // (#1492), and `c.Count = 5` is VB6's compile error. Any other is 438, as reading it is.
            if (variable.Value is VbCollection memberItems)
                throw UdtFieldName(members[0]).ToLowerInvariant() switch
                {
                    "item" => memberItems.ItemAssigned(variable,
                        members[0].iCS_S_ProcedureOrArrayCall() is { } itemCall && itemCall.argsCall().Length == 1
                            ? await expressionExecutor.EvaluateCallArgs(itemCall.argsCall(0))
                            : [], VbCollection.ItemAssignment.Member),
                    "count" => new VBCompileErrorException("Function call on left-hand side of assignment must return Variant or Object"),
                    _ => new VBRunTimeException(context, VBStandardError.ObjectDoesntSupportThisPropertyOrMethod),
                };

            if (await TabStripMembers.Set(variable.Value, memberIdentifier, value, interpreter, currentModule, currentEnv))
                return default;
            if (members[0].iCS_S_ProcedureOrArrayCall() is { } toolbarCall && toolbarCall.argsCall().Length == 1)
            {
                ToolbarItems? collection = variable.Value switch
                {
                    VBToolbar bar when UdtFieldName(members[0]).Equals("Buttons", StringComparison.OrdinalIgnoreCase) => bar.Buttons,
                    ToolbarItems items when UdtFieldName(members[0]).Equals("Item", StringComparison.OrdinalIgnoreCase) => items,
                    ToolbarItem item when UdtFieldName(members[0]).Equals("ButtonMenus", StringComparison.OrdinalIgnoreCase)
                        && item.TryGetProperty("ButtonMenus", out var menus) => (ToolbarItems)menus.Value!,
                    _ => null,
                };
                if (collection is not null)
                {
                    ((ToolbarItem)collection.Item(await expressionExecutor.EvaluateCallArgs(toolbarCall.argsCall(0))).Value!).SetDefault(value);
                    return default;
                }
            }
            if (variable.Value is ICSharpAsyncPropertyBag asyncPropertyBag
                && await asyncPropertyBag.TrySetPropertyAsync(memberIdentifier, value))
                return default;

            // A CSharp property bag (e.g. Err.Number = …) handles its own get/set by name.
            if (variable.Value is ICSharpPropertyBag propertyBag && propertyBag.TrySetProperty(memberIdentifier, value))
                return default;

            if (variable.Type != Vb6Value.ValueType.Control ||
                variable.Value is not Control control)
                throw new VBRunTimeException(context, VBStandardError.MethodOrDataMemberNotFound, $"Variable {identifier} doesn't have member {memberIdentifier}");
            // Every member of a control Controls.Remove took out is 735 (measured, #1556).
            VBProps.RefuseIfRemoved(control);

            if (control is VBTimer)
                TimerMembers.CheckWrite(memberIdentifier);

            UserControlMembers.Check(variable, memberIdentifier);
            if (UserControlMembers.TrySet(control, memberIdentifier, value))
                return default;

            if (RichTextBoxMembers.TrySet(control, memberIdentifier, value))
                return default;

            // An in-box control's own members, and the extender's it lacks, by its member table (#1082).
            if ((StatusBarMembers.TrySet(control, memberIdentifier, value)
                 ?? ProgressBarMembers.TrySet(control, memberIdentifier, value)) is { } inBox)
                return inBox ? default
                    : throw new VBRunTimeException(context, VBStandardError.MethodOrDataMemberNotFound, $"Variable {identifier} doesn't have member {memberIdentifier}");

            if (SSTabMembers.TrySet(control, memberIdentifier, value))
                return default;

            var props = VBProperties.PropertiesByName.GetValueOrDefault(memberIdentifier, []);

            foreach (var prop in props)
            {
                if (AvaloniaInteroperability.TrySet(control, prop, value))
                    return default;
            }
            // FontName sets the control's font by name (#1141).
            if (FontMembers.TrySet(control, memberIdentifier, value))
                return default;
            // A TextBox's SelStart, SelLength and SelText (#1150).
            if (TextBoxMembers.TrySet(control, memberIdentifier, value))
                return default;
            // A list's ListCount, which is read-only (#821), and a ListBox's Text (#1395).
            if (ListMembers.TrySet(control, memberIdentifier, value))
                return default;
            // Any control's Name and Index, which are read-only at run time (#1431).
            if (IdentityMembers.TrySet(control, memberIdentifier))
                return default;

            throw new VBRunTimeException(context, VBStandardError.MethodOrDataMemberNotFound, $"Variable {identifier} doesn't have member {memberIdentifier}");
        }
        if (context.implicitCallStmt_InStmt().iCS_S_ProcedureOrArrayCall() is { } procedureOrArrayCall)
        {
            if (procedureOrArrayCall.dictionaryCallStmt() != null)
                throw new NotImplementedException("Bang access (!) in an assignment target is not supported: "
                    + procedureOrArrayCall.GetText());
            if (procedureOrArrayCall.baseType() != null||
                procedureOrArrayCall.iCS_S_NestedProcedureCall() != null)
                throw new NotImplementedException();

            if (procedureOrArrayCall.argsCall().Length == 0)
                throw new NotImplementedException();
            if (procedureOrArrayCall.argsCall().Length > 1)
                throw new NotImplementedException($"storing into {procedureOrArrayCall.GetText()} is not implemented (#1476)");

            var identifier = procedureOrArrayCall.ambiguousIdentifier().GetText() ?? throw new VBRunTimeException(context, VBStandardError.ObjectRequired, "Null variable name");

            // Mid STATEMENT: `Mid(target, start[, length]) = replacement`. It routes HERE (not VisitMidStmt) because
            // the grammar's letStmt swallows the trailing `= value`, so `Mid(...)` looks like an array-element write.
            // The $ spellings use the same statement path; MidB counts bytes instead of characters (#2623).
            var byteMid = string.Equals(identifier, "MidB", StringComparison.OrdinalIgnoreCase);
            if ((string.Equals(identifier, "Mid", StringComparison.OrdinalIgnoreCase) || byteMid)
                && (procedureOrArrayCall.typeHint() == null || procedureOrArrayCall.typeHint().GetText() == "$"))
            {
                var midArgs = procedureOrArrayCall.argsCall(0).argCall();
                if (midArgs.Length < 2)
                    throw new VBRunTimeException(context, VBStandardError.ArgumentNotOptionalOrInvalidPropertyAssignment);
                var targetName = midArgs[0].valueStmt().GetText();
                RefuseConstantTarget(targetName, VariableRequired);
                if (!interpreter.ExecutionContext.TryGetVariable(currentEnv, targetName, out var midTarget))
                    throw new VBRunTimeException(context, VBStandardError.ObjectRequired, "Variable " + targetName + " is not declared");
                var startVal = await expressionExecutor.EvaluateScalar(midArgs[1].valueStmt());
                Vb6Value? lenVal = midArgs.Length >= 3 ? (Vb6Value?)await expressionExecutor.EvaluateScalar(midArgs[2].valueStmt()) : null;
                interpreter.ExecutionContext.TryUpdateVariable(currentEnv, targetName,
                    VB6BuiltIns.MidStatementReplace(midTarget, startVal, lenVal, value, byteMid));
                return default;
            }

            if (procedureOrArrayCall.typeHint() != null)
                throw new NotImplementedException();

            var indexes = await expressionExecutor.EvaluateCallArgs(procedureOrArrayCall.argsCall(0));
            // A Collection's item is read-only: `c(1) = x` is 424 once the index is found (measured, #1471, #1492).
            if (await interpreter.ReadVariable(currentEnv, identifier) is (true, { Value: VbCollection heldItems } held))
                throw heldItems.ItemAssigned(held, indexes, VbCollection.ItemAssignment.Index);
            if (await interpreter.ReadVariable(currentEnv, identifier) is (true, { Value: ToolbarItems toolbarItems }))
            {
                ((ToolbarItem)toolbarItems.Item(indexes).Value!).SetDefault(value);
                return default;
            }
            if (await interpreter.ReadVariable(currentEnv, identifier) is (true, { Value: StatusBarPanels panels }))
            {
                panels.Item(indexes).SetText(value);
                return default;
            }
            var indexesAsInt = Subscripts(indexes);

            if (!interpreter.ExecutionContext.TryGetVariable(currentEnv, identifier, out var array))
                throw new VBRunTimeException(context, VBStandardError.ObjectRequired, "Variable " + identifier + " is not declared");

            if (!array.Type.IsArray || array.Value is not VBArray arr)
                throw new VBCompileErrorException("Expected array");

            // An element takes a copy, as a variable does (measured, #970: the row-buffer idiom, `rows(i) = row`, and
            // `outer(0) = inner`). That is also what keeps `v(1) = v` from storing an array into itself. What the element
            // held before is released.
            Vb6Value oldElement;
            try
            {
                oldElement = arr.GetValue(indexesAsInt);
                arr.SetValue(indexesAsInt, interpreter.CopyForStore(value));
            }
            catch (IndexOutOfRangeException)
            {
                throw new VBRunTimeException(procedureOrArrayCall, VBStandardError.SubscriptOutOfRange);
            }
            await interpreter.ReleaseRef(oldElement);

            return ControlFlow.Nothing;
        }
        else
        {
            throw new NotImplementedException($"{context.implicitCallStmt_InStmt()} is not supported");
        }
    }

    /// <summary>
    /// <c>a(1)(0) = 9</c>: the element of an array that another array's element holds, written through to it, so <c>a</c>
    /// changes, where <c>v = a(1): v(1) = 99</c> changes only <c>v</c>'s copy. <c>F()(1) = 7</c> stores into the result of
    /// the call, which nothing keeps: it compiles, F runs, and nothing else changes. VB6 evaluates the last index group, then
    /// the value, then the other groups, the last of them first, then the first: <c>a(w1)(w2)(w3) = rhs</c> is w3, rhs, w2,
    /// w1. All measured (#1476).
    /// </summary>
    private async Task<ControlFlow> LetIntoIndexChain(VB6Parser.LetStmtContext context,
        VB6Parser.ICS_S_ProcedureOrArrayCallContext call, IReadOnlyList<VB6Parser.ArgsCallContext?> groups)
    {
        async Task<List<Vb6Value>> Subscripted(VB6Parser.ArgsCallContext? group) => group is not null
            ? await expressionExecutor.EvaluateCallArgs(group)
            : throw new NotImplementedException($"an empty index group, as in {call.GetText()}, is not implemented (#1476)");

        var last = await Subscripted(groups[^1]);
        var value = await LetValue(context);
        var middle = new List<Vb6Value>[groups.Count - 2];
        for (var g = groups.Count - 2; g >= 1; g--)
            middle[g - 1] = await Subscripted(groups[g]);
        var holder = await expressionExecutor.EvaluateCallOrElement(call, groups[0]) as Vb6Value?
            ?? throw new NotImplementedException($"storing into {call.GetText()} is not implemented (#1476)");
        foreach (var subscripts in middle)
            holder = ExpressionExecutor.IndexResult(holder, subscripts, call);
        if (holder.Value is not VBArray inner)
        {
            // `a(0)(1) = 5` with `a` an Integer array does not compile: "Type mismatch" (measured). A Variant holding no array
            // is unmeasured, and raises 13 as a read of it does.
            if (holder.Value is VbCollection or VbObject || holder.Type == Vb6Value.ValueType.Object)
                throw new NotImplementedException($"storing into {call.GetText()} is not implemented (#1476)");
            if (holder.HasFixedType)
                throw new VBCompileErrorException("Type mismatch");
            throw new VBRunTimeException(call, VBStandardError.TypeMismatch);
        }
        var at = Subscripts(last);
        Vb6Value oldElement;
        try
        {
            oldElement = inner.GetValue(at);
            inner.SetValue(at, interpreter.CopyForStore(value));
        }
        catch (IndexOutOfRangeException)
        {
            throw new VBRunTimeException(call, VBStandardError.SubscriptOutOfRange);
        }
        await interpreter.ReleaseRef(oldElement);
        return ControlFlow.Nothing;
    }

    // The next line of an Input file, without its ending (#1145). At the end of the file it is 62, and the variable keeps
    // what it had (measured), so the line is read before anything is stored.
    public override async Task<ControlFlow> VisitLineInputStmt(VB6Parser.LineInputStmtContext context)
    {
        RequireHash(context.valueStmt(0), "Line Input");
        var number = await FileNumber(context.valueStmt(0));
        var target = context.valueStmt(1).GetText();
        if (!System.Text.RegularExpressions.Regex.IsMatch(target, "^[A-Za-z][A-Za-z0-9_]*$"))
            throw new NotImplementedException($"Line Input # into {target} is not implemented: only a plain variable is (#1145)");
        var line = interpreter.BuiltIns.Files.LineInput(number);
        await StoreVariable(target, new Vb6Value(line));
        return ControlFlow.Nothing;
    }

    public override async Task<ControlFlow> VisitLineLabel(VB6Parser.LineLabelContext context)
    {
        // A label is just a jump target; the pc-driver maps its position. Executing it is a no-op.
        return ControlFlow.Nothing;
    }

    public override async Task<ControlFlow> VisitLoadStmt(VB6Parser.LoadStmtContext context)
    {
        // A form, by its name (#822): its instance is made if it has none, then it loads, hidden (measured).
        if (FindProcedureOrArrayCall(context.valueStmt()) is not { } indexedLoad || indexedLoad.argsCall().Length == 0)
        {
            var target = await expressionExecutor.EvaluateValue(context.valueStmt());
            if (target.Value is VbObject { ClassDef.Kind: InterpreterModuleKind.Form } form)
            {
                if (interpreter.Forms is not { } forms)
                    throw new NotImplementedException($"Load of {form.ClassName} is not implemented here (#822).");
                await forms.Load(form);
                return ControlFlow.Nothing;
            }
            // A menu that is not an element of a menu array can't be loaded: 361 (measured, #1126).
            if (target.Value is BuiltinControls.VBMenu or BuiltinControls.VBMenuSeparator)
                throw new VBRunTimeException(context, VBStandardError.CantLoadOrUnloadThisObject);
        }
        // Otherwise a control-array element, Load Command1(i): the only other thing Load loads.
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
        return (group, ControlArrayIndex(idxArgs[0]));
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
        await FileLock(context.number, context.start, context.end, unlock: false);
        return ControlFlow.Nothing;
    }

    public override async Task<ControlFlow> VisitLsetStmt(VB6Parser.LsetStmtContext context)
    {
        return await JustifyString(context.implicitCallStmt_InStmt(), context.valueStmt(), false);
    }

    private async Task<ControlFlow> JustifyString(VB6Parser.ImplicitCallStmt_InStmtContext target,
        VB6Parser.ValueStmtContext source, bool right)
    {
        if (target.iCS_S_VariableOrProcedureCall() is not { } variable || variable.dictionaryCallStmt() is not null)
            throw new NotImplementedException("LSet/RSet into this target is not implemented");
        var name = variable.ambiguousIdentifier().GetText();
        RefuseConstantTarget(name, VariableRequired);
        var (found, oldValue) = await interpreter.ReadVariable(currentEnv, name);
        if (!found || !currentEnv.TryGetVariableLocation(name, out var slot))
            throw new NotImplementedException("LSet/RSet into an undeclared target is not implemented");
        var declared = interpreter.ExecutionContext.State.DeclaredTypeOf(slot);
        if (declared is not null && declared != Vb6Value.ValueType.String
            || oldValue.Type != Vb6Value.ValueType.String && oldValue.Type != Vb6Value.ValueType.Integer)
            throw new NotImplementedException("LSet/RSet for this target type is not implemented");
        var value = await expressionExecutor.EvaluateScalar(source);
        if (value.IsNull)
            throw new VBRunTimeException(source, VBStandardError.InvalidUseOfNull);
        if (value.Type != Vb6Value.ValueType.String && value.Type != Vb6Value.ValueType.Integer
            && value.Type != Vb6Value.ValueType.Double && value.Type != Vb6Value.ValueType.Boolean
            && value.Type != Vb6Value.ValueType.EmptyVariant)
            throw new NotImplementedException("LSet/RSet conversion from this source type is not implemented");
        var width = VbDisplay.ToText(oldValue).Length;
        var text = VbDisplay.ToText(value);
        // Both statements keep the leftmost characters when the value is too long (#2517).
        var result = text.Length > width ? text[..width] : right ? text.PadLeft(width) : text.PadRight(width);
        interpreter.ExecutionContext.TryUpdateVariable(currentEnv, name, new Vb6Value(result));
        return ControlFlow.Nothing;
    }

    // As in PrePass: every directive was applied before the parse (#1112), and a `#` line left is code to vb6.exe.
    public override Task<ControlFlow> VisitMacroIfThenElseStmt(VB6Parser.MacroIfThenElseStmtContext context) =>
        throw new VBCompileErrorException("Syntax error");

    public override async Task<ControlFlow> VisitMidStmt(VB6Parser.MidStmtContext context)
    {
        throw new NotImplementedException("Mid not implemented");
    }

    public override async Task<ControlFlow> VisitMkdirStmt(VB6Parser.MkdirStmtContext context)
    {
        interpreter.BuiltIns.Files.MakeDirectory(VbDisplay.ToText(await expressionExecutor.EvaluateScalar(context.valueStmt())));
        return ControlFlow.Nothing;
    }

    public override async Task<ControlFlow> VisitNameStmt(VB6Parser.NameStmtContext context)
    {
        interpreter.BuiltIns.Files.Rename(VbDisplay.ToText(await expressionExecutor.EvaluateScalar(context.valueStmt(0))), VbDisplay.ToText(await expressionExecutor.EvaluateScalar(context.valueStmt(1))));
        return ControlFlow.Nothing;
    }

    public override async Task<ControlFlow> VisitOnErrorStmt(VB6Parser.OnErrorStmtContext context)
    {
        // Every form clears Err, so a helper's On Error Resume Next starts clean whatever its caller left (measured, #877).
        ClearError();
        if (context.RESUME() != null)
        {
            errorMode = ErrorMode.ResumeNext;   // On Error Resume Next
            handlerLabel = null;
        }
        else
        {
            // On Error GoTo <target>. `GoTo 0` disables handling; a real label installs a handler.
            var target = context.valueStmt().GetText();
            if (target == "-1")
            {
                // VB6 compiles it: it leaves the handler, so the handler traps the next error, and keeps the mode as it
                // was, so On Error Resume Next still traps after it (measured, #877).
                inHandler = false;
            }
            else if (target == "0")
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

    // Open For Input, Output or Append (#1145). Random and Binary aren't implemented. An Access clause is taken as it
    // stands, and a Lock keeps the file from being opened again by this program; Len = sizes a buffer, which this has none.
    public override async Task<ControlFlow> VisitOpenStmt(VB6Parser.OpenStmtContext context)
    {
        var mode = context.INPUT() != null ? VbFileMode.Input
            : context.OUTPUT() != null ? VbFileMode.Output
            : context.APPEND() != null ? VbFileMode.Append
            : context.BINARY() != null ? VbFileMode.Binary
            : VbFileMode.Random;
        var path = (string)VbNumeric.CoerceOnStore(await expressionExecutor.EvaluateValue(context.valueStmt(0)),
            Vb6Value.ValueType.String, context.valueStmt(0)).Value!;
        var locked = context.LOCK_READ() != null || context.LOCK_WRITE() != null || context.LOCK_READ_WRITE() != null;
        var number = await FileNumber(context.valueStmt(1));
        var recordLength = 128;
        if (context.LEN() != null && mode is VbFileMode.Binary or VbFileMode.Random)
            recordLength = RecordLength(await expressionExecutor.EvaluateValue(context.valueStmt(2)), context.valueStmt(2));
        // The Access clause, and what a Lock clause lets another handle on the file do: Lock Read lets it write, Lock Write
        // lets it read, and none or Shared lets it do both (#1646).
        VbAccess? access = context.READ_WRITE() != null ? VbAccess.ReadWrite
            : context.READ() != null ? VbAccess.Read
            : context.WRITE() != null ? VbAccess.Write
            : null;
        var share = context.LOCK_READ_WRITE() != null ? VbAccess.None
            : context.LOCK_READ() != null ? VbAccess.Write
            : context.LOCK_WRITE() != null ? VbAccess.Read
            : VbAccess.ReadWrite;
        interpreter.BuiltIns.Files.Open(path, mode, locked, number, access, share, recordLength);
        return ControlFlow.Nothing;
    }

    // `Len =` on a Binary or Random file, an Integer: 32768 and more is 6, and below 1 is 5, except -1, which is the 128 a
    // Random file has with no Len (measured; 1.5 and 2.5 are both 2, and "8" is 8). A Binary file checks it and does nothing
    // with it (measured: 3 opens, 0 is 5 and 40000 is 6). On a sequential file it is not measured and is ignored, as before.
    private static int RecordLength(Vb6Value value, VB6Parser.ValueStmtContext context)
    {
        var length = Convert.ToInt32(VbNumeric.CoerceOnStore(value, Vb6Value.ValueType.Integer, context).Value,
            System.Globalization.CultureInfo.InvariantCulture);
        if (length == -1)
            return 128;
        return length >= 1 ? length : throw new VBRunTimeException(context, VBStandardError.InvalidProcedureCall);
    }

    /// <summary>
    /// <c>Print #</c> (#1145), formatted as <see cref="VbPrint"/> says, which <c>Debug.Print</c> shares (#1380).
    /// A bare Print is refused as the form's Print method, which is not implemented yet (#993, #989).
    /// </summary>
    public override async Task<ControlFlow> VisitPrintStmt(VB6Parser.PrintStmtContext context)
    {
        // Bare Print belongs to this form, as Me.Print does (#993). Outside forms its VB6 behaviour is unmeasured.
        if (context.valueStmt() is not { } fileNumber || !fileNumber.GetText().StartsWith('#'))
        {
            // Refused in Me.Print's words, but not by calling the method with no arguments: an output list is not an
            // argument list, and when #989 implements Print it must receive this one, or a bare Print would print a
            // blank line where VB6 prints the text.
            if (currentModule.Kind == InterpreterModuleKind.Form)
                throw new NotImplementedException($"The Print method is not implemented for {currentModule.Name}");
            throw new NotImplementedException("Bare Print in a standard or class module is not implemented; VB6 behaviour is unmeasured (#993)");
        }
        var files = interpreter.BuiltIns.Files;
        var number = await FileNumber(context.valueStmt());
        files.Column(number);   // 52 or 54 before anything is evaluated
        await PrintList(context.outputList()?.children, new FileTarget(files, number), printed: null);
        return ControlFlow.Nothing;
    }

    private sealed class FileTarget(VbFiles files, int number) : VbPrint.ITarget
    {
        public int Column => files.Column(number);
        public int Width => files.PrintWidth(number);
        public void Write(string text) => files.Write(number, text);
    }

    /// <summary>
    /// Prints an output list to <paramref name="target"/>, evaluating each item as it comes, as VB6 does: <c>Print #</c>'s
    /// <c>outputList</c>, or <c>Debug.Print</c>'s arguments, where <c>Spc(n)</c> and <c>Tab(n)</c> parse as calls.
    /// <paramref name="printed"/> sees each item's value, an object already its default member, for the typed channel.
    /// </summary>
    private async Task PrintList(IList<IParseTree>? children, VbPrint.ITarget target, Action<Vb6Value>? printed)
    {
        var joined = false;
        foreach (var child in children ?? [])
        {
            switch (child)
            {
                case VB6Parser.OutputList_ExpressionContext position when position.valueStmt() is null:
                    await PrintPosition(target, position.SPC() != null, position.argsCall()?.argCall().FirstOrDefault()?.valueStmt());
                    joined = false;
                    break;
                case VB6Parser.OutputList_ExpressionContext item:
                    await PrintItem(item.valueStmt());
                    joined = false;
                    break;
                case VB6Parser.ArgCallContext argument when IsPrintPosition(argument.valueStmt(), out var isSpc, out var count):
                    await PrintPosition(target, isSpc, count);
                    joined = false;
                    break;
                case VB6Parser.ArgCallContext argument:
                    await PrintItem(argument.valueStmt());
                    joined = false;
                    break;
                case ITerminalNode { Symbol.Type: VB6Parser.SEMICOLON }:
                    joined = true;
                    break;
                case ITerminalNode { Symbol.Type: VB6Parser.COMMA }:
                    VbPrint.NextZone(target);
                    joined = true;
                    break;
            }
        }
        if (!joined)
            VbPrint.EndLine(target);

        async Task PrintItem(VB6Parser.ValueStmtContext expression)
        {
            var value = await expressionExecutor.EvaluateValue(expression);
            if (printed is not null)
            {
                // An object prints its default member, and one with none is 438 (ObjectAsValue).
                value = ObjectAsValue.Scalar(value, expression);
                printed(value);
            }
            VbPrint.Item(target, value);
        }
    }

    private async Task PrintPosition(VbPrint.ITarget target, bool isSpc, VB6Parser.ValueStmtContext? count)
    {
        if (count is null)
        {
            if (isSpc)
                throw new NotImplementedException("Spc with no count is not implemented");
            VbPrint.NextZone(target);   // a bare Tab
            return;
        }
        var n = Convert.ToInt32(VbNumeric.CoerceOnStore(await expressionExecutor.EvaluateValue(count), Vb6Value.ValueType.Integer, count).Value);
        if (isSpc)
            VbPrint.Spc(target, n);
        else
            VbPrint.Tab(target, n);
    }

    // `Spc(n)`, `Tab(n)` or a bare `Tab` among Debug.Print's arguments, which parse them as calls of those names. Both
    // are keywords, so no procedure of the program can have either name.
    private static bool IsPrintPosition(VB6Parser.ValueStmtContext value, out bool isSpc, out VB6Parser.ValueStmtContext? count)
    {
        isSpc = false;
        count = null;
        if (value is not VB6Parser.VsICSContext call || call.implicitCallStmt_InStmt() is not { } inStmt)
            return false;
        if (inStmt.iCS_S_VariableOrProcedureCall() is { } bare)
            return bare.typeHint() is null && bare.dictionaryCallStmt() is null
                   && bare.ambiguousIdentifier().GetText().Equals("Tab", StringComparison.OrdinalIgnoreCase);
        if (inStmt.iCS_S_ProcedureOrArrayCall() is not { } procedure || procedure.ambiguousIdentifier() is not { } identifier
            || procedure.argsCall().Length != 1 || procedure.argsCall(0).argCall() is not { Length: 1 } arguments)
            return false;
        var name = identifier.GetText();
        isSpc = name.Equals("Spc", StringComparison.OrdinalIgnoreCase);
        if (!isSpc && !name.Equals("Tab", StringComparison.OrdinalIgnoreCase))
            return false;
        count = arguments[0].valueStmt();
        return true;
    }

    // Line Input needs the #, where Open and Close take a number without one.
    private static void RequireHash(VB6Parser.ValueStmtContext number, string statement)
    {
        if (!number.GetText().StartsWith('#'))
            throw new NotImplementedException($"{statement} without a # file number is not implemented");
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
        // An event's arguments cannot be named: `RaiseEvent E(b:=1, a:=2)` and `RaiseEvent E(1, b:=5)` are VB6's "Syntax
        // error" (measured, #1465).
        if (NamedArguments.Any(args))
            throw new VBCompileErrorException("Syntax error");
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
                await interpreter.RunProcedure(listener.ClassDef, handler, args, listener.InstanceEnv, sink.Listener, stmtFrames,
                    isEntry: true);
        }
        return ControlFlow.Nothing;
    }

    public override async Task<ControlFlow> VisitRandomizeStmt(VB6Parser.RandomizeStmtContext context)
    {
        double? seed = null;
        if (context.valueStmt() is { } vs)
        {
            var v = await expressionExecutor.EvaluateScalar(vs);
            seed = Vb6Value.TryNumericToDouble(v, out var d) ? d : 0;
        }
        interpreter.BuiltIns.Reseed(seed);
        return ControlFlow.Nothing;
    }

    public override async Task<ControlFlow> VisitResetStmt(VB6Parser.ResetStmtContext context)
    {
        // A bare Reset can call the module's own Sub, including a private class sibling (#851).
        if (currentModule.PrePass.Procedures.TryGetValue("Reset", out var proc) && !proc.IsFunction)
        {
            await interpreter.CallProcedure("Reset", [], currentModule, callerEnv: currentEnv, callerFrames: stmtFrames);
            return ControlFlow.Nothing;
        }
        interpreter.BuiltIns.Files.CloseAll();
        return ControlFlow.Nothing;
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
        interpreter.BuiltIns.Files.RemoveDirectory(VbDisplay.ToText(await expressionExecutor.EvaluateScalar(context.valueStmt())));
        return ControlFlow.Nothing;
    }

    public override async Task<ControlFlow> VisitRsetStmt(VB6Parser.RsetStmtContext context)
    {
        return await JustifyString(context.implicitCallStmt_InStmt(), context.valueStmt(), true);
    }

    public override async Task<ControlFlow> VisitSavepictureStmt(VB6Parser.SavepictureStmtContext context)
    {
        throw new NotImplementedException("Savepicture not implemented");
    }

    public override async Task<ControlFlow> VisitSaveSettingStmt(VB6Parser.SaveSettingStmtContext context)
    {
        await RunSettingStatement("SaveSetting", context.valueStmt());
        return ControlFlow.Nothing;
    }

    // `Seek #n, position`, or with no #, on a sequential file (#1645): VbFiles.Seek says what it does.
    public override async Task<ControlFlow> VisitSeekStmt(VB6Parser.SeekStmtContext context)
    {
        var number = await FileNumber(context.valueStmt(0));
        var position = await expressionExecutor.EvaluateValue(context.valueStmt(1));
        interpreter.BuiltIns.Files.Seek(number, Convert.ToInt64(
            VbNumeric.CoerceOnStore(position, Vb6Value.ValueType.Long, context.valueStmt(1)).Value, System.Globalization.CultureInfo.InvariantCulture));
        return ControlFlow.Nothing;
    }

    public override async Task<ControlFlow> VisitSelectCaseStmt(VB6Parser.SelectCaseStmtContext context)
    {
        // A selector that raises under Resume Next skips the whole statement, through the per-statement trap. That is
        // what p-code does every time, and what native code does for a numeric selector whose Cases are constants. Native
        // code otherwise goes on to test the Cases against a selector it never computed, or reaches Case Else for an
        // object selector (measured, #839), which is not something to reproduce.
        var value = await expressionExecutor.EvaluateScalar(context.valueStmt());
        foreach (var @case in context.sC_Case())
        {
            erlLine = @case.Start.Line;
            var cond = @case.sC_Cond();
            if (cond is VB6Parser.CaseCondElseContext)
                return await RunCaseBody(@case);
            if (cond is not VB6Parser.CaseCondExprContext condExpr)
                throw new NotImplementedException($"Unepexected Select-Case");

            foreach (var subCond in condExpr.sC_CondExpr())
            {
                // A test that raises under Resume Next runs its own Case's body, and nothing after it is tested: the next
                // statement is the first in that body (measured, #839, native and p-code alike).
                bool matched;
                try
                {
                    matched = await CaseTestMatches(subCond, value);
                }
                catch (VBRunTimeException ex) when (ResumesNext)
                {
                    CaptureError(ex);
                    matched = true;
                }
                if (matched)
                    return await RunCaseBody(@case);
            }
        }

        return ControlFlow.Nothing;
    }

    /// <summary>
    /// Whether one test on a <c>Case</c> line, <c>Is &lt; x</c>, <c>x</c> or <c>x To y</c>, matches the selector. Which rule
    /// applies is the selector's (#987, measured, native and p-code; the oracle's <i>A typed selector and a Variant one</i>):
    /// <list type="bullet">
    /// <item>A TYPED selector, a declared variable, a literal, a typed Function, element, field or intrinsic, converts each
    /// Case value into its own type, as a store would, and a value that will not convert raises: against a declared 5,
    /// <c>Case Is &lt; 5.4</c> does not match and <c>Case Is &gt; "a"</c> is 13.</item>
    /// <item>A VARIANT selector converts nothing, and compares as <see cref="CompareAsVariants"/> describes, against a Case
    /// value that is typed or a Variant's.</item>
    /// <item>Anything else, a source nobody has measured or a Case value of one, compares as it did before any of this was
    /// measured (<see cref="MainCaseMatches"/>), so it cannot change.</item>
    /// </list>
    /// </summary>
    private async Task<bool> CaseTestMatches(VB6Parser.SC_CondExprContext subCond, Vb6Value value)
    {
        // Strings compare as the module's Option Compare says, the Select's own module (measured, #1567).
        var text = currentModule.PrePass.OptionCompareText;
        if (subCond is VB6Parser.CaseCondExprIsContext caseIs)
        {
            var val = await expressionExecutor.EvaluateScalar(caseIs.valueStmt());
            RefuseErrorAgainstOther(value, val);
            var op = caseIs.comparisonOperator();
            if (op.IS() != null || op.LIKE() != null)
                throw new NotImplementedException("Operator " + op.GetText() + " not impleemented");
            if (op.EQ() != null)
                return CaseMatches(value, val, caseIs, text);
            if (op.NEQ() != null)
                return CaseDiffers(value, val, caseIs, text);
            var order = Ruled(value, val)
                ? CompareCase(value, ForSelector(value, val, caseIs), text)
                : value.TryCompareTo(val, text);
            if (op.LT() != null)
                return order is < 0;
            if (op.LEQ() != null)
                return order is <= 0;
            if (op.GT() != null)
                return order is > 0;
            if (op.GEQ() != null)
                return order is >= 0;
            throw new NotImplementedException("Operator " + op.GetText() + " not impleemented");
        }
        if (subCond is VB6Parser.CaseCondExprValueContext caseCondExpr)
        {
            var val = await expressionExecutor.EvaluateScalar(caseCondExpr.valueStmt());
            RefuseErrorAgainstOther(value, val);
            return CaseMatches(value, val, caseCondExpr, text);
        }
        if (subCond is VB6Parser.CaseCondExprToContext caseTo)
        {
            var from = await expressionExecutor.EvaluateScalar(caseTo.valueStmt(0));
            var to = await expressionExecutor.EvaluateScalar(caseTo.valueStmt(1));
            RefuseErrorAgainstOther(value, from);
            RefuseErrorAgainstOther(value, to);
            if (!Ruled(value, from) || !Ruled(value, to))
                return value.TryCompareTo(from, text) is >= 0 && value.TryCompareTo(to, text) is <= 0;
            // Both bounds are converted before either is compared, so a bound that cannot convert raises even when the
            // other has already ruled the selector out.
            var low = ForSelector(value, from, caseTo);
            var high = ForSelector(value, to, caseTo);
            return CompareCase(value, low, text) is >= 0 && CompareCase(value, high, text) is <= 0;
        }
        throw new NotImplementedException($"Unepexected Select-Case");
    }

    /// <summary>An Error value against a value of any other type, a number, a string, Null or Empty, as the selector or as the
    /// Case value, in <c>Case x</c>, <c>Is &gt; x</c> or <c>x To y</c>, is 13, and under Resume Next the Case's body runs
    /// (measured both ways round, native and p-code, #2520). Two Error values compare by their numbers.</summary>
    private static void RefuseErrorAgainstOther(Vb6Value selector, Vb6Value caseValue)
    {
        if ((selector.Type == Vb6Value.ValueType.Error) != (caseValue.Type == Vb6Value.ValueType.Error))
            throw new VBRunTimeException(VBStandardError.TypeMismatch);
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

    /// <summary>Does a case expression match the selector, by the rule from before #987 measured which selectors are typed?
    /// It still decides every test the measured rules do not (see <see cref="CaseTestMatches"/>), and is right for many of
    /// them, for the reasons below.
    ///
    /// <para>
    /// VB6 coerces the CASE VALUE to the SELECTOR's runtime type and then compares — it does not compare
    /// numerically, and it is not symmetric. Both halves are measured (see <i>Select Case matching</i> in
    /// docs/vb6-fidelity-oracle/select-case-matching--the-coercion-rule.md): a <c>String</c> selector holding <c>"1.0"</c> does NOT match
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
    private static bool MainCaseMatches(Vb6Value selector, Vb6Value caseValue, ParserRuleContext ctx, bool text)
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

        var coerced = VbNumeric.CoerceOnStore(caseValue, selector.Type, ctx);
        return text && selector.Type == Vb6Value.ValueType.String
            ? VbTextCompare.AreEqual((string)selector.Value!, (string)coerced.Value!)
            : coerced.Equals(selector);
    }

    // Whether the measured rules decide this test: a typed selector, whatever the Case value, or a Variant one against a
    // Case value that is typed or a Variant's. Otherwise the test compares as it did before (MainCaseMatches).
    private static bool Ruled(Vb6Value selector, Vb6Value caseValue) =>
        HasFixedScalarType(selector) || selector.HeldAsVariant && (caseValue.HasFixedType || caseValue.HeldAsVariant);

    private static bool HasFixedScalarType(Vb6Value v) => v.HasFixedType && VbNumeric.IsDeclarableScalar(v.Type);

    /// <summary>The Case value as a typed selector's comparison takes it, converted into the selector's type as a store into
    /// a variable of that type converts it (<see cref="VbNumeric.CoerceOnStore"/>); a Variant selector takes it as it is.</summary>
    private static Vb6Value ForSelector(Vb6Value selector, Vb6Value caseValue, ParserRuleContext ctx) =>
        HasFixedScalarType(selector) ? VbNumeric.CoerceOnStore(caseValue, selector.Type, ctx) : caseValue;

    /// <summary>How the selector compares with a Case value <see cref="ForSelector"/> has prepared: below zero, zero or above
    /// zero, or null where the two are not ordered, which matches no test.</summary>
    private static int? CompareCase(Vb6Value selector, Vb6Value prepared, bool text) =>
        HasFixedScalarType(selector) ? selector.TryCompareTo(prepared, text) : CompareAsVariants(selector, prepared, text);

    /// <summary>Whether an equality Case, <c>Case x</c> or <c>Case Is = x</c>, matches the selector.</summary>
    private static bool CaseMatches(Vb6Value selector, Vb6Value caseValue, ParserRuleContext ctx, bool text) =>
        Ruled(selector, caseValue)
            ? CompareCase(selector, ForSelector(selector, caseValue, ctx), text) is 0
            : MainCaseMatches(selector, caseValue, ctx, text);

    /// <summary><c>Case Is &lt;&gt; x</c>. Null differs from nothing (measured: a Null selector is not <c>Is &lt;&gt; 1</c>).
    /// An object, array or UDT against a Variant selector is compared for identity, as it was before any of this was
    /// measured.</summary>
    private static bool CaseDiffers(Vb6Value selector, Vb6Value caseValue, ParserRuleContext ctx, bool text)
    {
        if (!Ruled(selector, caseValue))
            return !MainCaseMatches(selector, caseValue, ctx, text);
        if (!HasFixedScalarType(selector) && (KindOf(selector) == CaseKind.Other || KindOf(caseValue) == CaseKind.Other))
            return !selector.Equals(caseValue);
        return CompareCase(selector, ForSelector(selector, caseValue, ctx), text) is { } order && order != 0;
    }

    /// <summary>
    /// A Variant selector against a Case value, neither converted into the other's type. Measured, native and p-code
    /// (docs/vb6-fidelity-oracle/a-typed-selector-and-a-variant-one.md, <i>A typed selector and a Variant one</i>):
    /// <list type="bullet">
    /// <item>Against a TYPED Case value, a literal, a declared variable or a typed Function's result, the selector is read
    /// as that type. Against a String it is text, so 5 is not <c>Is &lt; "10"</c> and <c>Is = "5"</c> matches. Against a
    /// number it is a number, a string selector parsed as one without rounding, so "3" is <c>Is &lt; 9</c> and "2.5" is not
    /// <c>Case 2</c>. Against a Boolean it is read as CBool reads it, so "True" and "5" are <c>Case True</c>. A string that
    /// does not read as the Case value's type is above it, and raises nothing: "abc" is <c>Is &gt; 5</c>, and "True" is
    /// not <c>Case -1</c>.</item>
    /// <item>Against a VARIANT Case value, any number is below any string, whether or not the string reads as a number: 10
    /// is not <c>Is &gt; w</c> with w holding "9", and 5 is not <c>Case w</c> with w holding "5".</item>
    /// <item>Either way, Empty is 0 against a number and "" against a string, a Boolean is -1 or 0, so True is below 0, and
    /// Null is below, above and equal to nothing.</item>
    /// </list>
    /// </summary>
    private static int? CompareAsVariants(Vb6Value selector, Vb6Value caseValue, bool text)
    {
        if (selector.Type == Vb6Value.ValueType.Error && caseValue.Type == Vb6Value.ValueType.Error)
            return selector.TryCompareTo(caseValue);
        var s = KindOf(selector);
        var c = KindOf(caseValue);
        if (s == CaseKind.Null || c == CaseKind.Null)
            return null;
        if (s == CaseKind.Other || c == CaseKind.Other)
            return selector.Equals(caseValue) ? 0 : null;
        if (caseValue.HasFixedType && c != CaseKind.Empty)
        {
            if (c == CaseKind.Text)
                return CompareText(TextOf(selector), (string)caseValue.Value!, text);
            if (s != CaseKind.Text)
                return NumberOf(selector).CompareTo(NumberOf(caseValue));
            var read = caseValue.Type == Vb6Value.ValueType.Boolean
                ? TryRead(() => VB6BuiltIns.CBool(selector))
                : TryRead(() => new Vb6Value(VB6BuiltIns.ToNum(selector)));
            return read is { } number ? NumberOf(number).CompareTo(NumberOf(caseValue)) : 1;
        }
        if (s == CaseKind.Number && c == CaseKind.Text)
            return -1;
        if (s == CaseKind.Text && c == CaseKind.Number)
            return 1;
        if (s == CaseKind.Text || c == CaseKind.Text)
            return CompareText(TextOf(selector), TextOf(caseValue), text);
        return NumberOf(selector).CompareTo(NumberOf(caseValue));
    }

    private enum CaseKind { Empty, Null, Number, Text, Other }

    private static CaseKind KindOf(Vb6Value v) =>
        v.IsMissing ? CaseKind.Other
        : v.Type == Vb6Value.ValueType.EmptyVariant ? CaseKind.Empty
        : v.IsNull ? CaseKind.Null
        : v.Type == Vb6Value.ValueType.String ? CaseKind.Text
        : v.Value is bool || Vb6Value.TryNumericToDouble(v, out _) ? CaseKind.Number
        : CaseKind.Other;

    // Empty is "", and a number or a Boolean is written as CStr writes it.
    private static int CompareText(string a, string b, bool text) =>
        text ? VbTextCompare.Compare(a, b) : Math.Sign(string.CompareOrdinal(a, b));

    private static string TextOf(Vb6Value v) =>
        v.Type == Vb6Value.ValueType.EmptyVariant ? "" : v.Value as string ?? VbDisplay.ToText(v);

    // Empty is 0, a Boolean -1 or 0, and a Date its serial.
    private static double NumberOf(Vb6Value v) =>
        v.Value is bool b ? (b ? -1 : 0) : Vb6Value.TryNumericToDouble(v, out var d) ? d : 0;

    // A string read as a number or a Boolean, or null where it does not read as one (13).
    private static Vb6Value? TryRead(Func<Vb6Value> read)
    {
        try
        {
            return read();
        }
        catch (VBRunTimeException e) when (e.Error.ErrNo == VBStandardError.TypeMismatch.ErrNo)
        {
            return null;
        }
    }

    public override async Task<ControlFlow> VisitSendkeysStmt(VB6Parser.SendkeysStmtContext context)
    {
        throw new NotImplementedException("Sendkeys not implemented");
    }

    public override async Task<ControlFlow> VisitSetattrStmt(VB6Parser.SetattrStmtContext context)
    {
        interpreter.BuiltIns.Files.SetAttributes(VbDisplay.ToText(await expressionExecutor.EvaluateScalar(context.valueStmt(0))), Convert.ToInt32(VbNumeric.CoerceOnStore(await expressionExecutor.EvaluateScalar(context.valueStmt(1)), Vb6Value.ValueType.Long, context).Value));
        return ControlFlow.Nothing;
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
            // The value being replaced, read as it is: `Set Form2 = Nothing` must not make the instance it lets go of (#822).
            if (!interpreter.ExecutionContext.TryInspectVariable(currentEnv, id, out var oldVal))
            {
                // A form's own object property, bare (`Set Picture = ...`, #995): not set through Set anywhere yet, as
                // `Set Me.Picture` isn't either.
                if (FormMembers.TryBare(interpreter.ExecutionContext, currentEnv, id, out var form, out _))
                    throw new NotImplementedException($"Set on {form.ClassName}'s own {id} is not implemented");
                // An undeclared name is made on its first use, as a Let makes it (#171), a Variant or the DefType's type
                // (measured, #169: `Set o1 = New Collection` under DefObj, and with none). It was 424.
                if (currentModule.PrePass.RequireVariableDefinitions)
                    throw new VBVariableNotDefinedException(id);
                AllocImplicitVariant(id);
                interpreter.ExecutionContext.TryInspectVariable(currentEnv, id, out oldVal);
            }
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
            // A control held in a form's WithEvents variable hears its events too (#1556), but a control array element can't
            // be held (459, measured), and a class module's variable holding one is not implemented.
            IModuleExecutionRoot? evtRoot = null;
            if (evtListener != null && value.Value is Control heldControl)
            {
                if (heldControl is VBTabStrip)
                    throw TabStripMembers.Refused("WithEvents");
                if (VBProps.GetIndex(heldControl) is not null)
                    throw new VBRunTimeException(context, VBStandardError.ThisComponentDoesntSupportTheSetOfEvents);
                evtRoot = evtListener.FormWindow as IModuleExecutionRoot
                    ?? throw new NotImplementedException($"A class's WithEvents {id} holding a control is not implemented (#1556)");
            }

            // Refcount (Phase 4.2): AddRef the new referent BEFORE releasing the slot's old one — this ordering
            // protects `Set x = x` / aliased assignment, and (since a `New` RHS already ran its Initialize during
            // eval) makes the new object's Initialize precede the old object's Terminate.
            interpreter.AddRef(value);
            // Unadvise the OLD source's event connection BEFORE releasing it (VB6 unadvise-then-release): a
            // Class_Terminate triggered by the release must not still see the sink this Set is removing (else a
            // RaiseEvent during the old source's teardown would reach the just-detached listener).
            if (evtListener != null && oldVal.Value is VbObject oldSrc)
                oldSrc.RemoveSink(evtListener, id);
            if (evtListener != null && oldVal.Value is Control oldControl)
                ControlEvents.Unadvise(oldControl, evtListener, id);
            // Assign the new reference to the slot BEFORE releasing the old one (oracle-verified): a Class_Terminate
            // fired by the release observes the NEW value — `g` points to the new object, not the dying old one — and
            // a reassignment of the slot inside that Terminate is not clobbered by a late slot-write.
            // A Set the slot refuses (13, a class instance into an As Collection, measured) lets go of the hold it took, so
            // a `New` given to it ends with the statement, as in VB6 (#1471).
            try
            {
                interpreter.ExecutionContext.TryUpdateVariable(currentEnv, id, value);
            }
            catch
            {
                await interpreter.ReleaseRef(value);
                throw;
            }
            // Advise the new source (RaiseEvent on it now reaches this listener).
            if (evtListener != null && value.Value is VbObject newSrc)
                newSrc.AddSink(new EventSink(evtMe, id));
            if (evtListener != null && evtRoot != null && value.Value is Control newControl)
                ControlEvents.Advise(newControl, evtListener, evtRoot, id);
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
            if (await TryStoreModuleMember(membersCall, value, true, context))
                return default;
            if (!interpreter.TryReadNow(currentEnv, leadPart.GetText(), out var setLead, out var target, out var setLeadNeed))
                (setLead, target) = await interpreter.ReadVariableAfter(currentEnv, leadPart.GetText(), setLeadNeed!);
            if (setLead && target.Value is VBTimer)
            {
                var timerMembers = membersCall.iCS_S_MemberCall();
                if (timerMembers.Length != 1)
                    throw new NotImplementedException("Set on a Timer member chain is not implemented (#801)");
                TimerMembers.CheckWrite(UdtFieldName(timerMembers[0]));
                throw new NotImplementedException("Set on a Timer member is not measured (#801)");
            }

            if (setLead && target.Value is VBTabStrip or Components.InBox.TabStripTab
                && membersCall.iCS_S_MemberCall() is { Length: > 0 } tabMembers)
            {
                var receiver = tabMembers.Length == 1 ? target
                    : await expressionExecutor.ResolveMemberChain(target, tabMembers[..^1], 0, context);
                var tabName = UdtFieldName(tabMembers[^1]);
                if (await TabStripMembers.Set(receiver.Value, tabName, value, interpreter, currentModule, currentEnv))
                    return default;
                if (receiver.Value is Components.InBox.TabStripTab tab && tab.TrySetProperty(tabName, value))
                    return default;
            }
            if (setLead && target.Value is VBTreeView treeView
                && membersCall.iCS_S_MemberCall().Length == 1
                && !MemberHasArgs(membersCall.iCS_S_MemberCall()[0]))
            {
                treeView.SetObjectProperty(UdtFieldName(membersCall.iCS_S_MemberCall()[0]), value);
                return default;
            }
            if (setLead && target.Value is Components.InBox.TreeNode treeNode
                && membersCall.iCS_S_MemberCall().Length == 1
                && !MemberHasArgs(membersCall.iCS_S_MemberCall()[0]))
            {
                treeNode.SetObjectProperty(UdtFieldName(membersCall.iCS_S_MemberCall()[0]), value);
                return default;
            }
            if (setLead && target.Value is VBToolbar toolbar && membersCall.iCS_S_MemberCall().Length == 1)
            {
                var name = UdtFieldName(membersCall.iCS_S_MemberCall()[0]);
                if (name.ToLowerInvariant() is "imagelist" or "hotimagelist" or "disabledimagelist"
                    && toolbar.TrySetProperty(name, value)) return default;
                VBToolbar.RefusePending(name);
                if (name.Equals("Buttons", StringComparison.OrdinalIgnoreCase)) throw ToolbarItems.Error(383);
            }
            if (setLead && target.Value is ToolbarItem item && membersCall.iCS_S_MemberCall().Length == 1
                && UdtFieldName(membersCall.iCS_S_MemberCall()[0]).Equals("Tag", StringComparison.OrdinalIgnoreCase))
            {
                item.TrySetProperty("Tag", value);
                return default;
            }
            if (setLead && target.Value is Control setControl && membersCall.iCS_S_MemberCall() is { Length: 1 } setMembers)
            {
                var member = UdtFieldName(setMembers[0]);
                UserControlMembers.Check(target, member);
                if (UserControlContainers.TrySet(setControl, member, value))
                    return ControlFlow.Nothing;
            }
            if (setLead && target.Value is VBStatusBar or StatusBarPanel or StatusBarPanels)
            {
                var parts = membersCall.iCS_S_MemberCall();
                if (parts.Length > 1) target = await expressionExecutor.ResolveMemberChain(target, parts[..^1], 0, context);
                var name = UdtFieldName(parts[^1]);
                if (target.Value is ICSharpPropertyBag bag && bag.TrySetProperty(name, value)) return default;
                if (target.Value is VBStatusBar bar && StatusBarMembers.TrySet(bar, name, value) == true) return default;
                throw new NotImplementedException($"Set on StatusBar member {name} is not implemented (#1087)");
            }
            if (setLead && target.Value is VBImageList or ImageListImages or ImageListImage or VBUpDown)
            {
                var imageMembers = membersCall.iCS_S_MemberCall();
                var receiver = await expressionExecutor.ResolveMemberChain(target, imageMembers[..^1], 0, context);
                if (receiver.Value is ICSharpPropertyBag bag && bag.TrySetProperty(UdtFieldName(imageMembers[^1]), value))
                    return default;
                throw new VBRunTimeException(context, VBStandardError.ObjectDoesntSupportThisPropertyOrMethod);
            }
            // `Set s.ActiveForm = Nothing` is 382, naming the member, as a Let of it is (measured, #2431).
            if (setLead && target.Value is VbScreen screen && membersCall.iCS_S_MemberCall().Length == 1)
            {
                screen.TrySetProperty(UdtFieldName(membersCall.iCS_S_MemberCall()[0]), value);
                return default;
            }
            if (!setLead || target.Value is not VbObject targetObj)
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
            if (!interpreter.TryReadNow(targetObj.InstanceEnv, fieldName, out var setField, out var oldField, out var setFieldNeed))
                (setField, oldField) = await interpreter.ReadVariableAfter(targetObj.InstanceEnv, fieldName, setFieldNeed!);
            if (!setField)
            {
                // A form's built-in object property (`Set Me.Picture = ...`) is not set through Set anywhere yet: a
                // control's isn't either. Refused, rather than raising a trappable 438 VB6 would not (#822).
                if (await interpreter.Loading((Form: targetObj, Member: fieldName), static s => s.Form.BuiltInMembersFor(s.Member)) is not null)
                    throw new NotImplementedException($"Set on a form's own {fieldName} is not implemented");
                throw new VBRunTimeException(context, VBStandardError.MethodOrDataMemberNotFound, fieldName);
            }
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
            var setIndexes = await expressionExecutor.EvaluateCallArgs(arrCall.argsCall(0));
            // `Set c(1) = x`, a Collection's item, is 424 once the index is found (measured, #1471, #1492).
            if (await interpreter.ReadVariable(currentEnv, identifier) is (true, { Value: VbCollection heldItems } held))
                throw heldItems.ItemAssigned(held, setIndexes, VbCollection.ItemAssignment.SetIndex);
            var indexes = Subscripts(setIndexes);
            if (!interpreter.ExecutionContext.TryGetVariable(currentEnv, identifier, out var arrayVal))
                throw new VBRunTimeException(context, VBStandardError.ObjectRequired, "Variable " + identifier + " is not declared");
            if (!arrayVal.Type.IsArray || arrayVal.Value is not VBArray arr)
                throw new VBCompileErrorException("Expected array");
            try
            {
                var oldElem = arr.GetValue(indexes);   // release the element's previous occupant (refcount)
                interpreter.AddRef(value);
                try
                {
                    arr.SetValue(indexes, value);      // slot first, then release the old occupant (see above)
                }
                catch
                {
                    await interpreter.ReleaseRef(value);
                    throw;
                }
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
        // A form, as `Unload Me`, by its name (#973) or as `Unload Forms(i)` (#2496): QueryUnload and Unload run inside this
        // statement, and the ones after it run once they have. An operand indexing a control array is its element, as before.
        if (FindProcedureOrArrayCall(context.valueStmt()) is not { } indexed || indexed.argsCall().Length == 0
            || !IndexesControlArray(indexed))
        {
            var target = await expressionExecutor.EvaluateValue(context.valueStmt());
            if (target.Value is VbObject { FormWindow: IModuleExecutionRoot root })
            {
                await root.UnloadAsync(1);
                return ControlFlow.Nothing;
            }
            // An element held by a variable, as `Unload c` in a loop over a form's Controls does (measured, #1060).
            if (target.Value is Avalonia.Controls.Control element && ControlArrayGroup.TryGetArrayOf(element, out var array)
                && BuiltinControls.VBProps.GetIndex(element) is { } elementIndex)
            {
                array.Unload(elementIndex);
                return ControlFlow.Nothing;
            }
        }
        var (group, index) = await ResolveControlArrayTarget(context.valueStmt(), "Unload");
        group.Unload(index);   // Err 340 if missing, Err 362 if a design-time element
        return ControlFlow.Nothing;
    }

    // Whether `name(i)` names a control array, whose element Unload takes without reading it: reading a missing one is 340.
    private bool IndexesControlArray(VB6Parser.ICS_S_ProcedureOrArrayCallContext call) =>
        call.ambiguousIdentifier() is { } name
        && interpreter.ExecutionContext.TryGetVariable(currentEnv, name.GetText(), out var named)
        && named.Value is ControlArrayGroup;

    public override async Task<ControlFlow> VisitUnlockStmt(VB6Parser.UnlockStmtContext context)
    {
        await FileLock(context.number, context.start, context.end, unlock: true);
        return ControlFlow.Nothing;
    }

    private async Task FileLock(VB6Parser.ValueStmtContext number, VB6Parser.ValueStmtContext? start,
        VB6Parser.ValueStmtContext? end, bool unlock)
    {
        var file = await FileNumber(number);
        async Task<long> Record(VB6Parser.ValueStmtContext value)
        {
            var record = await expressionExecutor.EvaluateValue(value);
            if (record.Type != Vb6Value.ValueType.Byte && record.Type != Vb6Value.ValueType.Integer
                && record.Type != Vb6Value.ValueType.Long)
                throw new NotImplementedException("Lock range conversion from this type is not implemented: unmeasured in VB6");
            return Convert.ToInt64(record.Value, System.Globalization.CultureInfo.InvariantCulture);
        }
        var first = start is null ? (long?)null : await Record(start);
        var last = end is null ? (long?)null : await Record(end);
        interpreter.BuiltIns.Files.Lock(file, first, last, unlock);
    }

    private void AllocImplicitVariant(string name) => AllocImplicitVariable(interpreter, currentEnv, currentModule, name);

    // A variable VB6 creates on first use is a Variant, and is marked one for ReDim (ExecutionState.MarkVariant), unless a
    // DefType covers its first letter, which types it as a Dim would (measured, #169). First use is a store, or an argument
    // (#1505).
    internal static void AllocImplicitVariable(BasicInterpreter interpreter, ExecutionEnvironment env, ModuleInfo module, string name)
    {
        if (module.PrePass.DefaultTypeOf(name) is { } defaultType)
        {
            interpreter.ExecutionContext.AllocVariable(env, name, new Vb6Value(defaultType),
                VbNumeric.IsDeclarableScalar(defaultType) ? defaultType : null);
            return;
        }
        interpreter.ExecutionContext.AllocVariable(env, name, Vb6Value.Variant);
        if (env.TryGetVariableLocation(name, out var location))
            interpreter.ExecutionContext.State.MarkVariant(location);
    }

    private string? ObjectArrayClass(VB6Parser.AsTypeClauseContext clause)
    {
        if (clause.type().baseType()?.COLLECTION() != null
            || ExpressionExecutor.IsCollectionType(clause.type().GetText()))
            return "Collection";
        if (clause.type().complexType() is { } complex)
        {
            var (_, name) = SplitTypeName(complex.GetText());
            if (interpreter.Modules.TryGet(name, out var module) && module.Kind == InterpreterModuleKind.Class)
                return module.Name;
        }
        return null;
    }

    private Vb6Value.ValueType ExtractArrayType(VB6Parser.AsTypeClauseContext clause) =>
        ObjectArrayClass(clause) is not null
            ? new Vb6Value.ValueType(Vb6Value.ValueType.Object, true)
            : ExtractType(clause, true);

    public override async Task<ControlFlow> VisitRedimStmt(VB6Parser.RedimStmtContext context)
    {
        var preserve = context.PRESERVE() != null;

        foreach (var redim in context.redimSubStmt())
        {
            var fieldTarget = await UdtArrayField(redim.implicitCallStmt_InStmt());
            var varOrProcCall = redim.implicitCallStmt_InStmt().iCS_S_VariableOrProcedureCall();
            if (varOrProcCall is null && fieldTarget is null)
                throw new NotImplementedException();

            if (varOrProcCall?.dictionaryCallStmt() != null)
                throw new NotImplementedException();
            // `ReDim b(1) As New C1`, each element made on its first use (measured, #435), is an array of objects.
            if (redim.asTypeClause()?.NEW() != null)
                throw new NotImplementedException("An array declared As New is not implemented (#1482)");

            var variableName = varOrProcCall?.ambiguousIdentifier().GetText() ?? fieldTarget!.Name;
            RefuseConstantTarget(variableName, "Expected array");
            if (fieldTarget is null)
                RefuseParamArray(variableName);

            Vb6Value value;
            if (fieldTarget is not null)
                value = fieldTarget.Read();
            else if (!interpreter.ExecutionContext.TryGetVariable(currentEnv, variableName, out value))
                throw new VBCompileErrorException("Unknown variable " + variableName);

            void Store(Vb6Value next)
            {
                if (fieldTarget is not null) fieldTarget.Write(next);
                else interpreter.ExecutionContext.TryUpdateVariable(currentEnv, variableName, next);
            }

            List<(int, int)>? dimensions = await ExtractDimensions(redim.subscripts());
            // A variable declared an array keeps its element type; a Variant becomes a Variant array, unless it already
            // holds a typed one. It was given its own type, which for an Empty Variant was not an array type at all.
            // Only a slot known to be a Variant is treated as one: a slot whose declared type HexIDE does not carry,
            // such as a `b() As Long` parameter, is the typed array it holds (measured: it keeps its type).
            var located = currentEnv.TryGetVariableLocation(variableName, out var redimLoc) && fieldTarget is null;
            var declaredArray = fieldTarget is not null || located && interpreter.ExecutionContext.State.IsDeclaredArray(redimLoc);
            // An untyped or Variant parameter takes the Variant rule below although it shares a caller's declared array,
            // whose type and fixedness stay the slot's (measured, #970).
            var variant = located && (currentEnv.VariantParameters.Contains(variableName)
                || !declaredArray && interpreter.ExecutionContext.State.IsVariant(redimLoc));
            // `ReDim x%(3)` names the array `x`, whose elements must be the character's type: an Integer array's may be
            // redimensioned so, and a Long array's is VB6's compile error (measured, #742).
            if (varOrProcCall?.typeHint() is { } redimHint && declaredArray
                && (value.Value as VBArray)?.ElementType != BaseTypeMapper.Map(redimHint))
                throw new VBCompileErrorException("Type-declaration character does not match declared data type");
            Vb6Value.ValueType type = redim.asTypeClause() != null ? ExtractArrayType(redim.asTypeClause())
                : declaredArray || value.Type.IsArray ? value.Type
                : new Vb6Value.ValueType(Vb6Value.ValueType.EmptyVariant, true);

            var objectClass = redim.asTypeClause() is { } objectClause
                ? ObjectArrayClass(objectClause) : (value.Value as VBArray)?.DeclaredClass;
            Vb6Value NewArray(List<(int, int)>? bounds = null)
            {
                Vb6Value result;
                if (redim.asTypeClause()?.type()?.complexType() is { } ct
                    && SplitTypeName(ct.GetText()) is var named
                    && interpreter.TryResolveType(named.Name, currentModule, named.Qualifier, out var def))
                    result = interpreter.NewUdtArray(def, bounds);
                else if (value.Value is VBArray { ElementType: var et } previous
                    && et == Vb6Value.ValueType.UserDefinedType && redim.asTypeClause() is null)
                    result = value.WithArray(previous.NewWithBounds(bounds));
                else
                    result = bounds is null ? new Vb6Value(type) : new Vb6Value(type, bounds);
                ((VBArray)result.Value!).DeclaredClass = objectClass;
                return result;
            }

            if (dimensions == null)
                throw new VBCompileErrorException("Dimensions required");

            // A fixed array reached through a parameter is 10, "This array is fixed or temporarily locked", and is left
            // as it was: measured for a local and a module-level fixed array, a typed and an untyped array parameter, an
            // untyped `Sub G(v)`, `v As Variant`, ReDim Preserve (#1459: `GF f` is 10 inside and leaves (0 To 3)), and the same
            // bounds again. Its bounds are converted first (`ReDim b("x")` is 13), and the 10 comes before an inverted range's 9
            // (`ReDim b(5 To 2)` is 10). Named directly, a local or a module-level one, it does not compile: "Array already
            // dimensioned" (measured). It was silently replaced. Fixedness is the declaring slot's
            // (ExecutionState.MarkFixedArray), not the array's: a Let into another slot does not carry it (`d = f` then
            // `ReDim d(10)` works, measured), and a ByRef parameter that shares the caller's slot reaches the caller's
            // mark, a `v As Variant` given an array included (#970).
            if (located && interpreter.ExecutionContext.State.IsFixedArray(redimLoc)
                || fieldTarget is not null && value.Value is VBArray { IsDynamic: false })
            {
                if (currentEnv.Parameters.Contains(variableName))
                    throw new VBRunTimeException(context, VBStandardError.ThisArrayIsFixedOrTemporarilyLocked);
                throw new VBCompileErrorException("Array already dimensioned");
            }
            // So is a dynamic array one of whose elements a running call holds ByRef, and a Variant holding one, until the
            // call returns (measured, #1455: `ReDim d(5)` inside `PRD d(1)` is 10 and leaves UBound 2, and ReDim after works).
            // ReDim Preserve too, in the same order: `ReDim Preserve d("x")` is 13, and `d(5 To 2)` 10.
            if (value.Value is VBArray { IsLocked: true })
                throw new VBRunTimeException(context, VBStandardError.ThisArrayIsFixedOrTemporarilyLocked);

            // Different object declarations share the Object ValueType, but must not silently lose their constraint.
            if (preserve && redim.asTypeClause() is not null && value.Value is VBArray oldObjects
                && !string.Equals(objectClass, oldObjects.DeclaredClass, StringComparison.OrdinalIgnoreCase))
                throw new NotImplementedException("Changing an object array's declared class with ReDim Preserve is not implemented");
            if (preserve && await TryPreserve(context, value, dimensions, redim.asTypeClause() != null ? type : null, variant))
                continue;

            // An upper bound below its lower raises 9, and an array is left erased, typed as it was. Measured: ReDim
            // b(-1), b(5 To 2) and b(0 To -1) each raise 9, and UBound(b) then raises 9 too, for a declared array and
            // for a `b() As Long` parameter alike. A Variant is different, measured too: if any dimension's upper bound
            // is exactly one below its lower (`ReDim v(-1)`, `v(5 To 4)`, `v(5 To 2, 5 To 4)`), it raises 9 and leaves
            // v as it was; otherwise one further below (`v(5 To 2)`, `v(0 To 1, 5 To 2)`) raises 7 and empties v. A
            // bound that fails to coerce ("x", Null, beyond a Long) raises before this and leaves the variable alone,
            // as ExtractDimensions does.
            if (dimensions.Exists(d => d.Item2 < d.Item1))
            {
                if (!variant)
                    Store(NewArray());
                else if (!dimensions.Exists(d => d.Item2 == d.Item1 - 1))
                {
                    // Emptied: a Variant to Empty, and a caller's declared array, shared by the parameter, to unallocated.
                    Store(declaredArray ? NewArray() : Vb6Value.Variant);
                    throw new VBRunTimeException(context, VBStandardError.OutOfMemory);
                }
                throw new VBRunTimeException(context, VBStandardError.SubscriptOutOfRange);
            }

            // An array too big to hold is 7, and a declared array is left erased (measured: ReDim b(2) then
            // ReDim b(2147483647) raises 7, and UBound(b) then raises 9).
            Vb6Value redimmed;
            try
            {
                redimmed = NewArray(dimensions);
            }
            catch (VBRunTimeException e) when (e.Error.ErrNo == VBStandardError.OutOfMemory.ErrNo)
            {
                Store(variant && !declaredArray ? Vb6Value.Variant : NewArray());
                throw new VBRunTimeException(context, VBStandardError.OutOfMemory);
            }
            Store(redimmed);
        }

        return ControlFlow.Nothing;
    }

    /// <summary>
    /// ReDim Preserve of an array that is already dimensioned (#1459, measured native and p-code): only the last dimension's
    /// upper bound may change, and the array keeps its type and every element still in range. An undimensioned dynamic array
    /// is dimensioned as a plain ReDim does it, so this returns false for one and the caller goes on. Measured:
    /// <list type="bullet">
    /// <item>`ReDim a(2): a(2) = 7: ReDim Preserve a(4)` keeps 7, and a(4) is a Long 0; a String array's new element is "",
    /// a Variant array's Empty; an array of a Type keeps its elements' fields, and an object array its references.</item>
    /// <item>A shrink drops the elements past the new bound, and an object held only there terminates during the statement.
    /// One element is the least: `ReDim Preserve c(-1)` on (0 To 4) is 9, and so is an inverted range.</item>
    /// <item>Any other bound changing is 9, and leaves the array as it was: an earlier dimension's (`m(2, 2)` on m(1, 2)),
    /// the last one's lower (`bb(2 To 5)` and `bb(0 To 5)` on bb(1 To 3), and `m(1, 1 To 2)`), and the number of
    /// dimensions either way. That comes before the size: `a(1 To 2147483647)` on a(2) is 9.</item>
    /// <item>Too big to hold is 7, and leaves the array as it was, where a plain ReDim erases it: `a(2147483647)` on a(2).</item>
    /// <item>A Variant holding an array is preserved in place, keeping its type (`tv = a` then `tv(5)` is a Long); an Empty
    /// Variant, and one holding 5, is 13.</item>
    /// <item>An `As` clause naming the array's own type is accepted; another is VB6's compile error "Can't change data types
    /// of array elements" on a declared array, and 13 on a Variant holding one (`Variant()` As Long, `Long()` As Integer).</item>
    /// </list>
    /// </summary>
    private async Task<bool> TryPreserve(VB6Parser.RedimStmtContext context, Vb6Value value, List<(int, int)> dimensions,
        Vb6Value.ValueType? asType, bool variant)
    {
        if (value.Value is not VBArray kept)
            throw new VBRunTimeException(context, VBStandardError.TypeMismatch);
        if (asType is { } named && named != value.Type)
        {
            if (variant)
                throw new VBRunTimeException(context, VBStandardError.TypeMismatch);
            throw new VBCompileErrorException("Can't change data types of array elements");
        }
        if (!kept.IsDefined)
            return false;

        var rank = kept.Rank;
        if (dimensions.Count != rank)
            throw new VBRunTimeException(context, VBStandardError.SubscriptOutOfRange);
        for (var d = 0; d < rank - 1; d++)
            if (dimensions[d] != (kept.LowerBound(d + 1), kept.UpperBound(d + 1)))
                throw new VBRunTimeException(context, VBStandardError.SubscriptOutOfRange);
        var (lower, upper) = dimensions[^1];
        if (lower != kept.LowerBound(rank) || upper < lower)
            throw new VBRunTimeException(context, VBStandardError.SubscriptOutOfRange);

        List<Vb6Value> dropped;
        try
        {
            dropped = kept.Preserve(upper);
        }
        catch (VBRunTimeException e) when (e.Error.ErrNo == VBStandardError.OutOfMemory.ErrNo)
        {
            throw new VBRunTimeException(context, VBStandardError.OutOfMemory);
        }
        foreach (var element in dropped)
            await interpreter.ReleaseRef(element);
        return true;
    }

    public override async Task<ControlFlow> VisitVariableStmt(VB6Parser.VariableStmtContext context)
    {
        // `Dim`, `Private`, and `Public` module/local declarations all allocate the same way (visibility isn't
        // tracked). Class fields are usually declared `Private`/`Public`. A procedure's `Static` was declared at entry
        // with the rest; outside a procedure it does not compile (measured, #1491).
        foreach (var subStmt in context.variableListStmt().variableSubStmt())
        {
            if (subStmt.WITHEVENTS() != null)
                // Re-seed the field per instance; Set registers the sink recorded by PrePass.
                interpreter.ExecutionContext.AllocVariable(currentEnv, subStmt.ambiguousIdentifier().GetText(), Vb6Value.Nothing);
            else if (context.DIM() != null || context.visibility() != null || Statics is not null)
                await DeclareLocal(subStmt);
            else
                throw new VBCompileErrorException("Invalid outside procedure");
        }

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
                // `Dim s$` is `Dim s As String`, below; with an As clause as well it is VB6's compile error (measured, #742).
                if (subStmt.typeHint() != null && subStmt.asTypeClause() != null)
                    throw new VBCompileErrorException("Expected: end of statement");
                // A Dim allocates its local exactly once per activation; a re-executed Dim (inside a loop) keeps the
                // existing value. The first Dim rebinds the name to a fresh slot, so it still shadows a module var.
                if (!declaredLocals.Add(subStmt.ambiguousIdentifier().GetText()))
                    return;
                // A Static local is made at its procedure's first call, and every later call binds the name to that
                // same slot, which the call does not release (#1491).
                var keep = Statics is not null && StaticLocals.Declares(subStmt) ? Statics : null;
                if (keep is not null && keep.TryGet(subStmt, out var kept))
                {
                    currentEnv.DefineVariable(subStmt.ambiguousIdentifier().GetText(), kept);
                    return;
                }
                bool isArray = false;
                List<(int, int)>? dimensions = null;
                if (subStmt.LPAREN() != null && subStmt.RPAREN() != null) // array
                {
                    isArray = true;
                    if (ownedSlots == null && subStmt.subscripts() is { } bounds)
                        await ModuleLevelBounds.Check(interpreter, currentModule, currentEnv, bounds, subStmt);
                    dimensions = await ExtractDimensions(subStmt.subscripts());
                    // A dimension whose upper bound is below its lower is "Range has no values", at module and procedure
                    // level, from a literal or a Const (measured: `Dim d(-1)`, `Dim d(1 To N)` with N 0, `Dim d(5, 2 To 1)`).
                    // It built an array with no elements, so UBound read -1 and loops ran no passes.
                    if (dimensions?.Exists(d => d.Item2 < d.Item1) == true)
                        throw new VBCompileErrorException("Range has no values");
                }

                var asType = subStmt.asTypeClause();
                Vb6Value value;
                // The class or interface name a class-typed Dim was declared with, kept because an object slot
                // has nothing to coerce and the NAME is the whole of its declared type (see ExecutionState).
                string? declaredClass = null;
                // `Dim o As New C1`, `Dim c As New Collection`: Nothing until its first use makes the object (#435).
                string? autoNew = asType?.NEW() != null ? AutoNewClass(asType, isArray) : null;
                if (isArray && asType?.type()?.complexType() is { } arrayElement
                    && SplitTypeName(arrayElement.GetText()) is var arrayName
                    && interpreter.TryResolveType(arrayName.Name, currentModule, arrayName.Qualifier, out var arrayDef))
                {
                    value = interpreter.NewUdtArray(arrayDef, dimensions, dimensions is null);
                }
                else if (autoNew is not null)
                {
                    value = Vb6Value.Nothing;
                    declaredClass = autoNew;
                }
                else if (asType?.type()?.complexType() is { } ct && (!isArray || ObjectArrayClass(asType) is not null) && asType.NEW() == null && asType.fieldLength() == null)
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
                    // An IN-BOX enum is a type too: `Dim x As VbMsgBoxResult`, and — unlike a user enum,
                    // where a module-qualified type name is illegal — the LIBRARY may qualify it:
                    // `Dim x As VBA.VbMsgBoxResult` is measured legal. A constant MODULE is not a type,
                    // which is why this asks for an enum specifically: `Dim x As Constants` is illegal in
                    // VB6 ("Automation type not supported"), and falls through to the throw below.
                    else if (qualifier is null
                             ? VB6InBoxLibraries.TryEnumType(typeName, out _)
                             : VB6InBoxLibraries.TryEnumType(qualifier, typeName, out _))
                        value = new Vb6Value(0L);
                    // A referenced control library's enum is a type as well (#1082): `Dim s As ScrollingConstants`, and
                    // `As MSComctlLib.ScrollingConstants`. Without this the first was an object of a referenced library's
                    // type, Nothing, where VB6's is a Long.
                    else if (IsControlLibraryEnum(qualifier, typeName))
                        value = new Vb6Value(0L);
                    else if (interpreter.Modules.TryGet(typeName, out var classMod) && classMod.Kind == InterpreterModuleKind.Class)
                    {
                        value = Vb6Value.Nothing;
                        declaredClass = classMod.Name;   // the module's own casing, not the Dim's
                    }
                    else if (ExpressionExecutor.IsCollectionType(ct.GetText()))
                    {
                        value = Vb6Value.Nothing;
                        declaredClass = "Collection";
                    }
                    else if (qualifier is null && typeName.Equals("ErrObject", StringComparison.OrdinalIgnoreCase))
                    {
                        value = Vb6Value.Nothing;
                        declaredClass = "ErrObject";
                    }
                    else if (!isArray && interpreter.ControlClassFor(ct.GetText()) is { } controlClass)
                    {
                        value = Vb6Value.Nothing;
                        declaredClass = controlClass;
                    }
                    else if (IsObjectTypeWithoutClass(qualifier, typeName, out var aliasScalar))
                    {
                        // An object of a type the interpreter has no class for: VB's own (`As Form`, `As Control`,
                        // `As TextBox`, `As StdFont`), a form of the project, or a referenced library's (#822). The
                        // slot holds Nothing until it is Set, whatever the type, so knowing it is a type is enough.
                        // A stdole alias for a scalar (`As OLE_COLOR`) is that scalar.
                        value = aliasScalar is { } scalar ? new Vb6Value(scalar) : Vb6Value.Nothing;
                    }
                    else
                        throw new VBCompileErrorException("User-defined type not defined: " + typeName);
                }
                else if (asType?.NEW() == null && asType?.type()?.baseType() is { } objectType
                         && (objectType.OBJECT() != null || objectType.COLLECTION() != null))
                {
                    // `Dim o As Object`, `Dim c As Collection`: an object slot, Nothing until Set (#822 review). A Collection's
                    // records its type, so it refuses a class instance (13, measured, #1471).
                    value = Vb6Value.Nothing;
                    declaredClass = objectType.COLLECTION() != null ? "Collection" : isArray ? null : "Object";
                }
                else
                {
                    // With no As clause the type is the type character's, `Dim n%` an Integer (measured, #742), or else the
                    // DefType's for the name's letter, if one covers it (measured, #169). DefObj's is an Object slot, Nothing
                    // until Set, as `As Object` makes.
                    var defaultType = subStmt.typeHint() is { } hint ? BaseTypeMapper.Map(hint)
                        : asType is null ? currentModule.PrePass.DefaultTypeOf(subStmt.ambiguousIdentifier().GetText()) : null;
                    if (isArray && defaultType == Vb6Value.ValueType.Object)
                        throw new NotImplementedException("An array of objects is not implemented (#1482)");
                    var type = defaultType is not null
                        ? (isArray ? new Vb6Value.ValueType(defaultType, true) : defaultType)
                        : ExtractType(asType, isArray);
                    value = dimensions != null ? new Vb6Value(type, dimensions) : new Vb6Value(type);
                    if (dimensions != null && value.Value is VBArray fixedArr) fixedArr.IsDynamic = false;   // `Dim a(N)` = fixed
                }

                // Object elements start as Nothing and keep the scalar declaration's constraint (measured, #1482).
                if (isArray && value.Type == Vb6Value.ValueType.Object)
                {
                    var arrayType = new Vb6Value.ValueType(value.Type, true);
                    value = dimensions != null ? new Vb6Value(arrayType, dimensions) : new Vb6Value(arrayType);
                    ((VBArray)value.Value!).DeclaredClass = declaredClass;
                    declaredClass = null;
                    if (dimensions != null) ((VBArray)value.Value!).IsDynamic = false;
                }

                var localName = subStmt.ambiguousIdentifier().GetText();
                // Record the DECLARED type, so later assignments coerce to it instead of replacing it. Only
                // for a coercible scalar: an array, an object, a UDT and a Variant all take their value
                // wholesale, and `Dim v` / `Dim v As Variant` must stay exactly as untyped as they read.
                var declared = VbNumeric.IsDeclarableScalar(value.Type) ? value.Type : null;
                interpreter.ExecutionContext.AllocVariable(currentEnv, localName, value, declared, declaredClass);
                if (autoNew is not null && currentEnv.TryGetOwnVariableLocation(localName, out var autoNewLoc))
                    interpreter.ExecutionContext.State.MarkAutoNew(autoNewLoc, autoNew, localName);
                if (isArray && currentEnv.TryGetVariableLocation(localName, out var arrayLoc))
                {
                    interpreter.ExecutionContext.State.MarkDeclaredArray(arrayLoc);
                    if (dimensions != null)
                        interpreter.ExecutionContext.State.MarkFixedArray(arrayLoc);
                }
                else if (!isArray && declaredClass is null && value.Type == Vb6Value.ValueType.EmptyVariant
                         && (asType is null || asType.type()?.complexType() is null)
                         && currentEnv.TryGetVariableLocation(localName, out var variantLoc))
                    interpreter.ExecutionContext.State.MarkVariant(variantLoc);
                // Track this local so RunProcedure releases its reference (fires Class_Terminate) at scope-exit,
                // in declaration order. Every Dim slot is tracked (a Variant can later hold an object via Set);
                // ReleaseRef no-ops non-objects. Only proc-body executors have ownedSlots (module/field-init = null).
                // A Static's slot is kept instead, for the next call.
                if (keep is not null && currentEnv.TryGetOwnVariableLocation(localName, out var staticLoc))
                {
                    keep.Add(subStmt, staticLoc);
                    currentEnv.AllocatedSlots?.Remove(staticLoc);
                }
                else if (ownedSlots != null && currentEnv.TryGetVariableLocation(localName, out var localLoc))
                    ownedSlots.Add(localLoc);
            }
        }
    }

    /// <summary>
    /// What a declaration <c>As New</c> makes: a class's name, or "Collection" (#435). VB6 refuses the rest before the program
    /// runs, as measured: a scalar, a Variant or Object is "Syntax error", and a UDT or an Enum "Invalid use of New
    /// keyword".
    /// </summary>
    private string AutoNewClass(VB6Parser.AsTypeClauseContext asType, bool isArray)
    {
        if (asType.type()?.baseType() is { } baseType)
            return baseType.COLLECTION() != null && !isArray ? "Collection"
                : baseType.COLLECTION() != null ? throw ArrayAsNew()
                : throw new VBCompileErrorException("Syntax error");
        var typeText = asType.type()?.complexType()?.GetText() ?? throw new VBCompileErrorException("Syntax error");
        var (qualifier, typeName) = SplitTypeName(typeText);
        string made;
        if (ExpressionExecutor.IsCollectionType(typeText))
            made = "Collection";
        else if (interpreter.Modules.TryGet(typeName, out var classMod) && classMod.Kind == InterpreterModuleKind.Class)
            made = classMod.Name;
        else if (interpreter.TryResolveType(typeName, currentModule, qualifier, out _)
                 || (qualifier is null && interpreter.TryResolveEnum(typeName, currentModule, out _)))
            throw new VBCompileErrorException("Invalid use of New keyword");
        else if (IsObjectTypeWithoutClass(qualifier, typeName, out _))
            throw new NotImplementedException($"As New {typeText} is not implemented: New makes a class's object or a Collection here (#435)");
        else
            throw new VBCompileErrorException("User-defined type not defined: " + typeName);
        return isArray ? throw ArrayAsNew() : made;

        static NotImplementedException ArrayAsNew() =>
            new("An array declared As New is not implemented (#1482)");
    }

    /// <summary>
    /// Allocate every local a procedure declares, before its body runs — because in VB6 a Dim is a
    /// declaration, not an executable statement. Declaration order, which is also the order scope-exit
    /// releases them in; previously that was EXECUTION order, so a Dim in an untaken branch was released
    /// never and one in a loop was ordered by when it first ran.
    /// </summary>
    internal async Task HoistDeclaredLocals(ProcedureInfo proc)
    {
        foreach (var constant in proc.DeclaredConsts)
            ModuleLevelBounds.CheckProcedure(currentModule, proc, constant.valueStmt(), constant.Start.TokenIndex, inConst: true);
        foreach (var local in proc.DeclaredLocals)
            if (local.subscripts() is { } bounds)
                ModuleLevelBounds.CheckProcedure(currentModule, proc, bounds, local.Start.TokenIndex, inConst: false);
        // Consts first, in declaration order, because a Dim's bounds may name one (measured: `Const P = 2.5` then
        // `Dim a(P)` is a(0 To 2)).
        foreach (var constSub in proc.DeclaredConsts)
            await DeclareLocalConst(constSub);
        foreach (var subStmt in proc.DeclaredLocals)
            await DeclareLocal(subStmt);
    }

    /// <summary>
    /// A procedure's own Const, once per activation. It shadows a module variable or Const it shares a name with,
    /// in any case: measured, <c>Const LIMIT = 10</c> in a Sub reads 10 there and leaves the module's
    /// <c>Limit</c> at 1, and the same holds over a module Const. Updating whatever slot the name found wrote
    /// through the procedure's copy of the module scope into the module's slot, so the value outlived the call
    /// (#819).
    /// </summary>
    private async Task DeclareLocalConst(VB6Parser.ConstSubStmtContext sub)
    {
        var name = sub.ambiguousIdentifier().GetText();
        if (!declaredLocals.Add(name))
            return;
        localConsts.Add(name);
        var value = AsDeclared(sub, await expressionExecutor.EvaluateValue(sub.valueStmt()));
        interpreter.ExecutionContext.AllocVariable(currentEnv, name, value);
        if (currentEnv.TryGetVariableLocation(name, out var slot))
        {
            interpreter.ExecutionContext.State.MarkConstant(slot);
            if (PrePass.IsVariantConst(sub))
                interpreter.ExecutionContext.State.MarkVariant(slot);
            ownedSlots?.Add(slot);
        }
    }

    /// <summary>
    /// A Const's value as its <c>As</c> type holds it, at module and procedure level alike: converted as a store into a
    /// variable of that type converts it (CoerceOnStore), and VB6's compile error where that conversion fails. Measured
    /// with <c>/make</c>: <c>As Integer = 2.5</c> is 2, <c>As Long = 3.5</c> 4, <c>As String = 5</c> "5", <c>As Date = 1</c>
    /// 31/12/1899, <c>As Currency = 1.23456</c> 1.2346, <c>As Integer = True</c> -1, <c>As Integer = "5"</c> 5; and
    /// <c>As Byte = 300</c>, <c>As Byte = -1</c>, <c>As Integer = 32768</c>, a Currency or a Date beyond its range are
    /// "Overflow", and <c>As Integer = "x"</c> "Type mismatch". <c>As Variant</c> keeps the value as it is (2.5, a Double).
    /// It stored the value as the initialiser gave it, whatever the As clause said.
    /// <para>
    /// A type character on the name is that As clause, with the same conversions and the same errors (measured, #1614):
    /// <c>Const K% = 2.5</c> is the Integer 2, <c>Const K$ = 5</c> the String "5", and <c>Const K% = 40000</c> "Overflow".
    /// Both together, <c>Const K% As Integer = 1</c>, does not compile: "Expected: =".
    /// </para>
    /// </summary>
    private static Vb6Value AsDeclared(VB6Parser.ConstSubStmtContext sub, Vb6Value value)
    {
        Vb6Value.ValueType type;
        if (sub.typeHint() is { } hint)
        {
            if (sub.asTypeClause() != null)
                throw new VBCompileErrorException("Expected: =");
            type = BaseTypeMapper.Map(hint);
        }
        else if (sub.asTypeClause() is { } asType)
        {
            if (BaseTypeMapper.Map(asType.type()?.baseType()) is not { } declared || asType.fieldLength() != null)
                throw new NotImplementedException(
                    $"Const {sub.ambiguousIdentifier().GetText()} As {asType.type()?.GetText()} is not supported.");
            type = declared;
        }
        else
            return value;
        if (type == Vb6Value.ValueType.EmptyVariant)
            return value;
        try
        {
            return VbNumeric.CoerceOnStore(value, type, sub);
        }
        catch (VBRunTimeException ex) when (ex.Error.ErrNo == VBStandardError.Overflow.ErrNo)
        {
            throw new VBCompileErrorException("Overflow");
        }
        catch (VBRunTimeException ex) when (ex.Error.ErrNo == VBStandardError.TypeMismatch.ErrNo)
        {
            throw new VBCompileErrorException("Type mismatch");
        }
        // Measured: `Const C As Integer = Null`, `As String = Null` and a procedure's `Const P As Integer = Null` do not
        // compile; `As Variant = Null` and an untyped `Const C = Null` are Null.
        catch (VBRunTimeException ex) when (ex.Error.ErrNo == VBStandardError.InvalidUseOfNull.ErrNo)
        {
            throw new VBCompileErrorException("Invalid use of Null");
        }
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
                    arrayLowerBound = Subscript(await expressionExecutor.EvaluateValue(size[0]));
                    arrayUpperBound = Subscript(await expressionExecutor.EvaluateValue(size[1]));
                }
                else if (size.Length == 1)
                {
                    arrayLowerBound = currentModule.PrePass.ArrayBase;   // Option Base is per-module
                    arrayUpperBound = Subscript(await expressionExecutor.EvaluateValue(size[0]));
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
        // control flow (Exit Sub/Function/Property) propagates straight out. The body is one optional block, as a
        // Do loop's is; it was `block*`, which split `k.Bump` into a call of k and a With member call (#882).
        while (true)
        {
            // A condition that raised under Resume Next runs the body, and is asked again next time round.
            if (await ConditionMet(context.valueStmt(), context) is false)
                return ControlFlow.Nothing;

            if (context.block() is { } block)
            {
                var result = await Visit(block);
                if (result != ControlFlow.Nothing)
                    return result;
            }
        }
    }

    public override async Task<ControlFlow> VisitWidthStmt(VB6Parser.WidthStmtContext context)
    {
        if (!context.valueStmt(0).GetText().StartsWith('#'))
            throw new NotImplementedException("Width without a file number is not implemented");
        var number = await FileNumber(context.valueStmt(0));
        var value = await expressionExecutor.EvaluateValue(context.valueStmt(1));
        var width = Convert.ToInt32(VbNumeric.CoerceOnStore(value, Vb6Value.ValueType.Integer, context).Value);
        interpreter.BuiltIns.Files.SetWidth(number, width);
        return ControlFlow.Nothing;
    }

    public override async Task<ControlFlow> VisitWithStmt(VB6Parser.WithStmtContext context)
    {
        // Push the target so leading-dot members resolve against it, pop in finally.
        if (context.NEW() != null)
            throw new NotImplementedException("With New is not supported yet (needs the object model)");

        Vb6Value target;
        try
        {
            target = await expressionExecutor.EvaluateValue(context.implicitCallStmt_InStmt());
            if (target.Value is DebugProxy)
                throw new VBCompileErrorException("Syntax error");
        }
        catch (VBRunTimeException ex) when (ResumesNext)
        {
            // Resume Next goes on into the body with no object, so every leading-dot member raises 91 (measured, #839).
            CaptureError(ex);
            target = Vb6Value.Nothing;
        }
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
    private async Task SetUdtField(Vb6Value root, VB6Parser.ICS_S_MemberCallContext[] members, Vb6Value value, VB6Parser.LetStmtContext ctx)
    {
        var current = root;
        for (int i = 0; i < members.Length; i++)
        {
            if (current.Value is not VbUdt bag)
                throw new VBRunTimeException(ctx, VBStandardError.TypeMismatch, "Member access on a non-UDT value");
            var member = members[i];
            var fieldName = UdtFieldName(member);
            var found = i == members.Length - 1 && !MemberHasArgs(member)
                ? bag.TryInspect(fieldName, out var field) : bag.TryGet(fieldName, out field);
            if (!found)
                throw new VBRunTimeException(ctx, VBStandardError.MethodOrDataMemberNotFound, fieldName);
            if (MemberHasArgs(member))
            {
                if (field.Value is not VBArray array || member.iCS_S_ProcedureOrArrayCall().argsCall().Length != 1)
                    throw new NotImplementedException("Only a single array index group on a UDT field is implemented (#178)");
                var indices = Subscripts(await expressionExecutor.EvaluateCallArgs(member.iCS_S_ProcedureOrArrayCall().argsCall(0)));
                try
                {
                    current = array.GetValue(indices);
                    if (i == members.Length - 1)
                    {
                        array.SetValue(indices, interpreter.CopyForStore(value));
                        await interpreter.ReleaseRef(current);
                        return;
                    }
                }
                catch (IndexOutOfRangeException)
                {
                    throw new VBRunTimeException(ctx, VBStandardError.SubscriptOutOfRange);
                }
            }
            else if (i == members.Length - 1)
            {
                if (field.Value is VBArray array)
                {
                    if (!array.IsDynamic)
                        throw new VBCompileErrorException("Can't assign to array");
                    if (array.IsLocked)
                        throw new VBRunTimeException(ctx, VBStandardError.ThisArrayIsFixedOrTemporarilyLocked);
                    value = ArrayStore(field.Type, value, ctx);
                }
                bag.TrySet(fieldName, interpreter.CopyForStore(value), ctx);
                await interpreter.ReleaseRef(field);
                return;
            }
            else
                current = field;
        }
    }

    // Inspect only: deciding whether a Let reads an object's default must not evaluate its target twice.
    private bool IsBareUdtArrayField(VB6Parser.ImplicitCallStmt_InStmtContext target)
    {
        if (target.iCS_S_MembersCall() is not { } chain || chain.iCS_S_VariableOrProcedureCall() is not { } lead
            || !interpreter.ExecutionContext.TryInspectVariable(currentEnv, lead.GetText(), out var current))
            return false;
        foreach (var member in chain.iCS_S_MemberCall())
        {
            if (MemberHasArgs(member) || current.Value is not VbUdt bag || !bag.TryInspect(UdtFieldName(member), out current))
                return false;
        }
        return current.Value is VBArray;
    }

    private async Task<UdtFieldRef?> UdtArrayField(VB6Parser.ImplicitCallStmt_InStmtContext target)
    {
        if (target.iCS_S_MembersCall() is not { } chain || chain.dictionaryCallStmt() != null
            || chain.iCS_S_MemberCall() is not [.. var prefix, var last] || MemberHasArgs(last))
            return null;
        Vb6Value root;
        if (chain.iCS_S_VariableOrProcedureCall() is { } lead)
            root = await expressionExecutor.EvaluateValue(lead);
        else if (chain.iCS_S_ProcedureOrArrayCall() is { } indexed)
            root = (Vb6Value)(await expressionExecutor.EvaluateProcedureOrArrayCall(indexed))!;
        else
            root = WithTargetOrError(target);
        root = await expressionExecutor.ResolveMemberChain(root, prefix, 0, target);
        return root.Value is VbUdt bag && bag.TryGet(UdtFieldName(last), out var field) && field.Value is VBArray
            ? new UdtFieldRef(bag, UdtFieldName(last)) : null;
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

    /// <summary>
    /// <c>Write #</c> (#1521): each value as <see cref="VbWrite"/> writes it, and a comma for every <c>,</c> or <c>;</c>
    /// between them. A list ending in one ends there, with no line break, and the next <c>Write #</c> goes on from it
    /// without another comma. <c>Spc</c> and <c>Tab</c> move as in <c>Print #</c> (all measured).
    /// </summary>
    public override async Task<ControlFlow> VisitWriteStmt(VB6Parser.WriteStmtContext context)
    {
        var files = interpreter.BuiltIns.Files;
        RequireHash(context.valueStmt(), "Write");
        var number = await FileNumber(context.valueStmt());
        files.Column(number);   // 52 or 54 before anything is evaluated
        var target = new FileTarget(files, number);
        var afterSeparator = true;
        foreach (var child in context.outputList()?.children ?? [])
        {
            switch (child)
            {
                case VB6Parser.OutputList_ExpressionContext item:
                    if (!afterSeparator)
                        throw new NotImplementedException("Write # of two items with no separator between them is not implemented (#1521)");
                    if (item.valueStmt() is { } value)
                        files.Write(number, VbWrite.ItemText(await expressionExecutor.EvaluateValue(value)));
                    else
                        await PrintPosition(target, item.SPC() != null, item.argsCall()?.argCall().FirstOrDefault()?.valueStmt());
                    afterSeparator = false;
                    break;
                case ITerminalNode { Symbol.Type: VB6Parser.SEMICOLON or VB6Parser.COMMA }:
                    files.Write(number, ",");
                    afterSeparator = true;
                    break;
            }
        }
        if (context.outputList() is null || !afterSeparator)
            VbPrint.EndLine(target);
        return ControlFlow.Nothing;
    }

    public override async Task<ControlFlow> VisitICS_B_MemberProcedureCall(VB6Parser.ICS_B_MemberProcedureCallContext context)
    {
        // `x.Print` followed by separators alone has its own alternative, with no argument list (#1409).
        var identifier = context.ambiguousIdentifier()?.GetText() ?? context.PRINT().GetText();

        // A namespace-qualified statement call (`Module1.Foo args`, `VBA.MsgBox "x"`) — base is a module/library
        // qualifier, not a value. Object member calls fall through to the value path below.
        if (await TryQualifiedStatementCall(context.implicitCallStmt_InStmt(), identifier, context.argsCall()))
            return ControlFlow.Nothing;

        // A leading dot (`.Method args`) has no base — resolve it against the innermost With target.
        var value = context.implicitCallStmt_InStmt() is { } baseExpr
            ? await expressionExecutor.EvaluateValue(baseExpr)
            : WithTargetOrError(context);

        // A user class-instance method call (`obj.Method args`) — dispatch into the interpreter on the instance
        // (Me bound, args by-ref-capable). Nothing → Error 91.
        if (value.Type == Vb6Value.ValueType.Object)
        {
            if (value.Value is not VbObject vobj)
                throw new VBRunTimeException(context, VBStandardError.ObjectVariableOrWithBlockVariableNotSet);
            if (VbInterface.ResolveMember(value, vobj, identifier) is { } callName
                && vobj.ClassDef.PrePass.Procedures.TryGetValue(callName, out var method))
            {
                var objCallArgs = await expressionExecutor.ResolveCallArgs(context.argsCall());
                await interpreter.RunProcedure(vobj.ClassDef, method, objCallArgs, vobj.InstanceEnv, value, stmtFrames);
                return ControlFlow.Nothing;
            }
            // A form's built-in method is its window's: `Me.Hide`, `Form1.Move 0, 0` (#822).
            value = await interpreter.Loading((Form: vobj, Member: identifier), static s => s.Form.BuiltInMembersFor(s.Member)) is { } formWindow
                ? new Vb6Value(formWindow)
                : throw new VBRunTimeException(context, VBStandardError.MethodOrDataMemberNotFound, identifier);
        }

        // Debug.Print reads its list as Print # does, items and separators in order (#1380).
        if (value.Value is DebugProxy debug && identifier.Equals("Print", StringComparison.OrdinalIgnoreCase))
        {
            if (context.argsCall() is { } list)
                await PrintList(list.children, debug, debug.Printed);
            else if (context.PRINT() is not null)
                await PrintList(context.children, debug, debug.Printed);   // separators alone, `Debug.Print ,` (#1409)
            else
                debug.PrintBlankLine();
            return default;
        }

        // Debug.Assert (#1467). In the IDE a condition that does not hold enters break mode at the statement, as Stop does,
        // and one that holds goes on. The condition is read as If reads it, so Null, Empty, 0 and "False" break, and "abc" is
        // 13, which On Error traps. On Error Resume Next does not skip the break. A compiled program has no Debug.Assert at
        // all, so headless, with no debugger, the statement is skipped and its argument is not evaluated: in an EXE,
        // `Debug.Assert G()` never calls G, and `Debug.Assert 1 / 0` raises nothing (all measured, the IDE with /runexit).
        if (value.Value is DebugProxy && identifier.Equals("Assert", StringComparison.OrdinalIgnoreCase))
        {
            // No condition, or two, does not compile (measured).
            if (context.argsCall()?.argCall() is not [{ } condition] || context.argsCall().ChildCount != 1)
                throw new VBCompileErrorException("Syntax error");
            if (interpreter.DebugController is not { } dbg)
                return default;
            if (!await ConditionHolds(condition.valueStmt(), context))
            {
                currentLine = context.Start.Line;
                await dbg.EnterBreakFromAssertionAsync(currentLine, currentModule.Name, this);
            }
            return default;
        }

        if (value.Value is VbCollection)
        {
            await ExpressionExecutor.AsBound(value, identifier, async () =>
                await interpreter.CallProxy((ICSharpProxy)value.Value!, identifier,
                    await expressionExecutor.EvaluateMemberArgs(value.Value, identifier, context.argsCall())));
            return ControlFlow.Nothing;
        }
        var callArgs = await expressionExecutor.EvaluateMemberArgs(value.Value, identifier, context.argsCall());

        if (value.Type == Vb6Value.ValueType.CSharpProxyObject)
        {
            await interpreter.CallProxy((ICSharpProxy)value.Value!, identifier, callArgs);
        }
        else if (value.Type == Vb6Value.ValueType.Control)
        {
            UserControlMembers.Check(value, identifier);
            await ((Control)value.Value!).CallAsync(identifier, callArgs);
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
        // Only whether there is one: a form's name is a variable, and asking must not make its instance (#822).
        if (interpreter.ExecutionContext.TryInspectVariable(currentEnv, lead, out _))
            return false;   // a same-named variable shadows the qualifier
        if (!interpreter.TryResolveQualifier(lead, out var qualifier))
            return false;

        if (qualifier.Kind == BasicInterpreter.QualifierKind.Library)
        {
            // A library-qualified intrinsic invoked as a statement — evaluate and discard the result. An unknown
            // member stops the run even under Resume Next (#867); a known constant
            // used as a bare statement is a harmless no-op.
            if (memberName.Equals("CallByName", StringComparison.OrdinalIgnoreCase))
            {
                await interpreter.CallByName(await expressionExecutor.ResolveCallArgs(argsCall), stmtFrames);
                return true;
            }
            var args = await expressionExecutor.EvaluateIntrinsicArgs(memberName, argsCall);
            if (await expressionExecutor.EvaluateFunction(memberName, args) is null
                && !(args.Count == 0 && interpreter.BuiltIns.TryGetBuiltInConstant(memberName, out _)))
                throw new VBSubOrFunctionNotDefinedException(memberName);
            return true;
        }

        if (qualifier.Kind == BasicInterpreter.QualifierKind.RuntimeLibrary
            && interpreter.GlobalMember(memberName, qualifier.RuntimeLibrary) is { } global)
        {
            await interpreter.CallLibraryMember(global, memberName, await expressionExecutor.EvaluateCallArgs(argsCall));
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
        // In a form's code, a method of the form: `Cls`, `Hide`, `Move 0, 0` (#995). Before another module's Public Sub of
        // the name, as the form's members come before the project's globals (measured, #822).
        if (FormMembers.TryBare(interpreter.ExecutionContext, currentEnv, subName, out var form, out var formMember)
            && formMember.IsMethod)
        {
            var callArgs = await expressionExecutor.EvaluateCallArgs(context.argsCall());
            // Loaded first, then called, so a modal Show can wait (#822).
            var window = await interpreter.Loading((Form: form, Name: subName), static s => s.Form.BuiltInMembersFor(s.Name));
            await window!.CallAsync(subName, callArgs);
            return ControlFlow.Nothing;
        }
        // A bare procedure statement (Foo a, b): a user Sub/Function (this module, or another module's Public)
        // takes precedence over a builtin.
        if (interpreter.TryResolveProcedure(subName, currentModule, out _, out _))
        {
            var callArgs = await expressionExecutor.ResolveCallArgs(context.argsCall());
            await interpreter.CallProcedure(subName, callArgs, currentModule, callerEnv: currentEnv, callerFrames: stmtFrames);
        }
        else if (interpreter.GlobalMember(subName) is { } global)
            await interpreter.CallLibraryMember(global, subName, await expressionExecutor.EvaluateCallArgs(context.argsCall()));
        else if (subName.Equals("CallByName", StringComparison.OrdinalIgnoreCase))
            await interpreter.CallByName(await expressionExecutor.ResolveCallArgs(context.argsCall()), stmtFrames);
        else
        {
            List<Vb6Value> builtinArgs = await expressionExecutor.EvaluateIntrinsicArgs(subName, context.argsCall());
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

    internal bool ReachedProcedureEnd { get; private set; }

    internal async ValueTask ProcedureBoundary(int line, bool breakpointOnly = false)
    {
        if (interpreter.Ending)
            throw new Debugging.StopExecutionSignal();
        if (interpreter.DebugController is not { } dbg || interpreter.RunningDeclarations)
            return;
        currentLine = line;
        _suspendedAtTopLevel = false;
        var gate = breakpointOnly ? dbg.OnBreakpointPositionAsync(line, currentModule.Name, this) : DebugGate(dbg);
        if (breakpointOnly && interpreter.Events is { } events)
            await events.WhileSuspended(gate);
        else
            await gate;
    }

    public async Task Execute(VB6Parser.BlockContext block, bool? moduleDeclarations = null)
    {
        await ExecuteProcedureBody(block, moduleDeclarations);
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

    /// <summary>
    /// Whether a type name the interpreter has no class, UDT or enum for is still a type a declaration may name (#822
    /// review). It is when it is:
    /// <list type="bullet">
    /// <item>one of VB's own, read out of the in-box type libraries (<see cref="VB6InBoxTypes"/>);</item>
    /// <item>a form, this one or another of the project's, none of which is a class;</item>
    /// <item>qualified by a library the interpreter can't read, such as <c>ADODB.Connection</c>;</item>
    /// <item>or unqualified, in a project that references such a library, which may well declare it.</item>
    /// </list>
    /// The last two are a missing input, not a missing name: without the library there is no telling, and the slot is
    /// Nothing either way. A typo then runs on until the variable is used. With nothing referenced, a name nobody
    /// declares stays VB6's "User-defined type not defined".
    /// </summary>
    private bool IsControlLibraryEnum(string? qualifier, string typeName)
    {
        foreach (var library in interpreter.BuiltIns.ControlLibraries)
            if ((qualifier is null || library.Name.Equals(qualifier, StringComparison.OrdinalIgnoreCase))
                && library.ContainerName(typeName) is not null)
                return true;
        return false;
    }

    private bool IsObjectTypeWithoutClass(string? qualifier, string typeName, out Vb6Value.ValueType? aliasScalar)
    {
        aliasScalar = null;
        if (interpreter.LibraryClassFor(qualifier is null ? typeName : qualifier + "." + typeName) is not null)
            return true;
        if (VB6InBoxTypes.TryResolve(qualifier, typeName, out var inbox))
        {
            aliasScalar = inbox.Scalar;
            return true;
        }
        if (qualifier is null && (interpreter.ProjectFormNames.Contains(typeName)
                                  || interpreter.Modules.TryGet(typeName, out var m) && m.Kind == InterpreterModuleKind.Form))
            return true;
        if (qualifier is not null)
            return !interpreter.Modules.Contains(qualifier) && !VB6InBoxLibraries.IsLibrary(qualifier);
        return interpreter.ReferencesOtherLibraries;
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

    private async Task ExecuteProcedureBody(VB6Parser.BlockContext block, bool? moduleDeclarations = null)
    {
        var stmts = block.blockStmt();
        _bodyStmts = stmts;   // Set Next Statement targets this top-level list
        var labels = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < stmts.Length; i++)
            if (stmts[i].lineLabel() is { } lbl)
                labels[lbl.labelName().GetText()] = i;
        CollectLineHeads(block, stmts, labels);
        erlNumbers = LineNumbersOf(block);

        int pc = 0;
        int faultPc = -1;       // index of the statement that faulted (for Resume); -1 = no active error
        inHandler = false;
        var retry = false;      // the statement a halt retries, whose break has just been resumed (#1011)

        while (pc < stmts.Length)
        {
            // Execute's harness body runs after its declarations, through the same statement/error/debugger loop.
            if (moduleDeclarations is { } declarations && BasicInterpreter.IsModuleDeclaration(stmts[pc]) != declarations)
            {
                pc++;
                continue;
            }
            // Debugger pause-gate (before the temp-frame push). Sees every top-level statement; a StopExecutionSignal
            // it raises falls through all four catches below (none match) and unwinds via RunProcedure's finally.
            currentLine = stmts[pc].Start.Line;   // track for the Call Stack
            // A handler the last statement raised finishes before this one starts (#766).
            if (interpreter.Events is { } events)
                await events.WaitForRaised();
            // After End nothing more runs, with or without a debugger (#775). Ahead of the debugger's gate, so a
            // breakpoint on a statement that would run after End is never hit.
            if (interpreter.Ending)
                throw new Debugging.StopExecutionSignal();
            // An Attribute line is metadata, so the debugger never stops on one (see VisitAttributeStmt).
            // Nor on a module's or form's declarations, as VB6 never does (BasicInterpreter.RunningDeclarations).
            if (!retry && interpreter.DebugController is { } dbg && stmts[pc].attributeStmt() == null && !interpreter.RunningDeclarations)
            {
                _suspendedAtTopLevel = true;   // a pause here is at a top-level statement — Set Next Statement is valid
                await DebugGate(dbg);
            }
            retry = false;
            // Set Next Statement (P7b): apply a pc repoint requested while paused at this top-level gate, so the
            // resumed walk executes from the target instead of the paused statement.
            if (_setNextTargetPc is { } snt) { pc = snt; _setNextTargetPc = null; currentLine = stmts[pc].Start.Line; }
            erlLine = stmts[pc].Start.Line;

            try
            {
                // Per-statement temporary frame (Phase 4.2b) — finally-bracketed so it drains on the normal path,
                // on Exit, and when a GoTo/Resume/error signal unwinds out of the statement.
                stmtFrames.Push(new List<Vb6Value>());
                try
                {
                    if (await Visit(stmts[pc]) != ControlFlow.Nothing)
                    {
                        LeaveHandler();
                        return;   // Exit Sub/Function/Property — leave the body
                    }
                    pc++;
                }
                catch (Exception ex) when (interpreter.MarkUnimplementedFault(ex)) { throw; }
                finally
                {
                    await interpreter.FlushFrame(stmtFrames.Pop());
                }
            }
            catch (Exception ex) when (Locate(ex, stmts[pc])) { throw; }
            catch (GoToSignal g)
            {
                pc = ResolveLabel(labels, g.Label);
            }
            catch (ResumeSignal r)
            {
                if (faultPc < 0)
                    throw Raised(new VBRunTimeException(r.Context, VBStandardError.ResumeWithoutError));   // Error 20
                inHandler = false;
                ClearError();   // every Resume clears it (measured, #877)
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
                CaptureError(ex);
                faultPc = pc;
                inHandler = true;
                pc = ResolveLabel(labels, handlerLabel!);
            }
            catch (VBRunTimeException ex) when (ResumesNext)
            {
                CaptureError(ex);
                faultPc = pc;
                pc++;
            }
            catch (VBRunTimeException ex) when (HaltsHere())
            {
                // Set Next Statement is valid at this break, as at any top-level one, and applies as the loop goes round.
                _suspendedAtTopLevel = true;
                if (!await Halt(ex, stmts[pc].Start.Line))
                    throw;
                retry = true;
            }
        }
        ReachedProcedureEnd = true;
        LeaveHandler();
    }

    // A procedure left from its handler, by Exit or by its end, clears Err, as a Resume would, whatever On Error the
    // handler said; one left under On Error Resume Next keeps it, so its caller still sees the error (measured, #877).
    private void LeaveHandler()
    {
        if (inHandler)
            ClearError();
    }

    // A trapped error: into Err, and into Erl the line number at or above where it was raised, in this body in source order,
    // whether or not that line ran, wrapped at 65536 (line 70000 reads 4464), or 0 with none above it (measured, #2528).
    // After a Then part ran, an error past an unnumbered End If reads the Else part's number; a loop's second pass reads the
    // For line's, not the body's last.
    private void CaptureError(VBRunTimeException error)
    {
        interpreter.Err.Capture(error);
        interpreter.BuiltIns.Erl = LineNumberAtOrAbove(erlLine) % 65536;
    }

    private long LineNumberAtOrAbove(int line)
    {
        long number = 0;
        foreach (var (at, n) in erlNumbers)
        {
            if (at > line)
                break;
            number = n;
        }
        return number;
    }

    // Where VB6 clears Err itself, on an On Error statement, a Resume or leaving a handler, it clears Erl too. The program's
    // own Err.Clear clears Err alone (measured, #2528).
    private void ClearError()
    {
        interpreter.Err.Clear();
        interpreter.BuiltIns.Erl = 0;
    }

    // A body's line numbers and the source lines they stand on, in order, nested blocks included. Kept per body, since a
    // procedure runs many times.
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<VB6Parser.BlockContext, (int Line, long Number)[]> LineNumbers = new();

    private static (int Line, long Number)[] LineNumbersOf(VB6Parser.BlockContext body) =>
        LineNumbers.GetValue(body, _ =>
        {
            var found = new List<(int Line, long Number)>();
            var pending = new Stack<IParseTree>();
            pending.Push(body);
            while (pending.Count > 0)
            {
                var node = pending.Pop();
                if (node is VB6Parser.LineNumberContext number)
                {
                    if (long.TryParse(number.GetText(), out var n))
                        found.Add((number.Start.Line, n));
                    continue;
                }
                // Not into a procedure: module-level statements, which only a test runs, have only their own.
                if (node is VB6Parser.SubStmtContext or VB6Parser.FunctionStmtContext or VB6Parser.PropertyGetStmtContext
                    or VB6Parser.PropertyLetStmtContext or VB6Parser.PropertySetStmtContext)
                    continue;
                for (var c = 0; c < node.ChildCount; c++)
                    pending.Push(node.GetChild(c));
            }
            found.Sort((a, b) => a.Line.CompareTo(b.Line));
            return found.ToArray();
        });

    /// <summary>
    /// Whether a run-time error raised in this activation is one nothing handles, so the program halts at the statement
    /// that raised it (#1011). Asked where the error is caught, before anything has unwound. It is handled when this
    /// activation would trap it, or a caller below would, as far down as the activation the program was entered at:
    /// a handler in the code that raised an event never sees the event's error (measured, b1 to b3 in the oracle).
    /// A class's error that its caller traps goes to the caller, as a compiled program does (decided on #1011, D27).
    /// </summary>
    private bool HaltsHere()
    {
        if (interpreter.DebugController is not { CanHalt: true })
            return false;
        var stack = interpreter.ActivationStack;
        var at = -1;
        for (var i = stack.Count - 1; i >= 0; i--)
            if (ReferenceEquals(stack[i], this)) { at = i; break; }
        // A module's declarations, or code the host ran without entering the program, which halting has no frame for.
        if (at < 0)
            return false;
        for (var i = at; i >= 0; i--)
        {
            if (stack[i].Traps)
                return false;
            if (stack[i].IsEntry)
                return true;
        }
        return false;
    }

    // Halt at the statement on line, and answer whether to run it again. The dialog, and a break after it, give up the
    // thread, which the pump is told of, so that an event that comes meanwhile waits (#766): nothing runs behind it.
    private async Task<bool> Halt(VBRunTimeException error, int line)
    {
        currentLine = line;
        ErrorLocation.Note(error, currentModule.Name, line);
        var halt = interpreter.DebugController!.HaltAsync(error, line, currentModule.Name, this).AsTask();
        return interpreter.Events is { } events ? await events.WhileWaitingOnWindow(halt) : await halt;
    }

    // Notes where an error passing out of stmt was raised (#955), and never catches: the first catch of each statement
    // loop, so the innermost statement notes it before a handler or a halt sees it. At the statement's line, where a
    // halt stops; a condition notes its own first (ConditionMet).
    private bool Locate(Exception error, ParserRuleContext stmt)
    {
        if (error is VBRunTimeException runtimeError)
            runtimeError.VbSource ??= interpreter.ProjectName;
        var statement = error is NotImplementedException && stmt is { Start: { InputStream: { } input } start, Stop: { } stop }
            ? input.GetText(new Antlr4.Runtime.Misc.Interval(start.StartIndex, stop.StopIndex))
            : null;
        return ErrorLocation.Note(error, currentModule.Name, stmt.Start.Line, statement);
    }

    // An error raised in a statement loop's own catch, which that loop's Locate cannot see: at the statement running.
    private T Raised<T>(T error) where T : Exception
    {
        ErrorLocation.Note(error, currentModule.Name, currentLine);
        return error;
    }

    private int ResolveLabel(Dictionary<string, int> labels, string label) =>
        labels.TryGetValue(label, out var idx) ? idx : throw Raised(new VBCompileErrorException("Label not defined: " + label));

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
