using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

// vb6.exe SP6, native and p-code: https://github.com/hexide-io/HexIDE/issues/4596, rows 1–14.
public class UdtObjectArrayFieldTests : BaseVBTestFixture
{
    private const string Types = "Private Type T\nk(1 To 2) As Class1\no(1 To 2) As Object\nc() As Collection\nEnd Type\nDim x As T\n";
    private const string ClassSource = "Public V As Long\nPrivate Sub Class_Terminate()\nDebug.Print \"terminated\"\nEnd Sub\n";

    // Row 1.
    [Fact]
    public async Task Fresh_Object_elements_are_Nothing()
    {
        await RunClasses(Types + """
            Debug.Print x.o(1) Is Nothing

            """, ("Class1", ClassSource));
        AssertDebugLog([new Vb6Value(true)]);
    }

    // Row 2.
    [Fact]
    public async Task Object_elements_read_Collection_members()
    {
        await RunClasses(Types + """
            Set x.o(1) = New Collection
            x.o(1).Add 5
            Debug.Print x.o(1).Count
            Debug.Print TypeName(x.o(1))

            """, ("Class1", ClassSource));
        AssertDebugLog([new Vb6Value(1L), "Collection"]);
    }

    // Row 3.
    [Fact]
    public async Task Redim_makes_Nothing_elements()
    {
        await RunClasses(Types + """
            ReDim x.c(1)
            Debug.Print x.c(0) Is Nothing

            """, ("Class1", ClassSource));
        AssertDebugLog([new Vb6Value(true)]);
    }

    // Row 4.
    [Fact]
    public async Task Dynamic_Collection_elements_take_Set()
    {
        await RunClasses(Types + """
            ReDim x.c(1)
            Set x.c(1) = New Collection
            Debug.Print x.c(1).Count

            """, ("Class1", ClassSource));
        AssertDebugLog([new Vb6Value(0L)]);
    }

    // Row 5.
    [Fact]
    public async Task Class_elements_read_and_write_members()
    {
        await RunClasses(Types + """
            Set x.k(1) = New Class1
            x.k(1).V = 3
            Debug.Print x.k(1).V

            """, ("Class1", ClassSource));
        AssertDebugLog([new Vb6Value(3L)]);
    }

    // Row 6.
    [Fact]
    public async Task Wrong_class_is_13_and_keeps_Nothing()
    {
        await RunClasses(Types + """
            On Error Resume Next
            Set x.k(1) = New Collection
            Debug.Print Err.Number
            Debug.Print x.k(1) Is Nothing

            """, ("Class1", ClassSource));
        AssertDebugLog([new Vb6Value(13L), new Vb6Value(true)]);
    }

    // Row 7.
    [Fact]
    public async Task Wrong_class_through_Object_is_13()
    {
        await RunClasses(Types + """
            Dim ob As Object
            Set ob = New Collection
            On Error Resume Next
            Set x.k(1) = ob
            Debug.Print Err.Number

            """, ("Class1", ClassSource));
        AssertDebugLog([new Vb6Value(13L)]);
    }

    // Row 8.
    [Fact]
    public async Task Record_copy_shares_objects()
    {
        await RunClasses(Types + """
            Set x.o(1) = New Collection
            Dim y As T
            y = x
            y.o(1).Add 9
            Debug.Print x.o(1).Count

            """, ("Class1", ClassSource));
        AssertDebugLog([new Vb6Value(1L)]);
    }

    // Row 9.
    [Fact]
    public async Task Record_copy_owns_independent_slots()
    {
        await RunClasses(Types + """
            Set x.o(1) = New Collection
            Dim y As T
            y = x
            Set y.o(1) = Nothing
            Debug.Print x.o(1) Is Nothing

            """, ("Class1", ClassSource));
        AssertDebugLog([new Vb6Value(false)]);
    }

    // Row 10.
    [Fact]
    public async Task Erase_fixed_fields_releases_objects_and_keeps_bounds()
    {
        await RunClasses(Types + """
            Set x.k(1) = New Class1
            Set x.o(1) = New Collection
            Debug.Print "before"
            Erase x.k
            Erase x.o
            Debug.Print x.k(1) Is Nothing
            Debug.Print x.o(1) Is Nothing
            Debug.Print UBound(x.k)

            """, ("Class1", ClassSource));
        AssertDebugLog(["before", "terminated", new Vb6Value(true), new Vb6Value(true), new Vb6Value(2L)]);
    }

    // Row 11.
    [Fact]
    public async Task Erase_dynamic_field_frees_bounds()
    {
        await RunClasses(Types + """
            ReDim x.c(1)
            Set x.c(1) = New Collection
            Erase x.c
            On Error Resume Next
            Debug.Print UBound(x.c)
            Debug.Print Err.Number

            """, ("Class1", ClassSource));
        AssertDebugLog([new Vb6Value(9L)]);
    }

    // Row 12.
    [Fact]
    public async Task Local_record_releases_at_End_Sub()
    {
        await RunClasses(Types + """
            Sub Test()
            Dim local As T
            Set local.k(1) = New Class1
            Debug.Print "last"
            End Sub
            Test
            Debug.Print "caller"

            """, ("Class1", ClassSource));
        AssertDebugLog(["last", "terminated", "caller"]);
    }

    // Row 13.
    [Fact]
    public async Task Last_copied_reference_terminates()
    {
        await RunClasses(Types + """
            Dim y As T, z As T
            Set y.k(2) = New Class1
            z = y
            Set y.k(2) = Nothing
            Debug.Print "first"
            Set z.k(2) = Nothing
            Debug.Print "second"

            """, ("Class1", ClassSource));
        AssertDebugLog(["first", "terminated", "second"]);
    }

    // Row 14.
    [Fact]
    public async Task ByRef_record_Set_reaches_caller()
    {
        await RunClasses(Types + """
            Sub Modify(ByRef arg As T)
            Set arg.o(2) = New Collection
            End Sub
            Modify x
            Debug.Print TypeName(x.o(2))

            """, ("Class1", ClassSource));
        AssertDebugLog(["Collection"]);
    }
}
