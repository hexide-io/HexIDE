using HexIDE.Runtime.Interpreter;
using HexIDE.Runtime.Interpreter.ConditionalCompilation;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Runtime.Tests;

// VB6 SP6 native and p-code measurements (#4924): docs/vb6-fidelity-oracle/what-len-counts-in-a-user-defined-type.md.
// Each row checks both the size and its Variant subtype.
public class UdtLenTests : BaseVBTestFixture
{
    [Theory]
    [InlineData("b As Byte", "", 1)]
    [InlineData("i As Integer", "", 2)]
    [InlineData("b As Boolean", "", 2)]
    [InlineData("l As Long", "", 4)]
    [InlineData("s As Single", "", 4)]
    [InlineData("d As Double", "", 8)]
    [InlineData("c As Currency", "", 8)]
    [InlineData("d As Date", "", 8)]
    [InlineData("s As String", "", 4)]
    [InlineData("s As String", "u.s = \"hello\"", 4)]
    [InlineData("v As Variant", "", 16)]
    [InlineData("v As Variant", "u.v = 5", 16)]
    [InlineData("v As Variant", "u.v = \"hello\"", 16)]
    [InlineData("v As Variant", "u.v = Array(1, 2)", 16)]
    [InlineData("o As Object", "", 4)]
    [InlineData("c As Collection", "", 4)]
    [InlineData("x() As Long", "", 4)]
    [InlineData("x() As Long", "ReDim u.x(2)", 4)]
    [InlineData("x() As String", "", 4)]
    [InlineData("x() As String", "ReDim u.x(2)", 4)]
    [InlineData("x() As Byte", "", 4)]
    [InlineData("x() As Byte", "ReDim u.x(9)", 4)]
    [InlineData("x() As Double", "", 4)]
    [InlineData("x() As Double", "ReDim u.x(2)", 4)]
    [InlineData("x() As Variant", "", 4)]
    [InlineData("x() As Variant", "ReDim u.x(2)", 4)]
    [InlineData("x() As InLB", "", 4)]
    [InlineData("x(2) As Integer", "", 6)]
    [InlineData("x(2) As String", "", 12)]
    [InlineData("x(1) As Variant", "", 32)]
    [InlineData("x(1, 2) As Byte", "", 6)]
    [InlineData("x(1) As Boolean", "", 4)]
    [InlineData("x(1) As Object", "", 8)]
    [InlineData("x(1) As Collection", "", 8)]
    [InlineData("x As E", "", 4)]
    [InlineData("x(2) As E", "", 12)]
    [InlineData("x(1 To 2) As String * 3", "", 6)]
    [InlineData("n As InLB", "", 5)]
    [InlineData("x(1) As InLB", "", 10)]
    [InlineData("a As Byte\nb As Long\nc As Byte", "", 6)]
    [InlineData("a As Double\nb As Byte", "", 9)]
    [InlineData("i As Integer\ns As String", "u.s = \"hello\"", 6)]
    [InlineData("i As Integer\nf As String * 3\ns As String", "", 9)]
    [InlineData("f As String * 3\nv As Variant", "", 19)]
    [InlineData("f As String * 3\nx() As Long", "", 7)]
    [InlineData("n As InFixed\ns As String", "", 7)]
    [InlineData("i As Integer\ns As String * 4", "", 6)]
    [InlineData("b(32766) As Byte", "", 32767)]
    [InlineData("b(32767) As Byte", "", 32768)]
    [InlineData("b(39999) As Byte", "", 40000)]
    [InlineData("s As String * 20000", "", 20000)]
    [InlineData("a As InLarge\nb As InLarge", "", 33600)]
    public async Task Type_length_sums_fields_without_padding(string fields, string assign, int expected)
    {
        await Run($"Enum E\nEA\nEB\nEnd Enum\nType InLB\nl As Long\nb As Byte\nEnd Type\nType InFixed\ns As String * 3\nEnd Type\nType InLarge\nv(1049) As Variant\nEnd Type\nType T\n{fields}\nEnd Type\nDim u As T\n{assign}\nDebug.Print Len(u)\nDebug.Print TypeName(Len(u))");
        AssertDebugLog([expected <= short.MaxValue ? new Vb6Value(expected) : new Vb6Value((long)expected),
            new Vb6Value(expected <= short.MaxValue ? "Integer" : "Long")]);
    }

    [Theory]
    [InlineData("c As Class1\ni As Integer", 6)]
    [InlineData("c As New Class1\ni As Integer", 6)]
    [InlineData("c(1) As Class1", 8)]
    [InlineData("c() As Class1", 4)]
    public async Task Project_class_reference_counts_four_bytes(string fields, int expected)
    {
        await RunClasses($"Type T\n{fields}\nEnd Type\nDim u As T\nDebug.Print Len(u)\nDebug.Print TypeName(Len(u))", ("Class1", "Public A As Long"));
        AssertDebugLog([new Vb6Value(expected), new Vb6Value("Integer")]);
    }

    [Fact]
    public async Task Private_type_in_class_counts_each_field()
    {
        await RunClasses("Dim c As New Class1\nc.Measure", ("Class1",
            "Private Type T\ni As Integer\nv As Variant\ns As String * 3\nEnd Type\nPrivate u As T\nPublic Sub Measure()\nDebug.Print Len(u)\nDebug.Print TypeName(Len(u))\nEnd Sub"));
        AssertDebugLog([new Vb6Value(21), new Vb6Value("Integer")]);
    }

    [Fact]
    public async Task Type_array_element_counts_its_fields()
    {
        await Run("Type T\nl As Long\nEnd Type\nDim arr(2) As T\nDebug.Print Len(arr(1))\nDebug.Print TypeName(Len(arr(1)))");
        AssertDebugLog([new Vb6Value(4), new Vb6Value("Integer")]);
    }

    [Fact]
    public async Task Declared_scalar_keeps_Long_result()
    {
        // #780's measured scalar path is unchanged by #4924.
        await Run("Dim i As Integer\nDebug.Print Len(i)\nDebug.Print TypeName(Len(i))");
        AssertDebugLog([new Vb6Value(2L), new Vb6Value("Long")]);
    }

    [Theory]
    [InlineData("LenB", "Integer", "LenB of a user-defined type is not implemented: it is the Type's size in memory, padded, which HexIDE does not lay out (#1510).")]
    [InlineData("Len", "LongLong", "The layout of a Type containing LongLong is unmeasured (#1548)")]
    public async Task Unmeasured_layouts_still_refuse(string function, string fieldType, string message)
    {
        if (fieldType == "LongLong")
            Compilation = CompilationConstants.ForProject(
                new ProjectDefinition(VBProjectType.EXE, "Test") { VbaBitness = VbaBitness.Bit64 });
        var run = () => Run($"Type T\nn As {fieldType}\nEnd Type\nDim u As T\nDebug.Print {function}(u)");
        await run.Should().ThrowAsync<NotImplementedException>().WithMessage(message);
    }
}
