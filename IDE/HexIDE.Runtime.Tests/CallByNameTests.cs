using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

public class CallByNameTests : BaseVBTestFixture
{
    [Theory]
    [InlineData("CallByName k, \"Add\", VbMethod, \"a\", \"ka\"")]
    [InlineData("Call VBA.CallByName(k, \"Add\", VbMethod, \"a\", \"ka\")")]
    public async Task Collection_Add_AsStatement(string call)
    {
        await Run("Dim k As New Collection\n" + call + "\nDebug.Print k.Count\n");
        AssertDebugLog([new Vb6Value(1L)]);
    }

    [Theory]
    [InlineData("CallByName(k, \"Item\", VbMethod, \"ka\")")]
    [InlineData("VBA.CallByName(k, \"Item\", VbMethod, \"ka\")")]
    [InlineData("CallByName(k, \"ITEM\", VbMethod, \"ka\")")]
    public async Task Collection_Item_AsExpression(string expression)
    {
        await Run("Dim k As New Collection\nk.Add \"a\", \"ka\"\nDebug.Print " + expression + "\n");
        AssertDebugLog([new Vb6Value("a")]);
    }

    [Theory]
    [InlineData("\"Count\", VbGet")]
    [InlineData("\"Item\", VbGet, 1")]
    public async Task Collection_Get_Is438(string arguments)
    {
        await Run("Dim k As New Collection\nk.Add \"a\"\nOn Error Resume Next\nDebug.Print CallByName(k, " + arguments + ")\nDebug.Print Err.Number\n");
        AssertDebugLog([new Vb6Value(438L)]);
    }

    [Fact]
    public async Task Err_Number_IsLong()
    {
        await Run("Debug.Print CallByName(Err, \"Number\", VbGet)\nDebug.Print TypeName(CallByName(Err, \"Number\", VbGet))\n");
        AssertDebugLog([new Vb6Value(0L), new Vb6Value("Long")]);
    }

    [Theory]
    [InlineData("Dim o As Object", "o", "\"x\"", 5)]
    [InlineData("Dim five\nfive = 5", "five", "\"x\"", 424)]
    [InlineData("Dim k As New Collection\nDim nv\nnv = Null", "k", "nv", 94)]
    public async Task InvalidReceiverOrName(string setup, string receiver, string name, long error)
    {
        await Run(setup + "\nOn Error Resume Next\nDebug.Print CallByName(" + receiver + ", " + name + ", VbGet)\nDebug.Print Err.Number\n");
        AssertDebugLog([new Vb6Value(error)]);
    }

    [Theory]
    [InlineData("CallByName(k, \"Count\", VbMethod)")]
    [InlineData("CallByName(k, \"Item\", VbLet, \"a\")")]
    [InlineData("CallByName(k, \"Add\", VbMethod, \"a\")")]
    [InlineData("CallByName(k, \"Item\")")]
    [InlineData("CallByName(k, 1, VbGet)")]
    [InlineData("CallByName(k, \"Item\", 1#, \"ka\")")]
    [InlineData("CallByName(k, ProcName:=\"Item\", CallType:=VbMethod, Args:=\"ka\")")]
    public async Task UnsupportedShape_Refuses(string expression)
    {
        var run = () => Run("Dim k As New Collection\nDebug.Print " + expression + "\n");
        await run.Should().ThrowAsync<NotImplementedException>();
    }

    private const string Box = """
        Public x As Integer
        Private pv As Variant
        Private ov As Object
        Public Property Get P() As Variant
            P = pv
        End Property
        Public Property Let P(ByVal v As Variant)
            pv = v
        End Property
        Public Property Get O() As Object
            Set O = ov
        End Property
        Public Property Set O(ByVal v As Object)
            Set ov = v
        End Property
        Public Property Get Item(ByVal i As Integer) As String
            Item = "item " & i
        End Property
        Public Function F(ByVal a As Integer, ByVal b As Integer) As Long
            F = a + b
        End Function
        Public Function G(Optional ByVal a As Variant, Optional ByVal b As Integer = 7) As String
            G = IsMissing(a) & "," & b
        End Function
        Public Function H(ParamArray rest() As Variant) As String
            H = "count " & (UBound(rest) + 1)
        End Function
        Public Sub S(ByVal a As Variant)
            Debug.Print a
        End Sub
        Public Sub Twice(ByRef n As Integer)
            n = n * 2
        End Sub
        """;

