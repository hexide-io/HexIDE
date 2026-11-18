using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

public class ParameterizedDefaultMemberTests : BaseVBTestFixture
{
    // vb6.exe SP6, native and p-code: #4606's supervisor comment (2026-11-18), and
    // docs/vb6-fidelity-oracle/with-new-default-instances-and-set-through-members.md (Bag/Self).
    private const string Bag = """
        Private store As New Collection
        Public Property Get Item(ByVal k As Variant) As Variant
        Attribute Item.VB_UserMemId = 0
            Item = store(CStr(k))
        End Property
        Public Property Let Item(ByVal k As Variant, ByVal v As Variant)
            store.Add v, CStr(k)
        End Property
        Public Property Get Self() As Bag
            Set Self = Me
        End Property

        """;
    private const string Holder = """
        Private names As Bag
        Public Sub Init()
            Set names = New Bag
        End Sub
        Public Sub PutIt(ByVal k As String, ByVal v As String)
            names(k) = v
        End Sub
        Public Function GetIt(ByVal k As String) As String
            GetIt = names(k)
        End Function
        Public Property Get State() As Bag
            Set State = names
        End Property

        """;
    private const string MakeBag = """
        Function MakeBag() As Bag
            Set MakeBag = New Bag
            MakeBag.Item("x") = "m"
        End Function

        """;
    private Task Probe(string code) => RunClasses(code + "\n", ("Bag", Bag), ("Holder", Holder),
        ("FBag", "Public Function Item(ByVal k As Variant) As Variant\nAttribute Item.VB_UserMemId = 0\nItem = \"f\" & k\nEnd Function\n"),
        ("TBag", "Public Property Get Item(ByVal a As Long, ByVal b As Long) As Long\nAttribute Item.VB_UserMemId = 0\nItem = a * 10 + b\nEnd Property\n"));

    [Theory]
    [InlineData("Dim b As New Bag\nb(\"x\") = \"v\"\nDebug.Print b(\"x\") & \" \" & b.Self(\"x\")", "v v")]
    [InlineData("Dim v As Variant\nSet v = New Bag\nv(\"x\") = \"v\"\nDebug.Print v(\"x\")", "v")]
    [InlineData("Dim o As Object\nSet o = New Bag\no(\"x\") = \"o\"\nDebug.Print o(\"x\")", "o")]
    [InlineData("Dim h As New Holder\nh.Init\nh.PutIt \"k\", \"hv\"\nDebug.Print h.GetIt(\"k\")", "hv")]
    [InlineData("Dim h As New Holder\nh.Init\nh.PutIt \"k\", \"hv\"\nDebug.Print h.State(\"k\")", "hv")]
    [InlineData("Dim h As New Holder\nh.Init\nh.State(\"k2\") = \"w\"\nDebug.Print h.GetIt(\"k2\")", "w")]
    [InlineData("Dim f As New FBag\nDebug.Print f(\"q\")", "fq")]
    [InlineData("Dim t As New TBag\nDebug.Print CStr(t(1, 2))", "12")]
    [InlineData("Dim b As New Bag, s As String\nb(\"x\") = \"bv\"\ns = b(\"x\") & \"!\"\nDebug.Print s", "bv!")]
    [InlineData(MakeBag + "Debug.Print MakeBag()(\"x\")", "m")]
    [InlineData(MakeBag + "Debug.Print MakeBag(\"x\")", "m")]
    [InlineData("Dim v As Variant, b As New Bag\nSet v = b\nb.Item(\"x\") = \"v\"\nDebug.Print TypeName(v(\"x\")) & \" \" & TypeName(b(\"x\"))", "String String")]
    [InlineData("Dim arr(1 To 2) As Bag\nSet arr(1) = New Bag\narr(1)(\"x\") = \"a\"\nDebug.Print arr(1)(\"x\")", "a")]
    [InlineData("Dim arr(1 To 2) As Bag\nSet arr(1) = New Bag\narr(1)(\"x\") = \"a\"\nDebug.Print arr(1).Item(\"x\")", "a")]
    public async Task Measured_calls_read_and_assign_the_default_member(string code, string expected)
    {
        await Probe(code);
        AssertDebugLog([new Vb6Value(expected)]);
    }

    [Theory]
    [InlineData("Debug.Print n(\"x\")")]
    [InlineData("n(\"x\") = \"n\"")]
    public async Task A_typed_Nothing_raises_91(string statement)
    {
        var run = () => Probe("Dim n As Bag\n" + statement);
        var error = (await run.Should().ThrowAsync<VBRunTimeException>()).Which;
        error.Error.ErrNo.Should().Be(91);
    }

    [Theory]
    [InlineData("Debug.Print b.Item(\"x\")")]
    [InlineData("Debug.Print b.Self.Item(\"x\")")]
    public async Task Explicit_members_still_work(string statement)
    {
        await Probe("Dim b As New Bag\nb.Item(\"x\") = \"v\"\n" + statement);
        AssertDebugLog([new Vb6Value("v")]);
    }

    [Theory]
    [InlineData("Public Sub Other()\nEnd Sub\n", "Debug.Print c(\"q\")")]
    [InlineData("Public Sub Item(ByVal k As Variant)\nAttribute Item.VB_UserMemId = 0\nEnd Sub\n", "Debug.Print c(\"q\")")]
    [InlineData("Public Function Item(ByVal k As Variant) As Variant\nAttribute Item.VB_UserMemId = 0\nEnd Function\n", "c(\"q\") = 1")]
    [InlineData("Public v As Variant\nAttribute v.VB_VarUserMemId = 0\n", "Debug.Print c(\"q\")")]
    [InlineData("Public Property Get Item(ByVal k As Variant) As Variant\nAttribute Item.VB_UserMemId = 0\nEnd Property\n", "c(\"q\") = 1")]
    public async Task Unmeasured_members_refuse(string source, string statement)
    {
        var run = () => RunClasses("Dim c As New C\n" + statement + "\n", ("C", source));
        (await run.Should().ThrowAsync<NotImplementedException>()).WithMessage("*#4606*");
    }
}
