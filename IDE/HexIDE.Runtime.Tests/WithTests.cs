using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

public class WithTests : BaseVBTestFixture
{
    [Fact]
    public async Task WithDebug_IsSyntaxError()
    {
        Func<Task> act = () => Run("With Debug\n.Print 1\nEnd With\n");
        await act.Should().ThrowAsync<VBCompileErrorException>().WithMessage("Compile error:*Syntax error");
    }

    [Fact]
    public async Task With_ResolvesLeadingDotMethodCall()
    {
        await RunClasses(
            "Dim c As Writer\nSet c = New Writer\nWith c\n.Write 1\n.Write 2\nEnd With\n",
            ("Writer", "Public Sub Write(n As Integer)\nDebug.Print n\nEnd Sub\n"));
        AssertDebugLog([1, 2]);
    }

    [Fact]
    public async Task NestedWith_ResolvesToInnermostThenOuter()
    {
        await RunClasses(
            "Dim a As Writer\nSet a = New Writer\na.N = 10\n" +
            "Dim b As Writer\nSet b = New Writer\nb.N = 20\n" +
            "With a\n.Write\nWith b\n.Write\nEnd With\n.Write\nEnd With\n",
            ("Writer", "Public N As Integer\nPublic Sub Write()\nDebug.Print N\nEnd Sub\n"));
        AssertDebugLog([10, 20, 10]);
    }

    [Fact]
    public async Task LeadingDot_OutsideAnyWith_RaisesError91()
    {
        Func<Task> act = () => Run(".Print 1\n");
        (await act.Should().ThrowAsync<VBRunTimeException>()).Which.Error.ErrNo.Should().Be(91);
    }
}
