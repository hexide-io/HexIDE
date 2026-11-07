using System.Xml.Linq;
using HexIDE.Keymaps;

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
        foreach (var file in new[] { "MainView.axaml", "Forms/Views/CodeEditorView.axaml" })
        {
            var key = file == "MainView.axaml" ? "Str.Menu.View." + name : "Str.CodeEditor.Context.GoTo" + name;
            var document = XDocument.Load(Path.Combine(root!.FullName, "IDE/HexIDE", file));
            document.Descendants().Where(e => e.Name.LocalName == "MenuItem"
                && (string?)e.Attribute("Command") == "{x:Static vb:ApplicationCommands.GoTo" + name + "Command}")
                .Should().ContainSingle().Which.Attribute("Header")!.Value.Should().Be("{DynamicResource " + key + "}");
        }
        CommandKeyMapping.Table.Should().ContainKey("GoTo" + name + "Command");
        CommandKeyMapping.Table["GoTo" + name + "Command"].Gestures.Should().BeEmpty();
    }
}
