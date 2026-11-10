using System;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using AvaloniaEdit.Highlighting;
using HexIDE.IDE;
using HexIDE.Themes;

namespace HexIDE.Integration.Tests.Controls;

public sealed class HighContrastSyntaxTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void EverySyntaxColourMeetsAaa(bool preview)
    {
        var themes = new ThemeService();
        SyntaxHighlightingTheme.Configure(Substitute.For<ISettingsService>(), themes);
        try
        {
            foreach (var id in new[] { "HighContrastLight", "HighContrastDark" })
            {
                themes.Apply(id);
                var definition = preview ? SyntaxHighlightingTheme.CreatePreviewDefinition() : SyntaxHighlightingTheme.Definition;
                var background = id.EndsWith("Dark", StringComparison.Ordinal) ? Colors.Black : Colors.White;
                foreach (var color in definition.NamedHighlightingColors)
                    SyntaxHighlightingTheme.ContrastRatio(color.Foreground!.GetColor(null!)!.Value, background)
                        .Should().BeGreaterThanOrEqualTo(7, $"{id}: {color.Name}");
            }
        }
        finally { themes.Apply("Classic"); }
    }

    [AvaloniaTheory]
    [InlineData("HighContrastLight")]
    [InlineData("HighContrastDark")]
    public void AdoptedSyntaxMeetsAaaAndRestoresItsOriginalColour(string id)
    {
        var themes = new ThemeService();
        SyntaxHighlightingTheme.Configure(Substitute.For<ISettingsService>(), themes);
        var original = Color.Parse("#808080");
        var color = new HighlightingColor { Name = "Token", Foreground = new SimpleHighlightingBrush(original) };
        var definition = Substitute.For<IHighlightingDefinition>();
        definition.NamedHighlightingColors.Returns(new[] { color });
        try
        {
            themes.Apply(id);
            SyntaxHighlightingTheme.Adopt(definition);
            var background = id.EndsWith("Dark", StringComparison.Ordinal) ? Colors.Black : Colors.White;
            SyntaxHighlightingTheme.ContrastRatio(color.Foreground.GetColor(null!)!.Value, background)
                .Should().BeGreaterThanOrEqualTo(7);
            themes.Apply("Classic");
            color.Foreground.GetColor(null!).Should().Be(original);
        }
        finally { themes.Apply("Classic"); }
    }
}
