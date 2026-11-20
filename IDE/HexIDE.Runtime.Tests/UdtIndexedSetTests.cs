using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

// vb6.exe SP6, native and p-code, 2026-11-20: https://github.com/hexide-io/HexIDE/issues/3924, B–D.
public class UdtIndexedSetTests : BaseVBTestFixture
{
    [Fact]
    public async Task Indexed_records_keep_independent_object_slots_and_support_nested_Set()
    {
        await RunClasses("""
            Private Type T
                Obj As Object
            End Type
            Private Type Outer
                In1 As T
            End Type
            Dim ua(1 To 2) As T
            Dim ub(1 To 2) As Outer
            Set ua(1).Obj = New Collection
            Debug.Print TypeName(ua(1).Obj), ua(2).Obj Is Nothing
            ua(1).Obj.Add 5
            Debug.Print ua(1).Obj.Count
            Set ua(1).Obj = New Leaf
            ua(1).Obj.Value = 4
            Debug.Print ua(1).Obj.Value
            Set ua(1).Obj = Nothing
            Debug.Print ua(1).Obj Is Nothing
            Set ub(2).In1.Obj = New Leaf
            Debug.Print ub(1).In1.Obj Is Nothing
            On Error Resume Next
            Set ua(3).Obj = New Collection
            Debug.Print Err.Number
            """, ("Leaf", "Public Value As Long\n"));
        AssertDebugLog(["Collection", true, new Vb6Value(1L), new Vb6Value(4L), true, true, new Vb6Value(9L)]);
    }

    [Fact]
    public async Task Dynamic_record_Set_survives_Preserve()
    {
        await Run("""
            Private Type T
                Obj As Object
            End Type
            Dim ud() As T
            ReDim ud(1)
            Set ud(0).Obj = New Collection
            ReDim Preserve ud(2)
            Debug.Print TypeName(ud(0).Obj), ud(2).Obj Is Nothing
            """);
        AssertDebugLog(["Collection", true]);
    }

    [Fact]
    public async Task Array_and_Variant_fields_take_Set_with_their_declared_constraints()
    {
        await RunClasses("""
            Private Type T
                a(1 To 2) As Leaf
                c(1) As Collection
                v(1) As Variant
                o(1) As Object
                w As Variant
            End Type
            Dim u As T
            Debug.Print u.a(1) Is Nothing
            Set u.a(1) = New Leaf
            u.a(1).Value = 3
            Debug.Print u.a(1).Value, TypeName(u.a(1))
            On Error Resume Next
            Set u.a(2) = New Collection
            Debug.Print Err.Number
            Err.Clear
            Set u.c(0) = New Collection
            Debug.Print TypeName(u.c(0))
            Set u.c(1) = New Leaf
            Debug.Print Err.Number
            Set u.v(0) = New Collection
            Debug.Print TypeName(u.v(0))
            u.v(1) = 5
            Debug.Print TypeName(u.v(1))
            Set u.o(0) = New Leaf
            Debug.Print TypeName(u.o(0))
            Err.Clear
            Set u.o(2) = New Collection
            Debug.Print Err.Number
            u.w = Array(1, 2)
            Set u.w(1) = New Collection
            Debug.Print TypeName(u.w(1))
            """, ("Leaf", "Public Value As Long\n"));
        AssertDebugLog([true, new Vb6Value(3L), "Leaf", new Vb6Value(13L), "Collection", new Vb6Value(13L), "Collection", "Integer", "Leaf", new Vb6Value(9L), "Collection"]);
    }

    [Fact]
    public async Task Empty_Variant_with_indices_is_13()
    {
        await Run("""
            Private Type T
                w As Variant
            End Type
            Dim u As T
            On Error Resume Next
            Set u.w(1) = New Collection
            Debug.Print Err.Number
            """);
        AssertDebugLog([new Vb6Value(13L)]);
    }

    [Fact]
    public async Task Variant_field_takes_an_object_and_Nothing()
    {
        await Run("""
            Private Type T
                w As Variant
            End Type
            Dim u As T
            Set u.w = New Collection
            Debug.Print TypeName(u.w)
            Set u.w = Nothing
            Debug.Print u.w Is Nothing
            """);
        AssertDebugLog(["Collection", true]);
    }

    [Theory]
    [InlineData("n As Long", "n", "")]
    [InlineData("a(1) As String", "a(0)", "")]
    [InlineData("s As String * 4", "s", "")]
    [InlineData("w As Variant\na(1) As Integer", "w(0)", "u.w = u.a")]
    public async Task Unmeasured_non_object_Set_targets_keep_the_existing_refusal(string declaration, string target, string setup)
    {
        // HexIDE's measured-only policy, not an assertion of a VB6 error for these unmeasured cases.
        var run = () => Run($"Private Type T\n{declaration}\nEnd Type\nDim u As T\n{setup}\nSet u.{target} = New Collection\n");
        (await run.Should().ThrowAsync<NotImplementedException>()).WithMessage("*not implemented (#3924)");
    }

    [Theory]
    [InlineData("n As Integer", "n")]
    [InlineData("s As String", "s")]
    [InlineData("nested As Inner", "nested")]
    [InlineData("a(1) As Integer", "a(0)")]
    public async Task Non_object_field_Set_is_a_compile_error(string declaration, string target)
    {
        var run = () => Run($"Private Type Inner\nx As Long\nEnd Type\nPrivate Type T\n{declaration}\nEnd Type\nDim u As T\nSet u.{target} = New Collection\n");
        (await run.Should().ThrowAsync<VBCompileErrorException>()).WithMessage("*Object required");
    }
}
