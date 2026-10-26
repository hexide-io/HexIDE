using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.Folding;
using HexIDE.IDE;

namespace HexIDE.Controls;

/// <summary>Pins the server's enclosing fold headers, without including the editor's serialized metadata folds.</summary>
internal sealed class StickyScrollController : IDisposable
{
    private readonly StackPanel _headers;
    private readonly TextEditor _editor;
    private readonly ISettingsService _settings;
    private readonly Func<IEnumerable<FoldingSection>> _folds;
    private (int Line, string Text)[] _shown = [];

    public StickyScrollController(StackPanel headers, TextEditor editor, ISettingsService settings,
        Func<IEnumerable<FoldingSection>> folds)
    {
        _headers = headers;
        _editor = editor;
        _settings = settings;
        _folds = folds;
        editor.TextArea.TextView.VisualLinesChanged += OnChanged;
        editor.TextArea.TextView.ScrollOffsetChanged += OnChanged;
        editor.TextChanged += OnChanged;
        settings.PropertyChanged += OnSettingsChanged;
    }

    private void OnChanged(object? sender, EventArgs e) => Refresh();
    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ISettingsService.IsStickyScrollEnabled) or nameof(ISettingsService.StickyScrollDepth))
            Refresh();
    }

    public void Refresh()
    {
        var view = _editor.TextArea.TextView;
        if (!view.VisualLinesValid) return;
        var top = view.VisualLines.FirstOrDefault()?.FirstDocumentLine.LineNumber ?? 1;
        var document = _editor.Document;
        var headers = new List<(int Line, string Text)>();
        if (_settings.IsStickyScrollEnabled)
        {
            foreach (var fold in _folds())
            {
                var first = document.GetLineByOffset(fold.StartOffset);
                var last = document.GetLineByOffset(fold.EndOffset);
                if (first.LineNumber >= top || last.LineNumber < top || fold.IsFolded) continue;
                if (headers.Any(h => h.Line == first.LineNumber)) continue;
                headers.Add((first.LineNumber, document.GetText(first)));
                if (headers.Count >= Math.Clamp(_settings.StickyScrollDepth, 1, 10)) break;
            }
        }
        if (_shown.SequenceEqual(headers)) return;
        _shown = headers.ToArray();
        _headers.Children.Clear();
        foreach (var header in headers)
        {
            var button = new Button
            {
                Content = new TextBlock { Text = header.Text, TextTrimming = TextTrimming.CharacterEllipsis },
                FontFamily = _editor.FontFamily,
                FontSize = _editor.FontSize,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(10, 1),
                MinHeight = 0
            };
            button.Click += (_, _) =>
            {
                _editor.TextArea.Caret.Line = header.Line;
                _editor.TextArea.Caret.Column = 1;
                _editor.ScrollToLine(header.Line);
                _editor.TextArea.Caret.BringCaretToView();
                _editor.TextArea.Focus();
            };
            _headers.Children.Add(button);
        }
    }

    public void Dispose()
    {
        _editor.TextArea.TextView.VisualLinesChanged -= OnChanged;
        _editor.TextArea.TextView.ScrollOffsetChanged -= OnChanged;
        _editor.TextChanged -= OnChanged;
        _settings.PropertyChanged -= OnSettingsChanged;
        _headers.Children.Clear();
    }
}
