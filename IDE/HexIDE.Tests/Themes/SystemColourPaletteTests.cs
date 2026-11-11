using System.Xml.Linq;
using HexIDE.Tests.Infrastructure;

namespace HexIDE.Tests.Themes;

/// <summary>
/// The runtime defines VB6's system colours for a program it runs outside the IDE (#3695), and the IDE's Classic theme
/// defines the same keys for itself. A program should look the same made as it does run in the IDE under Classic, so
/// the two tables must agree until system colours have one source of their own (#806).
/// </summary>
public class SystemColourPaletteTests
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void The_runtime_s_system_colours_are_the_Classic_theme_s()
    {
        var runtime = Palette("HexIDE.Runtime", "BuiltinControls", "SystemColors.axaml");
        var classic = Palette("HexIDE", "Themes", "Classic.axaml");

        runtime.Should().HaveCount(25, "VB6 has 25 system colours, and each has a key of its own");
        runtime.Should().Equal(classic);
    }

    // Each SystemColors brush key a dictionary defines, by name, with its colour as written.
    private static SortedDictionary<string, string> Palette(params string[] path)
    {
        var dictionary = XDocument.Load(Path.Join([RepoTree.Root(), "IDE", .. path]));
        var palette = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var brush in dictionary.Descendants().Where(element => element.Name.LocalName == "SolidColorBrush"))
        {
            var key = (string?)brush.Attribute(Xaml + "Key");
            if (key is null || !key.Contains("SystemColors."))
                continue;
            palette.Add(key[(key.IndexOf("SystemColors.", StringComparison.Ordinal) + "SystemColors.".Length)..].TrimEnd('}'),
                ((string)brush.Attribute("Color")!).ToUpperInvariant());
        }
        return palette;
    }
}
