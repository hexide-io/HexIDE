using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

// vb6.exe native and p-code measurements: https://github.com/hexide-io/HexIDE/issues/4675.
public class LetIntoObject4675Tests : BaseVBTestFixture
{
    private const string D0 = """
        Public Property Get Value() As Long
        Attribute Value.VB_UserMemId = 0
            Value = 0
        End Property
        Public Property Let Value(v As Long)
            Debug.Print CStr(v)
        End Property
        """;
    private const string D1 = """
        Public Property Get Item(i As Long) As Long
        Attribute Item.VB_UserMemId = 0
            Item = i
        End Property
        Public Property Let Item(i As Long, v As Long)
        End Property
        """;
    // An interface with no default member, and a class implementing it whose own default member needs an argument.
    private const string IFoo = """
        Public Sub M()
        End Sub
        """;
    private const string D1i = """
        Implements IFoo
        Public Property Get Item(i As Long) As Long
        Attribute Item.VB_UserMemId = 0
            Item = i
        End Property
        Public Property Let Item(i As Long, v As Long)
        End Property
        Private Sub IFoo_M()
        End Sub
        """;

    private Task Probe(string code, string? type = null, bool setter = true) => RunModulesAndClasses(code,
        type is null ? [] : [("Module2", $"""
            Dim held As {type}
            Public Property Get PO() As {type}
                Set PO = held
            End Property
            {(setter ? $"Public Property Set PO(v As {type})\nSet held = v\nEnd Property" : "")}
            """)], [("D0", D0), ("D1", D1), ("N0", "Public n As Long"), ("IFoo", IFoo), ("D1i", D1i)]);

    [Theory]
    [InlineData("a1", "Collection", "PO = New Collection", true)]
    [InlineData("a2", "Collection", "Set PO = New Collection\nPO = 5", true)]
    [InlineData("a10", "Collection", "PO = New Collection", false)]
    [InlineData("a5", "D1", "Set PO = New D1\nPO = 5", true)]
    public async Task Early_bound_property_refuses(string row, string type, string code, bool setter)
    {
        var run = () => Probe("On Error Resume Next\n" + code, type, setter);
        (await run.Should().ThrowAsync<VBCompileErrorException>(row)).Which.Message.Should().Contain("Argument not optional");
    }

    [Theory]
    [InlineData("v1", "Dim c As Collection\nc = New Collection")]
    [InlineData("v5", "Dim c As Collection\nSet c = New Collection\nc = 5")]
    [InlineData("v3", "Dim c As D1\nSet c = New D1\nc = 5")]
    public async Task Early_bound_variable_refuses(string row, string code)
    {
        var run = () => Probe("On Error Resume Next\n" + code);
        (await run.Should().ThrowAsync<VBCompileErrorException>(row)).Which.Message.Should().Contain("Argument not optional");
    }

    [Theory]
    [InlineData("a3", "D0", "PO = 5", "91")]
    [InlineData("a6", "N0", "Set PO = New N0\nPO = 5", "438")]
    [InlineData("a7", "Object", "PO = New Collection", "91")]
    [InlineData("a8", "Object", "Set PO = New Collection\nPO = 5", "438")]
    public async Task Property_uses_held_object(string row, string type, string code, string error)
    {
        await Probe("On Error Resume Next\n" + code + "\nDebug.Print CStr(Err.Number)", type);
        debug.Should().Equal([new Vb6Value(error)], row);
    }

    [Theory]
    [InlineData("v4", "N0", "N0", "438")]
    [InlineData("v6", "Object", "Collection", "438")]
    [InlineData("v7", "Object", "D1", "449")]
    // The interface's default member is reached, not the class's: 438, not v7's 449.
    [InlineData("v9", "IFoo", "D1i", "438")]
    public async Task Variable_keeps_held_object(string row, string type, string held, string error)
    {
        await Probe($"On Error Resume Next\nDim c As {type}\nSet c = New {held}\nc = 5\nDebug.Print CStr(Err.Number)\nDebug.Print TypeName(c)");
        debug.Should().Equal([new Vb6Value(error), new Vb6Value(held)], row);
    }

