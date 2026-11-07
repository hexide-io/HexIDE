using System.Xml.Linq;

namespace HexIDE.Tests.Themes;

public class IconGeometryTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "openspec")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Could not locate repository root.");
    }

    [Fact]
    public void SpecAllowsAttributedPermissiveIconSetsAndRequiresTintablePaths()
    {
        var spec = File.ReadAllText(Path.Combine(RepoRoot(), "openspec", "specs", "icon-system", "spec.md"))
            .ReplaceLineEndings("\n");
        spec.Should().NotContain("original vector geometries");
        spec.Should().Contain("Third-party icon sets MAY be used");
        spec.Should().Contain("THIRD-PARTY-NOTICES.md");
        spec.Should().Contain("single path geometry with no fill");
        spec.Should().Contain("SHALL NOT ship artwork extracted from another application");
    }

    [Fact]
    public void ShippedIconsAreSingleUncolouredKeyedPaths()
    {
        XNamespace avalonia = "https://github.com/avaloniaui";
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        var document = XDocument.Load(Path.Combine(RepoRoot(), "IDE", "HexIDE", "Themes", "IconGeometry.axaml"));
        var icons = document.Descendants()
            .Where(e => ((string?)e.Attribute(xaml + "Key"))?.StartsWith("Geo.", StringComparison.Ordinal) == true)
            .ToList();
        icons.Should().NotBeEmpty();
        foreach (var icon in icons)
        {
            var key = (string)icon.Attribute(xaml + "Key")!;
            icon.Name.Should().Be(avalonia + "StreamGeometry", $"{key} must be a single path, not a drawing or group");
            icon.Elements().Should().BeEmpty($"{key} must not contain multiple geometries or brushes");
            icon.Attributes().Where(a => a.Name != xaml + "Key").Should().BeEmpty(
                $"{key} carries only path data; the host supplies its fill");
            icon.Value.Should().NotBeNullOrWhiteSpace($"{key} must contain path data");
        }
    }
}
