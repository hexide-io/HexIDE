using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Input.Platform;
using AvaloniaEdit;
using AvaloniaEdit.Rendering;
using AvaloniaEdit.Folding;
using HexIDE.Controls;
using HexIDE.Lsp.Messages;

namespace HexIDE.Integration.Tests.Controls;

public class InlayHintTests
{
    // RDCore wire shape measured in #4253. These are protocol positions, not inferred VB6 semantics.
    private const string Source = "    total = AddUp(1, 2)";
    private static InlayHint Hint(int character, string label, bool left = false, bool right = false) =>
        JsonSerializer.Deserialize<InlayHint>($$"""
        {"position":{"line":0,"character":{{character}}},"label":"{{label}}","kind":2,
         "paddingLeft":{{left.ToString().ToLowerInvariant()}},"paddingRight":{{right.ToString().ToLowerInvariant()}}}
        """)!;

    [AvaloniaFact]
    public async Task ArgumentLabelsOccupyTheirReportedVisualPositions()
    {
        var editor = new TextEditor { Text = Source, FontSize = 14 };
        var window = new Window { Content = editor, Width = 700, Height = 250 };
        window.SetRenderScaling(1.5);
        window.Show();
        try
        {
            var view = editor.TextArea.TextView;
            var renderer = new InlayHintRenderer(editor);
            view.ElementGenerators.Add(renderer);
            renderer.SetHints([Hint(18, "[first:]"), Hint(21, "[second:]")]);
            view.EnsureVisualLines();
            var line = view.VisualLines.Single();
            var elements = line.Elements.OfType<InlineObjectElement>().ToArray();
            elements.Should().HaveCount(2);
            elements.Select(e => line.GetRelativeOffset(e.VisualColumn)).Should().Equal(18, 21);
            elements.SelectMany(e => ((StackPanel)e.Element).Children.Cast<TextBlock>())
                .Select(b => b.Text).Should().Equal("[first:]", "[second:]");
            foreach (var element in elements)
            {
                var before = line.GetVisualPosition(element.VisualColumn, VisualYPosition.TextTop).X;
                var after = line.GetVisualPosition(element.VisualColumn + element.VisualLength, VisualYPosition.TextTop).X;
                after.Should().BeGreaterThan(before, "the hint occupies space before the next document character");
                after.Should().BeLessThan(line.GetVisualPosition(line.VisualLength, VisualYPosition.TextTop).X);
            }
            editor.TextArea.Focus().Should().BeTrue();
            editor.CaretOffset = 17;
            window.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null);
            editor.CaretOffset.Should().Be(18);
            window.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null);
            editor.CaretOffset.Should().Be(19);
            window.KeyPress(Key.Left, RawInputModifiers.None, PhysicalKey.ArrowLeft, null);
            editor.CaretOffset.Should().Be(18);
            editor.SelectAll();
            editor.SelectedText.Should().Be(Source);
            editor.Copy();
            (await window.Clipboard!.TryGetTextAsync()).Should().Be(Source);
            editor.Document.Text.Should().Be(Source);
            editor.Document.UndoStack.CanUndo.Should().BeFalse();
            renderer.SetHints([]);
            view.EnsureVisualLines();
            view.VisualLines.Single().Elements.OfType<InlineObjectElement>().Should().BeEmpty();
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void PaddingAddsOnlyTheRequestedGaps(bool left, bool right)
    {
        var editor = new TextEditor { Text = Source, FontSize = 14 };
        var window = new Window { Content = editor, Width = 700, Height = 250 };
        window.Show();
        try
        {
            var view = editor.TextArea.TextView;
            var renderer = new InlayHintRenderer(editor);
            view.ElementGenerators.Add(renderer);
            renderer.SetHints([Hint(18, "[first:]")]);
            view.EnsureVisualLines();
            var unpaddedWidth = Width(view.VisualLines.Single());
            renderer.SetHints([Hint(18, "[first:]", left, right)]);
            view.EnsureVisualLines();
            var element = view.VisualLines.Single().Elements.OfType<InlineObjectElement>().Should().ContainSingle().Subject;
            var label = ((StackPanel)element.Element).Children.Cast<TextBlock>().Single();
            if (left) label.Margin.Left.Should().BeGreaterThan(0).And.BeLessThan(editor.FontSize);
            else label.Margin.Left.Should().Be(0);
            if (right) label.Margin.Right.Should().BeGreaterThan(0).And.BeLessThan(editor.FontSize);
            else label.Margin.Right.Should().Be(0);
            if (left || right) Width(view.VisualLines.Single()).Should().BeGreaterThan(unpaddedWidth);
            else Width(view.VisualLines.Single()).Should().Be(unpaddedWidth);
            editor.Document.Text.Should().Be(Source);
        }
        finally { window.Close(); }
    }

    private static double Width(VisualLine line)
    {
        var element = line.Elements.OfType<InlineObjectElement>().Single();
        return line.GetVisualPosition(element.VisualColumn + element.VisualLength, VisualYPosition.TextTop).X
            - line.GetVisualPosition(element.VisualColumn, VisualYPosition.TextTop).X;
    }

    [AvaloniaTheory]
    [InlineData("\"' Sub Foo\"")]
    [InlineData("[{\"value\":\"' Sub \"},{\"value\":\"Foo\"}]")]
    public void EndLabelsStayAtTheLineEndAndDisappearWhenFolded(string label)
    {
        var editor = new TextEditor { Text = "Sub Foo()\nEnd Sub\nx", FontSize = 14 };
        var window = new Window { Content = editor, Width = 600, Height = 250 };
        window.Show();
        try
        {
            var view = editor.TextArea.TextView;
            var renderer = new InlayHintRenderer(editor);
            view.ElementGenerators.Add(renderer);
            renderer.SetHints([new InlayHint(new Position(1, 7), JsonDocument.Parse(label).RootElement.Clone())]);
            view.EnsureVisualLines();
            var line = view.VisualLines[1];
            var element = line.Elements.OfType<InlineObjectElement>().Should().ContainSingle().Subject;
            line.GetRelativeOffset(element.VisualColumn).Should().Be(7);
            ((StackPanel)element.Element).Children.Cast<TextBlock>().Single().Text.Should().Be("' Sub Foo");
            editor.TextArea.Focus().Should().BeTrue();
            var closing = editor.Document.GetLineByNumber(2);
            editor.CaretOffset = closing.EndOffset - 1;
            window.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null);
            editor.CaretOffset.Should().Be(closing.EndOffset);
            window.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null);
            editor.CaretOffset.Should().Be(closing.NextLine!.Offset);
            window.KeyPress(Key.Left, RawInputModifiers.None, PhysicalKey.ArrowLeft, null);
            editor.CaretOffset.Should().Be(closing.EndOffset);
            window.KeyPress(Key.Left, RawInputModifiers.None, PhysicalKey.ArrowLeft, null);
            editor.CaretOffset.Should().Be(closing.EndOffset - 1);
            editor.CaretOffset = 0;
            var folding = FoldingManager.Install(editor.TextArea);
            folding.CreateFolding(0, closing.EndOffset).IsFolded = true;
            view.EnsureVisualLines();
            view.VisualLines.SelectMany(l => l.Elements).OfType<InlineObjectElement>().Should().BeEmpty();
            FoldingManager.Uninstall(folding);
            renderer.SetHints([]);
            view.EnsureVisualLines();
            view.VisualLines.SelectMany(l => l.Elements).OfType<InlineObjectElement>().Should().BeEmpty();
        }
        finally { window.Close(); }
    }
}
