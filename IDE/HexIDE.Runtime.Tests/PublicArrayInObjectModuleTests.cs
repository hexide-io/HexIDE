using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// A Public array in a class or a form module (#3335). vb6.exe refuses the program at /make, native and p-code, and the IDE
/// refuses it before Sub Main runs, with Compile On Demand on or off, whether or not anything uses the class. HexIDE refuses
/// it as the program loads. A Private or Dim array there, a Public scalar, and a Public array in a standard module all run.
/// Measured on VB6 SP6; the oracle page is a-public-array-in-an-object-module.md.
/// </summary>
public class PublicArrayInObjectModuleTests : BaseVBTestFixture
{
    private const string Refused =
        "Constants, fixed-length strings, arrays, user-defined types and Declare statements not allowed as Public members of object modules";

    [Theory]
    [InlineData("Public Items(1)")]
    [InlineData("Public Items(1) As Long")]
    [InlineData("Public Items() As Long")]
    [InlineData("Public Items()")]
    [InlineData("Public Items(1 To 3) As String")]
    [InlineData("Public n, Items(1)")]
    public async Task A_Public_array_in_a_class_is_refused_as_the_program_loads(string declaration)
    {
        var run = async () => await RunClasses("Dim o\nSet o = New Source\nDebug.Print \"ran\"\n", ("Source", declaration + "\n"));
        (await run.Should().ThrowAsync<VBCompileErrorException>()).Which.Message.Should().EndWith(Refused);
        debug.Should().BeEmpty();
    }

    [Fact]
    public async Task It_is_refused_when_nothing_uses_the_class()
    {
        var run = async () => await RunClasses("Debug.Print \"ran\"\n", ("Source", "Public Items(1)\n"));
        (await run.Should().ThrowAsync<VBCompileErrorException>()).Which.Message.Should().EndWith(Refused);
        debug.Should().BeEmpty();
    }

    [Theory]
    [InlineData("Private Items(1)")]
    [InlineData("Dim Items(1)")]
    [InlineData("Public n")]
    public async Task A_Private_or_Dim_array_and_a_Public_scalar_in_a_class_run(string declaration)
    {
        await RunClasses("Dim o\nSet o = New Source\nDebug.Print \"ran\"\n", ("Source", declaration + "\n"));
        AssertDebugLog(["ran"]);
    }

    [Fact]
    public async Task A_Public_array_in_a_standard_module_runs()
    {
        await RunModules("Items(0) = 1\nDebug.Print Items(0)\n", ("Module2", "Public Items(1)\n"));
        AssertDebugLog([1]);
    }

    [Theory]
    [InlineData("Public Items(1)", true)]
    [InlineData("Public Items() As Long", true)]
    [InlineData("Private Items(1)", false)]
    public void In_a_form_module_only_the_Public_array_is_refused(string declaration, bool refused)
    {
        var load = () => new BasicInterpreter(new MockStdLib(debug), context, rootEnv, declaration + "\n",
            primaryKind: InterpreterModuleKind.Form);
        if (refused)
            load.Should().Throw<VBCompileErrorException>().Which.Message.Should().EndWith(Refused);
        else
            load.Should().NotThrow();
    }
}
