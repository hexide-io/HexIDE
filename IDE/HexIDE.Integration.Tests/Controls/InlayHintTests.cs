using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using AvaloniaEdit;
using AvaloniaEdit.Rendering;
using AvaloniaEdit.Folding;
using HexIDE.Controls;
using HexIDE.Lsp.Messages;

namespace HexIDE.Integration.Tests.Controls;

public class InlayHintTests
{
    [AvaloniaTheory]
    [InlineData("\"' Sub Foo\"")]
    [InlineData("[{\"value\":\"' Sub \"},{\"value\":\"Foo\"}]")]
    public void LabelsPaintAfterTheLineWithoutMovingTheCaret(string label)
    {
        var editor = new TextEditor { Text = "Sub Foo()\nEnd Sub", FontSize = 14 };
        var window = new Window { Content = editor, Width = 600, Height = 250 };
        window.Show();
        try
        {
            editor.TextArea.TextView.EnsureVisualLines();
            editor.TextArea.Caret.Offset = editor.Document.TextLength;
            var column = editor.TextArea.Caret.Column;
            var renderer = new InlayHintRenderer(editor);
            renderer.SetHints([new InlayHint(new Position(1, 7), JsonDocument.Parse(label).RootElement.Clone())]);
            var placement = renderer.Placements(editor.TextArea.TextView).Should().ContainSingle().Subject;
            var line = editor.TextArea.TextView.VisualLines.Last();
            var end = line.GetVisualPosition(line.VisualLength, VisualYPosition.TextTop);
            placement.Text.Should().Be("' Sub Foo");
            placement.Origin.X.Should().BeGreaterThan(end.X);
            placement.Origin.Y.Should().Be(end.Y);
            editor.TextArea.Caret.Column.Should().Be(column);
            editor.Document.Text.ReplaceLineEndings("\n").Should().Be("Sub Foo()\nEnd Sub");
            var folding = FoldingManager.Install(editor.TextArea);
            folding.CreateFolding(0, editor.Document.TextLength).IsFolded = true;
            editor.TextArea.TextView.EnsureVisualLines();
            renderer.Placements(editor.TextArea.TextView).Should().BeEmpty("the closing line is folded away");
            FoldingManager.Uninstall(folding);
            editor.TextArea.TextView.EnsureVisualLines();
            renderer.SetHints([]);
            renderer.Placements(editor.TextArea.TextView).Should().BeEmpty();
        }
        finally { window.Close(); }
    }
}
