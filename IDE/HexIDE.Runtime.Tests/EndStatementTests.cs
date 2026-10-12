using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// <c>End</c> stops the program where it stands (#775). Measured on vb6.exe, from a Timer tick, from Form_Load
/// and from Sub Main alike: nothing after it runs, no form raises QueryUnload, Unload or Terminate, and no
/// Class_Terminate runs, for an object held at module, form or procedure level. These pin the interpreter's half;
/// the End section of RunFaultTests pins what the IDE does with the run.
/// </summary>
/// <remarks>
/// End unwinds the walk that reached it by throwing, so a test of that walk alone cannot tell whether anything else
/// stops it. Each check that stops some OTHER walk has a test of its own below, in which only that check stands
/// between End and a statement that would otherwise run.
/// </remarks>
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
    public async Task Without_End_leaving_a_procedure_terminates_its_local()
    {
        // The control for the next test: leaving the procedure releases its local, which terminates it.
        await RunClasses(
            "Sub Use()\nDim local As Ob\nSet local = New Ob\nlocal.Id = \"local\"\nEnd Sub\nUse\nDebug.Print \"done\"\n",
            ("Ob", Ob));
        AssertDebugLog(["T:local", "done"]);
    }

    [Fact]
    public async Task End_runs_no_Class_Terminate_for_a_module_level_or_a_local_object()
    {
        // What stops the local's Class_Terminate here is the statement gate, at its first statement; the guard that
        // keeps it from being entered at all is pinned by After_End_releasing_an_object_never_enters_its_Class_Terminate.
        await RunClasses(
            "Dim held As Ob\n" +
            "Sub Quit()\nDim local As Ob\nSet local = New Ob\nlocal.Id = \"local\"\nEnd\nEnd Sub\n" +
            "Set held = New Ob\nheld.Id = \"module\"\nQuit\n",
            ("Ob", Ob));

        AssertDebugLog([]);
    }

    [Fact]
    public async Task After_End_a_procedure_started_later_runs_none_of_its_statements_and_the_host_is_told_once()
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

    // A walk suspended on a dialog when another reaches End must not run its next statement when the dialog
    // answers. With no debugger attached, the interpreter's own Ending flag is all that can stop it, and each
    // place a statement can come next is checked separately: a procedure's top level, a nested block, and the
    // statements joined to a single-line If, which have no gate of their own.
    [Theory]
    [InlineData("MsgBox \"sure?\"\nDebug.Print \"after the dialog\"\n")]
    [InlineData("If True Then\nMsgBox \"sure?\"\nDebug.Print \"after the dialog\"\nEnd If\n")]
    [InlineData("If True Then MsgBox \"sure?\": Debug.Print \"after the dialog\"\n")]
    public async Task A_procedure_waiting_on_a_MsgBox_when_another_reaches_End_does_not_carry_on(string body)
    {
        var dialog = new TaskCompletionSource<HexIDE.IDE.MessageBoxResult>();
        var vb = new BasicInterpreter(new StdLib(debug, () => dialog.Task), context, rootEnv,
            "Sub Ask()\n" + body + "End Sub\n" +
            "Sub Quit()\nEnd\nEnd Sub\n");
        await vb.Execute();

        var asking = vb.ExecuteSub("Ask");
        asking.IsCompleted.Should().BeFalse("the dialog is still up");
        await vb.ExecuteSub("Quit");
        dialog.SetResult(HexIDE.IDE.MessageBoxResult.Ok);
        await asking;

        AssertDebugLog([]);
    }

    [Fact]
    public async Task After_End_a_breakpoint_on_a_later_statement_is_never_hit()
    {
        // The Ending check comes before the debugger's gate. After it, a breakpoint on a statement that would have
        // run broke into the debugger after the program had ended.
        var (vb, dbg) = NewDebuggable("Sub Later()\nDebug.Print \"later\"\nEnd Sub\nEnd\n", "M");
        dbg.SetBreakpoints("M", new[] { 2 });
        await vb.Execute().Guarded();

        await vb.ExecuteSub("Later").Guarded();

        dbg.State.Should().Be(HexIDE.Runtime.Debugging.DebugState.Running, "nothing broke into the debugger");
        AssertDebugLog([]);
    }

    [Fact]
    public async Task After_End_releasing_an_object_never_enters_its_Class_Terminate()
    {
        // The statement gate would stop Class_Terminate's body at its first statement anyway, so no output can
        // show this guard. What it does is keep the procedure from being entered at all, as VB6 runs none: the
        // object is never marked terminated, and nothing is thrown out of the release.
        var vb = new BasicInterpreter(new StdLib(debug), context, rootEnv,
            "Sub Quit()\nEnd\nEnd Sub\n", "Module1", null, [("Ob", Ob)]);
        await vb.Execute();
        var instance = await vb.NewObject("Ob");
        await vb.ExecuteSub("Quit");

        await vb.FireTerminate(instance);

        instance.Terminated.Should().BeFalse();
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
