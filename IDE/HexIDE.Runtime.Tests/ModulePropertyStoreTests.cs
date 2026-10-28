using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

public class ModulePropertyStoreTests : BaseVBTestFixture
{
    private const string Scalar = """
        Public Property Get PG() As Long
            PG = 5
        End Property
        Public Property Let PG(v As Long)
            Debug.Print CStr(v)
        End Property
        """;

    private const string ObjectProperty = """
        Dim held As Collection
        Public Property Get PO() As Collection
            Set PO = held
        End Property
        Public Property Set PO(v As Collection)
            Set held = v
        End Property
        """;

    [Theory]
    [InlineData("PG = 5", "5")]
    [InlineData("Module2.PG = 7", "7")]
    public async Task ScalarLet(string statement, string expected)
    {
        await RunModules("Option Explicit\n" + statement, ("Module2", Scalar));
        AssertDebugLog([expected]);
    }

    [Fact]
    public async Task IndexedLet()
    {
        await RunModules("Option Explicit\nPI(3) = 100", ("Module2", """
            Public Property Get PI(i As Long) As Long
                PI = i
            End Property
            Public Property Let PI(i As Long, v As Long)
                Debug.Print CStr(i)
                Debug.Print CStr(v)
            End Property
            """));
        AssertDebugLog(["3", "100"]);
    }

    [Fact]
    public async Task ObjectSet()
    {
        await RunModules("Option Explicit\nSet PO = New Collection\nPO.Add 1\nDebug.Print CStr(PO.Count)",
            ("Module2", ObjectProperty));
        AssertDebugLog(["1"]);
    }

    [Fact]
    public async Task LetCoercionError()
    {
        await RunModules("Option Explicit\nOn Error Resume Next\nPG = \"x\"\nDebug.Print CStr(Err.Number)",
            ("Module2", Scalar));
        AssertDebugLog(["13"]);
    }

    [Fact]
    public async Task LetWithoutLetReadsObjectGetter()
    {
        await RunModules("Option Explicit\nOn Error Resume Next\nPO = New Collection\nDebug.Print CStr(Err.Number)",
            ("Module2", ObjectProperty));
        AssertDebugLog(["91"]);
    }

    [Fact]
    public async Task ReadOnlyLet()
    {
        var run = async () => await RunModules("Option Explicit\nRO = \"x\"",
            ("Module2", "Public Property Get RO() As Long\nRO = 5\nEnd Property"));
        (await run.Should().ThrowAsync<VBCompileErrorException>()).Which.Message.Should().Contain("Can't assign to read-only property");
    }

    [Fact]
    public async Task NonNothingGetterWithoutLetIsUnmeasured()
    {
        var run = async () => await RunModules("Option Explicit\nOn Error Resume Next\nSet PO = New Collection\nPO = New Collection", ("Module2", ObjectProperty));
        await run.Should().ThrowAsync<NotImplementedException>();
    }
}
