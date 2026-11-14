using System.Threading.Tasks;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.Components.InBox;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

// HexIDE extension contract: https://github.com/hexide-io/HexIDE/issues/1116 (first-slice decision).
// Value's Single conversion/errors: docs/vb6-fidelity-oracle/in-box-controls-the-progressbar.md.
public class ProgressBarIncrementTests : BaseVBTestFixture
{
    [Theory]
    [InlineData(false, "5", 55)]
    [InlineData(true, "5", 55)]
    [InlineData(false, "100", 100)]
    [InlineData(false, "-100", 10)]
    [InlineData(false, "-5", 45)]
    [InlineData(false, "\"5\"", 55)]
    public async Task Increment_adds_and_clamps(bool version5, string argument, float expected)
    {
        await Execute($"pb.Increment {argument}\nDebug.Print pb.Value", version5);
        AssertDebugLog([new Vb6Value(expected)]);
    }

    [Theory]
    [InlineData("\"abc\"", 13)]
    [InlineData("Null", 13)]
    [InlineData("1E+39", 6)]
    [InlineData("", 449)]
    public async Task Increment_refuses_invalid_or_missing_arguments_without_changing_Value(string argument, long error)
    {
        await Execute($"On Error Resume Next\npb.Increment {argument}\nDebug.Print Err.Number, pb.Value", false);
        AssertDebugLog([new Vb6Value(error), new Vb6Value(50f)]);
    }

    [Fact]
    public void Increment_is_a_code_only_HexIDE_method_in_both_libraries()
    {
        var member = ProgressBarComponentClass.Instance.Member("Increment");
        member.Should().NotBeNull();
        member!.Kind.Should().Be(MemberKind.Method);
        member.Origin.Should().Be(MemberOrigin.HexIde);
        member.IsFrmProperty.Should().BeFalse();
        member.Implemented.Should().BeTrue();
        member.IsIn(ControlLibraries.ComctlLib).Should().BeTrue();
        member.IsIn(ControlLibraries.MSComctlLib).Should().BeTrue();
    }

    private async Task Execute(string code, bool version5)
    {
        context.AllocVariable(rootEnv, "pb", new Vb6Value(new VBProgressBar
        {
            Library = version5 ? ControlLibraries.ComctlLib : ControlLibraries.MSComctlLib,
            VbMin = 10, VbMax = 100, VbValue = 50,
        }));
        await new BasicInterpreter(new MockStdLib(debug), context, rootEnv, code).Execute();
    }
}
