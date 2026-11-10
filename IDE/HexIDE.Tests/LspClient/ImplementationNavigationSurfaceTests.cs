using System.Xml.Linq;
using HexIDE.Keymaps;
using HexIDE.Menus;

namespace HexIDE.Tests.LspClient;

public class ImplementationNavigationSurfaceTests
{
    [Theory]
    [InlineData("Implementation")]
    [InlineData("TypeDefinition")]
    public void BothMenusAndTheKeymapOfferTheCommandWithoutADefault(string name)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !Directory.Exists(Path.Combine(root.FullName, "openspec"))) root = root.Parent;
        root.Should().NotBeNull();
        MainMenuDeclaration.Load().Single(i => i.Id == "view").Children!
            .Should().ContainSingle(i => i.Command == "application.GoTo" + name + "Command")
            .Which.LabelKey.Should().Be("Str.Menu.View." + name);
        var document = XDocument.Load(Path.Combine(root!.FullName, "IDE/HexIDE/Forms/Views/CodeEditorView.axaml"));
        document.Descendants().Where(e => e.Name.LocalName == "MenuItem"
            && (string?)e.Attribute("Command") == "{x:Static vb:ApplicationCommands.GoTo" + name + "Command}")
            .Should().ContainSingle().Which.Attribute("Header")!.Value.Should()
            .Be("{DynamicResource Str.CodeEditor.Context.GoTo" + name + "}");
        CommandKeyMapping.Table.Should().ContainKey("GoTo" + name + "Command");
        CommandKeyMapping.Table["GoTo" + name + "Command"].Gestures.Should().BeEmpty();
    }
}
