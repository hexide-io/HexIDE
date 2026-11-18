using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

// Measured SP6 native/p-code rows: https://github.com/hexide-io/HexIDE/issues/4679 (oracle PR #4686).
public class CollectionEnumeratorTests : BaseVBTestFixture
{
    private static string Enumerable(string kind, string returned = "items.[_NewEnum]", bool marked = true) => $$"""
        Private items As New Collection
        Private Sub Class_Initialize()
            items.Add "first"
            items.Add "second"
        End Sub
        Public {{kind}} NewEnum() As IUnknown
        {{(marked ? "Attribute NewEnum.VB_UserMemId = -4" : "")}}
            Set NewEnum = {{returned}}
        End {{(kind == "Function" ? "Function" : "Property")}}
        Public Sub AddItem()
            items.Add "third"
        End Sub
        """;

    [Theory]
    [InlineData("Bag", "Function", false)]
    [InlineData("Object", "Function", false)]
    [InlineData("Variant", "Function", false)]
    [InlineData("Bag", "Property Get", false)]
    [InlineData("Bag", "Function", true)]
    public async Task Class_walks_the_marked_member(string holder, string kind, bool add)
    {
        await RunClasses($$"""
            Dim b As New Bag, held As {{holder}}, item As Variant
            Set held = b
            For Each item In held
                Debug.Print item
                {{(add ? "If item = \"first\" Then b.AddItem" : "")}}
            Next
            """, ("Bag", Enumerable(kind)));
        AssertDebugLog(add ? ["first", "second", "third"] : ["first", "second"]);
    }

    [Theory]
    [InlineData("items.[_NewEnum]", false, false)]
    [InlineData("Nothing", true, false)]
    [InlineData("items", true, false)]
    [InlineData("items.[_NewEnum]", false, true)]
    [InlineData("Nothing", true, true)]
    [InlineData("items", true, true)]
    public async Task Invalid_class_enumeration_is_92(string returned, bool marked, bool resume)
    {
        var code = $$"""
            Dim b As New Bag, item As Variant
            {{(resume ? "On Error Resume Next" : "")}}
            For Each item In b
                Debug.Print Err.Number
                Debug.Print IsEmpty(item)
            Next
            """;
        if (resume)
        {
            await RunClasses(code, ("Bag", Enumerable("Function", returned, marked)));
            AssertDebugLog([new Vb6Value(92L), true]);
        }
        else
        {
            Func<Task> run = () => RunClasses(code, ("Bag", Enumerable("Function", returned, marked)));
            var error = await run.Should().ThrowAsync<VBRunTimeException>();
            error.Which.Error.ErrNo.Should().Be(92);
            error.Which.Error.Description.Should().Be("For loop not initialized");
        }
    }

    [Theory]
    [InlineData("TypeName(c.[_NewEnum])", "Unknown")]
    [InlineData("VarType(v)", "13")]
    [InlineData("IsObject(v)", "False")]
    [InlineData("c.[_NewEnum] Is c.[_NewEnum]", "False")]
    public async Task Enumerator_inspection(string expression, string expected)
    {
        await Run($$"""
            Dim c As New Collection, v As Variant
            Set v = c.[_NewEnum]
            Debug.Print CStr({{expression}})
            """);
        AssertDebugLog([new Vb6Value(expected)]);
    }

    [Theory]
    [InlineData("IUnknown", 0)]
    [InlineData("Variant", 0)]
    [InlineData("Object", 13)]
    public async Task Enumerator_set(string holder, int error)
    {
        await Run($$"""
            Dim c As New Collection, held As {{holder}}
            On Error Resume Next
            Set held = c.[_NewEnum]
            Debug.Print Err.Number
            """);
        AssertDebugLog([new Vb6Value((long)error)]);
    }

    [Fact]
    public async Task Enumerator_let_follows_p_code()
    {
        await Run("""
            Dim c As New Collection, v As Variant
            On Error Resume Next
            v = c.[_NewEnum]
            Debug.Print Err.Number
            """);
        AssertDebugLog([new Vb6Value(13L)]);
    }

    [Fact]
    public async Task Enumerator_late_call_is_424()
    {
        await Run("""
            Dim c As New Collection, v As Variant
            Set v = c.[_NewEnum]
            On Error Resume Next
            v.Foo
            Debug.Print Err.Number
            """);
        AssertDebugLog([new Vb6Value(424L)]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Direct_enumerator_loop_is_92(bool resume)
    {
        var code = $$"""
            Dim c As New Collection, held As IUnknown, item As Variant
            Set held = c.[_NewEnum]
            {{(resume ? "On Error Resume Next" : "")}}
            For Each item In held
                Debug.Print Err.Number
                Debug.Print IsEmpty(item)
            Next
            """;
        if (resume)
        {
            await Run(code);
            AssertDebugLog([new Vb6Value(92L), true]);
        }
        else
        {
            Func<Task> run = () => Run(code);
            var error = await run.Should().ThrowAsync<VBRunTimeException>();
            error.Which.Error.ErrNo.Should().Be(92);
            error.Which.Error.Description.Should().Be("For loop not initialized");
        }
    }
}
