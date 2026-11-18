using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

// Measured native and p-code logs: https://github.com/hexide-io/HexIDE/issues/4599;
// lifetime also recorded in docs/vb6-fidelity-oracle/class_terminate-lifecycle.md.
public class WithNewTests : BaseVBTestFixture
{
    private const string Class1 =
        "Public X As Long\n" +
        "Private Sub Class_Initialize()\nDebug.Print \"initialize\"\nEnd Sub\n" +
        "Private Sub Class_Terminate()\nDebug.Print \"terminate\"\nEnd Sub\n" +
        "Public Property Get Self() As Class1\nSet Self = Me\nEnd Property\n";

    [Fact]
    public async Task WithNewClass_TerminatesAtEndWith()
    {
        await RunClasses(
            "Test\nSub Test()\nWith New Class1\n.X = 5\n" +
            "Debug.Print \"inside, X \" & .X\nEnd With\nDebug.Print \"after\"\nEnd Sub\n",
            ("Class1", Class1));
        AssertDebugLog(["initialize", "inside, X 5", "terminate", "after"]);
    }

    [Fact]
    public async Task WithNewClass_RetainedReferenceSurvivesEndWith()
    {
        await RunClasses(
            "Test\nSub Test()\nDim keep As Class1\nWith New Class1\n" +
            "Set keep = .Self\nEnd With\nDebug.Print \"\" & keep.X\nSet keep = Nothing\nEnd Sub\n",
            ("Class1", Class1));
        AssertDebugLog(["initialize", "0", "terminate"]);
    }

    [Fact]
    public async Task WithNewForm_RefusesNamingTheUnmeasuredLifetime()
    {
        var interpreter = new BasicInterpreter(new MockStdLib(debug), context, rootEnv,
            "Main\nSub Main()\nWith New Form2\nEnd With\nEnd Sub\n", formModules: [("Form2", "")]);
        var run = () => interpreter.Execute();
        await run.Should().ThrowAsync<NotImplementedException>().WithMessage("With New Form2*#435*");
    }

    [Fact]
    public async Task WithNewCollection_AddsItem()
    {
        await RunClasses("Test\nSub Test()\nWith New Collection\n.Add 1\nDebug.Print \"\" & .Count\nEnd With\nEnd Sub\n");
        AssertDebugLog(["1"]);
    }
}
