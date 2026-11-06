using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

public class PtrSafeDeclareTests : BaseVBTestFixture
{
    [Fact]
    public async Task PtrSafeRemainsAnIdentifierInABareProcedureCall()
    {
        await Run("Sub PtrSafe()\nDebug.Print \"called\"\nEnd Sub\nPtrSafe\n");
        AssertDebugLog([new Vb6Value("called")]);
    }

    // #1548's first slice: accept the VBA7 modifier, retaining Declare's existing no-op behaviour.
    [Theory]
    [InlineData("Private Declare PtrSafe Function GetTickCount Lib \"kernel32\" () As Long")]
    [InlineData("Public Declare PtrSafe Sub Sleep Lib \"kernel32\" (ByVal milliseconds As Long)")]
    [InlineData("declare ptrsafe function GetTickCount lib \"kernel32\" () as long")]
    public async Task APtrSafeDeclarationDoesNotPreventTheModuleFromRunning(string declaration)
    {
        await RunModules("Main", ("Module1", declaration + "\nSub Main()\nDebug.Print \"loaded\"\nEnd Sub\n"));
        AssertDebugLog([new Vb6Value("loaded")]);
    }

    [Theory]
    [InlineData("Private Declare Function GetTickCount Lib \"kernel32\" () As Long")]
    [InlineData("Private Declare Function PtrSafe Lib \"kernel32\" () As Long")]
    [InlineData("Private Declare Sub PtrSafe Lib \"kernel32\" ()")]
    public async Task AnExistingDeclarationStillLoads(string declaration)
    {
        await RunModules("Main", ("Module1", declaration + "\nSub Main()\nDebug.Print \"loaded\"\nEnd Sub\n"));
        AssertDebugLog([new Vb6Value("loaded")]);
    }
}
