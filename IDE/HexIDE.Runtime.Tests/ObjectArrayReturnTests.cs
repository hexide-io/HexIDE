using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

// Measured on vb6.exe SP6, native and p-code agreeing (#999); see the oracle's "An array of objects".
public class ObjectArrayReturnTests : BaseVBTestFixture
{
    [Theory]
    [InlineData("Object")]
    [InlineData("Collection")]
    public async Task A_Variant_array_is_refused_at_the_return_store(string type)
    {
        await Run($"""
            On Error Resume Next
            Dim x
            x = F()
            Debug.Print Err.Number & " " & TypeName(x)
            Function F() As {type}()
                F = Array(1, 2)
            End Function
            """);

        AssertDebugLog([new Vb6Value("13 Empty")]);
    }

    [Theory]
    [InlineData("Collection", "Collection", false)]
    [InlineData("Class1", "Class1", false)]
    [InlineData("Object", "Object", true)]
    [InlineData("Class1", "Class1", true)]
    [InlineData("Class1", "Object", true)]
    [InlineData("Collection", "Object", true)]
    [InlineData("Object", "Collection", true)]
    [InlineData("Class1", "Class2", true)]
    [InlineData("Object", "Class1", true)]
    public async Task An_object_array_return_accepts_its_type_or_any_object_array_through_a_Variant(string target, string source, bool variant)
    {
        await RunClasses($"""
            On Error Resume Next
            Dim x
            x = F()
            Debug.Print Err.Number & " " & TypeName(x)
            Function F() As {target}()
                Dim a(1) As {source}, v
                v = a
                F = {(variant ? "v" : "a")}
            End Function
            """, ("Class1", ""), ("Class2", ""));

        AssertDebugLog([new Vb6Value("0 Object()")]);
    }

    [Theory]
    [InlineData("Collection", "Object", false)]
    [InlineData("Object", "Collection", false)]
    [InlineData("Object", "Class1", false)]
    [InlineData("Class1", "Object", false)]
    [InlineData("Class1", "Class2", false)]
    [InlineData("Object", "Variant", false)]
    [InlineData("Collection", "Object", true)]
    [InlineData("Object", "Collection", true)]
    [InlineData("Object", "Class1", true)]
    [InlineData("Class1", "Class2", true)]
    [InlineData("Class1", "Object", true)]
    public async Task A_named_array_requires_the_exact_declared_element_type(string target, string source, bool variable)
    {
        var body = variable
            ? $"Dim b() As {target}\nb = a\n"
            : $"Dim x\nx = F()\nFunction F() As {target}()\nDim a(1) As {source}\nF = a\nEnd Function\n";
        var run = () => RunClasses((variable ? $"Dim a(1) As {source}\n" : "") + body,
            ("Class1", ""), ("Class2", ""));

        (await run.Should().ThrowAsync<VBCompileErrorException>()).WithMessage("*Can't assign to array*");
    }

    [Theory]
    [InlineData("Object")]
    [InlineData("Class2")]
    public async Task A_class_array_variable_accepts_any_object_array_through_a_Variant(string source)
    {
        await RunClasses($"""
            On Error Resume Next
            Dim a(1) As {source}, b() As Class1, v
            v = a
            b = v
            Debug.Print Err.Number & " " & TypeName(b)
            """, ("Class1", ""), ("Class2", ""));

        AssertDebugLog([new Vb6Value("0 Object()")]);
    }

    [Fact]
    public async Task A_class_array_return_checks_the_array_but_not_the_elements_classes()
    {
        await RunClasses("""
            On Error Resume Next
            Dim x, y As Class1
            x = F()
            Debug.Print Err.Number & " " & TypeName(x(0))
            Set y = x(0)
            Debug.Print Err.Number
            Function F() As Class1()
                Dim a(1) As Object, v
                Set a(0) = New Collection
                v = a
                F = v
            End Function
            """, ("Class1", ""));

        AssertDebugLog([new Vb6Value("0 Collection"), new Vb6Value(13L)]);
    }
}
