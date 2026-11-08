using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

// #3350: /make measurements, native and p-code, in
// docs/vb6-fidelity-oracle/a-public-array-in-an-object-module.md (#3349).
public class PublicMemberInObjectModuleTests : BaseVBTestFixture
{
    private const string Refused =
        "Constants, fixed-length strings, arrays, user-defined types and Declare statements not allowed as Public members of object modules";
    private const string Types = "Public Type TT\nValue As Long\nEnd Type\n";

    [Theory]
    [InlineData("Public Const K = 1", InterpreterModuleKind.Class)]
    [InlineData("Public S As String * 5", InterpreterModuleKind.Class)]
    [InlineData("Public Declare Function GetTickCount Lib \"kernel32\" () As Long", InterpreterModuleKind.Class)]
    [InlineData("Public Const K = 1", InterpreterModuleKind.Form)]
    [InlineData("Public S As String * 5", InterpreterModuleKind.Form)]
    [InlineData("Public Declare Function GetTickCount Lib \"kernel32\" () As Long", InterpreterModuleKind.Form)]
    public void Public_members_are_refused_at_load(string declaration, InterpreterModuleKind kind)
    {
        var load = () => Load(declaration, kind);
        load.Should().Throw<VBCompileErrorException>().Which.Message.Should().EndWith(Refused);
        debug.Should().BeEmpty();
    }

    [Theory]
    [InlineData(InterpreterModuleKind.Class)]
    [InlineData(InterpreterModuleKind.Form)]
    public async Task Public_UDT_member_is_refused_when_instance_storage_is_created(InterpreterModuleKind kind)
    {
        var interpreter = Load("Public t As TT", kind);
        debug.Should().BeEmpty();
        var create = async () =>
        {
            if (kind == InterpreterModuleKind.Class)
                await interpreter.NewObject(interpreter.PrimaryModule.Name);
            else
                await interpreter.RunFormDeclarations(interpreter.NewFormInstance(interpreter.PrimaryModule));
        };
        (await create.Should().ThrowAsync<VBCompileErrorException>()).Which.Message.Should().EndWith(Refused);
    }

    [Theory]
    [InlineData("Private t As TT", InterpreterModuleKind.Class)]
    [InlineData("Dim t As TT", InterpreterModuleKind.Class)]
    [InlineData("Private t As TT", InterpreterModuleKind.Form)]
    [InlineData("Dim t As TT", InterpreterModuleKind.Form)]
    public async Task Private_and_Dim_UDT_members_still_allocate(string declaration, InterpreterModuleKind kind)
    {
        var interpreter = Load(declaration, kind);
        if (kind == InterpreterModuleKind.Class)
            await interpreter.NewObject(interpreter.PrimaryModule.Name);
        else
            await interpreter.RunFormDeclarations(interpreter.NewFormInstance(interpreter.PrimaryModule));
    }

    [Theory]
    [InlineData("Private Const K = 1")]
    [InlineData("Const K = 1")]
    [InlineData("Private t As TT")]
    [InlineData("Dim t As TT")]
    [InlineData("Private Declare Function GetTickCount Lib \"kernel32\" () As Long")]
    [InlineData("Public n As Long")]
    [InlineData("Public o As Object")]
    public void Other_object_members_still_load(string declaration)
    {
        foreach (var kind in new[] { InterpreterModuleKind.Class, InterpreterModuleKind.Form })
            Load(declaration, kind);
    }

    [Theory]
    [InlineData("Public Const K = 1")]
    [InlineData("Public t As TT")]
    [InlineData("Public Declare Function GetTickCount Lib \"kernel32\" () As Long")]
    public void Standard_module_declarations_still_load(string declaration)
        => Load(declaration, InterpreterModuleKind.Standard);

    // These fixed-length strings already stop at the existing implementation gap.
    [Theory]
    [InlineData("Private S As String * 5", InterpreterModuleKind.Class)]
    [InlineData("Dim S As String * 5", InterpreterModuleKind.Class)]
    [InlineData("Private S As String * 5", InterpreterModuleKind.Form)]
    [InlineData("Dim S As String * 5", InterpreterModuleKind.Form)]
    [InlineData("Public S As String * 5", InterpreterModuleKind.Standard)]
    public void Other_fixed_length_strings_keep_the_existing_refusal(string declaration, InterpreterModuleKind kind)
    {
        var load = () => Load(declaration, kind);
        load.Should().Throw<NotImplementedException>().WithMessage("fieldLength as type not implemented");
    }

    private BasicInterpreter Load(string declaration, InterpreterModuleKind kind)
        => new(new MockStdLib(debug), context, rootEnv, declaration + "\n",
            additionalModules: [("Definitions", Types)], primaryKind: kind);
}
