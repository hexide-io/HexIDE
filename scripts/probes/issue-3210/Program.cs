// SPDX-License-Identifier: MIT
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.InteropServices;
using Antlr4.Runtime;
using Antlr4.Runtime.Atn;
using Antlr4.Runtime.Tree;
using HexIDE.Runtime.Interpreter.ConditionalCompilation;
using HexIDE.Runtime.Interpreter;
using HexIDE.Runtime.Tests;
using HexIDE.Runtime.Debugging;
using HexIDE.VbLspServer;

static class Program
{
    public static void Main(string[] args)
    {
        if (args[0] == "threads")
        {
            ThreadInfo.Print("entry");
            Task.Run(() => ThreadInfo.Print("pool")).GetAwaiter().GetResult();
            OnThread(0, () => ThreadInfo.Print("new Thread default"));
            foreach (var mib in new[] { 1, 4, 16 })
                OnThread(mib * 1024 * 1024, () => ThreadInfo.Print($"new Thread {mib} MiB"));
            return;
        }
        var stage = args[0];
        var shape = args[1];
        var depth = int.Parse(args[2]);
        var stackBytes = int.Parse(args[3]);
        var body = shape == "if"
            ? string.Concat(Enumerable.Repeat("If True Then\n", depth)) + "v = 1\n" + string.Concat(Enumerable.Repeat("End If\n", depth))
            : "v = " + new string('(', depth) + "1" + new string(')', depth) + "\n";
        var source = "Option Explicit\nSub Main()\nDim v As Integer\n" + body + "Debug.Print v\nEnd Sub\n";
        Action work = () => throw new InvalidOperationException(stage);
        // Setup happens on a separate generous stack, never on the measured stack.
        OnThread(256 * 1024 * 1024, () =>
        {
            if (stage is "bounds-guard" or "bounds-procedure")
            {
                var expression = new string('(', depth) + "1" + new string(')', depth);
                var parser = new VB6Parser(new CommonTokenStream(new VB6Lexer(new AntlrInputStream(expression))));
                var tree = parser.valueStmt();
                var fixture = new Fixture();
                var (vb, _) = fixture.Prepare("Sub Main()\nEnd Sub\n");
                var module = vb.Modules.All.First();
                var type = typeof(BasicInterpreter).Assembly.GetType("HexIDE.Runtime.Interpreter.ModuleLevelBounds")!;
                var walker = Activator.CreateInstance(type, BindingFlags.Instance | BindingFlags.NonPublic, null,
                    [vb, module, module.ModuleEnv, "probe", false], null)!;
                work = stage == "bounds-guard"
                    ? () => Pump.Run(() => (Task)type.GetMethod("Walk", BindingFlags.Instance | BindingFlags.NonPublic)!
                        .Invoke(walker, [tree, true, 0])!)
                    : () => type.GetMethod("CheckProcedure")!
                        .Invoke(null, [module, module.PrePass.Procedures["Main"], tree, 0, true, 0]);
            }
            else if (stage.StartsWith("cc-", StringComparison.Ordinal))
            {
                var expression = new string('(', depth) + "1" + new string(')', depth);
                var directive = shape == "if"
                    ? string.Concat(Enumerable.Repeat("#If True Then\n", depth)) + "Dim v As Integer\n" + string.Concat(Enumerable.Repeat("#End If\n", depth))
                    : shape == "const" ? "#Const C = " + expression + "\nDim v As Integer\n"
                    : "#If " + expression + " Then\nDim v As Integer\n#End If\n";
                var runtimeType = typeof(BasicInterpreter).Assembly.GetType("HexIDE.Runtime.Interpreter.ConditionalCompilation.ConstantExpression")!;
                var serverType = typeof(VbSymbolProvider).Assembly.GetType("HexIDE.VbLspServer.VbConditionalCompilation")!;
                if (stage == "cc-runtime-inspect")
                    work = () =>
                    {
                        var parser = new VB6Parser(new CommonTokenStream(new VB6Lexer(new AntlrInputStream(expression))));
                        var tree = parser.valueStmt();
                        var pending = new Stack<(IParseTree Node, int Depth)>();
                        pending.Push((tree, 1));
                        var maximum = 0;
                        while (pending.TryPop(out var item))
                        {
                            maximum = Math.Max(maximum, item.Depth);
                            for (var i = 0; i < item.Node.ChildCount; i++) pending.Push((item.Node.GetChild(i), item.Depth + 1));
                        }
                        Console.WriteLine("TREE_DEPTH=" + maximum);
                        var guarded = new VB6Parser(new CommonTokenStream(new VB6Lexer(new AntlrInputStream(expression))));
                        var guardType = typeof(BasicInterpreter).Assembly.GetType("HexIDE.Runtime.Interpreter.ParseDepthGuard")!;
                        guarded.AddParseListener((IParseTreeListener)Activator.CreateInstance(guardType, [300, null])!);
                        try { guarded.valueStmt(); Console.WriteLine("PARSE_GUARD=pass"); }
                        catch (VBCompileErrorException e) { Console.WriteLine("PARSE_GUARD=" + e.Message); }
                        Console.WriteLine("TRY_PARSE=" + (runtimeType.GetMethod("TryParse", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [expression]) is not null));
                    };
                else if (stage == "cc-runtime")
                    work = () =>
                    {
                        var applied = ConditionalCompiler.Preprocess(directive, CompilationConstants.Vb6, "Probe");
                        Console.WriteLine("DIRECTIVES_LEFT=" + applied.Text.Contains('#'));
                        if (applied.Text.Contains('#'))
                        {
                            try { new SyntaxChecker().Run(applied.Text); }
                            catch (Exception e) { Console.WriteLine("USER=" + e.Message); }
                            Environment.Exit(29);
                        }
                    };
                else if (stage == "cc-lsp")
                    work = () =>
                    {
                        var applied = serverType.GetMethod("Apply", BindingFlags.NonPublic | BindingFlags.Static)!
                            .Invoke(null, [directive, VbCompilationContext.NoProject, Enum.ToObject(serverType.GetMethod("Apply", BindingFlags.NonPublic | BindingFlags.Static)!.GetParameters()[2].ParameterType, 0)])!;
                        var text = (string)applied.GetType().GetProperty("Text")!.GetValue(applied)!;
                        var error = applied.GetType().GetProperty("Error")!.GetValue(applied);
                        Console.WriteLine("DIRECTIVES_LEFT=" + text.Contains('#') + "; ERROR=" + error);
                        if (text.Contains('#') || error is not null)
                        {
                            foreach (var diagnostic in VbDiagnosticsProvider.GetDiagnostics(directive))
                                Console.WriteLine("USER=" + diagnostic.Message);
                            Environment.Exit(29);
                        }
                    };
                else if (stage == "cc-runtime-parse")
                    work = () => new VB6Parser(new CommonTokenStream(new VB6Lexer(new AntlrInputStream(expression))))
                        { ErrorHandler = new BailErrorStrategy() }.valueStmt();
                else if (stage == "cc-lsp-parse")
                    work = () => new VisualBasic6Parser(new CommonTokenStream(new VisualBasic6Lexer(new AntlrInputStream(expression))))
                        { ErrorHandler = new BailErrorStrategy() }.valueStmt();
                else if (stage == "cc-runtime-eval")
                {
                    var parser = new VB6Parser(new CommonTokenStream(new VB6Lexer(new AntlrInputStream(expression))));
                    var tree = parser.valueStmt();
                    work = () => runtimeType.GetMethod("Evaluate", BindingFlags.Static | BindingFlags.NonPublic)!
                        .Invoke(null, [tree, (Func<string, Vb6Value>)(_ => new Vb6Value(0)), false]);
                }
                else if (stage == "cc-lsp-eval")
                {
                    var parser = new VisualBasic6Parser(new CommonTokenStream(new VisualBasic6Lexer(new AntlrInputStream(expression))));
                    var tree = parser.valueStmt();
                    work = () => serverType.GetNestedType("Evaluator", BindingFlags.NonPublic)!
                        .GetMethod("Value", BindingFlags.NonPublic | BindingFlags.Static)!
                        .Invoke(null, [tree, (Func<string, long?>)(_ => 0)]);
                }
                else throw new InvalidOperationException(stage);
            }
            else if (stage == "syntax")
                work = () => new SyntaxChecker().Run(source);
            else if (stage.StartsWith("runtime-parse"))
                work = () => RuntimeParse(source, stage.EndsWith("-ll", StringComparison.Ordinal) ? PredictionMode.LL : PredictionMode.SLL);
            else if (stage.StartsWith("lsp-parse"))
                work = () => LspParse(source, stage.EndsWith("-ll", StringComparison.Ordinal) ? PredictionMode.LL : PredictionMode.SLL);
            else if (stage == "prepass")
            {
                var tree = RuntimeParse(source, PredictionMode.SLL);
                work = () => new PrePass(new ExecutionEnvironment(), new ModuleExecutionContext().State).Visit(tree);
            }
            else if (stage is "execute" or "debug" or "debug-sync" or "step" or "execute-guard")
            {
                var fixture = new Fixture();
                var (vb, dbg) = fixture.Prepare(source);
                if (stage is "execute" or "execute-guard") vb.DebugController = null;
                work = () => Pump.Run(async () =>
                {
                    // Isolate the worst uninterrupted debug segment; normal debug keeps its timer.
                    if (stage == "debug-sync")
                        typeof(DebugController).GetField("_lastYieldTick", BindingFlags.Instance | BindingFlags.NonPublic)!
                            .SetValue(dbg, long.MaxValue);
                    if (stage == "step")
                    {
                        dbg.StepInto();
                        dbg.Stopped += _ => SynchronizationContext.Current!.Post(_ => dbg.StepInto(), null);
                    }
                    await vb.RunStartupSubMain();
                    fixture.Check();
                });
            }
            else if (stage == "folding")
                work = () => VbFoldingProvider.GetFoldings(source);
            else
            {
                var tree = LspParse(source, PredictionMode.SLL);
                work = stage switch
                {
                    "symbols" => () => VbSymbolProvider.GetSymbols(tree),
                    "declarations" => () => VbScopeAnalyzer.GetDeclaredTypes(tree),
                    "scope" => () => VbScopeAnalyzer.GetOptionExplicitDiagnostics(tree),
                    "inlay" => () => typeof(VbSymbolProvider).Assembly.GetType("HexIDE.VbLspServer.VbInlayHintProvider")!
                        .GetMethod("GetHints")!.Invoke(null, [tree, source]),
                    _ => throw new InvalidOperationException(stage)
                };
            }
        });
        Console.WriteLine("READY");
        OnThread(stackBytes, () =>
        {
            ThreadInfo.Print("measured");
            try { work(); }
            catch (VBRunTimeException e) when (stage.EndsWith("-guard", StringComparison.Ordinal) && e.Error.ErrNo == 28)
            {
                Console.WriteLine("ERROR28: " + e.Error.ErrNo + "; " + e.StackTrace?.Split('\n')[0]);
                Environment.Exit(28);
            }
        });
        Console.WriteLine("PASS");
    }

