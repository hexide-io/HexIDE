using System.Linq;
using System.Threading.Tasks;
using HexIDE.IDE;
using HexIDE.Runtime.Components.InBox;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

// #2265, oracle/in-box-classes-a-bare-name-binds-to-the-first-library.md and
// its "More bare names" section (oracle PR #3956).
public class ControlClassNameTests : BaseVBTestFixture
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Object_reference_alone_declares_every_measured_bare_class(bool five)
    {
        var names = new[] { "ImageList", "ListImages", "ListImage", "ListView", "ListItems", "ListItem",
            "ColumnHeaders", "ColumnHeader", "TreeView", "Nodes", "Node", "ProgressBar", "StatusBar",
            "Panels", "Panel", "Toolbar", "Buttons", "Button", "TabStrip", "Tabs" };
        if (!five) names = names.Concat(["ListSubItems", "ListSubItem", "ButtonMenus", "ButtonMenu"]).ToArray();
        var library = five ? ControlLibraries.ComctlLib : ControlLibraries.MSComctlLib;
        var code = string.Join("\n", names.Select((name, i) => $"Dim x{i} As {name}\nDebug.Print TypeName(x{i})"));
        var interpreter = new BasicInterpreter(new MockStdLib(debug), context, rootEnv, code);
        interpreter.SetAppInfo(new AppInfo(ControlLibraries: [library]));
        await interpreter.Execute();
        debug.Select(v => v.Value).Should().Equal(names.Select(_ => "Nothing"));
    }

    [Fact]
    public async Task Project_class_keeps_precedence_over_a_library_name()
    {
        // Preserve today's project-name precedence; its ranking against a library is not measured.
        var interpreter = new BasicInterpreter(new MockStdLib(debug), context, rootEnv, """
            Dim n As Node
            Set n = New Node
            Debug.Print TypeName(n), TypeOf n Is Node, TypeOf n Is MSComctlLib.Node
            """, "Module1", null, [("Node", "Public Value As Long")]);
        interpreter.SetAppInfo(new AppInfo(ControlLibraries: [ControlLibraries.MSComctlLib]));
        await interpreter.Execute();
        debug.Select(v => v.Value?.ToString()).Should().Equal("Node", "True", "False");
    }

    [Fact]
    public void First_library_declaring_each_name_wins()
    {
        var interpreter = new BasicInterpreter(new MockStdLib(debug), context, rootEnv, "");
        interpreter.SetAppInfo(new AppInfo(ControlLibraries: [ControlLibraries.ComctlLib, ControlLibraries.MSComctlLib]));
        // Two-library binding was measured for ListView/ListItem/ProgressBar; the other names follow that rule.
        interpreter.ControlClassFor("Node").Should().Be("ComctlLib.Node");
        interpreter.ControlClassFor("ButtonMenu").Should().Be("MSComctlLib.ButtonMenu");
        interpreter.ControlClassFor("MSComctlLib.Node").Should().Be("MSComctlLib.Node");
        interpreter.ControlClassFor("Tab").Should().BeNull();
        interpreter.ControlClassFor("ComctlLib.Tab").Should().Be("ComctlLib.Tab");
    }

    [Fact]
    public async Task Without_a_library_ListImage_is_still_undefined()
    {
        var run = () => Run("Dim x As ListImage");
        await run.Should().ThrowAsync<VBCompileErrorException>().WithMessage("*User-defined type not defined: ListImage*");
    }

    [Theory]
    [InlineData("ListSubItems")]
    [InlineData("ListSubItem")]
    [InlineData("ButtonMenus")]
    [InlineData("ButtonMenu")]
    public async Task Five_does_not_declare_six_only_classes(string name)
    {
        var interpreter = new BasicInterpreter(new MockStdLib(debug), context, rootEnv, $"Dim x As {name}");
        interpreter.SetAppInfo(new AppInfo(ControlLibraries: [ControlLibraries.ComctlLib]));
        var run = () => interpreter.Execute();
        await run.Should().ThrowAsync<VBCompileErrorException>().WithMessage("*User-defined type not defined*");
    }
}
