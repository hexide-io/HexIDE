using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// Regression for the adversarial bug-hunt MED: a <c>Dim</c> inside a loop was re-initialising the variable on every
/// iteration. VB6 allocates a local exactly once per procedure call — a re-executed Dim keeps the existing value.
/// Oracle-pinned against vb6.exe (Dim total in a 1..3 loop that accumulates → 1,3,6).
/// </summary>
public class DimSemanticsTests : BaseVBTestFixture
{
    [Fact]
    public async Task DimInsideLoop_DoesNotReinitialize()
    {
        await Run("For i = 1 To 3\nDim total As Integer\ntotal = total + i\nDebug.Print total\nNext\n");
        AssertDebugLog([new Vb6Value(1), new Vb6Value(3), new Vb6Value(6)]);
    }

    // The per-activation dedup must NOT break shadowing: a proc-local Dim shadows a module var of the same name (the
    // first Dim rebinds the name to a fresh slot); the module variable is untouched.
    [Fact]
    public async Task LocalDim_ShadowsModuleVar()
    {
        await Run("Dim x As Integer\nx = 100\nFoo\nDebug.Print x\n" +
                  "Sub Foo()\nDim x As Integer\nx = 5\nDebug.Print x\nEnd Sub\n");
        AssertDebugLog([new Vb6Value(5), new Vb6Value(100)]);
    }
}
