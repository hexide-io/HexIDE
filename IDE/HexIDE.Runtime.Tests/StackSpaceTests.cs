using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// Issue #80 — unbounded recursion raises VB6's Error 28 instead of killing the process.
///
/// <para>
/// A <see cref="System.StackOverflowException"/> on .NET cannot be caught: the runtime tears the process down
/// where it stands. So an infinitely recursive VB6 program took the whole IDE with it, and every unsaved form
/// in it, for a mistake a beginner makes in their first hour. That is the worst failure mode in the product —
/// worse than a wrong answer, because there is nothing left to correct it in.
/// </para>
///
/// <para>
/// <b>The fix is the error, not a recursion cap.</b> VB6's stack limit is a physically real constraint, so
/// reproducing Error 28 is fidelity; a fixed depth ceiling would be reintroducing an artificial limit, which
/// this project deliberately does not do (see docs/OUT_OF_SCOPE.md and the artificial-vs-real distinction the
/// issue draws). The measurement makes the point better than the principle does: real VB6 reaches
/// <b>258,825</b> frames before it raises 28. Any number a person would have picked — 1,000? 10,000? — is off
/// by two orders of magnitude, in the direction that breaks working programs.
/// </para>
///
/// <para>
/// Every expectation here is vb6.exe output; see <i>Out of stack space</i> in docs/vb6-fidelity-oracle.md.
/// </para>
/// </summary>
public class StackSpaceTests : BaseVBTestFixture
{
    [Fact]
    public async Task UnboundedRecursion_RaisesError28_InsteadOfKillingTheProcess()
    {
        // The whole of the bug, in five lines. Before the guard this test did not fail — the test HOST died,
        // taking the other 950 with it, which is exactly how the defect behaves in the product.
        await Run(
            "On Error Resume Next\n" +
            "Call A\n" +
            "Debug.Print Err.Number\n" +
            "Sub A()\n" +
            "  Call A\n" +
            "End Sub\n");
        AssertDebugLog([new Vb6Value(28L)]);
    }

    [Fact]
    public async Task Error28_CarriesVB6sOwnDescription()
    {
        await Run(
            "On Error Resume Next\n" +
            "Call A\n" +
            "Debug.Print Err.Description\n" +
            "Sub A()\n" +
            "  Call A\n" +
            "End Sub\n");
        AssertDebugLog(["Out of stack space"]);
    }

    [Fact]
    public async Task Error28_IsTrappableByAHandlerInACaller()
    {
        // Measured: an On Error GoTo in the caller catches it and the stack unwinds to that frame.
        await Run(
            "On Error GoTo H\n" +
            "Call A\n" +
            "Debug.Print \"not reached\"\n" +
            "Exit Sub\n" +
            "H:\n" +
            "Debug.Print Err.Number\n" +
            "Sub A()\n" +
            "  Call A\n" +
            "End Sub\n");
        AssertDebugLog([new Vb6Value(28L)]);
    }

    [Fact]
    public async Task TheProgramStillRunsAfterError28()
    {
        // The half that matters most, and the half a crash cannot give you: VB6 does not terminate on 28, so
        // neither does HexIDE. If the stack were not genuinely unwound by the time the handler resumes, the
        // next call would fault again immediately.
        await Run(
            "On Error Resume Next\n" +
            "Call A\n" +
            "Err.Clear\n" +
            "Debug.Print 2 + 2\n" +
            "Debug.Print Err.Number\n" +
            "Sub A()\n" +
            "  Call A\n" +
            "End Sub\n");
        AssertDebugLog([4, new Vb6Value(0L)]);
    }

    [Fact]
    public async Task RecursionThatTerminates_IsUnaffected()
    {
        // The guard must not touch legitimate recursion, which is the reason it probes the real stack rather
        // than counting frames.
        //
        // Fact(7), not Fact(10), and the reason is a separate defect rather than a stack one: 7! is 5040 and
        // fits an Integer, where 10! is 3,628,800 and does not. VB6 PROMOTES a Variant result that outgrows
        // its type (10! comes back a Long) and HexIDE raises Err 6 instead — found while writing this test,
        // measured against vb6.exe, and filed on its own. Using 10 here would tie a stack-space test to an
        // arithmetic bug and report the wrong thing when either broke.
        await Run(
            "Debug.Print Fact(7)\n" +
            "Function Fact(n)\n" +
            "  If n <= 1 Then\n" +
            "    Fact = 1\n" +
            "  Else\n" +
            "    Fact = n * Fact(n - 1)\n" +
            "  End If\n" +
            "End Function\n");
        AssertDebugLog([5040]);
    }

    [Fact]
    public async Task DeepButFiniteRecursion_Completes()
    {
        // A FLOOR, not a ceiling, and deliberately well below the real one — the point is that a regression
        // which makes frames much fatter gets caught by a test rather than by a user whose working program
        // stopped working.
        //
        // It cannot assert the real number, because the real number is not a property of HexIDE. Measured on
        // Windows against a 1 MB thread stack: about 202 frames for a bare `Call A(n + 1)` sub, and between
        // 60 and 80 for a recursive FUNCTION like this one, whose frame carries an expression evaluation as
        // well. CI runs Linux, where the main thread gets 8 MB and the same code goes several times deeper.
        // Pinning either figure would encode a platform's default as if it were a behaviour.
        //
        // That HexIDE manages ~70 frames where vb6.exe manages 258,825 is a real and large divergence,
        // tracked separately. It is NOT a regression from this change: the process was already dying at
        // that same depth, so nothing that used to work stopped working — a crash became a trappable error.
        await Run(
            "Debug.Print Down(30)\n" +
            "Function Down(n)\n" +
            "  If n <= 0 Then\n" +
            "    Down = 0\n" +
            "  Else\n" +
            "    Down = Down(n - 1) + 1\n" +
            "  End If\n" +
            "End Function\n");
        AssertDebugLog([30]);
    }
}
