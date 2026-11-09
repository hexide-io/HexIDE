using HexIDE.Runtime.Debugging;

namespace HexIDE.Tests.DebugAdapter;

public class DebuggerCompositionTests : CompositionTestBase
{
    // The debugger windows hold an IDebugController and the interpreter an IDebugGate. Two objects would compile and
    // start, and then the program would never stop at a breakpoint the windows had set (#3499).
    [Fact]
    public void The_debugger_the_windows_drive_is_the_one_the_interpreter_waits_on()
    {
        var gate = Composition.DebugController.Should().BeAssignableTo<IDebugGate>().Subject;
        var runner = Composition.ProjectRunnerService;

        runner.HaltHost = _ => Task.FromResult(true);
        gate.CanHalt.Should().BeTrue("the runner's halt host reaches the debugger the interpreter calls");

        runner.HaltHost = null;
        gate.CanHalt.Should().BeFalse();
    }
}
