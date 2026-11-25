using HexIDE.Runtime.Debugging;

namespace HexIDE.Tests.DebugAdapter;

public class DebuggerCompositionTests : CompositionTestBase
{
    // The debugger windows hold an IDebugController and the interpreter an IDebugGate. Two objects would compile and
    // start, and then the program would never stop at a breakpoint the windows had set (#3499). The windows' controller
    // forwards to the debugger the registered engine brings (#5616), which is its gate.
    [Fact]
    public void The_debugger_the_windows_drive_is_the_one_the_interpreter_waits_on()
    {
        var runner = Composition.ProjectRunnerService;
        var gate = Engine!.Debugger.Should().BeAssignableTo<IDebugGate>().Subject;
        gate.Reset();
        Composition.DebugController.State.Should().Be(gate.State);
        Composition.DebugController.Pause();
        Composition.DebugController.State.Should().Be(gate.State, "what the windows do reaches the interpreter's gate");

        runner.HaltHost = _ => Task.FromResult(true);
        gate.CanHalt.Should().BeTrue("the runner's halt host reaches the debugger the interpreter calls");

        runner.HaltHost = null;
        gate.CanHalt.Should().BeFalse();
    }
}
