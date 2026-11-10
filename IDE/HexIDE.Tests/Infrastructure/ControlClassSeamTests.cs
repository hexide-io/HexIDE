using System.Text.RegularExpressions;

namespace HexIDE.Tests.Infrastructure;

/// <summary>The IDE's lists of control classes are the registry's (#3601).</summary>
public class ControlClassSeamTests
{
    [Fact]
    public void Only_the_registry_names_the_intrinsic_controls() =>
        ProductSource.FilesMatching(
                new Regex(@"\b(PictureBox|Label|TextBox|Frame|CommandButton|CheckBox|OptionButton|ComboBox|ListBox|HScrollBar|VScrollBar|Timer|Shape|Image)ComponentClass\b"),
                "ComponentRegistry.cs")
            .Should().BeEmpty("the toolbox offers what the registry lists, so a provider's classes can take their place");

    [Fact]
    public void Only_the_registry_reads_the_control_libraries() =>
        ProductSource.FilesMatching(new Regex(@"\bControlLibraries\b|\bInBoxComponentClass\s*\.\s*Implemented\b"), "ComponentRegistry.cs")
            .Should().BeEmpty("the toolbox, the Components dialog and add_control offer the libraries the registry lists");
}
