using System.Runtime.InteropServices;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

// The seven Error 28 cases below pin HexIDE's own #1930 guard, not VB6 behaviour (#3203).
// Corpus rows 2466, 3139, 3665, 4040, 5891, 6045 and 6112 come from these chains.
// vb6.exe refuses the modules at load; measurements:
// docs/vb6-fidelity-oracle/long-lines-line-continuations-and-long-expressions.md.
public class ExpressionDepthGuardTests : BaseVBTestFixture
{
    /// <summary>The stack each Error 28 case runs its program on (#3243). The guard trips on the stack the program
    /// runs on, and a test runner's own thread has whatever the host gives it: 1.5 MiB on Windows, `ulimit -s` on
    /// Linux. On Linux CI that is 16 MiB, where a 10,000-term chain sometimes fits and runs to the end. 1 MiB is
    /// less than Windows' first thread has, so a chain this long overflows it on every runner.</summary>
    private const int StackBytes = 1024 * 1024;

    private static int runnerStackLogged;

    [Fact]
    public async Task LongAdditionChain_RaisesError28_AndResumes()
    {
        await RunOnSizedStack("Dim x\nOn Error Resume Next\nx = " + string.Join(" + ", Enumerable.Repeat("1", 10000))
            + "\nDebug.Print Err.Number\nDebug.Print 7\n");
        AssertDebugLog([new Vb6Value(28L), new Vb6Value(7)]);
    }

    [Fact]
    public async Task LongContinuedChain_InProcedure_RaisesError28_AndResumes()
    {
        await RunOnSizedStack("Sub Test()\nDim x\nOn Error Resume Next\nx = "
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
    // Each statement variant pins HexIDE's guard, not a VB6 run-time result.
    [InlineData("If {0} Then\nDebug.Print 1\nEnd If")]
    [InlineData("Debug.Print {0}")]
    [InlineData("Call Consume({0})")]
    [InlineData("Select Case 1\nCase {0}\nDebug.Print 1\nEnd Select")]
    public async Task LongChain_InStatement_RaisesError28(string statement)
    {
        var code = "Sub Consume(ByVal x)\nEnd Sub\nOn Error GoTo Failed\n"
            + string.Format(statement, string.Join(" + ", Enumerable.Repeat("1", 10000)))
            + "\nExit Sub\nFailed:\nDebug.Print Err.Number\n";
        await RunOnSizedStack(code);
        AssertDebugLog([new Vb6Value(28L)]);
    }

    [Fact]
    public async Task LongChain_InModuleConstant_RaisesError28()
    {
        var code = "Const X = " + string.Join(" + ", Enumerable.Repeat("1", 10000)) + "\n";
        var error = await ((Func<Task>)(() => RunOnSizedStack(code))).Should().ThrowAsync<VBRunTimeException>();
        error.Which.Error.Should().Be(VBStandardError.OutOfStackSpace);
    }

    /// <summary>Runs <paramref name="code"/> to the end on a thread of <see cref="StackBytes"/>.</summary>
    private Task RunOnSizedStack(string code)
    {
        LogRunnerStackOnce();
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                var run = Run(code);
                // A program that yielded would finish on a pool thread, on whatever stack that has.
                if (!run.IsCompleted)
                    throw new InvalidOperationException("The program yielded, so part of it ran off the sized stack.");
                run.GetAwaiter().GetResult();
                completion.SetResult();
            }
            catch (Exception error)
            {
                completion.SetException(error);
            }
        }, StackBytes) { IsBackground = true };
        thread.Start();
        return completion.Task;
    }

    /// <summary>Says once what stack the test runner's own thread has, which is where these programs used to run
    /// (#3243): in the test's output, and on CI in the job summary, since a passing test's output is not shown.</summary>
    private static void LogRunnerStackOnce()
    {
        if (Interlocked.Exchange(ref runnerStackLogged, 1) != 0)
            return;
        var line = $"{nameof(ExpressionDepthGuardTests)}: the test runner's thread has a "
            + $"{ThreadStack.Describe(ThreadStack.ReservedBytes())} stack, on {RuntimeInformation.OSDescription}; "
            + $"its Error 28 programs run on {ThreadStack.Describe(StackBytes)} (#3243).";
        TestContext.Current.TestOutputHelper?.WriteLine(line);
        if (Environment.GetEnvironmentVariable("GITHUB_STEP_SUMMARY") is { Length: > 0 } summary)
            File.AppendAllText(summary, line + Environment.NewLine);
    }
}
