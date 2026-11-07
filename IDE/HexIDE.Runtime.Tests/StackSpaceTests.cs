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
/// Every expectation here is vb6.exe output; see <i>Out of stack space</i> in docs/vb6-fidelity-oracle/out-of-stack-space--error-28.md.
/// </para>
/// </summary>
public class StackSpaceTests : BaseVBTestFixture
{
    [Fact]
    public void PointerIdentities_DoNotGrowValuesOnTheRecursiveWalk()
    {
        // #2622: a fourth reference in Vb6Value made every by-value copy on the walk heavier.
        // Keep main's three references and two flags (including alignment), on either pointer width.
        var size = System.Runtime.CompilerServices.Unsafe.SizeOf<Vb6Value>();
        size.Should().BeLessThanOrEqualTo(4 * IntPtr.Size, "pointer identities must not enlarge values on the recursive walk");
    }

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

    /// <summary>The stack <see cref="DeepButFiniteRecursion_Completes"/> runs on (#2953).</summary>
    private const int DeepRecursionStackBytes = 4 * 1024 * 1024;

    /// <summary>How deep <c>Down</c> must get on that stack.</summary>
    private const int DeepRecursionFloor = 68;

    [Fact]
    public async Task DeepButFiniteRecursion_Completes()
    {
        // A FLOOR, not a ceiling. It is here so that frames growing much fatter fail a test, not a user's
        // working program.
        //
        // It runs on a thread of its own with a stack of a stated size (#2953). It used to run on whichever
        // thread the test landed on, so its depth followed that thread's stack, and CI passed and failed the
        // same code an hour apart. On a fixed stack the depth depends on the frames, and in Debug, which CI
        // tests, it varies by three levels at most between runs.
        //
        // Measured on main 85d64b98b, 2026-11-03, Debug, on GitHub's windows-latest, ubuntu-latest and
        // macos-latest runners: the largest n for which Down(n) completes.
        //
        //   Stack   Windows x64   Linux x64   macOS arm64
        //   1 MB         19           22          20
        //   4 MB       89-92         108          95
        //
        // The floor is about three quarters of the smallest, so frames a third fatter than today's fail it.
        // Today's frames are a regression already: interpreter-gaps.md recorded 74 levels for a recursive
        // Function on a 1 MB Windows stack in September, and #2953 tracks finding what grew them. Raise the
        // floor when they shrink. vb6.exe reaches 258,825 (out-of-stack-space--error-28.md in the oracle).
        await RunOnOwnStack(
            $"Debug.Print Down({DeepRecursionFloor})\n" +
            "Function Down(n)\n" +
            "  If n <= 0 Then\n" +
            "    Down = 0\n" +
            "  Else\n" +
            "    Down = Down(n - 1) + 1\n" +
            "  End If\n" +
            "End Function\n",
            DeepRecursionStackBytes);
        AssertDebugLog([DeepRecursionFloor]);
    }

    /// <summary>Run <paramref name="code"/> on a new thread whose stack is <paramref name="stackBytes"/>. A run with
    /// no debugger attached never yields, so all of its recursion is on that stack.</summary>
    private async Task RunOnOwnStack(string code, int stackBytes)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                var run = Run(code);
                // After a yield the rest of the run would go on a pool thread, whose stack this test doesn't size.
                // Fail and say so, rather than pass or fail by which thread that was.
                if (!run.IsCompleted)
                    throw new InvalidOperationException("The run yielded, so its recursion left the thread this test sized.");
                run.GetAwaiter().GetResult();
                completion.SetResult();
            }
            catch (Exception error)
            {
                completion.SetException(error);
            }
        }, stackBytes);
        thread.Start();
        await completion.Task;
    }
}
