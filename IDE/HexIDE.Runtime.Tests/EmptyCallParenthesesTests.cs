namespace HexIDE.Runtime.Tests;

// VB6 SP6 acceptance measured in #3779 and measuring PR #3780 (native and p-code).
public class EmptyCallParenthesesTests : BaseVBTestFixture
{
    [Theory]
    [InlineData("\n", false)]
    [InlineData("\r\n", false)]
    [InlineData("\n", true)]
    [InlineData("\r\n", true)]
    public async Task Empty_parentheses_call_a_local_Sub_and_continue(string lineEnding, bool optionExplicit)
    {
        var code = "Call Helper()\nDebug.Print \"after\"\nSub Helper()\nDebug.Print \"helper\"\nEnd Sub\n";
        await Run(((optionExplicit ? "Option Explicit\n" : "") + code).ReplaceLineEndings(lineEnding));
        AssertDebugLog(["helper", "after"]);
    }

    [Theory]
    [InlineData("Call Real.F()")]
    [InlineData("Call Real.F")]
    [InlineData("Call Real.G(1, 2)")]
    public async Task Explicit_module_call_runs_and_continues(string call)
    {
        await RunModules($"{call}\nDebug.Print \"after\"\n",
            ("Real", "Public Function F()\nDebug.Print \"called\"\nEnd Function\nPublic Sub G(a, b)\nDebug.Print \"called\"\nEnd Sub\n"));
        AssertDebugLog(["called", "after"]);
    }

    [Fact]
    public async Task Empty_parentheses_call_an_instance_method_and_continue()
    {
        await RunClasses("Dim k As Worker\nSet k = New Worker\nCall k.M()\nDebug.Print \"after\"\n",
            ("Worker", "Public Sub M()\nDebug.Print \"method\"\nEnd Sub\n"));
        AssertDebugLog(["method", "after"]);
    }

    [Theory]
    [InlineData("Call Helper")]
    [InlineData("Call Helper(1)")]
    public async Task Existing_local_call_forms_still_run(string call)
    {
        await Run($"{call}\nDebug.Print \"after\"\nSub Helper(Optional x As Long = 1)\nDebug.Print \"helper\"\nEnd Sub\n");
        AssertDebugLog(["helper", "after"]);
    }
}
