using System.Linq;
using HexIDE.Runtime.Debugging;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// Interpreter-debugger Phase 3 (Locals) — the runtime inspector behind <see cref="IDebugController.GetLocals"/>.
/// Drive a real run to a breakpoint, then read the paused frame's Locals tree and assert names/values/types and
/// the lazy expansion of objects, UDTs and arrays (including the cycle guard). Same headless gate the IDE uses.
/// </summary>
public class LocalsInspectorTests : BaseVBTestFixture
{

    private static Task<StoppedInfo> NextStop(DebugController dbg)
    {
        var tcs = new TaskCompletionSource<StoppedInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Handler(StoppedInfo info) { dbg.Stopped -= Handler; tcs.TrySetResult(info); }
        dbg.Stopped += Handler;
        return tcs.Task;
    }

    private static DebugNode Child(DebugScope scope, string name) =>
        scope.Locals.Single(n => n.Name == name);

    private static DebugNode Child(DebugNode node, string name) =>
        node.Expand().Single(n => n.Name == name);

    [Fact]
    public async Task Locals_ProcScalars_ShowNameValueType()
    {
        var (vb, dbg) = NewDebuggable(
            "Go\nSub Go()\nDim i As Integer\ni = 3\nDim s As String\ns = \"Ada\"\nDebug.Print i\nEnd Sub\n", "Module1");
        dbg.SetBreakpoints("Module1", new[] { 7 });   // Debug.Print i — after i and s are set

        var stop = NextStop(dbg);
        var run = vb.Execute();
        await stop.Guarded();

        var scope = dbg.GetLocals();
        scope.Should().NotBeNull();
        scope!.Context.Should().Be("Module1.Go");

        var i = Child(scope, "i");
        i.Value.Should().Be("3");
        i.TypeName.Should().Be("Integer");
        i.HasChildren.Should().BeFalse();

        var s = Child(scope, "s");
        s.Value.Should().Be("\"Ada\"");
        s.TypeName.Should().Be("String");

        dbg.Stop();
        await run.Guarded();
    }

    [Fact]
    public async Task Locals_NotPaused_ReturnsNull()
    {
        var (vb, dbg) = NewDebuggable("Debug.Print 1\n", "Module1");
        dbg.GetLocals().Should().BeNull();   // never started
        await vb.Execute().Guarded();
        dbg.GetLocals().Should().BeNull();   // finished, not paused
    }

    [Fact]
    public async Task Locals_ClassMethod_MeRootExpandsToFields()
    {
        var (vb, dbg) = NewDebuggable(
            "Dim o As C\nSet o = New C\no.Work\n", "Module1",
            ("C", "Private mx As Integer\nPrivate my As String\nPublic Sub Work()\nmx = 7\nDebug.Print mx\nEnd Sub\n"));
        dbg.SetBreakpoints("C", new[] { 5 });   // Debug.Print mx — inside Work, on the instance

        var stop = NextStop(dbg);
        var run = vb.Execute();
        await stop.Guarded();

        var scope = dbg.GetLocals();
        scope!.Context.Should().Be("C.Work");

        var me = Child(scope, "Me");
        me.TypeName.Should().Be("C");
        me.HasChildren.Should().BeTrue();

        Child(me, "mx").Value.Should().Be("7");
        Child(me, "mx").TypeName.Should().Be("Integer");
        Child(me, "my").Value.Should().Be("\"\"");   // default empty string, quoted

        dbg.Stop();
        await run.Guarded();
    }

    [Fact]
    public async Task Locals_Udt_ExpandsToFields()
    {
        var (vb, dbg) = NewDebuggable(
            "Go\nSub Go()\nDim p As TPoint\np.X = 5\nDebug.Print p.X\nEnd Sub\n" +
            "Type TPoint\nX As Integer\nY As Integer\nEnd Type\n", "Module1");
        dbg.SetBreakpoints("Module1", new[] { 5 });   // Debug.Print p.X

        var stop = NextStop(dbg);
        var run = vb.Execute();
        await stop.Guarded();

        var p = Child(dbg.GetLocals()!, "p");
        p.TypeName.Should().Be("TPoint");
        p.HasChildren.Should().BeTrue();
        Child(p, "X").Value.Should().Be("5");
        Child(p, "Y").Value.Should().Be("0");   // unset field defaults to 0

        dbg.Stop();
        await run.Guarded();
    }

    [Fact]
    public async Task Locals_Array_ExpandsToElementsBySubscript()
    {
        var (vb, dbg) = NewDebuggable(
            "Go\nSub Go()\nDim a(1 To 3) As Integer\na(1) = 10\na(2) = 20\nDebug.Print a(1)\nEnd Sub\n", "Module1");
        dbg.SetBreakpoints("Module1", new[] { 6 });   // Debug.Print a(1)

        var stop = NextStop(dbg);
        var run = vb.Execute();
        await stop.Guarded();

        var a = Child(dbg.GetLocals()!, "a()");
        a.TypeName.Should().Be("Integer()");
        a.HasChildren.Should().BeTrue();

        var elems = a.Expand();
        elems.Select(e => e.Name).Should().Equal("a(1)", "a(2)", "a(3)");
        elems.Select(e => e.Value).Should().Equal("10", "20", "0");

        dbg.Stop();
        await run.Guarded();
    }

