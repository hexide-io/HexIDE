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
    [InlineData("CallByName(k, \"Item\")")]
    [InlineData("CallByName(k, 1, VbGet)")]
    [InlineData("CallByName(k, \"Item\", 1#, \"ka\")")]
    public async Task UnsupportedShape_Refuses(string expression)
    {
        var run = () => Run("Dim k As New Collection\nDebug.Print " + expression + "\n");
        await run.Should().ThrowAsync<NotImplementedException>();
    }

    // Measured on vb6.exe SP6, native and p-code: hexide-io/HexIDE#4688 (oracle PR #4690).
    // The six rows already matching main (duplicate key, missing Item and four direct calls) are regression guards.
    [Theory]
    [InlineData("Set k = New Collection", "v = CallByName(k, \"Count\", VbMethod)\nDebug.Print TypeName(v) & \" \" & v", "Long 0")]
    [InlineData("Fill", "Debug.Print CallByName(k, \"Count\", VbMethod)", 2L)]
    [InlineData("Fill\nSet vk = k", "Debug.Print CallByName(vk, \"Count\", VbMethod)", 2L)]
    [InlineData("Fill", "v = CallByName(k, \"Add\", VbMethod, \"a\")\nDebug.Print TypeName(v) & \"|\" & k.Count & \"|\" & k(3)", "Empty|3|a")]
    [InlineData("Fill", "CallByName k, \"Add\", VbMethod, \"b\", \"kb\", 1\nDebug.Print k.Count & \"|\" & k(1) & \"|\" & k(\"kb\")", "3|b|b")]
    [InlineData("Fill", "CallByName k, \"Add\", VbMethod, \"c\", \"kc\", \"kx\"\nDebug.Print k.Count & \"|\" & k(1)", "3|c")]
    [InlineData("Fill", "CallByName k, \"Add\", VbMethod, \"d\", \"kx\"", "err 457")]
    [InlineData("Fill", "CallByName k, \"Remove\", VbMethod, 1\nDebug.Print k.Count & \"|\" & k(1)", "1|y")]
    [InlineData("Fill", "CallByName k, \"Remove\", VbMethod, \"ky\"\nDebug.Print k.Count & \"|\" & k(1)", "1|x")]
    [InlineData("Fill", "v = CallByName(k, \"Remove\", VbMethod, 1)\nDebug.Print TypeName(v) & \"|\" & k.Count", "Empty|1")]
    [InlineData("Fill", "CallByName k, \"Remove\", VbMethod, 5", "err 9")]
    [InlineData("Fill", "CallByName k, \"Remove\", VbMethod, \"nokey\"", "err 5")]
    [InlineData("Fill", "v = CallByName(k, \"Item\", VbMethod, 5)", "err 9")]
    [InlineData("Fill", "v = CallByName(k, \"Count\", VbMethod, 1)", "err 450")]
    [InlineData("Fill", "v = CallByName(k, \"Item\", VbMethod)", "err 450")]
    [InlineData("Fill", "v = CallByName(k, \"Nope\", VbMethod)", "err 438")]
    [InlineData("Fill", "v = CallByName(k, \"Item\", VbLet, \"a\")", "err 438")]
    [InlineData("Set k = New Collection\nk.Add New C1", "Debug.Print TypeName(CallByName(k, \"Item\", VbMethod, 1))", "C1")]
    [InlineData("Set k = New Collection\nk.Add New C1", "Set o = CallByName(k, \"Item\", VbMethod, 1)\nDebug.Print TypeName(o) & \"|\" & (o Is k(1))", "C1|True")]
    [InlineData("Set k = New Collection\nk.Add New C1", "Set v = CallByName(k, \"Item\", VbMethod, 1)\nDebug.Print TypeName(v) & \"|\" & (v Is k(1))", "C1|True")]
    [InlineData("Set k = New Collection\nk.Add New C1", "v = CallByName(k, \"Item\", VbMethod, 1)", "err 438")]
    [InlineData("Set k = New Collection\nk.Add New Collection", "Debug.Print TypeName(CallByName(k, \"Item\", VbMethod, 1))", "Collection")]
    [InlineData("Fill", "k.Remove 5", "err 9")]
    [InlineData("Fill", "k.Remove \"nokey\"", "err 5")]
    [InlineData("Fill", "v = k(5)", "err 9")]
    [InlineData("Set k = New Collection\nk.Add New C1", "v = k(1)", "err 438")]
    public async Task Collection_MeasuredMembers(string setup, string statements, object expected)
    {
        await RunClasses($"""
            Public k As Collection
            Private Sub Fill()
                Set k = New Collection
                k.Add "x", "kx"
                k.Add "y", "ky"
            End Sub
            Sub Main()
                Dim v As Variant, vk As Variant, o As Object
                On Error GoTo Failed
                {setup}
                {statements}
                Exit Sub
            Failed:
                Debug.Print "err " & Err.Number
            End Sub
            Main

            """, ("C1", "Public X As Long\n"));
        AssertDebugLog([expected is long number ? new Vb6Value(number) : new Vb6Value((string)expected)]);
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

    [Theory]
    [InlineData("VBA.CallByName o, \"x\", VbGet")]
    [InlineData("CallByName o, \"x\", VbGet")]
    public async Task Nothing_AsStatement_IsTrappable5(string statement)
    {
        await Run("Dim o As Object\nOn Error Resume Next\n" + statement + "\nDebug.Print Err.Number\n");
        AssertDebugLog([new Vb6Value(5L)]);
    }
}
