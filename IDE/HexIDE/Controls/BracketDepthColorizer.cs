using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Reactive;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using HexIDE.IDE;

namespace HexIDE.Controls;

internal sealed class BracketDepthColorizer : DocumentColorizingTransformer, IDisposable
{
    private readonly TextEditor _editor;
    private readonly ISettingsService _settings;
    private readonly TextDocument _document;
    private readonly List<IDisposable> _subscriptions = [];
    private readonly IBrush?[] _brushes = new IBrush?[4];
    private IReadOnlyList<BracketDepth.Mark>? _marks;

    public BracketDepthColorizer(TextEditor editor, Control owner, ISettingsService settings)
    {
        _editor = editor;
        _settings = settings;
        _document = editor.Document;
        _document.TextChanged += OnTextChanged;
        settings.PropertyChanged += OnSettingsChanged;
        string[] keys = ["BracketDepth1Brush", "BracketDepth2Brush", "BracketDepth3Brush", "BracketUnmatchedBrush"];
        for (var i = 0; i < keys.Length; i++)
        {
            var index = i;
            _subscriptions.Add(owner.GetResourceObservable(keys[i]).Subscribe(new AnonymousObserver<object?>(value =>
            {
                _brushes[index] = value as IBrush;
                editor.TextArea.TextView.Redraw();
            })));
        }
    }

    private void OnTextChanged(object? sender, EventArgs e)
    {
        _marks = null;
        // An edit can change pairing on lines AvaloniaEdit would otherwise retain.
        _editor.TextArea.TextView.Redraw();
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ISettingsService.ColorBracketsByDepth))
            _editor.TextArea.TextView.Redraw();
    }

    protected override void ColorizeLine(DocumentLine line)
    {
        if (!_settings.ColorBracketsByDepth) return;
        _marks ??= BracketDepth.Scan(_document.Text);
        foreach (var mark in _marks)
        {
            if (mark.Offset >= line.EndOffset) break;
            if (mark.Offset < line.Offset) continue;
            var brush = _brushes[mark.Depth < 0 ? 3 : mark.Depth % 3];
            if (brush is not null)
                ChangeLinePart(mark.Offset, mark.Offset + 1, element => element.TextRunProperties.SetForegroundBrush(brush));
        }
    }

    public void Dispose()
    {
        _document.TextChanged -= OnTextChanged;
        _settings.PropertyChanged -= OnSettingsChanged;
        foreach (var subscription in _subscriptions) subscription.Dispose();
    }
}
