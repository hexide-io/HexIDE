using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

public class UdtObjectFieldTests : BaseVBTestFixture
{
    [Theory]
    [InlineData("TextBox")]
    [InlineData("StdFont")]
    [InlineData("ADODB.Connection")]
    public async Task Other_recognised_object_fields_refuse_without_guessing_their_behaviour(string type)
    {
        // HexIDE's measured-only refusal policy, not a VB6 error expectation.
        var run = () => Run($"Private Type T\nObj As {type}\nEnd Type\nDim u As T\n");
        (await run.Should().ThrowAsync<NotImplementedException>()).WithMessage("*not implemented (#3924)*");
    }

    [Theory]
    [InlineData(7, "u")]
    [InlineData(8, "Module1.u")]
    public async Task A_project_class_field_takes_Set_through_a_nested_Type(int n, string target)
    {
        // vb6.exe SP6, native and p-code: chained-writes-qualified-set-and-udt-returning-members.md, B3/B4 (#3924).
        await RunClasses($"""
            Private Type InnerO
                Obj As Leaf
            End Type
            Private Type OuterO
                In1 As InnerO
            End Type
            Dim u As OuterO
            Private Function NewLeaf(ByVal n As Long) As Leaf
                Dim o As Leaf
                Set o = New Leaf
                o.Value = n
                Set NewLeaf = o
            End Function
            Set {target}.In1.Obj = NewLeaf({n})
            Debug.Print u.In1.Obj.Value

            """, ("Leaf", "Public Value As Long\n"));
        AssertDebugLog([new Vb6Value((long)n)]);
    }

    // vb6.exe SP6, native and p-code: #3924 round 2, E1–E7;
    // https://github.com/hexide-io/HexIDE/pull/3955, a-type-field-that-holds-an-object.md.
    [Fact]
    public async Task An_Object_field_takes_Set_and_reads_members()
    {
        await RunClasses("""
            Private Type InnerO
                Obj As Object
            End Type
            Dim u As InnerO
            Dim o As Leaf
            Set o = New Leaf
            o.Value = 7
            Set u.Obj = o
            Debug.Print u.Obj.Value

            """, ("Leaf", "Public Value As Long\n"));
        AssertDebugLog([new Vb6Value(7L)]);
    }

    [Fact]
    public async Task Fresh_object_fields_are_Nothing()
    {
        await RunClasses("""
            Private Type InnerO
                Obj As Object
            End Type
            Private Type T3
                L As Leaf
            End Type
            Private Type T2
                C As Collection
            End Type
            Dim x As InnerO
            Dim y As T3
            Dim z As T2
            Debug.Print x.Obj Is Nothing
            Debug.Print y.L Is Nothing
            Debug.Print z.C Is Nothing

            """, ("Leaf", "Public Value As Long\n"));
        AssertDebugLog([new Vb6Value(true), new Vb6Value(true), new Vb6Value(true)]);
    }

    [Fact]
    public async Task A_Collection_field_takes_a_Collection()
    {
        await Run("""
            Private Type T2
                C As Collection
            End Type
            Dim y As T2
            Set y.C = New Collection
            y.C.Add 5
            Debug.Print y.C.Count
            Debug.Print TypeName(y.C)

            """);
        AssertDebugLog([new Vb6Value(1L), new Vb6Value("Collection")]);
    }

    [Fact]
    public async Task A_copied_Type_shares_its_object_but_owns_its_slot()
    {
        await RunClasses("""
            Private Type T3
                L As Leaf
            End Type
            Dim a As T3
            Dim b As T3
            Set a.L = New Leaf
            a.L.Value = 1
            b = a
            a.L.Value = 2
            Debug.Print b.L.Value
            Set a.L = New Leaf
            a.L.Value = 3
            Debug.Print b.L.Value
            Debug.Print a.L.Value
            Debug.Print a.L Is b.L

            """, ("Leaf", "Public Value As Long\n"));
        AssertDebugLog([new Vb6Value(2L), new Vb6Value(2L), new Vb6Value(3L), new Vb6Value(false)]);
    }

    [Fact]
    public async Task An_unrelated_class_is_13_and_keeps_the_field_Nothing()
    {
        await RunClasses("""
            Private Type T3
                L As Leaf
            End Type
            Dim t As T3
            Dim o As Object
            Set o = New Other
            On Error Resume Next
            Set t.L = o
            Debug.Print Err.Number
            Debug.Print t.L Is Nothing

            """, ("Leaf", "Public Value As Long\n"), ("Other", "Public Value As Long\n"));
        AssertDebugLog([new Vb6Value(13L), new Vb6Value(true)]);
    }

    [Fact]
    public async Task A_member_write_through_a_Nothing_field_is_91()
    {
        await RunClasses("""
            Private Type T3
                L As Leaf
            End Type
            Dim t As T3
            On Error Resume Next
            t.L.Value = 4
            Debug.Print Err.Number

            """, ("Leaf", "Public Value As Long\n"));
        AssertDebugLog([new Vb6Value(91L)]);
    }

    [Fact]
    public async Task Let_into_a_Nothing_object_field_is_91_and_keeps_it_Nothing()
    {
        await RunClasses("""
            Private Type InnerO
                Obj As Object
            End Type
            Dim x As InnerO
            On Error Resume Next
            x.Obj = NewLeaf(9)
            Debug.Print Err.Number
            Debug.Print x.Obj Is Nothing
            Private Function NewLeaf(ByVal n As Long) As Leaf
                Dim o As Leaf
                Set o = New Leaf
                o.Value = n
                Set NewLeaf = o
            End Function

            """, ("Leaf", "Public Value As Long\n"));
        AssertDebugLog([new Vb6Value(91L), new Vb6Value(true)]);
    }

}
