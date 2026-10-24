using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// A construct HexIDE does not implement is never trappable (#754). <c>On Error</c> must not see it.
///
/// <para>
/// If <c>On Error Resume Next</c> stepped over an unimplemented statement, the program would carry on as
/// though the statement had worked. That is a wrong result, not a late error, and CLAUDE.md's guardrail
/// allows the one and never the other. Only <see cref="VBRunTimeException"/> is trapped, at each of the three
/// trap sites: a procedure body's own statements (<c>ExecuteProcedureBody</c>, one catch for Resume Next and
/// one for a handler) and the statements of a block nested inside it (<c>VisitBlock</c>). These pin all
/// three, because the IDE now reports the untrapped exception and would report nothing if a trap swallowed it.
/// </para>
///
/// <para>
/// <c>AppActivate</c> and <c>GoSub</c> stand for every unimplemented construct. They were chosen as the
/// least likely to be implemented soon: <c>GoSub</c> is the tree-walk limit in <c>interpreter-gaps.md</c>.
/// If either is implemented, substitute another.
/// </para>
/// </summary>
public class UnimplementedIsNotTrappableTests : BaseVBTestFixture
{
    [Theory]
    // A statement of the procedure body itself: ExecuteProcedureBody's Resume Next catch.
    [InlineData("AppActivate \"Notepad\"", "AppActivate", "")]
    [InlineData("GoSub Later", "GoSub", "    Exit Sub\nLater:\n    Return\n")]
    // A statement inside a nested block: VisitBlock's per-block catch, a different trap.
    [InlineData("If True Then\n        AppActivate \"Notepad\"\n    End If", "AppActivate", "")]
    public async Task On_Error_Resume_Next_does_not_step_over_an_unimplemented_statement(
        string statement, string construct, string tail)
    {
        var code = "Sub Probe()\n"
                 + "    On Error Resume Next\n"
                 + "    Debug.Print \"before\"\n"
                 + "    " + statement + "\n"
                 + "    Debug.Print \"after \" & Err.Number\n"
                 + tail
                 + "End Sub\n"
                 + "Probe\n";

        var act = async () => await Run(code);

        (await act.Should().ThrowAsync<NotImplementedException>()).Which.Message.Should().Contain(construct);
        AssertDebugLog([new Vb6Value("before")]);
    }

    [Theory]
    [InlineData("Take New Bomb, Fail()", "")]
    [InlineData("If True Then\nTake New Bomb, Fail()\nEnd If", "")]
    [InlineData("Fail", "Dim local As Bomb\nSet local = New Bomb\n")]
    public async Task Terminate_cannot_replace_an_unimplemented_fault(string statement, string locals)
    {
        var act = async () => await RunClasses(
            "Sub Probe()\nOn Error Resume Next\n" + statement + "\nDebug.Print \"after\"\nEnd Sub\n" +
            "Sub Take(ByVal obj As Bomb, ByVal value)\nEnd Sub\n" +
            "Function Fail()\n" + locals + "AppActivate \"Notepad\"\nEnd Function\nProbe\n",
            ("Bomb", "Private Sub Class_Terminate()\nDebug.Print \"terminate\"\nError 5\nEnd Sub\n"));

        (await act.Should().ThrowAsync<NotImplementedException>()).Which.Message.Should().Contain("AppActivate");
        AssertDebugLog([]);
    }

    [Fact]
    public async Task On_Error_GoTo_does_not_divert_an_unimplemented_statement_to_its_handler()
    {
        const string code = "Sub Probe()\n"
                          + "    On Error GoTo Handler\n"
                          + "    Debug.Print \"before\"\n"
                          + "    AppActivate \"Notepad\"\n"
                          + "    Debug.Print \"after\"\n"
                          + "    Exit Sub\n"
                          + "Handler:\n"
                          + "    Debug.Print \"handled \" & Err.Number\n"
                          + "End Sub\n"
                          + "Probe\n";

        var act = async () => await Run(code);

        await act.Should().ThrowAsync<NotImplementedException>();
        AssertDebugLog([new Vb6Value("before")]);
    }

    [Fact]
    public async Task A_VB6_error_in_the_same_place_is_still_trapped()
    {
        // The control. Without it the two tests above would pass just as well if On Error had stopped
        // working altogether.
        const string code = "Sub Probe()\n"
                          + "    On Error Resume Next\n"
                          + "    Debug.Print \"before\"\n"
                          + "    Error 5\n"
                          + "    Debug.Print \"after \" & Err.Number\n"
                          + "End Sub\n"
                          + "Probe\n";

        await Run(code);

        AssertDebugLog([new Vb6Value("before"), new Vb6Value("after 5")]);
    }
}
