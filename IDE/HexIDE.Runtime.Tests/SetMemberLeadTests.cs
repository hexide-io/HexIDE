using Antlr4.Runtime.Tree;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

// Measurements: https://github.com/hexide-io/HexIDE/issues/4600, latest supervisor comment
// (vb6.exe SP6, native and p-code, 2026-11-18), including #4602 and #4607.
public class SetMemberLeadTests : BaseVBTestFixture
{
    private const string Box = """
        Private held As Object
        Public X As Long
        Public Property Get O() As Object
            Set O = held
        End Property
        Public Property Set O(ByVal value As Object)
            Set held = value
        End Property

        """;

    [Fact]
    public async Task With_AsNew_target_takes_an_object_reference()
    {
        await RunClasses("""
            Dim c As New Box
            With c
                Set .O = New Collection
            End With
            Debug.Print TypeName(c.O)

            """, ("Box", Box));
        AssertDebugLog([new Vb6Value("Collection")]);
    }

    [Fact]
    public async Task With_target_takes_Nothing()
    {
        await RunClasses("""
            Dim b As Box
            Set b = New Box
            With b
                Set .O = Nothing
            End With
            Debug.Print TypeName(b.O)

            """, ("Box", Box));
        AssertDebugLog([new Vb6Value("Nothing")]);
    }

    [Fact]
    public async Task An_object_array_element_is_a_Set_member_receiver()
    {
        await RunClasses("""
            Dim arr(1 To 2) As Box
            Set arr(1) = New Box
            Set arr(1).O = New Collection
            Debug.Print TypeName(arr(1).O)

            """, ("Box", Box));
        AssertDebugLog([new Vb6Value("Collection")]);
    }

    [Theory]
    [InlineData("Inner")]
    [InlineData("Me.Inner")]
    public async Task A_Property_Get_is_a_Set_member_receiver(string receiver)
    {
        await RunClasses("""
            Dim h As New Holder
            h.PutIt New Collection
            Debug.Print TypeName(h.Inner.O)

            """, ("Box", Box), ("Holder", Holder(receiver)));
        AssertDebugLog([new Vb6Value("Collection")]);
    }

    private static string Holder(string receiver) => $$"""
        Private hv As Box
        Private Sub Class_Initialize()
            Set hv = New Box
        End Sub
        Public Property Get Inner() As Box
            Set Inner = hv
        End Property
        Public Sub PutIt(ByVal value As Object)
            Set {{receiver}}.O = value
        End Sub

        """;

    [Fact]
    public async Task A_qualified_Property_Get_remains_a_Set_member_receiver()
    {
        await RunClasses("""
            Dim h As New Holder
            Set h.Inner.O = New Collection
            Debug.Print TypeName(h.Inner.O)

            """, ("Box", Box), ("Holder", Holder("Inner")));
        AssertDebugLog([new Vb6Value("Collection")]);
    }

    [Fact]
    public async Task A_named_receiver_still_parses_as_Set_and_takes_a_reference()
    {
        const string code = """
            Sub Main()
                Dim c As New Box
                Set c.O = New Collection
                Debug.Print TypeName(c.O)
            End Sub
            Main

            """;
        var vb = new BasicInterpreter(new MockStdLib(debug, written, dialogs), context, rootEnv, code);
        Descendants(vb.PrePass.Procedures["Main"].Body!).OfType<VB6Parser.SetStmtContext>()
            .Should().ContainSingle();
        await RunClasses(code, ("Box", Box));
        AssertDebugLog([new Vb6Value("Collection")]);
    }

    [Fact]
    public void A_leading_dot_Set_parses_as_Set()
    {
        const string code = """
            Sub Main()
                With c
                    Set .O = New Collection
                End With
            End Sub

            """;
        var vb = new BasicInterpreter(new MockStdLib(debug, written, dialogs), context, rootEnv, code);
        Descendants(vb.PrePass.Procedures["Main"].Body!).OfType<VB6Parser.SetStmtContext>()
            .Should().ContainSingle();
    }

    [Fact]
    public void An_explicit_leading_dot_Let_still_parses_as_Let()
    {
        const string code = """
            Sub Main()
                With c
                    Let .X = 1
                End With
            End Sub

            """;
        var vb = new BasicInterpreter(new MockStdLib(debug, written, dialogs), context, rootEnv, code);
        Descendants(vb.PrePass.Procedures["Main"].Body!).OfType<VB6Parser.LetStmtContext>()
            .Should().ContainSingle();
    }

    private static IEnumerable<IParseTree> Descendants(IParseTree tree)
    {
        yield return tree;
        for (var i = 0; i < tree.ChildCount; i++)
            foreach (var child in Descendants(tree.GetChild(i)))
                yield return child;
    }

    [Fact]
    public async Task An_undeclared_receiver_still_raises_91()
    {
        await RunClasses("""
            On Error Resume Next
            Set zz.O = New Collection
            Debug.Print Err.Number

            """, ("Box", Box));
        AssertDebugLog([new Vb6Value(91L)]);
    }

    [Fact]
    public async Task An_indexed_Type_receiver_takes_Set()
    {
        // vb6.exe SP6 native/p-code, #3924, 2026-11-20, B.
        await RunClasses("""
            Private Type T
                Obj As Object
            End Type
            Dim ua(1 To 2) As T
            Set ua(1).Obj = New Collection
            Debug.Print TypeName(ua(1).Obj), ua(2).Obj Is Nothing

            """, ("Box", Box));
        AssertDebugLog(["Collection", true]);
    }
}
