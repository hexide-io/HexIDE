using System.Threading.Tasks;

namespace HexIDE.Runtime.Tests;

// VB6 SP6, native and p-code: https://github.com/hexide-io/HexIDE/issues/4624,
// supervisor measurement comment (2026-11-18). Set-through-member is covered separately by #4600.
public class BareFunctionLeadTests : BaseVBTestFixture
{
    private const string BoxCode = """
        Public X As Long
        Public Function Q() As String
            Q = "q"
        End Function
        Public Sub DoIt()
            Debug.Print "did"
        End Sub
        Public Property Get Me2() As Box
            Set Me2 = Me
        End Property
        """;

    private const string ModuleCode = """
        Dim kept As Box
        Dim calls As Long
        Function GetB() As Box
            calls = calls + 1
            Set GetB = kept
        End Function
        Function GetK() As Box
            Set GetK = kept
        End Function
        Function GetC() As Collection
            Dim items As New Collection
            items.Add 1
            Set GetC = items
        End Function
        Function GetO() As Object
            Set GetO = New Box
        End Function
        Function GetV() As Variant
            Set GetV = New Box
        End Function
        Property Get PB() As Box
            Set PB = kept
        End Property
        Set kept = New Box
        """;

    [Theory]
    [InlineData("GetB.Q", "q")]
    [InlineData("CStr(GetC.Count)", "1")]
    [InlineData("GetO.Q", "q")]
    [InlineData("GetV.Q", "q")]
    [InlineData("GetB.Me2.Q", "q")]
    [InlineData("TypeName(GetB.Me2)", "Box")]
    public async Task BareFunctionHeadsAReadChain(string expression, string expected)
    {
        await RunClasses(ModuleCode + "\nDebug.Print " + expression, ("Box", BoxCode));
        AssertDebugLog([expected]);
    }

    [Fact]
    public async Task BareFunctionChainAssignsAString()
    {
        await RunClasses(ModuleCode + "\nDim s As String\ns = GetB.Q\nDebug.Print s", ("Box", BoxCode));
        AssertDebugLog(["q"]);
    }

    [Fact]
    public async Task BareFunctionHeadsALetChain()
    {
        await RunClasses(ModuleCode + "\nGetK.X = 5\nDebug.Print CStr(kept.X)", ("Box", BoxCode));
        AssertDebugLog(["5"]);
    }

    [Fact]
    public async Task EachBareFunctionLeadCallsTheFunctionOnce()
    {
        await RunClasses(ModuleCode + "\nDebug.Print GetB.Q & GetB.Q\nDebug.Print CStr(calls)", ("Box", BoxCode));
        AssertDebugLog(["qq", "2"]);
    }

    [Theory]
    [InlineData("Debug.Print GetB().Q", "q")]
    [InlineData("Debug.Print PB.Q", "q")]
    [InlineData("Debug.Print Module1.GetB.Q", "q")]
    [InlineData("GetB.DoIt", "did")]
    [InlineData("Call GetB.DoIt", "did")]
    public async Task ExistingChainFormsStillWork(string statement, string expected)
    {
        await RunClasses(ModuleCode + "\n" + statement, ("Box", BoxCode));
        AssertDebugLog([expected]);
    }
}
