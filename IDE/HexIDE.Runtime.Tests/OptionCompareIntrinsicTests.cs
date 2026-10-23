using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

// Each row is from #1572's vb6.exe measurements (native and p-code, en-GB).
public class OptionCompareIntrinsicTests : BaseVBTestFixture
{
    [Theory]
    [InlineData("InStr(\"ABC\", \"b\")", "0", "2")]
    [InlineData("InStr(1, \"ABC\", \"b\")", "0", "2")]
    [InlineData("InStr(1, \"ABC\", \"b\", 0)", "0", "0")]
    [InlineData("InStrRev(\"ABCabc\", \"B\")", "2", "5")]
    [InlineData("Replace(\"ABCabc\", \"b\", \"x\")", "ABCaxc", "AxCaxc")]
    [InlineData("UBound(Split(\"aXbxc\", \"x\")) + 1", "2", "3")]
    [InlineData("UBound(Filter(Array(\"A\", \"b\", \"a\"), \"a\")) + 1", "1", "2")]
    [InlineData("StrComp(\"a\", \"A\")", "1", "0")]
    public async Task MeasuredDefaults(string expression, string binary, string text)
    {
        await Run($"Option Compare Binary\nSub Main()\nDebug.Print CStr({expression})\nEnd Sub\nMain\n");
        await Run($"Option Compare Text\nSub Main()\nDebug.Print CStr({expression})\nEnd Sub\nMain\n");
        AssertDebugLog([new Vb6Value(binary), new Vb6Value(text)]);
    }

    // The issue specifies that the module whose code makes the intrinsic call supplies the mode.
    // Reuse only its measured InStr call and values across opposite-mode callers, including a class.
    [Theory]
    [InlineData("Binary", "Text", "2")]
    [InlineData("Text", "Binary", "0")]
    public async Task CallingCodeOwnsTheMode(string callerMode, string calleeMode, string expected)
    {
        var callee = $"Option Compare {calleeMode}\nPublic Function Probe() As String\nProbe = CStr(InStr(\"ABC\", \"b\"))\nEnd Function\n";
        await RunModules($"Option Compare {callerMode}\nSub Main()\nDebug.Print Probe()\nEnd Sub\nMain\n", ("Other", callee));
        await RunClasses($"Option Compare {callerMode}\nSub Main()\nDim c As New Other\nDebug.Print c.Probe()\nEnd Sub\nMain\n", ("Other", callee));
        AssertDebugLog([new Vb6Value(expected), new Vb6Value(expected)]);
    }
}
