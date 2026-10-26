using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

public class ExpressionDepthGuardTests : BaseVBTestFixture
{
    [Fact]
    public async Task LongAdditionChain_RaisesError28_AndResumes()
    {
        await Run("Dim x\nOn Error Resume Next\nx = " + string.Join(" + ", Enumerable.Repeat("1", 10000))
            + "\nDebug.Print Err.Number\nDebug.Print 7\n");
        AssertDebugLog([new Vb6Value(28L), new Vb6Value(7)]);
    }

    [Fact]
    public async Task LongContinuedChain_InProcedure_RaisesError28_AndResumes()
    {
        await Run("Sub Test()\nDim x\nOn Error Resume Next\nx = "
            + string.Join(" + _\n", Enumerable.Repeat("1", 10000))
            + "\nDebug.Print Err.Number\nDebug.Print 7\nEnd Sub\nTest\n");
        AssertDebugLog([new Vb6Value(28L), new Vb6Value(7)]);
    }

    [Fact]
    public Task LongIfDirectiveChain_IsRefused() => LongDirectiveChain_IsRefused(
        "#If {0} Then\nDebug.Print 1\n#End If\nDebug.Print 2\n");

    [Fact]
    public Task LongConstDirectiveChain_IsRefused() => LongDirectiveChain_IsRefused(
        "#Const X = {0}\n#If X Then\nDebug.Print 1\n#End If\nDebug.Print 2\n");

    private async Task LongDirectiveChain_IsRefused(string source)
    {
        var code = string.Format(source, string.Join(" + ", Enumerable.Repeat("1", 10000)));
        await ((Func<Task>)(() => Run(code))).Should().ThrowAsync<VBCompileErrorException>();
    }

    [Theory]
    [InlineData("If {0} Then\nDebug.Print 1\nEnd If")]
    [InlineData("Debug.Print {0}")]
    [InlineData("Call Consume({0})")]
    [InlineData("Select Case 1\nCase {0}\nDebug.Print 1\nEnd Select")]
    public async Task LongChain_InStatement_RaisesError28(string statement)
    {
        var code = "Sub Consume(ByVal x)\nEnd Sub\nOn Error GoTo Failed\n"
            + string.Format(statement, string.Join(" + ", Enumerable.Repeat("1", 10000)))
            + "\nExit Sub\nFailed:\nDebug.Print Err.Number\n";
        await Run(code);
        AssertDebugLog([new Vb6Value(28L)]);
    }

    [Fact]
    public async Task LongChain_InModuleConstant_RaisesError28()
    {
        var code = "Const X = " + string.Join(" + ", Enumerable.Repeat("1", 10000)) + "\n";
        // An explicit worker stack also exercises the Windows-sized stack on Linux.
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                Run(code).GetAwaiter().GetResult();
                completion.SetResult();
            }
            catch (Exception error)
            {
                completion.SetException(error);
            }
        }, 1024 * 1024);
        thread.Start();
        var error = await ((Func<Task>)(() => completion.Task)).Should().ThrowAsync<VBRunTimeException>();
        error.Which.Error.Should().Be(VBStandardError.OutOfStackSpace);
    }
}
