using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

// VB6 native and p-code measurements for module and class Subs: https://github.com/hexide-io/HexIDE/issues/4674.
public class SubKeywordNameTests : BaseVBTestFixture
{
    public static TheoryData<string, bool> RefusedNames => Cases(
        ["Close", "Debug", "End", "Get", "Input", "Len", "Lock", "Open", "Print", "Put", "Seek", "Spc", "Stop", "Tab", "Unlock", "Write"]);

    public static TheoryData<string, bool> AcceptedNames => Cases(
        ["Beep", "Error", "Kill", "Line", "Load", "Mid", "Name", "Reset", "Unload", "Width"]);

    private static TheoryData<string, bool> Cases(string[] names)
    {
        var cases = new TheoryData<string, bool>();
        foreach (var name in names)
        {
            cases.Add(name, false);
            cases.Add(name, true);
        }
        return cases;
    }

    [Theory]
    [MemberData(nameof(RefusedNames))]
    public async Task StatementKeywordSub_IsRefusedAtLoad(string name, bool inClass)
    {
        Func<Task> act = () => inClass
            ? RunClasses("Debug.Print 1\n", ("Worker", $"Public Sub {name}()\nEnd Sub\n"))
            : RunModules("Debug.Print 1\n", ("Worker", $"Sub {name}()\nEnd Sub\n"));
        await act.Should().ThrowAsync<VBCompileErrorException>().WithMessage("Compile error:*Expected: identifier");
        AssertDebugLog([]);
    }

    [Theory]
    [MemberData(nameof(AcceptedNames))]
    public async Task AcceptedKeywordSub_DoesNotPreventModuleRun(string name, bool inClass)
    {
        // The measurements cover declarations; class Line call syntax is explicitly out of scope.
        var declaration = $"Public Sub {name}()\nEnd Sub\n";
        if (inClass)
            await RunClasses("Debug.Print 1\n", ("Worker", declaration));
        else
            await RunModules("Debug.Print 1\n", ("Worker", declaration));
        AssertDebugLog([1]);
    }
}