    [Fact]
    public async Task Locals_CyclicObjectGraph_TerminatesAtAncestor()
    {
        // a.Other = a — a self-reference. Expanding a -> Other must show `a` again as a NON-expandable leaf (the
        // ancestor guard), proving the tree can't recurse forever.
        var (vb, dbg) = NewDebuggable(
            "Go\nSub Go()\nDim a As C\nSet a = New C\nSet a.Other = a\nDebug.Print 1\nEnd Sub\n", "Module1",
            ("C", "Public Other As C\n"));
        dbg.SetBreakpoints("Module1", new[] { 6 });   // Debug.Print 1 — a.Other now points back at a

        var stop = NextStop(dbg);
        var run = vb.Execute();
        await stop.Guarded();

        var a = Child(dbg.GetLocals()!, "a");
        a.HasChildren.Should().BeTrue();
        var other = Child(a, "Other");
        other.TypeName.Should().Be("C");
        other.HasChildren.Should().BeFalse();   // cycle back to `a` — shown as a leaf, not expanded

        dbg.Stop();
        await run.Guarded();
    }

    [Fact]
    public async Task Locals_ShadowingLocal_ShowsLocalAtTopLevelAndModuleVarUnderRoot()
    {
        // A proc local that SHADOWS a module var must appear at proc level (its own value), while the module var
        // keeps its own value under the root. (Regression: name-based subtraction dropped the shadowing local.)
        var (vb, dbg) = NewDebuggable(
            "Public G As Integer\nG = 100\nGo\nSub Go()\nDim G As Integer\nG = 5\nDebug.Print G\nEnd Sub\n", "Module1");
        dbg.SetBreakpoints("Module1", new[] { 7 });   // Debug.Print G — local G=5, module G=100

        var stop = NextStop(dbg);
        var run = vb.Execute();
        await stop.Guarded();

        var scope = dbg.GetLocals()!;
        scope.Locals.Single(n => n.Name == "G").Value.Should().Be("5");   // the local, at proc level
        Child(scope.Locals.Single(n => n.Name == "Me"), "G").Value.Should().Be("100");   // the module var, under root

        dbg.Stop();
        await run.Guarded();
    }

    [Fact]
    public async Task Locals_EmptyArray_IsALeaf_NoCrash()
    {
        // Split("") yields a zero-length array. It must be a leaf — expanding it would index an empty backing
        // store and throw. (Regression: the Rank-only guard let it become an expandable node.)
        var (vb, dbg) = NewDebuggable(
            "Go\nSub Go()\nDim a() As String\na = Split(\"\")\nDebug.Print 1\nEnd Sub\n", "Module1");
        dbg.SetBreakpoints("Module1", new[] { 5 });   // Debug.Print 1 — a is an empty array

        var stop = NextStop(dbg);
        var run = vb.Execute();
        await stop.Guarded();

        var a = dbg.GetLocals()!.Locals.Single(n => n.Name == "a()");
        a.HasChildren.Should().BeFalse();
        a.Expand().Should().BeEmpty();   // and even if forced, no throw

        dbg.Stop();
        await run.Guarded();
    }

    [Fact]
    public async Task Locals_ObjectWithNoFields_IsALeaf()
    {
        // A field-less class instance must not show an expander that opens to nothing. (Regression finding.)
        var (vb, dbg) = NewDebuggable(
            "Go\nSub Go()\nDim c As EmptyC\nSet c = New EmptyC\nDebug.Print 1\nEnd Sub\n", "Module1",
            ("EmptyC", "Public Sub Noop()\nEnd Sub\n"));
        dbg.SetBreakpoints("Module1", new[] { 5 });   // Debug.Print 1 — c is a field-less instance

        var stop = NextStop(dbg);
        var run = vb.Execute();
        await stop.Guarded();

        var c = dbg.GetLocals()!.Locals.Single(n => n.Name == "c");
        c.TypeName.Should().Be("EmptyC");
        c.HasChildren.Should().BeFalse();

        dbg.Stop();
        await run.Guarded();
    }

    [Fact]
    public async Task Locals_ByRefParamAliasingModuleVar_ShownAtTopLevel()
    {
        // A ByRef param aliases the caller's slot (here a module var's slot). The slot-based partition must show
        // the param at proc level (its aliased value) — not swallow it because a same-slot module var exists.
        var (vb, dbg) = NewDebuggable(
            "Public G As Integer\nG = 42\nFoo G\nSub Foo(ByRef X As Integer)\nDebug.Print X\nEnd Sub\n", "Module1");
        dbg.SetBreakpoints("Module1", new[] { 5 });   // Debug.Print X — X aliases G (=42)

        var stop = NextStop(dbg);
        var run = vb.Execute();
        await stop.Guarded();

        var scope = dbg.GetLocals()!;
        scope.Locals.Single(n => n.Name == "X").Value.Should().Be("42");        // the aliased param, at proc level
        Child(scope.Locals.Single(n => n.Name == "Me"), "G").Value.Should().Be("42");   // the module var, under root

        dbg.Stop();
        await run.Guarded();
    }

