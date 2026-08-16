using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// Phase 5.2 (subset 5b) — On Error GoTo &lt;label&gt;, Resume / Resume Next / Resume &lt;label&gt;, labels,
/// GoTo, and On Error GoTo 0. The procedure body's top-level statements run under a pc-driver that maps labels,
/// jumps on GoTo, installs the handler, and repositions on Resume.
/// </summary>
public class ErrorHandlingGoToTests : BaseVBTestFixture
{
    [Fact]
    public async Task OnErrorGoTo_Handler_Then_ResumeNext()
    {
        await Run(
            "Sub Go()\n" +
            "On Error GoTo Handler\n" +
            "Debug.Print 1\n" +
            "Err.Raise 6\n" +      // faults -> handler
            "Debug.Print 2\n" +    // Resume Next lands here
            "Exit Sub\n" +
            "Handler:\n" +
            "Debug.Print 99\n" +
            "Resume Next\n" +
            "End Sub\n" +
            "Go\n");
        AssertDebugLog([1, 99, 2]);
    }

    [Fact]
    public async Task Resume_RetriesFaultingStatement_AfterHandlerFixesState()
    {
        await Run(
            "Sub Go()\n" +
            "Dim ok\n" +
            "ok = 0\n" +
            "On Error GoTo Handler\n" +
            "If ok = 0 Then\n" +
            "Err.Raise 6\n" +      // faults on the first pass
            "End If\n" +
            "Debug.Print 42\n" +
            "Exit Sub\n" +
            "Handler:\n" +
            "ok = 1\n" +           // fix the condition, then retry the whole If
            "Resume\n" +
            "End Sub\n" +
            "Go\n");
        AssertDebugLog([42]);
    }

    [Fact]
    public async Task ResumeLabel_JumpsToNamedLabel()
    {
        await Run(
            "Sub Go()\n" +
            "On Error GoTo Handler\n" +
            "Err.Raise 6\n" +
            "Debug.Print 1\n" +
            "Exit Sub\n" +
            "Handler:\n" +
            "Debug.Print 99\n" +
            "Resume Recover\n" +
            "Recover:\n" +
            "Debug.Print 7\n" +
            "End Sub\n" +
            "Go\n");
        AssertDebugLog([99, 7]);
    }

    [Fact]
    public async Task PlainGoTo_JumpsForward()
    {
        await Run(
            "Debug.Print 1\n" +
            "GoTo Skip\n" +
            "Debug.Print 2\n" +   // skipped
            "Skip:\n" +
            "Debug.Print 3\n");
        AssertDebugLog([1, 3]);
    }

    [Fact]
    public async Task OnErrorGoTo0_MakesErrorFatal_EvenWithAHandlerLabelPresent()
    {
        Func<Task> act = () => Run(
            "On Error GoTo Handler\n" +
            "On Error GoTo 0\n" +
            "Err.Raise 6\n" +
            "Handler:\n" +
            "Resume Next\n");
        (await act.Should().ThrowAsync<VBRunTimeException>()).Which.Error.ErrNo.Should().Be(6);
    }

    [Fact]
    public async Task ResumeWithNoActiveError_IsError20()
    {
        Func<Task> act = () => Run("Resume\n");
        (await act.Should().ThrowAsync<VBRunTimeException>()).Which.Error.ErrNo.Should().Be(20);
    }
}
