using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// Phase 5.1 (subset 5a) — On Error Resume Next + the Err object. Every VisitBlock traps a VBRunTimeException
/// when the mode is Resume Next, records it in the global Err, and continues with the next statement.
/// Err.Number is a VB6 Long.
/// </summary>
public class ErrorHandlingTests : BaseVBTestFixture
{
    [Fact]
    public async Task ResumeNext_SkipsFaultingStatement_AndContinues()
    {
        await Run(
            "On Error Resume Next\n" +
            "Debug.Print 1\n" +
            "Err.Raise 5\n" +      // faults — skipped
            "Debug.Print 2\n");
        AssertDebugLog([1, 2]);
    }

    [Fact]
    public async Task ErrNumber_CarriesRaisedCode()
    {
        await Run(
            "On Error Resume Next\n" +
            "Err.Raise 6\n" +
            "Debug.Print Err.Number\n");
        AssertDebugLog([new Vb6Value(6L)]);   // Err.Number is a Long
    }

    [Fact]
    public async Task NaturalError_ArraySubscript_IsError9()
    {
        await Run(
            "On Error Resume Next\n" +
            "Dim a(1 To 3) As Integer\n" +
            "Dim x\n" +
            "x = a(10)\n" +
            "Debug.Print Err.Number\n");
        AssertDebugLog([new Vb6Value(9L)]);
    }

    [Fact]
    public async Task ErrDescription_UsesStandardText()
    {
        await Run(
            "On Error Resume Next\n" +
            "Err.Raise 6\n" +
            "Debug.Print Err.Description\n");
        AssertDebugLog(["Overflow"]);
    }

    [Fact]
    public async Task ErrClear_Resets_And_ErrPersistsUntilCleared()
    {
        await Run(
            "On Error Resume Next\n" +
            "Err.Raise 13\n" +
            "Debug.Print Err.Number\n" +   // 13
            "Debug.Print Err.Number\n" +   // still 13 (persists)
            "Err.Clear\n" +
            "Debug.Print Err.Number\n");   // 0
        AssertDebugLog([new Vb6Value(13L), new Vb6Value(13L), new Vb6Value(0L)]);
    }

    [Fact]
    public async Task ErrorStatement_IsEquivalentToRaise()
    {
        await Run(
            "On Error Resume Next\n" +
            "Error 9\n" +
            "Debug.Print Err.Number\n");
        AssertDebugLog([new Vb6Value(9L)]);
    }

    [Fact]
    public async Task OnErrorGoTo0_DisablesHandling_MakingErrorFatal()
    {
        Func<Task> act = () => Run(
            "On Error Resume Next\n" +
            "On Error GoTo 0\n" +
            "Err.Raise 6\n");
        (await act.Should().ThrowAsync<VBRunTimeException>()).Which.Error.ErrNo.Should().Be(6);
    }
}
