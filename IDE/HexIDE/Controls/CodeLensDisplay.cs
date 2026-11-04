using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using HexIDE.Lsp;
using HexIDE.Lsp.Messages;

namespace HexIDE.Controls;

/// <summary>Visual space and clickable labels, with no document characters or caret stops.</summary>
internal sealed class CodeLensDisplay : VisualLineElementGenerator, IDisposable
{
    private readonly TextEditor editor;
    private readonly CodeLensSession session;
    private readonly Action<Command> execute;
    private readonly Canvas layer;
    private CodeLens[] lenses = [];
    private bool disposed;
    private CodeLens[] shown = [];

    public CodeLensDisplay(TextEditor editor, Canvas layer, CodeLensSession session, Action<Command> execute)
    {
        this.editor = editor;
        this.layer = layer;
        this.session = session;
        this.execute = execute;
        var view = editor.TextArea.TextView;
        view.ElementGenerators.Insert(0, this);
        view.LayoutUpdated += OnVisualLinesChanged;
        view.VisualLinesChanged += OnVisualLinesChanged;
        view.ScrollOffsetChanged += OnVisualLinesChanged;
        session.Changed += OnChanged;
        session.RequestFailed += OnFailed;
        session.RefreshAsync().ListenErrors();
    }

    private void OnFailed(object? sender, Exception error) => Serilog.Log.Warning(error, "Code lens request failed");

    private void OnChanged(object? sender, EventArgs args) => Dispatcher.UIThread.Post(() =>
    {
        if (disposed) return;
        lenses = session.Lenses;
        editor.TextArea.TextView.Redraw();
    });

    private void OnVisualLinesChanged(object? sender, EventArgs args)
    {
        var view = editor.TextArea.TextView;
        if (disposed || !view.VisualLinesValid) return;
        var visible = view.VisualLines.Where(l => l.VisualTop + l.Height >= view.VerticalOffset
            && l.VisualTop <= view.VerticalOffset + view.Bounds.Height).ToArray();
        session.SetViewportAsync(visible.Length == 0 ? -1 : visible[0].FirstDocumentLine.LineNumber - 1,
            visible.Length == 0 ? -1 : visible[^1].LastDocumentLine.LineNumber - 1).ListenErrors();
        var origin = view.TranslatePoint(default, layer) ?? default;
        layer.Clip = new Avalonia.Media.RectangleGeometry(new Rect(origin, view.Bounds.Size));
        var placements = visible.SelectMany(line => OnLine(line.FirstDocumentLine.LineNumber)
            .Select((lens, row) => (Lens: lens, Top: origin.Y + line.VisualTop - view.VerticalOffset + row * RowHeight))).ToArray();
        var changed = !shown.SequenceEqual(placements.Select(p => p.Lens));
        if (changed)
        {
            layer.Children.Clear();
            shown = placements.Select(p => p.Lens).ToArray();
        }
        for (var i = 0; i < placements.Length; i++)
        {
            var (lens, top) = placements[i];
            var command = lens.Command!;
            Button button;
            if (changed)
            {
                button = new Button
                {
                    Content = command.Title, FontFamily = editor.FontFamily,
                    FontSize = editor.FontSize * .8, Foreground = editor.Foreground,
                    Opacity = .65, Background = Avalonia.Media.Brushes.Transparent,
                    BorderThickness = new Thickness(0), Padding = new Thickness(0),
                    Height = RowHeight, MinHeight = 0, Cursor = new Cursor(StandardCursorType.Hand),
                    Focusable = false
                };
                button.Classes.Add("codeLens");
                button.Click += (_, _) => execute(command);
                layer.Children.Add(button);
            }
            else button = (Button)layer.Children[i];
            button.FontFamily = editor.FontFamily;
            button.FontSize = editor.FontSize * .8;
            button.Foreground = editor.Foreground;
            button.Height = RowHeight;
            Canvas.SetLeft(button, origin.X - view.HorizontalOffset);
            Canvas.SetTop(button, top);
        }
    }

    private double RowHeight => editor.FontSize * 1.2;
    private CodeLens[] OnLine(int line) => lenses.Where(l => l.Range.Start.Line == line - 1 && l.Command is not null).ToArray();

    public override int GetFirstInterestedOffset(int startOffset)
    {
        var line = CurrentContext.VisualLine.FirstDocumentLine;
        return startOffset == line.Offset && OnLine(line.LineNumber).Length > 0 ? startOffset : -1;
    }

    public override VisualLineElement? ConstructElement(int offset)
    {
        var count = OnLine(CurrentContext.VisualLine.FirstDocumentLine.LineNumber).Length;
        return count == 0 ? null : new Spacer(editor.TextArea.TextView.DefaultBaseline + count * RowHeight);
    }

    private sealed class Spacer : InlineObjectElement
    {
        public Spacer(double height) : base(0, new Border { Width = 0, Height = height })
        {
            // InlineObjectRun reads this attached property; Border's default puts the space below the baseline.
            TextBlock.SetBaselineOffset(Element, height);
        }

        public override int GetNextCaretPosition(int visualColumn, LogicalDirection direction, CaretPositioningMode mode) => -1;
    }

    public void Dispose()
    {
        disposed = true;
        session.Changed -= OnChanged;
        session.RequestFailed -= OnFailed;
        session.Dispose();
        var view = editor.TextArea.TextView;
        view.VisualLinesChanged -= OnVisualLinesChanged;
        view.ScrollOffsetChanged -= OnVisualLinesChanged;
        view.ElementGenerators.Remove(this);
        view.LayoutUpdated -= OnVisualLinesChanged;
        layer.Children.Clear();
    }
}
