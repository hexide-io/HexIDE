using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// Phase 4.4 — With. `Debug` is a CSharpProxy seeded by the interpreter, so `With Debug / .Print … / End With`
/// exercises the leading-dot → With-target stack mechanism (method dispatch). A leading dot with no active With
/// raises Error 91. Control property read/write via With is covered by an HexIDE.Integration.Tests test.
/// </summary>
public class WithTests : BaseVBTestFixture
{
    [Fact]
    public async Task With_ResolvesLeadingDotMethodCall()
    {
        await Run(
            "With Debug\n" +
            ".Print 1\n" +
            ".Print 2\n" +
            "End With\n");
        AssertDebugLog([1, 2]);
    }

    [Fact]
    public async Task NestedWith_ResolvesToInnermostThenOuter()
    {
        await Run(
            "With Debug\n" +
            ".Print 10\n" +
            "With Debug\n" +
            ".Print 20\n" +
            "End With\n" +
            ".Print 30\n" +   // back to the outer With
            "End With\n");
        AssertDebugLog([10, 20, 30]);
    }

    [Fact]
    public async Task LeadingDot_OutsideAnyWith_RaisesError91()
    {
        Func<Task> act = () => Run(".Print 1\n");
        (await act.Should().ThrowAsync<VBRunTimeException>()).Which.Error.ErrNo.Should().Be(91);
    }
}
