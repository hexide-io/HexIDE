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

    // #179: non-Public object members and standard-module declarations now load.
    [Theory]
    [InlineData("Private S As String * 5", InterpreterModuleKind.Class)]
    [InlineData("Dim S As String * 5", InterpreterModuleKind.Class)]
    [InlineData("Private S As String * 5", InterpreterModuleKind.Form)]
    [InlineData("Dim S As String * 5", InterpreterModuleKind.Form)]
    [InlineData("Public S As String * 5", InterpreterModuleKind.Standard)]
    public void Other_fixed_length_strings_load(string declaration, InterpreterModuleKind kind)
    {
        Load(declaration, kind);
    }

    // #5042, rows C1/C3 and F1/F3, measured native and p-code in oracle PR #5043.
    [Theory]
    [InlineData("Public Type", InterpreterModuleKind.Class, "")]
    [InlineData("Type", InterpreterModuleKind.Class, "")]
    [InlineData("Public Type", InterpreterModuleKind.Form, "")]
    [InlineData("Type", InterpreterModuleKind.Form, "")]
    [InlineData("Public Type", InterpreterModuleKind.Class, "Attribute VB_Exposed = False\n")]
    [InlineData("Type", InterpreterModuleKind.Class, "Attribute VB_Exposed = False\n")]
    [InlineData("Public Type", InterpreterModuleKind.Form, "Attribute VB_Exposed = False\n")]
    [InlineData("Type", InterpreterModuleKind.Form, "Attribute VB_Exposed = False\n")]
    public void Public_types_in_private_object_modules_are_refused_at_load(string type, InterpreterModuleKind kind, string attributes)
    {
        var load = () => Load(attributes + type + " T\na(1 To 3) As Long\nEnd Type\nPrivate x As T", kind);
        load.Should().Throw<VBCompileErrorException>().Which.Message.Should()
            .EndWith("Cannot define a Public user-defined type within a private object module");
    }

    // #5042, oracle PRs #5043 (private Types) and #5053 (exposed classes D1/D3).
    [Theory]
    [InlineData("Private Type", InterpreterModuleKind.Class, "")]
    [InlineData("Private Type", InterpreterModuleKind.Form, "")]
    [InlineData("Public Type", InterpreterModuleKind.Standard, "")]
    [InlineData("Type", InterpreterModuleKind.Standard, "")]
    [InlineData("Public Type", InterpreterModuleKind.Class, "Attribute VB_Exposed = True\n")]
    [InlineData("Type", InterpreterModuleKind.Class, "Attribute VB_Exposed = True\n")]
    public void Other_type_declarations_still_load(string type, InterpreterModuleKind kind, string attributes)
        => Load(attributes + type + " T\na(1 To 3) As Long\nEnd Type\nPrivate x As T", kind);

    private BasicInterpreter Load(string declaration, InterpreterModuleKind kind)
        => new(new MockStdLib(debug), context, rootEnv, declaration + "\n",
            additionalModules: [("Definitions", Types)], primaryKind: kind);
}
