using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

// Measured cases from #741 and its #1476 follow-up; errors are translated to execution time.
public class SurplusArgumentTests : BaseVBTestFixture
{
    private const string WrongCount = "Wrong number of arguments or invalid property assignment";

    [Theory]
    [InlineData("Function H(ByVal a As Long) As Long\nH = a\nEnd Function", "Debug.Print H(5, 6)")]
    [InlineData("Function H(ByVal a As Long) As Variant\nH = a\nEnd Function", "Debug.Print H(5, 6)")]
    [InlineData("Sub H(ByVal a As Long)\nDebug.Print a\nEnd Sub", "Call H(5, 6)")]
    [InlineData("Function H(ByVal a As Long, Optional b As Long = 2) As Long\nH = a\nEnd Function", "Debug.Print H(5, 6, 7)")]
    public async Task Procedures_with_parameters_reject_surplus(string declaration, string call)
    {
        var run = () => Run(declaration + "\nSub Main()\n" + call + "\nEnd Sub\nMain\n");
        (await run.Should().ThrowAsync<VBCompileErrorException>()).WithMessage("Compile error:\n\n" + WrongCount);
    }

    [Theory]
    [InlineData("Long")]
    [InlineData("String")]
    [InlineData("Long()")]
    public async Task Parameterless_typed_returns_reject_arguments(string type)
    {
        var run = () => Run($"Function G() As {type}\nEnd Function\nSub Main()\nDebug.Print G(1)\nEnd Sub\nMain\n");
        (await run.Should().ThrowAsync<VBCompileErrorException>()).WithMessage("Compile error:\n\n" + WrongCount);
    }

    [Theory]
    [InlineData("Function H(ByVal a As Long) As Long\nH = a\nEnd Function")]
    [InlineData("Property Get H(ByVal a As Long) As Long\nH = a\nEnd Property")]
    public async Task Class_members_reject_surplus(string member)
    {
        var run = () => RunClasses("Sub Main()\nDim k As New K\nDebug.Print k.H(5, 6)\nEnd Sub\nMain\n", ("K", member + "\n"));
        (await run.Should().ThrowAsync<VBCompileErrorException>()).WithMessage("Compile error:\n\n" + WrongCount);
    }

    [Fact]
    public async Task Indexed_getter_binds_its_declared_parameter()
    {
        await RunClasses("Dim k As New K\nDebug.Print k.H(5)\n",
            ("K", "Property Get H(ByVal a As Long) As Long\nH = a\nEnd Property\n"));
        AssertDebugLog([new Vb6Value(5L)]);
    }

    [Fact]
    public async Task Parameterless_Left_divergence_refuses_instead_of_returning_a_wrong_value()
    {
        var run = () => Run("Function Left() As String\nLeft = \"noparams\"\nEnd Function\nDebug.Print Left(\"abc\", 1)\n");
        (await run.Should().ThrowAsync<VBCompileErrorException>()).WithMessage("Compile error:\n\n" + WrongCount);
    }

    [Theory]
    [InlineData("Chr(65, 66)")]
    [InlineData("Chr$(65, 66)")]
    [InlineData("Left(\"abc\", 1, 2)")]
    [InlineData("Abs(1, 2)")]
    [InlineData("InStr(1, \"a\", \"a\", 0, 0)")]
    [InlineData("StrComp(\"a\", \"a\", 0, 0)")]
    [InlineData("LBound(Array(1), 1, 1)")]
    [InlineData("UBound(Array(1), 1, 1)")]
    public async Task Intrinsics_reject_known_surplus(string call)
    {
        var run = () => Run("Debug.Print " + call + "\n");
        (await run.Should().ThrowAsync<VBCompileErrorException>()).WithMessage("Compile error:\n\n" + WrongCount);
    }

    [Theory]
    [InlineData("Left", "String", "\"abc\", 1")]
    [InlineData("Trim", "String", "\"abc\"")]
    [InlineData("x", "Long", "1")]
    public async Task Declared_scalars_claim_their_names(string name, string type, string args)
    {
        var run = () => Run($"Dim {name} As {type}\nDebug.Print {name}({args})\n");
        (await run.Should().ThrowAsync<VBCompileErrorException>()).WithMessage("Compile error:\n\nExpected array");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" As Variant")]
    public async Task Parameterless_Variant_results_still_index(string declaration)
    {
        await Run($"Function F(){declaration}\nF = Array(5, 6)\nEnd Function\nSub Main()\nDebug.Print F(1)\nEnd Sub\nMain\n");
        AssertDebugLog([new Vb6Value(6)]);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" As Variant")]
    public async Task Variant_variables_still_index(string declaration)
    {
        await Run($"Dim Left{declaration}\nLeft = Array(5, 6)\nDebug.Print Left(1)\n");
        AssertDebugLog([new Vb6Value(6)]);
    }

    [Theory]
    [InlineData("Object")]
    [InlineData("Collection")]
    [InlineData("Variant")]
    public async Task Object_variables_still_reach_Item(string type)
    {
        await Run($"Dim Left As {type}\nSet Left = New Collection\nLeft.Add \"one\"\nDebug.Print Left(1)\n");
        AssertDebugLog([new Vb6Value("one")]);
    }

    [Fact]
    public async Task Parameterless_object_result_still_reaches_Item()
    {
        await Run("Function F() As Collection\nSet F = New Collection\nF.Add \"one\"\nEnd Function\nSub Main()\nDebug.Print F(1)\nEnd Sub\nMain\n");
        AssertDebugLog([new Vb6Value("one")]);
    }

    [Fact]
    public async Task Own_name_still_recurses()
    {
        await Run("Function Fact(ByVal n As Long) As Long\nIf n = 0 Then\nFact = 1\nElse\nFact = n * Fact(n - 1)\nEnd If\nEnd Function\nSub Main()\nDebug.Print Fact(5)\nEnd Sub\nMain\n");
        AssertDebugLog([new Vb6Value(120L)]);
    }

    [Fact]
    public async Task Optional_slots_and_intrinsic_ParamArrays_still_work()
    {
        await Run("Function H(ByVal a As Long, Optional b As Long = 2) As Long\nH = a + b\nEnd Function\nSub Main()\nDebug.Print H(5), H(5, 6), UBound(Array(1, 2, 3)), Choose(2, 5, 6, 7)\nEnd Sub\nMain\n");
        AssertDebugLog([new Vb6Value(7L), new Vb6Value(11L), new Vb6Value(2L), new Vb6Value(6)]);
    }
}
