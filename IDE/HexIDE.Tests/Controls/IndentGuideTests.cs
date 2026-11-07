using HexIDE.Controls;
using HexIDE.Forms.ViewModels.Options;
using HexIDE.IDE;

namespace HexIDE.Tests.Controls;

public class IndentGuideTests
{
    // Resolve the new surface so these tests also run (and fail) on the pre-feature assembly.
    private static bool GuideOption(object target)
    {
        var property = target.GetType().GetProperty("ShowIndentGuides");
        property.Should().NotBeNull();
        return (bool)property!.GetValue(target)!;
    }

    [Fact]
    public void NestedIndentation_HighlightsOnlyCaretBlock()
    {
        var guides = IndentGuideRenderer.GetGuides("Sub A()\n    One\n        Two\n    Three\nEnd Sub\nSub B()\n        Four".Split('\n'), 4, 3, true);
        guides.Where(g => g.Active).Select(g => (g.Line, g.Column, g.Active)).Should().Equal((3, 4, true));
        guides.Where(g => g.Line == 3).Select(g => g.Column).Should().Equal(0, 4);
        guides.Where(g => g.Line == 7).Should().OnlyContain(g => !g.Active);
    }

    [Fact]
    public void TabsAndBlankLines_UseSharedVisualIndentation()
    {
        var guides = IndentGuideRenderer.GetGuides("\tOne\n\n \tTwo\nEnd".Split('\n'), 4, 2, true);
        guides.Select(g => (g.Line, g.Column, g.Active)).Should().Equal((1, 0, true),
            (2, 0, true), (3, 0, true));
        IndentGuideRenderer.GetGuides("    One".Split('\n'), 4, 1, false).Should().BeEmpty();
    }

    [Fact]
    public void DisplayOption_LoadsSavesRestoresAndPersists()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            var settings = new SettingsService(path);
            var page = new EditorGeneralPageViewModel(settings);
            GuideOption(page).Should().BeTrue();
            page.GetType().GetProperty("ShowIndentGuides")!.SetValue(page, false);
            page.SaveToSettings();
            settings.Save();
            GuideOption(new SettingsService(path)).Should().BeFalse();
            page.RestoreDefaults();
            GuideOption(page).Should().BeTrue();
        }
        finally { File.Delete(path); }
    }
}