    static VB6Parser.StartRuleContext RuntimeParse(string source, PredictionMode mode)
    {
        var parser = new VB6Parser(new CommonTokenStream(new VB6Lexer(new AntlrInputStream(source))));
        parser.RemoveErrorListeners();
        parser.Interpreter.PredictionMode = mode;
        Console.WriteLine("PREDICTION=" + (ReferenceEquals(parser.Interpreter.PredictionMode, PredictionMode.SLL) ? "SLL" :
            ReferenceEquals(parser.Interpreter.PredictionMode, PredictionMode.LL) ? "LL" : "other"));
        parser.ErrorHandler = new BailErrorStrategy();
        var tree = parser.startRule();
        if (parser.NumberOfSyntaxErrors != 0) throw new Exception("Invalid runtime source");
        return tree;
    }

    static VisualBasic6Parser.StartRuleContext LspParse(string source, PredictionMode mode)
    {
        var parser = new VisualBasic6Parser(new CommonTokenStream(new VisualBasic6Lexer(new AntlrInputStream(source))));
        parser.RemoveErrorListeners();
        parser.Interpreter.PredictionMode = mode;
        Console.WriteLine("PREDICTION=" + (ReferenceEquals(parser.Interpreter.PredictionMode, PredictionMode.SLL) ? "SLL" :
            ReferenceEquals(parser.Interpreter.PredictionMode, PredictionMode.LL) ? "LL" : "other"));
        parser.ErrorHandler = new BailErrorStrategy();
        var tree = parser.startRule();
        if (parser.NumberOfSyntaxErrors != 0) throw new Exception("Invalid LSP source");
        return tree;
    }

