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

    [Fact]
    public void A_colour_value_disables_vertical_scrolling_too()
    {
        // BackColor, ForeColor and the rest draw through PropertyColorBox's own template, which a live check found
        // still drawing a vertical scroll bar inside the row after the shared editor was fixed.
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !(Directory.Exists(Path.Combine(root.FullName, "IDE"))
                                 && Directory.Exists(Path.Combine(root.FullName, "LspServer"))))
            root = root.Parent;

        var markup = XDocument.Load(Path.Combine(root!.FullName, "IDE", "HexIDE", "Controls", "Properties", "PropertyBox.axaml"));
        XNamespace axaml = "https://github.com/avaloniaui";
        var colourBox = markup.Descendants(axaml + "ControlTheme")
            .Single(e => ((string?)e.Attribute("TargetType"))?.EndsWith("PropertyColorBox", StringComparison.Ordinal) == true)
            .Descendants(axaml + "TextBox")
            .Single(e => ((string?)e.Attribute("Text"))?.Contains("VBColorConverter", StringComparison.Ordinal) == true);

        ((string?)colourBox.Attribute("ScrollViewer.VerticalScrollBarVisibility")).Should().Be("Disabled",
            "a colour's value must not move vertically inside its row either (#524)");
    }
}