    [Fact]
    public async Task Locals_ModuleTopLevelFrame_VarsUnderRootNotDuplicated()
    {
        // At a module top-level break, currentEnv IS the module env, so every entry matches the base slot — nothing
        // should appear at top level (no duplication); the vars live under the root only.
        var (vb, dbg) = NewDebuggable("Public A As Integer\nA = 7\nDebug.Print A\n", "Module1");
        dbg.SetBreakpoints("Module1", new[] { 3 });

        var stop = NextStop(dbg);
        var run = vb.Execute();
        await stop.Guarded();

        var scope = dbg.GetLocals()!;
        scope.Locals.Where(n => n.Name == "A").Should().BeEmpty();   // NOT a top-level local
        Child(scope.Locals.Single(n => n.Name == "Me"), "A").Value.Should().Be("7");

        dbg.Stop();
        await run.Guarded();
    }

    [Fact]
    public async Task Locals_FunctionReturnName_ShownAsLocal()
    {
        // VB6 Locals shows a Function's return variable (its own name) as a local carrying the pending result.
        var (vb, dbg) = NewDebuggable(
            "Debug.Print Calc()\nFunction Calc() As Integer\nCalc = 9\nDebug.Print Calc\nEnd Function\n", "Module1");
        dbg.SetBreakpoints("Module1", new[] { 4 });   // Debug.Print Calc — inside Calc, return-name = 9

        var stop = NextStop(dbg);
        var run = vb.Execute();
        await stop.Guarded();

        var scope = dbg.GetLocals()!;
        scope.Context.Should().Be("Module1.Calc");
        scope.Locals.Single(n => n.Name == "Calc").Value.Should().Be("9");

        dbg.Stop();
        await run.Guarded();
    }

    [Fact]
    public async Task Locals_NestedUdt_ExpandsRecursively()
    {
        var (vb, dbg) = NewDebuggable(
            "Go\nSub Go()\nDim o As Outer\no.N = 3\no.P.X = 5\nDebug.Print o.N\nEnd Sub\n" +
            "Type TPoint\nX As Integer\nY As Integer\nEnd Type\nType Outer\nP As TPoint\nN As Integer\nEnd Type\n",
            "Module1");
        dbg.SetBreakpoints("Module1", new[] { 6 });   // Debug.Print o.N

        var stop = NextStop(dbg);
        var run = vb.Execute();
        await stop.Guarded();

        var o = Child(dbg.GetLocals()!, "o");
        o.TypeName.Should().Be("Outer");
        Child(o, "N").Value.Should().Be("3");
        var p = Child(o, "P");
        p.TypeName.Should().Be("TPoint");
        p.HasChildren.Should().BeTrue();
        Child(p, "X").Value.Should().Be("5");   // two levels deep

        dbg.Stop();
        await run.Guarded();
    }

    [Fact]
    public async Task Locals_ArrayOfObjects_ExpandsElementsAndTheirFields()
    {
        var (vb, dbg) = NewDebuggable(
            "Go\nSub Go()\nDim arr(1 To 2) As C\nDim c As C\nSet c = New C\nc.V = 7\nSet arr(1) = c\nDebug.Print 1\nEnd Sub\n",
            "Module1", ("C", "Public V As Integer\n"));
        dbg.SetBreakpoints("Module1", new[] { 8 });   // Debug.Print 1 — arr(1) set (V=7), arr(2) Nothing

        var stop = NextStop(dbg);
        var run = vb.Execute();
        await stop.Guarded();

        var arr = Child(dbg.GetLocals()!, "arr()");
        arr.HasChildren.Should().BeTrue();
        var e1 = Child(arr, "arr(1)");
        e1.HasChildren.Should().BeTrue();
        Child(e1, "V").Value.Should().Be("7");             // object element's field
        Child(arr, "arr(2)").Value.Should().Be("Nothing"); // unset element is a Nothing leaf
        Child(arr, "arr(2)").HasChildren.Should().BeFalse();

        dbg.Stop();
        await run.Guarded();
    }

    [Fact]
    public async Task Locals_TwoLocalsSharingOneObject_BothExpand()
    {
        // a and b reference the SAME instance — NOT a cycle. Both must expand (the ancestor guard is per-path, so
        // sibling sharing must not be mistaken for a loop).
        var (vb, dbg) = NewDebuggable(
            "Go\nSub Go()\nDim a As C\nDim b As C\nSet a = New C\na.V = 5\nSet b = a\nDebug.Print 1\nEnd Sub\n", "Module1",
            ("C", "Public V As Integer\n"));
        dbg.SetBreakpoints("Module1", new[] { 8 });   // Debug.Print 1

        var stop = NextStop(dbg);
        var run = vb.Execute();
        await stop.Guarded();

        var scope = dbg.GetLocals()!;
        Child(Child(scope, "a"), "V").Value.Should().Be("5");
        Child(Child(scope, "b"), "V").Value.Should().Be("5");   // shared, still expands

        dbg.Stop();
        await run.Guarded();
    }
}
