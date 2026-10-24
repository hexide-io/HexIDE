using System.Xml.Linq;

namespace HexIDE.Tests.Components;

public class PropertyValueScrollingTests
{
    [Fact]
    public void Single_line_property_values_disable_vertical_scrolling()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !(Directory.Exists(Path.Combine(root.FullName, "IDE"))
                                 && Directory.Exists(Path.Combine(root.FullName, "LspServer"))))
            root = root.Parent;

        // The dictionary is compiled internal to HexIDE. Guard the shared editor declaration itself;
        // the multiline list flyout has a separate template and must keep its scrolling.
        var markup = XDocument.Load(Path.Combine(root!.FullName, "IDE", "HexIDE", "Controls", "Properties", "PropertyBox.axaml"));
        XNamespace axaml = "https://github.com/avaloniaui";
        var editor = markup.Descendants(axaml + "Setter")
            .Single(e => (string?)e.Attribute("Property") == "GenericTemplate")
            .Descendants(axaml + "TextBox").Single();

        ((string?)editor.Attribute("ScrollViewer.VerticalScrollBarVisibility")).Should().Be("Disabled",
            "a single-line value must not move vertically inside the 16-pixel property row (#524)");
    }
}