    [Theory]
    [InlineData("CallByName c, \"x\", VbLet, 5", "c.x", "5")]
    [InlineData("c.x = 5", "CallByName(c, \"x\", VbGet)", "5")]
    [InlineData("c.x = 5", "CallByName(c, \"X\", VbGet)", "5")]
    [InlineData("c.x = 5", "CallByName(c, \"x\", 3)", "5")]
    [InlineData("c.x = 5", "VBA.CallByName(c, \"x\", VbGet)", "5")]
    [InlineData("CallByName c, \"P\", VbLet, \"hello\"", "c.P", "hello")]
    [InlineData("c.P = \"hello\"", "CallByName(c, \"P\", VbGet)", "hello")]
    [InlineData("", "CallByName(c, \"Item\", VbGet, 3)", "item 3")]
    [InlineData("", "CallByName(c, \"F\", VbMethod, 2, 3)", "5")]
    [InlineData("", "CallByName(c, \"F\", VbMethod, \"2\", 3.6)", "6")]
    [InlineData("", "TypeName(CallByName(c, \"F\", VbMethod, 1, 1))", "Long")]
    [InlineData("", "TypeName(CallByName(c, \"x\", VbGet))", "Integer")]
    [InlineData("", "CallByName(c, \"G\", VbMethod)", "True,7")]
    [InlineData("", "CallByName(c, \"G\", VbMethod, 1, 2)", "False,2")]
    [InlineData("", "CallByName(c, \"H\", VbMethod, 1, 2, 3)", "count 3")]
    [InlineData("", "CallByName(c, \"H\", VbMethod)", "count 0")]
    [InlineData("Dim n As Integer\nn = 4\nCallByName c, \"Twice\", VbMethod, n", "n", "8")]
    public async Task ClassMember(string setup, string expression, string expected)
    {
        await RunClasses("Dim c As New Box\n" + setup + "\nDebug.Print CStr(" + expression + ")\n", ("Box", Box));
        AssertDebugLog([new Vb6Value(expected)]);
    }

    [Theory]
    [InlineData("\"F\", VbMethod, 2", 449)]
    [InlineData("\"F\", VbMethod, 2, 3, 4", 450)]
    [InlineData("\"F\", VbGet, 2, 3", 450)]
    [InlineData("\"Nope\", VbGet", 438)]
    [InlineData("\"Nope\", VbMethod", 438)]
    [InlineData("\"x\", 0", 438)]
    [InlineData("\"x\", 16", 438)]
    [InlineData("\"\", VbGet", 438)]
    public async Task ClassError(string arguments, long error)
    {
        await RunClasses("Dim c As New Box\nOn Error Resume Next\nDebug.Print CallByName(c, " + arguments + ")\nDebug.Print Err.Number\n", ("Box", Box));
        AssertDebugLog([new Vb6Value(error)]);
    }

    [Theory]
    [InlineData("CallByName c, \"S\", VbMethod, \"s1\"", "s1")]
    [InlineData("Dim v\nv = CallByName(c, \"S\", VbMethod, \"s2\")\nDebug.Print TypeName(v)", "s2", "Empty")]
    public async Task ClassSub(string statement, params string[] expected)
    {
        await RunClasses("Dim c As New Box\n" + statement + "\n", ("Box", Box));
        AssertDebugLog(expected.Select(s => new Vb6Value(s)).ToList());
    }

    [Theory]
    [InlineData("Debug.Print c.O Is k", true)]
    [InlineData("Dim got As Object\nSet got = CallByName(c, \"O\", VbGet)\nDebug.Print got Is k", true)]
    public async Task ClassObject(string read, bool expected)
    {
        await RunClasses("Dim c As New Box\nDim k As New Collection\nCallByName c, \"O\", VbSet, k\n" + read + "\n", ("Box", Box));
        AssertDebugLog([new Vb6Value(expected)]);
    }

    [Fact]
    public async Task ClassObjectWithoutSet_Is450()
    {
        await RunClasses("Dim c As New Box\nDim k As New Collection\nSet c.O = k\nDim v\nOn Error Resume Next\nv = CallByName(c, \"O\", VbGet)\nDebug.Print Err.Number\n", ("Box", Box));
        AssertDebugLog([new Vb6Value(450L)]);
    }

    [Fact]
    public async Task ObjectResult_RefusesUntilSetHandlingIsImplemented()
    {
        var run = () => Run("Dim k As New Collection\nDim inner As New Collection\nk.Add inner, \"obj\"\nDim got As Object\nSet got = CallByName(k, \"Item\", VbMethod, \"obj\")\n");
        await run.Should().ThrowAsync<NotImplementedException>().WithMessage("*object result*#2519*");
    }

    [Theory]
    [InlineData("VBA.CallByName o, \"x\", VbGet")]
    [InlineData("CallByName o, \"x\", VbGet")]
    public async Task Nothing_AsStatement_IsTrappable5(string statement)
    {
        await Run("Dim o As Object\nOn Error Resume Next\n" + statement + "\nDebug.Print Err.Number\n");
        AssertDebugLog([new Vb6Value(5L)]);
    }
}
