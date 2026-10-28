using HexIDE.IDE;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// Every form, control, menu and timer event runs through <see cref="VBWindowContext.ExecuteSub"/>, and an
/// exception that was not a VB6 error used to stop there: logged, and nothing else (#754). It is now raised as
/// <see cref="VBWindowContext.Faulted"/>, for whoever owns the run to report.
///
/// <para>
/// The event is static, so each test listens only for its own context. The standard library here answers
/// with tasks that are already complete, which makes each procedure finish before <c>ExecuteSub</c> returns;
/// the first test asserts that directly, so the negative assertions after it cannot pass merely by looking
/// too early.
/// </para>
/// </summary>
public class WindowContextFaultTests
{
    [Fact]
    public void Visibility_before_a_form_load_does_not_start_its_controls()
    {
        // A managed window's initial IsVisible notification arrives while its constructor is still running (#1631).
        var context = new VBWindowContext(new StandardLibrary(() => Task.FromResult(MessageBoxResult.Ok)));
        var shown = () => context.Shown();

        shown.Should().NotThrow();
        context.Interpreter.Should().BeNull();
    }

    private sealed class StandardLibrary(Func<Task<MessageBoxResult>> msgBox) : IBasicStandardLibrary
    {
        public Task<MessageBoxResult> MsgBox(string text, string? caption, MessageBoxButtons buttons, MessageBoxIcon icon)
            => msgBox();
        public Task<string?> InputBox(string prompt, string? title, string defaultText) => Task.FromResult<string?>(null);
        public void DebugPrint(Vb6Value value) { }
    }

    private sealed class Raised
    {
        public List<Exception> Faults { get; } = new();
        public List<VBRunTimeException> RunTimeErrors { get; } = new();
    }

    private static Raised Execute(string code, string sub, Func<Task<MessageBoxResult>>? msgBox = null)
    {
        var context = new VBWindowContext(new StandardLibrary(msgBox ?? (() => Task.FromResult(MessageBoxResult.Ok))));
        context.SetCode(code, "Form1");
        var raised = new Raised();
        void OnFaulted(VBWindowContext sender, Exception e)
        {
            if (ReferenceEquals(sender, context))
                raised.Faults.Add(e);
        }
        void OnRunTimeError(VBWindowContext sender, VBRunTimeException e)
        {
            if (ReferenceEquals(sender, context))
                raised.RunTimeErrors.Add(e);
        }

        VBWindowContext.Faulted += OnFaulted;
        VBWindowContext.RunTimeError += OnRunTimeError;
        try
        {
            context.ExecuteSub(sub);
        }
        finally
        {
            VBWindowContext.Faulted -= OnFaulted;
            VBWindowContext.RunTimeError -= OnRunTimeError;
        }
        return raised;
    }

    [Fact]
    public void An_unimplemented_statement_in_an_event_procedure_is_raised_as_a_fault()
    {
        var raised = Execute("Private Sub Command1_Click()\n    AppActivate \"Notepad\"\nEnd Sub\n", "Command1_Click");

        raised.Faults.Should().ContainSingle("it is raised once, before ExecuteSub returns")
            .Which.Should().BeOfType<NotImplementedException>()
            .Which.Message.Should().Contain("AppActivate");
        raised.RunTimeErrors.Should().BeEmpty("it is not a VB6 error, so it must not read as one");
    }

    [Fact]
    public void An_exception_from_anywhere_else_is_raised_as_a_fault_too()
    {
        // Not only NotImplementedException: anything that is not a VB6 error used to stop at the same catch.
        var raised = Execute("Private Sub Form_Load()\n    MsgBox \"hello\"\nEnd Sub\n", "Form_Load",
            () => throw new InvalidOperationException("the dialog could not be shown"));

        raised.Faults.Should().ContainSingle()
            .Which.Should().BeOfType<InvalidOperationException>()
            .Which.Message.Should().Be("the dialog could not be shown");
    }

    [Fact]
    public void A_VB6_runtime_error_is_still_a_runtime_error_and_not_a_fault()
    {
        var raised = Execute("Private Sub Command1_Click()\n    Error 5\nEnd Sub\n", "Command1_Click");

        raised.RunTimeErrors.Should().ContainSingle().Which.Error.ErrNo.Should().Be(5);
        raised.Faults.Should().BeEmpty();
    }

    [Fact]
    public void A_cancellation_is_not_a_fault()
    {
        // How a run being torn down reaches a statement still waiting on a dialog. Reporting it would put an
        // error in front of someone who had just pressed End.
        var raised = Execute("Private Sub Command1_Click()\n    MsgBox \"hello\"\nEnd Sub\n", "Command1_Click",
            () => Task.FromCanceled<MessageBoxResult>(new CancellationToken(canceled: true)));

        raised.Faults.Should().BeEmpty();
        raised.RunTimeErrors.Should().BeEmpty();
    }

    [Fact]
    public void The_Immediate_window_refusing_an_event_procedure_is_not_a_fault()
    {
        // While paused, `Text1.Text = "x"` typed into the Immediate window raises Text1_Change, which the
        // interpreter refuses to run in break mode with an ImmediateEvalException. That is the Immediate window's
        // answer to what was typed. Raised as a fault, it would tear down the run the developer is inspecting.
        // RunFaultTests drives the real path; this pins the boundary's own rule.
        var raised = Execute("Private Sub Command1_Click()\n    MsgBox \"hello\"\nEnd Sub\n", "Command1_Click",
            () => throw new Debugging.ImmediateEvalException("can't call a procedure here"));

        raised.Faults.Should().BeEmpty();
        raised.RunTimeErrors.Should().BeEmpty();
    }
}
