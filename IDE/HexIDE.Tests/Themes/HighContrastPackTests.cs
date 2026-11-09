using System.Text.Json;
using System.Xml.Linq;
using Avalonia.Media;
using HexIDE.Themes;

namespace HexIDE.Tests.Themes;

public sealed class HighContrastPackTests
{
    private static string ThemesDirectory()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var path = Path.Combine(dir.FullName, "IDE", "HexIDE", "Themes");
            if (Directory.Exists(path)) return path;
        }
        throw new DirectoryNotFoundException();
    }

    private static ThemePackRecord Pack(string id) => JsonSerializer.Deserialize(
        File.ReadAllText(Path.Combine(ThemesDirectory(), "Packs", id + ".json")),
        ThemeJsonContext.Default.ThemePackRecord)!;

    [Theory]
    [InlineData("HighContrastLight", "Light")]
    [InlineData("HighContrastDark", "Dark")]
    public void PacksCoverEveryClassicColour(string id, string variant)
    {
        var pack = Pack(id);
        pack.ThemeVariant.Should().Be(variant);
        var keys = XDocument.Load(Path.Combine(ThemesDirectory(), "Classic.axaml")).Root!.Elements()
            .Where(e => e.Name.LocalName is "Color" or "SolidColorBrush")
            .Select(e => (string)e.Attribute(XName.Get("Key", "http://schemas.microsoft.com/winfx/2006/xaml"))!);
        var mapped = pack.Colors!.Keys.Select(name => ColorKeyMapping.Table[name].Key).ToArray();
        foreach (var key in keys)
        {
            var resourceKey = key.StartsWith("{x:Static", StringComparison.Ordinal)
                ? typeof(Classic.CommonControls.SystemColors).GetProperty(key.Split('.')[1].TrimEnd('}'))!.GetValue(null)!
                : key;
            mapped.Should().Contain(resourceKey);
        }
    }

    [Theory]
    [InlineData("HighContrastLight")]
    [InlineData("HighContrastDark")]
    public void EveryTextBackgroundPairMeetsAaa(string id)
    {
        var colors = Pack(id).Colors!;
        (string Text, string[] Backgrounds)[] pairs =
        [
            ("WindowText", ["Window"]),
            ("LinkText", ["Window"]),
            ("ControlText", ["Control", "DockTitleBar"]),
            ("MenuText", ["Menu"]),
            ("ActiveCaptionText", ["ActiveTitleBar", "ActiveCaption"]),
            ("InactiveCaptionText", ["InactiveCaption", "InactiveTitleBar"]),
            ("HighlightText", ["Highlight"]),
            ("GrayText", ["Window", "Control", "Menu"]),
            ("InfoText", ["Info"]),
            ("WarningBannerForeground", ["WarningBannerBackground"]),
            ("ValidationError", ["Window", "Control"]),
            ("ReadOnlyText", ["Window"]),
            ("InactiveCodeText", ["Window"]),
            ("BracketDepth1", ["Window"]),
            ("BracketDepth2", ["Window"]),
            ("BracketDepth3", ["Window"]),
            ("BracketUnmatched", ["Window"])
        ];
        foreach (var (text, backgrounds) in pairs)
        foreach (var background in backgrounds)
            SyntaxHighlightingTheme.ContrastRatio(Color.Parse(colors[text]), Color.Parse(colors[background]))
                .Should().BeGreaterThanOrEqualTo(7, $"{id}: {text} on {background}");
    }
}
