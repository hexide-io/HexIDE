using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using HexIDE.IDE;

namespace HexIDE.Controls;

internal sealed class IndentGuideRenderer : IBackgroundRenderer, IDisposable
{
    private readonly TextEditor _editor;
    private readonly ISettingsService _settings;
    public KnownLayer Layer => KnownLayer.Background;

    public IndentGuideRenderer(TextEditor editor, ISettingsService settings)
    {
        _editor = editor;
        _settings = settings;
        editor.TextArea.Caret.PositionChanged += Redraw;
        settings.PropertyChanged += SettingsChanged;
    }

    private void Redraw(object? sender, EventArgs e) => _editor.TextArea.TextView.InvalidateLayer(Layer);
    private void SettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ISettingsService.TabWidth))
            _editor.Options.IndentationSize = _settings.TabWidth;
        if (e.PropertyName is nameof(ISettingsService.ShowIndentGuides) or nameof(ISettingsService.TabWidth) or nameof(ISettingsService.ActiveTheme))
            Redraw(sender, e);
    }

    internal readonly record struct Guide(int Line, int Column, bool Active);

    internal static IReadOnlyList<Guide> GetGuides(TextDocument document, int tabWidth, int caretLine, bool enabled)
    {
        var result = new List<Guide>();
        if (!enabled) return result;
        tabWidth = Math.Max(1, tabWidth);
        var indents = new int[document.LineCount];
        var blank = new bool[document.LineCount];
        foreach (var line in document.Lines)
        {
            var text = document.GetText(line);
            int column = 0, index = 0;
            for (; index < text.Length; index++)
            {
                if (text[index] == ' ') column++;
                else if (text[index] == '\t') column += tabWidth - column % tabWidth;
                else break;
            }
            indents[line.LineNumber - 1] = column;
            blank[line.LineNumber - 1] = index == text.Length;
        }
        // Empty lines continue only the indentation shared by their surrounding code.
        int previous = 0;
        for (int i = 0; i < indents.Length; i++)
            if (blank[i]) indents[i] = previous;
            else previous = indents[i];
        int next = 0;
        for (int i = indents.Length - 1; i >= 0; i--)
            if (blank[i]) indents[i] = Math.Min(indents[i], next);
            else next = indents[i];

        int caret = Math.Clamp(caretLine - 1, 0, indents.Length - 1);
        int active = indents[caret] / tabWidth * tabWidth;
        int start = caret, end = caret;
        if (active > 0)
        {
            while (start > 0 && indents[start - 1] >= active) start--;
            while (end + 1 < indents.Length && indents[end + 1] >= active) end++;
        }
        for (int i = 0; i < indents.Length; i++)
            for (int level = tabWidth; level <= indents[i]; level += tabWidth)
                result.Add(new Guide(i + 1, level - tabWidth, level == active && i >= start && i <= end));
        return result;
    }

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (!textView.VisualLinesValid || !_settings.ShowIndentGuides) return;
        var guides = GetGuides(_editor.Document, _settings.TabWidth, _editor.TextArea.Caret.Line, true);
        var byLine = guides.ToLookup(g => g.Line);
        var normal = _editor.FindResource("IndentGuideBrush") as IBrush;
        var active = _editor.FindResource("ActiveIndentGuideBrush") as IBrush;
        foreach (var line in textView.VisualLines)
        {
            foreach (var guide in byLine[line.FirstDocumentLine.LineNumber])
            {
                double x = guide.Column * textView.WideSpaceWidth - textView.HorizontalOffset;
                double y = line.VisualTop - textView.VerticalOffset;
                if (x < 0) continue;
                drawingContext.DrawLine(new Pen(guide.Active ? active : normal, 1),
                    new Point(x + 0.5, y), new Point(x + 0.5, y + line.Height));
            }
        }
    }

    public void Dispose()
    {
        _editor.TextArea.Caret.PositionChanged -= Redraw;
        _settings.PropertyChanged -= SettingsChanged;
        _editor.TextArea.TextView.BackgroundRenderers.Remove(this);
    }
}
