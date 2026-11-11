using System.Text.Json;
using System.Xml.Linq;

namespace HexIDE.Tests.Themes;

public sealed class ChromeBorderTests
{
    private static string Root()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "IDE", "HexIDE", "App.axaml"))) return dir.FullName;
        throw new DirectoryNotFoundException();
    }

    [Fact]
    public void SharedStylesCoverFocusAndSelectionAcrossChrome()
    {
        var path = Path.Combine(Root(), "IDE", "HexIDE", "Themes", "ChromeBorders.axaml");
        File.Exists(path).Should().BeTrue("#3641 requires shared focus and selection styles");
        var styles = XDocument.Load(path);
        var selectors = styles.Descendants().Where(e => e.Name.LocalName == "Style")
            .Select(e => (string?)e.Attribute("Selector")).ToArray();
        foreach (var selector in new[] { ":focus", "ListBoxItem", "TreeViewItem", "TabItem", "MenuItem", "dock|DocumentTabStripItem", "dock|ToolTabStripItem" })
            selectors.Should().Contain(s => s != null && s.Contains(selector, StringComparison.Ordinal));
        styles.Descendants().Attributes("Value").Select(a => a.Value)
            .Should().OnlyContain(value => value == "Transparent" || value.StartsWith("{DynamicResource", StringComparison.Ordinal));
        selectors.Should().NotContain(s => s != null && s.Contains("HighContrast", StringComparison.Ordinal));
        var app = File.ReadAllText(Path.Combine(Root(), "IDE", "HexIDE", "App.axaml"));
        app.Should().NotContain("Themes/ChromeBorders.axaml");
    }

    [Theory]
    [InlineData("HighContrastLight")]
    [InlineData("HighContrastDark")]
    public void HighContrastPacksEnableTwoPixelBorders(string id)
    {
        using var pack = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root(), "IDE", "HexIDE", "Themes", "Packs", id + ".json")));
        pack.RootElement.TryGetProperty("chromeBorderThickness", out var thickness).Should().BeTrue();
        thickness.GetDouble().Should().Be(2);
        foreach (var path in Directory.EnumerateFiles(Path.Combine(Root(), "IDE", "HexIDE", "Themes", "Packs"), "*.json")
                     .Where(p => !Path.GetFileName(p).StartsWith("HighContrast", StringComparison.Ordinal)))
        {
            using var other = JsonDocument.Parse(File.ReadAllText(path));
            other.RootElement.TryGetProperty("chromeBorderThickness", out _).Should().BeFalse(path);
        }
    }
}
