using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

// Object array declarations and lifetime (measured, #1482).
public class ObjectArrayTests : BaseVBTestFixture
{
    private const string Class1 = """
        Private Sub Class_Terminate()
            Debug.Print "terminated"
        End Sub

        """;

    [Fact]
    public async Task Object_array_TypeName()
    {
        await Run("Dim a(1) As Object\nDebug.Print TypeName(a)");
        AssertDebugLog(["Object()"]);
    }

    [Fact]
    public async Task Object_elements_start_as_Nothing()
    {
        await Run("Dim a(1) As Object\nDebug.Print a(0) Is Nothing\nDebug.Print a(1) Is Nothing");
        AssertDebugLog([true, true]);
    }

    [Fact]
    public async Task Object_element_stores_a_Collection()
    {
        await Run("Dim a(1) As Object\nSet a(0) = New Collection\na(0).Add 5\nDebug.Print a(0).Item(1)");
        AssertDebugLog([5]);
    }

    [Fact]
    public async Task Collection_array_TypeName()
    {
        await Run("Dim b(1) As Collection\nDebug.Print TypeName(b)");
        AssertDebugLog(["Object()"]);
    }

    [Fact]
    public async Task Collection_elements_start_as_Nothing()
    {
        await Run("Dim b(1) As Collection\nDebug.Print b(0) Is Nothing\nDebug.Print b(1) Is Nothing");
        AssertDebugLog([true, true]);
    }

    [Fact]
    public async Task Collection_element_stores_a_Collection()
    {
        await Run("Dim b(1) As Collection\nSet b(0) = New Collection\nb(0).Add 6\nDebug.Print b(0).Item(1)");
        AssertDebugLog([6]);
    }

    [Fact]
    public async Task Collection_element_refuses_and_releases_a_class_instance()
    {
        await RunClasses("""
            Dim b(1) As Collection
            On Error Resume Next
            Set b(1) = New Class1
            Debug.Print Err.Number
            Debug.Print b(1) Is Nothing
            """, ("Class1", Class1));
        AssertDebugLog(["terminated", new Vb6Value(13L), true]);
    }

    [Fact]
    public async Task Class_array_TypeName()
    {
        await RunClasses("Dim k(1) As Class1\nDebug.Print TypeName(k)", ("Class1", Class1));
        AssertDebugLog(["Object()"]);
    }

    [Fact]
    public async Task Erase_releases_a_Collection_and_its_items()
    {
        await RunClasses("""
            Dim b(1) As Collection
            Set b(0) = New Collection
            b(0).Add New Class1
            Erase b
            Debug.Print "after"
            Debug.Print b(0) Is Nothing
            """, ("Class1", Class1));
        AssertDebugLog(["terminated", "after", true]);
    }


    [Theory]
    [InlineData("Object")]
    [InlineData("Collection")]
    [InlineData("Class1")]
    public async Task Procedure_local_object_arrays_use_scalar_object_rules(string type)
    {
        var make = type == "Class1" ? "Class1" : "Collection";
        await RunClasses($"""
            Private Sub Check()
                Dim a(1) As {type}
                Debug.Print TypeName(a)
                Debug.Print a(0) Is Nothing
                Set a(0) = New {make}
                Debug.Print TypeName(a(0))
                Erase a
                Debug.Print a(0) Is Nothing
            End Sub
            Check
            """, ("Class1", ""));
        AssertDebugLog(["Object()", true, make, true]);
    }

    [Theory]
    [InlineData("Object")]
    [InlineData("Collection")]
    [InlineData("Class1")]
    public async Task Redim_and_Preserve_keep_object_references(string type)
    {
        var make = type == "Class1" ? "Class1" : "Collection";
        await RunClasses($"""
            Dim a() As {type}
            ReDim a(2) As {type}
            Debug.Print TypeName(a)
            Debug.Print a(2) Is Nothing
            Set a(0) = New {make}
            Dim o As Object
            Set o = a(0)
            ReDim Preserve a(3) As {type}
            Debug.Print a(0) Is o
            Debug.Print a(3) Is Nothing
            """, ("Class1", ""));
        AssertDebugLog(["Object()", true, true, true]);
    }
}
