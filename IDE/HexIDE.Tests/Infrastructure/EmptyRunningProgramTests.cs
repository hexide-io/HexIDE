using HexIDE.IDE;

namespace HexIDE.Tests.Infrastructure;

public class EmptyRunningProgramTests
{
    [Fact]
    public void An_absent_engine_has_no_forms_activity_or_output()
    {
        IRunningProgram program = new EmptyRunningProgram();
        var output = new List<string>();
        program.Written += output.Add;
        program.ShownWindows(new object()).Should().BeEmpty();
        program.Form(new object()).Should().BeNull();
        program.Activity(new object()).Should().BeNull();
        program.Activity(null).Should().BeNull("no activity means there is nothing to wait for");
        program.Written -= output.Add;
        output.Should().BeEmpty();
    }
}
