using HexIDE.Runtime.Interpreter;
using HexIDE.Runtime.Debugging;
using HexIDE.IDE;

namespace HexIDE.Runtime.Tests;

public abstract class BaseVBTestFixture
{
    protected List<Vb6Value> debug = new();
    protected  ModuleExecutionContext context;
    protected ExecutionEnvironment rootEnv;

    protected BaseVBTestFixture()
    {
        context = new ModuleExecutionContext();
        rootEnv = new ExecutionEnvironment();
        // `Debug` is seeded by BasicInterpreter itself (so it works in the live F5 run too); the interpreter
        // routes Debug.Print to IBasicStandardLibrary.DebugPrint, which MockStdLib captures into `debug`.
    }

    public class Comparer : System.Collections.IComparer
    {
        private readonly double epsilon;

        public Comparer(double epsilon)
        {
            this.epsilon = epsilon;
        }

        public int Compare(object? x, object? y)
        {
            if (x is Vb6Value xVal && y is Vb6Value yVal)
            {
                // Type-first: two values of different VB6 subtypes are never equal. This lets tests
                // distinguish Integer/Long/Currency/Decimal (and avoids CompareTo throwing across
                // mismatched boxed CLR types, e.g. int vs long).
                if (xVal.Type != yVal.Type)
                    return -1;
                if (xVal.Value is double aD && yVal.Value is double bD)
                    return Math.Abs(aD - bD) < epsilon ? 0 : aD.CompareTo(bD);
                if (xVal.Value is float aF && yVal.Value is float bF)
                    return Math.Abs(aF - bF) < epsilon ? 0 : aF.CompareTo(bF);
                if (xVal.Value is IComparable comparable)
                    return comparable.CompareTo(yVal.Value);
                if (x.Equals(y))
                    return 0;
                return -1;
            }

            return -1;
        }
    }

    protected string ConvertToVb6Value(object? value)
    {
        return value switch
        {
            null => "Null",
            bool b => b ? "True" : "False",
            int i => i.ToString(),
            long l => l + "&",                  // VB6 Long suffix (consumed once literal typing lands, 2.4)
            byte bt => bt.ToString(),           // no VB6 Byte literal; a Byte target coerces the number
            float f => f.ToString("F") + "!",   // VB6 float suffix
            double d => d.ToString("F") + "#",  // VB6 double suffix
            decimal m => m.ToString(System.Globalization.CultureInfo.InvariantCulture) + "@",  // Currency suffix
            DateTime dt => "#" + dt.ToString("M/d/yyyy", System.Globalization.CultureInfo.InvariantCulture) + "#",
            string s => $"\"{s}\"",
            _ => throw new ArgumentException("Unsupported type")
        };
    }

    protected void AssertDebugLog(List<Vb6Value> expected)
    {
        debug.Should().Equal(expected, (a, b) => new Comparer(0.001).Compare(a, b) == 0);
    }

    protected async Task Run(string code)
    {
        var vb = new BasicInterpreter(new MockStdLib(debug), context, rootEnv, code);
        await vb.Execute();
    }

    /// <summary>Build a debuggable interpreter: the given code as the startup module <paramref name="moduleName"/>
    /// with a real <see cref="DebugController"/> attached. Do NOT await <c>vb.Execute()</c> directly — start it,
    /// assert the paused state, then drive Continue/Stop and await the run. (Optional class modules for class tests.)</summary>
    protected (BasicInterpreter vb, DebugController dbg) NewDebuggable(
        string code, string moduleName = "Module1", params (string Name, string Code)[] classModules)
    {
        var dbg = new DebugController();
        var vb = new BasicInterpreter(new MockStdLib(debug), context, rootEnv, code, moduleName, null,
            classModules.Length == 0 ? null : classModules) { DebugController = dbg };
        return (vb, dbg);
    }

    /// <summary>Run <paramref name="primaryCode"/> as the startup module "Module1", with additional named
    /// standard modules also loaded into the project-wide registry (for cross-module resolution tests).</summary>
    protected async Task RunModules(string primaryCode, params (string Name, string Code)[] modules)
    {
        var vb = new BasicInterpreter(new MockStdLib(debug), context, rootEnv, primaryCode, "Module1", modules);
        await vb.Execute();
    }

    /// <summary>Run <paramref name="primaryCode"/> with the given named CLASS modules registered (each
    /// instantiable via <c>New Name</c>). Class modules are templates — not run at startup.</summary>
    protected async Task RunClasses(string primaryCode, params (string Name, string Code)[] classModules)
    {
        var vb = new BasicInterpreter(new MockStdLib(debug), context, rootEnv, primaryCode, "Module1", null, classModules);
        await vb.Execute();
    }

    private class MockStdLib(List<Vb6Value> debug) : IBasicStandardLibrary
    {
        public async Task<MessageBoxResult> MsgBox(string text, string? caption, MessageBoxButtons buttons, MessageBoxIcon icon) => default;
        public async Task<string?> InputBox(string prompt, string? title, string defaultText) => default;
        // Locked, because two interpreter walks really can reach here at once.
        //
        // DebugController's freeze model releases the decider and any frozen newcomer from Continue() two
        // statements apart, and both gates are RunContinuationsAsynchronously — so in a test the two
        // continuations land on the thread pool and run concurrently. List<T>.Add is then a lost update:
        // both threads read the same _size, both store _size + 1, both write the same slot, and one
        // Debug.Print silently disappears. That is the whole of the intermittent failure in issue #139 —
        // measured at 13 losses in 60,000 runs, driven to zero by this lock alone with nothing else changed.
        //
        // Only the two tests that fire a second activation while paused can hit it (DebuggerTests, the
        // ExecuteSub calls); everywhere else the test thread is parked on an await while the walk runs, and
        // that await is the happens-before edge. Locking regardless costs nothing and removes the whole class.
        public void DebugPrint(Vb6Value value)
        {
            lock (debug) debug.Add(value);
        }
    }
}