    // vb6.exe calls D0's Let with 5. #4694 runs it for a variable, and a module property reaches it the same way.
    [Theory]
    [InlineData("a4", "D0")]
    [InlineData("v2", "D0")]
    [InlineData("v8", "Object")]
    public async Task Parameterless_default_takes_the_value(string row, string type)
    {
        if (row == "a4")
            await Probe("Set PO = New D0\nPO = 5", type);
        else
            await Probe($"Dim c As {type}\nSet c = New D0\nc = 5");
        debug.Should().Equal([new Vb6Value("5")], row);
    }

    [Fact]
    public async Task A9_Variant_non_object_requires_object()
    {
        await RunModules("On Error Resume Next\nPO = 5\nDebug.Print CStr(Err.Number)", ("Module2", """
            Public Property Get PO() As Variant
            End Property
            Public Property Set PO(v As Object)
            End Property
            """));
        AssertDebugLog(["424"]);
    }

    [Fact]
    public async Task A12_Variant_get_only_requires_object()
    {
        await RunModules("On Error Resume Next\nPO = 5\nDebug.Print CStr(Err.Number)", ("Module2", """
            Public Property Get PO() As Variant
            End Property
            """));
        AssertDebugLog(["424"]);
    }

    [Fact]
    public async Task A14_Object_get_only_reads_Nothing()
    {
        await RunModules("On Error Resume Next\nPO = New Collection\nDebug.Print CStr(Err.Number)", ("Module2", """
            Public Property Get PO() As Object
            End Property
            """));
        AssertDebugLog(["91"]);
    }

    [Fact]
    public async Task A11_A_Let_into_a_Collection_on_a_line_never_run_is_not_caught()
    {
        await Probe("Private Sub Uncalled()\nPO = New Collection\nEnd Sub\nDebug.Print \"ran\"", "Collection");
        AssertDebugLog(["ran"]);
    }

    [Theory]
    [InlineData("Dim c As D1\nc = 5")]
    [InlineData("Private c As D1\nPrivate Sub Assign()\nc = 5\nEnd Sub\nAssign")]
    [InlineData("Private Sub Assign(ByVal c As D1)\nc = 5\nEnd Sub\nAssign Nothing")]
    [InlineData("Private Sub Assign(c As D1)\nc = 5\nEnd Sub\nDim c As D1\nAssign c")]
    [InlineData("Dim h As Holder\nSet h = New Holder\nh.c = 5")]
    public async Task Declared_views_cover_slots(string code)
    {
        var run = () => RunModulesAndClasses(code, [], [("D1", D1), ("Holder", "Public c As D1")]);
        (await run.Should().ThrowAsync<VBCompileErrorException>()).Which.Message.Should().Contain("Argument not optional");
    }

    [Fact]
    public async Task Early_variable_check_precedes_right_hand_side()
    {
        var run = () => Probe("Private Function RHS() As Long\nDebug.Print \"rhs\"\nRHS = 5\nEnd Function\nDim c As D1\nc = RHS()");
        await run.Should().ThrowAsync<VBCompileErrorException>();
        debug.Should().BeEmpty();
    }

    [Fact]
    public async Task Early_property_check_precedes_getter()
    {
        var run = () => RunModules("PO = 5", ("Module2", "Public Property Get PO() As Collection\nDebug.Print \"getter\"\nEnd Property"));
        await run.Should().ThrowAsync<VBCompileErrorException>();
        debug.Should().BeEmpty();
    }

    [Fact]
    public async Task Variant_getter_returning_object_remains_unmeasured()
    {
        var run = () => RunModules("PO = 5", ("Module2", "Public Property Get PO() As Variant\nSet PO = New Collection\nEnd Property"));
        (await run.Should().ThrowAsync<NotImplementedException>()).Which.Message.Should().Contain("#4675");
    }

}
