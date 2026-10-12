using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// A construct HexIDE does not implement is never trappable (#754). <c>On Error</c> must not see it.
///
/// <para>
/// If <c>On Error Resume Next</c> stepped over an unimplemented statement, the program would carry on as
/// though the statement had worked. That is a wrong result, not a late error, and CLAUDE.md's guardrail
/// allows the one and never the other. Only <see cref="VBRunTimeException"/> is trapped, at both trap sites
/// (the plain block walk and the label-addressed one a procedure gets once it has a label); these pin it,
/// because the IDE now reports the untrapped exception and would report nothing if a trap swallowed it.
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
    // No label in the procedure: the plain block walk, whose trap is StatementExecutor's per-block catch.
    [InlineData("AppActivate \"Notepad\"", "AppActivate", "")]
    // A label makes the procedure use the label-addressed walk, which has a trap of its own.
    [InlineData("GoSub Later", "GoSub", "    Exit Sub\nLater:\n    Return\n")]
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
