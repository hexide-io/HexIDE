using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

public class ModulePropertyGetTests : BaseVBTestFixture
{
    [Theory]
    [InlineData("PG")]
    [InlineData("PG()")]
    [InlineData("Module2.PG")]
    public async Task ScalarRead(string expression)
    {
        await RunModules($"Option Explicit\nDebug.Print TypeName({expression})", ("Module2", "Public Property Get PG() As Long\nPG = 5\nEnd Property"));
        AssertDebugLog(["Long"]);
    }

    [Fact]
    public async Task ParameterRead()
    {
        await RunModules("Option Explicit\nDebug.Print PI(3)", ("Module2", "Public Property Get PI(i As Long) As Long\nPI = i\nEnd Property"));
        AssertDebugLog([new Vb6Value(3L)]);
    }

    [Theory]
    [InlineData("PV(1)", 2)]
    [InlineData("PV()(2)", 3)]
    public async Task VariantArrayResult(string expression, int expected)
    {
        await RunModules($"Option Explicit\nDebug.Print {expression}", ("Module2", "Public Property Get PV() As Variant\nPV = Array(1, 2, 3)\nEnd Property"));
        AssertDebugLog([expected]);
    }

    [Fact]
    public async Task PrivateReadInsideModule()
    {
        await Run("Option Explicit\nPrivate Property Get PP() As Long\nPP = 42\nEnd Property\nDebug.Print PP");
        AssertDebugLog([new Vb6Value(42L)]);
    }

    [Theory]
    [InlineData("PG")]
    [InlineData("Call PG")]
    [InlineData("PG 5")]
    [InlineData("Set PG = Nothing")]
    public async Task InvalidUse(string statement)
    {
        var run = async () => await RunModules("Option Explicit\n" + statement,
            ("Module2", "Public Property Get PG() As Long\nPG = 5\nEnd Property"));
        (await run.Should().ThrowAsync<VBCompileErrorException>()).Which.Message.Should().Contain("Invalid use of property");
    }

    [Fact]
    public async Task SurplusArgument()
    {
        var run = async () => await RunModules("Option Explicit\nDebug.Print PG(1)",
            ("Module2", "Public Property Get PG() As Long\nPG = 5\nEnd Property"));
        (await run.Should().ThrowAsync<VBCompileErrorException>()).Which.Message.Should().Contain("Wrong number of arguments or invalid property assignment");
    }

    [Fact]
    public async Task PrivateReadOutsideModule()
    {
        var run = async () => await RunModules("Option Explicit\nDebug.Print PP",
            ("Module2", "Private Property Get PP() As Long\nPP = 42\nEnd Property"));
        await run.Should().ThrowAsync<VBVariableNotDefinedException>();
    }

    [Fact]
    public async Task TypedArrayRead()
    {
        await RunModules("Option Explicit\nDim x() As Long\nx = PG\nDebug.Print TypeName(x)\nDebug.Print x(1)",
            ("Module2", "Dim a(1) As Long\nPublic Property Get PG() As Long()\na(1) = 7\nPG = a\nEnd Property"));
        AssertDebugLog(["Long()", new Vb6Value(7L)]);
    }
}
