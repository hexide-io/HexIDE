using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

// Expectations recorded on vb6.exe in #1006 (including both comments) and #965.
public class ModuleQualifiedStoreTests : BaseVBTestFixture
{
    [Fact]
    public async Task Qualified_Integer_store_and_read()
    {
        await RunModules("Public G As Integer\nModule1.G = 8\nDebug.Print Module1.G\n");
        AssertDebugLog([new Vb6Value((short)8)]);
    }

    [Fact]
    public async Task Qualified_Long_store_from_another_module()
    {
        await RunModules("Module2.pn = 7\nDebug.Print pn\n", ("Module2", "Public pn As Long\n"));
        AssertDebugLog([new Vb6Value(7L)]);
    }

    private const string ArrayModule = "Public g(3) As Long\nSub Init()\n g(1) = 5\nEnd Sub\nFunction Peek(i)\n Peek = g(i)\nEnd Function\n";

    [Fact]
    public async Task Qualified_array_element_read_is_a_Long()
    {
        await RunModules("Module2.Init\nx = Module2.g(1)\nDebug.Print x\nDebug.Print TypeName(Module2.g(1))\n", ("Module2", ArrayModule));
        AssertDebugLog([new Vb6Value(5L), new Vb6Value("Long")]);
    }

    [Fact]
    public async Task Qualified_array_element_store()
    {
        await RunModules("Module2.g(2) = 7\nDebug.Print Module2.Peek(2)\n", ("Module2", ArrayModule));
        AssertDebugLog([new Vb6Value(7L)]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Qualified_Set_holds_and_releases_the_object(bool clear)
    {
        // #1006's second comment measures both Set forms. Seed the clear case through the working bare name so
        // that its teeth do not depend on the qualified New store failing first.
        var code = clear
            ? "Set pq = New C1\nSet Module2.pq = Nothing\nDebug.Print pq Is Nothing\n"
            : "Set Module2.pq = New C1\nDebug.Print pq Is Nothing\n";
        var vb = new BasicInterpreter(new MockStdLib(debug), context, rootEnv, code, "Module1",
            [("Module2", "Public pq As C1\n")],
            [("C1", "Private Sub Class_Terminate()\nDebug.Print \"terminated\"\nEnd Sub\n")]);
        await vb.Execute();
        AssertDebugLog(clear
            ? [new Vb6Value("terminated"), new Vb6Value(true)]
            : [new Vb6Value(false)]);
    }

    [Fact]
    public async Task Qualified_Const_store_is_refused()
    {
        // #1006 records the #1003 measurement explicitly.
        var act = async () => await RunModules("Public Const MK = 1\nModule1.MK = 2\n");
        (await act.Should().ThrowAsync<VBCompileErrorException>()).Which.Message.Should().Contain("Assignment to constant not permitted");
    }
}
