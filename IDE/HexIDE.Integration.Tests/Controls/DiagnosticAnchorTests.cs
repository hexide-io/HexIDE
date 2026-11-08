using System.Linq;
using Avalonia.Headless.XUnit;
using AvaloniaEdit;
using HexIDE.Controls;

namespace HexIDE.Integration.Tests.Controls;

// Header growth and shrinkage reported in #512, before any replacement server response.
public class DiagnosticAnchorTests
{
    [AvaloniaTheory]
    [InlineData("short\n", "a longer\nheader\n")]
    [InlineData("a longer\nheader\n", "short\n")]
    public void DiagnosticsFollowHeaderReplacementBeforeAnotherPublication(string header, string replacement)
    {
        var editor = new TextEditor { Text = header + "bad token\n" };
        var service = new LspTextMarkerService(editor);
        var marker = new LspMarker(header.Length, header.Length + 3, true, "diagnostic", "server");
        service.SetMarkers([marker]);

        editor.Document.Replace(0, header.Length, replacement);

        service.Markers.Should().ContainSingle().Which.Should().Be(
            marker with { StartOffset = replacement.Length, EndOffset = replacement.Length + 3 });
    }

    [AvaloniaTheory]
    [InlineData("short\n", "a longer\nheader\n")]
    [InlineData("a longer\nheader\n", "short\n")]
    public void HighlightsFollowHeaderReplacementBeforeAnotherResponse(string header, string replacement)
    {
        var editor = new TextEditor { Text = header + "highlight token\n" };
        var renderer = new DocumentHighlightRenderer(editor);
        renderer.SetHighlights([(header.Length, header.Length + 9)]);

        editor.Document.Replace(0, header.Length, replacement);

        renderer.Segments.Select(s => (s.StartOffset, s.EndOffset)).Should()
            .Equal((replacement.Length, replacement.Length + 9));
    }

    [AvaloniaTheory]
    [InlineData("short\n", "a longer\nheader\n")]
    [InlineData("a longer\nheader\n", "short\n")]
    public void DiagnosticColourFollowsHeaderReplacement(string header, string replacement)
    {
        var editor = new TextEditor { Text = header + "bad token\n" };
        var view = editor.TextArea.TextView;
        var colorizer = new LspDiagnosticsColorizer(view);
        view.LineTransformers.Add(colorizer);
        colorizer.SetMarkers([new LspMarker(header.Length, header.Length + 3, true, "diagnostic")]);

        editor.Document.Replace(0, header.Length, replacement);

        var line = view.GetOrConstructVisualLine(editor.Document.GetLineByOffset(replacement.Length));
        var run = line.Elements.First(e => e.DocumentLength > 0).TextRunProperties;
        ((Avalonia.Media.ISolidColorBrush)run.ForegroundBrush!).Color.Should().Be(Avalonia.Media.Colors.Red);
    }
}
