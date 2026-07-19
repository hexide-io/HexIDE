using System.Linq;
using Avalonia;
using Avalonia.Headless.XUnit;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using HexIDE.Controls;

namespace HexIDE.Integration.Tests.Controls;

public class MinimapControlTests
{
    /// <summary>Build a laid-out TextEditor + attached MinimapControl with a document of the
    /// given line count, so the minimap's layout math and scroll behaviour can be exercised.</summary>
    private static (TextEditor Editor, MinimapControl Minimap) Build(
        int lineCount, double editorHeight, double minimapHeight)
    {
        var doc = new TextDocument();
        doc.Text = string.Join("\n", Enumerable.Range(1, lineCount).Select(i => $"    Line {i} content"));

        var editor = new TextEditor { Document = doc };
        editor.ApplyTemplate();
        editor.Measure(new Size(600, editorHeight));
        editor.Arrange(new Rect(0, 0, 600, editorHeight));

        var minimap = new MinimapControl();
        minimap.Attach(editor, null);
        minimap.Measure(new Size(110, minimapHeight));
        minimap.Arrange(new Rect(0, 0, 110, minimapHeight));

        return (editor, minimap);
    }

    [AvaloniaFact]
    public void LineMapping_NonSampled_RoundTripsExactly()
    {
        var (_, mm) = Build(lineCount: 40, editorHeight: 400, minimapHeight: 400);

        mm.MinimapRowCount.Should().Be(40);  // step == 1, every line gets its own row
        mm.MinimapYForLine(1).Should().Be(0);

        foreach (var line in new[] { 1, 5, 20, 40 })
        {
            var y = mm.MinimapYForLine(line);
            mm.MinimapLineForY(y).Should().Be(line);  // exact round-trip when not sampling
        }
    }

    [AvaloniaFact]
    public void LineMapping_LargeFile_SamplesAndStaysBounded()
    {
        const int lineCount = 5000;
        const double height = 300;
        var (_, mm) = Build(lineCount, editorHeight: height, minimapHeight: height);

        // Sampling engaged: far fewer rows than lines, bounded by height / MinLineHeight (1.5).
        mm.MinimapRowCount.Should().BeLessThan(lineCount);
        mm.MinimapRowCount.Should().BeLessThanOrEqualTo((int)(height / 1.5) + 1);

        // The whole file maps within the control height.
        mm.MinimapYForLine(1).Should().Be(0);
        mm.MinimapYForLine(lineCount).Should().BeLessThanOrEqualTo(height);

        // Mapping is idempotent on row representatives (y -> line -> y -> line is stable).
        foreach (var y in new[] { 0.0, height * 0.25, height * 0.5, height * 0.9 })
        {
            var line = mm.MinimapLineForY(y);
            line.Should().BeInRange(1, lineCount);
            mm.MinimapLineForY(mm.MinimapYForLine(line)).Should().Be(line);
        }
    }

    [AvaloniaFact]
    public void ClickToScroll_TargetsLineUnderPointer_AndScrollPathRuns()
    {
        const double height = 400;
        var (_, mm) = Build(lineCount: 500, editorHeight: height, minimapHeight: height);

        // The minimap owns the click → document-line mapping: the top targets the first line,
        // the bottom the last, strictly increasing in between. (The editor scroll itself is
        // AvaloniaEdit's ScrollToVerticalOffset; a headless text layout has no real extent, so
        // the actual pixel scroll can't be asserted here — that's exercised live in the IDE.)
        mm.MinimapLineForY(0).Should().Be(1);
        mm.MinimapLineForY(height).Should().BeGreaterThan(400).And.BeLessThanOrEqualTo(500);
        mm.MinimapLineForY(height * 0.25).Should().BeLessThan(mm.MinimapLineForY(height * 0.75));

        // The full click→scroll path (LineForY → GetVisualTopByDocumentLine → ScrollToVerticalOffset)
        // runs end-to-end for any pointer position without throwing.
        var act = () => mm.ScrollEditorToMinimapY(height * 0.9);
        act.Should().NotThrow();
    }
}
