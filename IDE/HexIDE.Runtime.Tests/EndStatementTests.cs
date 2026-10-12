using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// <c>End</c> stops the program where it stands (#775). Measured on vb6.exe, from a Timer tick, from Form_Load
/// and from Sub Main alike: nothing after it runs, no form raises QueryUnload, Unload or Terminate, and no
/// Class_Terminate runs, for an object held at module, form or procedure level. These pin the interpreter's half;
/// the End section of RunFaultTests pins what the IDE does with the run.
/// </summary>
public class EndStatementTests : BaseVBTestFixture
{
    private const string Ob =
        "Public Id As String\n" +
        "Private Sub Class_Terminate()\nDebug.Print \"T:\" & Id\nEnd Sub\n";

    [Fact]
    public async Task Nothing_after_End_runs()
    {
        await Run("Debug.Print \"before\"\nEnd\nDebug.Print \"after\"\n");

        AssertDebugLog(["before"]);
    }

    [Fact]
    public async Task End_in_a_called_procedure_ends_its_callers_too()
    {
        await Run(
            "Sub Quit()\nDebug.Print \"in Quit\"\nEnd\nDebug.Print \"after End\"\nEnd Sub\n" +
            "Quit\nDebug.Print \"after the call\"\n");

        AssertDebugLog(["in Quit"]);
    }

    [Fact]
    public async Task An_error_handler_does_not_catch_End()
    {
        // End is not an error, so neither form of handler sees it.
        await Run(
            "Sub Quit()\nOn Error GoTo Handler\nEnd\nExit Sub\nHandler:\nDebug.Print \"handled\"\nEnd Sub\n" +
            "On Error Resume Next\nQuit\nDebug.Print \"after\"\n");

        AssertDebugLog([]);
    }

    [Fact]
    public async Task End_in_a_single_line_If_stops_the_rest_of_the_line()
    {
        await Run("If True Then Debug.Print \"before\": End: Debug.Print \"after\"\n");

        AssertDebugLog(["before"]);
    }

    [Fact]
    public async Task No_Class_Terminate_runs_after_End()
    {
        // The control: leaving the procedure releases its local, which terminates it.
        await RunClasses(
            "Sub Use()\nDim local As Ob\nSet local = New Ob\nlocal.Id = \"local\"\nEnd Sub\nUse\nDebug.Print \"done\"\n",
            ("Ob", Ob));
        AssertDebugLog(["T:local", "done"]);
    }

    [Fact]
    public async Task End_runs_no_Class_Terminate_for_a_module_level_or_a_local_object()
    {
        debug.Clear();
        await RunClasses(
            "Dim held As Ob\n" +
            "Sub Quit()\nDim local As Ob\nSet local = New Ob\nlocal.Id = \"local\"\nEnd\nEnd Sub\n" +
            "Set held = New Ob\nheld.Id = \"module\"\nQuit\n",
            ("Ob", Ob));

        AssertDebugLog([]);
    }

    [Fact]
    public async Task After_End_the_program_starts_nothing_again_and_says_it_ended()
    {
        // A Timer tick or a click queued behind the statement that reached End finds the program over.
        var vb = new BasicInterpreter(new StdLib(debug), context, rootEnv,
            "Sub Later()\nDebug.Print \"later\"\nEnd Sub\nEnd\n");
        var ended = new List<BasicInterpreter>();
        void OnEnd(BasicInterpreter i)
        {
            if (ReferenceEquals(i, vb))
                ended.Add(i);
        }

        BasicInterpreter.EndReached += OnEnd;
        try
        {
            await vb.Execute();
            await vb.ExecuteSub("Later");
        }
        finally
        {
            BasicInterpreter.EndReached -= OnEnd;
        }

        vb.Ending.Should().BeTrue();
        ended.Should().ContainSingle("the host is told once, so it can end the run");
        AssertDebugLog([]);
    }

    [Fact]
    public async Task A_procedure_waiting_on_a_MsgBox_when_another_reaches_End_does_not_carry_on()
    {
        // Two walks of one interpreter: the first is suspended on a dialog when a second procedure reaches End.
        // When the dialog answers, the first must find the program over rather than run its next statement. With
        // no debugger attached, the interpreter's own Ending flag is all that can stop it.
        var dialog = new TaskCompletionSource<HexIDE.IDE.MessageBoxResult>();
        var vb = new BasicInterpreter(new StdLib(debug, () => dialog.Task), context, rootEnv,
            "Sub Ask()\nMsgBox \"sure?\"\nDebug.Print \"after the dialog\"\nEnd Sub\n" +
            "Sub Quit()\nEnd\nEnd Sub\n");
        await vb.Execute();

        var asking = vb.ExecuteSub("Ask");
        asking.IsCompleted.Should().BeFalse("the dialog is still up");
        await vb.ExecuteSub("Quit");
        dialog.SetResult(HexIDE.IDE.MessageBoxResult.Ok);
        await asking;

        AssertDebugLog([]);
    }

    private sealed class StdLib(List<Vb6Value> debug, Func<Task<HexIDE.IDE.MessageBoxResult>>? msgBox = null)
        : IBasicStandardLibrary
    {
        public Task<HexIDE.IDE.MessageBoxResult> MsgBox(string text, string? caption, HexIDE.IDE.MessageBoxButtons buttons,
            HexIDE.IDE.MessageBoxIcon icon) => msgBox?.Invoke() ?? Task.FromResult(HexIDE.IDE.MessageBoxResult.Ok);
        public Task<string?> InputBox(string prompt, string? title, string defaultText) => Task.FromResult<string?>(null);
        public void DebugPrint(Vb6Value value) => debug.Add(value);
    }
}