    static void OnThread(int stack, Action action)
    {
        Exception? error = null;
        var thread = new Thread(() => { try { action(); } catch (Exception e) { error = e; } }, stack);
        thread.Start();
        thread.Join();
        if (error is not null) throw new Exception("Probe failed", error);
    }
}

sealed class Fixture : BaseVBTestFixture
{
    public (BasicInterpreter, DebugController) Prepare(string source) => NewDebuggable(source);
    public void Check()
    {
        if (debug.Count != 1 || Convert.ToInt32(debug[0].Value) != 1) throw new Exception("Incomplete execution");
    }
}

// Keep all async resumes on the measured thread, including debugger yields and real step suspensions.
sealed class Pump : SynchronizationContext
{
    readonly BlockingCollection<(SendOrPostCallback, object?)> queue = new();
    public override void Post(SendOrPostCallback callback, object? state) => queue.Add((callback, state));
    public static void Run(Func<Task> action)
    {
        var pump = new Pump();
        SetSynchronizationContext(pump);
        var task = action();
        while (!task.IsCompleted)
        {
            if (pump.queue.TryTake(out var item, 100)) item.Item1(item.Item2);
        }
        task.GetAwaiter().GetResult();
    }
}

static class ThreadInfo
{
    [DllImport("libc")] static extern nuint pthread_self();
    [DllImport("libc")] static extern int pthread_getattr_np(nuint thread, IntPtr attr);
    [DllImport("libc")] static extern int pthread_attr_getstack(IntPtr attr, out IntPtr address, out nuint bytes);
    [DllImport("libc")] static extern int pthread_attr_destroy(IntPtr attr);
    public static void Print(string label)
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("Stack inventory uses Linux pthread APIs");
        var attr = Marshal.AllocHGlobal(128);
        try
        {
            if (pthread_getattr_np(pthread_self(), attr) != 0) throw new Exception("pthread_getattr_np");
            try
            {
                if (pthread_attr_getstack(attr, out _, out var bytes) != 0) throw new Exception("pthread_attr_getstack");
                Console.WriteLine($"{label}: managed={Environment.CurrentManagedThreadId}, pool={Thread.CurrentThread.IsThreadPoolThread}, bytes={bytes}");
            }
            finally { pthread_attr_destroy(attr); }
        }
        finally { Marshal.FreeHGlobal(attr); }
    }
}
