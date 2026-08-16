using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// Phase 4.3 — Const. Stored/resolved as a plain slot (the value model has no read-only concept, so
/// reassignment isn't caught — a documented fidelity gap). Module-level consts are hoisted by PrePass so a Sub
/// declared after the Const can see them.
/// </summary>
public class ConstTests : BaseVBTestFixture
{
    [Fact]
    public async Task ModuleLevel_InArithmetic()
    {
        await Run(
            "Const A = 10\n" +
            "Const B = 20\n" +
            "Debug.Print A + B\n");
        AssertDebugLog([30]);
    }

    [Fact]
    public async Task MultiplePerLine()
    {
        await Run(
            "Const A = 1, B = 2\n" +
            "Debug.Print A\n" +
            "Debug.Print B\n");
        AssertDebugLog([1, 2]);
    }

    [Fact]
    public async Task LocalConst_InsideSub()
    {
        await Run(
            "Sub Compute()\n" +
            "Const LOCAL = 7\n" +
            "Debug.Print LOCAL\n" +
            "End Sub\n" +
            "Compute\n");
        AssertDebugLog([7]);
    }

    [Fact]
    public async Task ModuleConst_VisibleInASubDeclaredAfterIt()
    {
        await Run(
            "Const K = 42\n" +
            "Sub Show()\n" +
            "Debug.Print K\n" +
            "End Sub\n" +
            "Show\n");
        AssertDebugLog([42]);
    }

    [Fact]
    public async Task Const_AsLoopBound()
    {
        await Run(
            "Const N = 3\n" +
            "Dim i\n" +
            "For i = 1 To N\n" +
            "Debug.Print i\n" +
            "Next\n");
        AssertDebugLog([1, 2, 3]);
    }
}
