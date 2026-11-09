using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

// Measured in docs/vb6-fidelity-oracle/what-a-timer-has-at-run-time.md (#801, #3431).
public class TimerMemberTests : BaseVBTestFixture
{
    [Theory]
    [InlineData("TabIndex")]
    [InlineData("ToolTipText")]
    public async Task Object_reads_of_absent_extender_members_raise_trappable_438(string member)
    {
        context.AllocVariable(rootEnv, "Timer1", new Vb6Value(new VBTimer()));

        await Run($"""
            Dim o As Object
            Set o = Timer1
            On Error Resume Next
            Dim v
            v = o.{member}
            Debug.Print Err.Number
            Debug.Print Err.Description
            """);

        AssertDebugLog([new Vb6Value(438L), new Vb6Value("Object doesn't support this property or method")]);
    }

    [Theory]
    [InlineData("TabIndex")]
    [InlineData("ToolTipText")]
    public async Task Direct_reads_keep_the_missing_member_compile_error(string member)
    {
        context.AllocVariable(rootEnv, "Timer1", new Vb6Value(new VBTimer()));

        var act = () => Run($"On Error Resume Next\nDebug.Print Timer1.{member}");

        await act.Should().ThrowAsync<VBMethodOrDataMemberNotFoundException>();
    }
}
