using AvaloniaEdit.Document;
using System.Collections;
using System.Reflection;
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

    private static IReadOnlyList<(int Line, int Column, bool Active)> GetGuides(
        TextDocument document, int tabWidth, int caretLine, bool enabled)
    {
        var type = typeof(SettingsService).Assembly.GetType("HexIDE.Controls.IndentGuideRenderer");
        type.Should().NotBeNull();
        var method = type!.GetMethod("GetGuides", BindingFlags.Static | BindingFlags.NonPublic)!;
        var result = (IEnumerable)method.Invoke(null, [document, tabWidth, caretLine, enabled])!;
        return result.Cast<object>().Select(guide => (
            (int)guide.GetType().GetProperty("Line")!.GetValue(guide)!,
            (int)guide.GetType().GetProperty("Column")!.GetValue(guide)!,
            (bool)guide.GetType().GetProperty("Active")!.GetValue(guide)!
        )).ToArray();
    }

    [Fact]
    public void NestedIndentation_HighlightsOnlyCaretBlock()
    {
        var guides = GetGuides(new TextDocument("Sub A()\n    One\n        Two\n    Three\nEnd Sub\nSub B()\n        Four"), 4, 3, true);
        guides.Where(g => g.Active).Should().Equal((3, 4, true));
        guides.Where(g => g.Line == 3).Select(g => g.Column).Should().Equal(0, 4);
        guides.Where(g => g.Line == 7).Should().OnlyContain(g => !g.Active);
    }

    [Fact]
    public void TabsAndBlankLines_UseSharedVisualIndentation()
    {
        var guides = GetGuides(new TextDocument("\tOne\n\n \tTwo\nEnd"), 4, 2, true);
        guides.Should().Equal((1, 0, true),
            (2, 0, true), (3, 0, true));
        GetGuides(new TextDocument("    One"), 4, 1, false).Should().BeEmpty();
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
