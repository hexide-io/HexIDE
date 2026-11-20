using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using Avalonia.Input;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using HexIDE.Controls;
using HexIDE.IDE;
using HexIDE.Lsp.Messages;
using Serilog;

namespace HexIDE.Forms.Views;

internal sealed class LinkedEditingController : IDisposable
{
    private readonly TextEditor editor;
    private readonly ISettingsService settings;
    private readonly Func<bool> available;
    private readonly Func<Position, CancellationToken, Task<LinkedEditingRanges?>> request;
    private readonly Func<int, int, bool> refusesTyping;
    private readonly DocumentHighlightRenderer renderer;
    private CancellationTokenSource? pending;
    private (TextAnchor Start, TextAnchor End)[] ranges = [];
    private Regex? pattern;
    private int source = -1;
    private bool copying;
    private bool disposed;

    internal LinkedEditingController(TextEditor editor, ISettingsService settings, Func<bool> available,
        Func<Position, CancellationToken, Task<LinkedEditingRanges?>> request, Func<int, int, bool> refusesTyping)
    {
        this.editor = editor;
        this.settings = settings;
        this.available = available;
        this.request = request;
        this.refusesTyping = refusesTyping;
        renderer = new DocumentHighlightRenderer(editor);
        editor.TextArea.TextView.BackgroundRenderers.Add(renderer);
        editor.TextArea.Caret.PositionChanged += CaretMoved;
        editor.TextArea.AddHandler(Avalonia.Input.InputElement.KeyDownEvent, KeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
        editor.Document.Changing += Changing;
        editor.Document.TextChanged += TextChanged;
        settings.PropertyChanged += SettingsChanged;
    }

    private void SettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ISettingsService.LinkedEditing)) { End(); Schedule(); }
    }

    private void KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) End();
    }

    private int AtCaret() => Array.FindIndex(ranges, r =>
        editor.CaretOffset >= r.Start.Offset && editor.CaretOffset <= r.End.Offset);

    private void CaretMoved(object? sender, EventArgs e)
    {
        if (copying || source >= 0) return;
        if (ranges.Length > 0 && AtCaret() < 0) End();
        Schedule();
    }

    private void End()
    {
        pending?.Cancel();
        pending?.Dispose();
        pending = null;
        ranges = [];
        pattern = null;
        source = -1;
        renderer.Clear();
    }

    private void Schedule()
    {
        pending?.Cancel();
        pending?.Dispose();
        pending = null;
        if (disposed || !settings.LinkedEditing || !available() || !InWord()) return;
        pending = new CancellationTokenSource();
        _ = Ask(pending.Token);
    }

    private bool InWord()
    {
        var offset = editor.CaretOffset;
        var document = editor.Document;
        return offset > 0 && Word(document.GetCharAt(offset - 1))
            || offset < document.TextLength && Word(document.GetCharAt(offset));
        static bool Word(char c) => char.IsLetterOrDigit(c) || c == '_';
    }

    private async Task Ask(CancellationToken token)
    {
        try
        {
            var version = editor.Document.Version;
            var offset = editor.CaretOffset;
            await Task.Delay(500, token);
            var caret = editor.TextArea.Caret;
            var answer = await request(new Position(caret.Line - 1, caret.Column - 1), token);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (disposed || token.IsCancellationRequested || !settings.LinkedEditing || !available()
                    || version.CompareAge(editor.Document.Version) != 0 || editor.CaretOffset != offset) return;
                End();
                Accept(answer);
            });
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Log.Debug("[linked editing] {ErrorMessage}", ex.Message); }
    }

    private void Accept(LinkedEditingRanges? answer)
    {
        if (answer?.Ranges is not { Length: >= 2 } supplied) return;
        var document = editor.Document;
        var offsets = new List<(int Start, int End)>();
        foreach (var range in supplied)
        {
            if (!Offset(range.Start, out var start) || !Offset(range.End, out var end) || end <= start
                || refusesTyping(start, end - start)) return;
            offsets.Add((start, end));
        }
        offsets.Sort((a, b) => a.Start.CompareTo(b.Start));
        if (offsets.Zip(offsets.Skip(1)).Any(pair => pair.First.End >= pair.Second.Start)
            || !offsets.Any(r => editor.CaretOffset >= r.Start && editor.CaretOffset <= r.End)) return;
        var text = document.GetText(offsets[0].Start, offsets[0].End - offsets[0].Start);
        if (offsets.Any(r => document.GetText(r.Start, r.End - r.Start) != text)) return;
        try
        {
            pattern = answer.WordPattern is { } wordPattern
                ? new Regex("\\A(?:" + wordPattern + ")\\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)) : null;
            if (pattern is not null && !pattern.IsMatch(text)) { pattern = null; return; }
        }
        catch (ArgumentException) { pattern = null; return; }
        ranges = offsets.Select(r => (Anchor(r.Start, AnchorMovementType.BeforeInsertion),
            Anchor(r.End, AnchorMovementType.AfterInsertion))).ToArray();
        Mark();

        bool Offset(Position position, out int value)
        {
            value = 0;
            if (position.Line < 0 || position.Line >= document.LineCount || position.Character < 0) return false;
            var line = document.GetLineByNumber(position.Line + 1);
            if (position.Character > line.Length) return false;
            value = line.Offset + position.Character;
            return true;
        }
        TextAnchor Anchor(int offset, AnchorMovementType movement)
        {
            var anchor = document.CreateAnchor(offset);
            anchor.MovementType = movement;
            anchor.SurviveDeletion = true;
            return anchor;
        }
    }

    private void Changing(object? sender, DocumentChangeEventArgs e)
    {
        if (copying) return;
        pending?.Cancel();
        if (ranges.Length == 0) return;
        source = AtCaret();
        if (!settings.LinkedEditing || !available() || !editor.Document.UndoStack.AcceptChanges || source < 0
            || ranges.Any(r => refusesTyping(r.Start.Offset, r.End.Offset - r.Start.Offset))
            || e.Offset < ranges[source].Start.Offset || e.Offset + e.RemovalLength > ranges[source].End.Offset)
        { End(); return; }
        var range = ranges[source];
        var text = editor.Document.GetText(range.Start.Offset, range.End.Offset - range.Start.Offset);
        var next = text.Remove(e.Offset - range.Start.Offset, e.RemovalLength).Insert(e.Offset - range.Start.Offset, e.InsertedText.Text);
        try
        {
            if (pattern is not null && !pattern.IsMatch(next)) End();
        }
        catch (RegexMatchTimeoutException) { End(); }
    }

    private void TextChanged(object? sender, EventArgs e)
    {
        if (copying) return;
        if (source >= 0 && ranges.Length > 0)
        {
            copying = true;
            try
            {
                var document = editor.Document;
                var range = ranges[source];
                var text = document.GetText(range.Start.Offset, range.End.Offset - range.Start.Offset);
                document.UndoStack.StartContinuedUndoGroup(null);
                try
                {
                    using (document.RunUpdate())
                        foreach (var mirror in ranges.Where((_, i) => i != source).Reverse())
                            document.Replace(mirror.Start.Offset, mirror.End.Offset - mirror.Start.Offset, text);
                }
                finally { document.UndoStack.EndUndoGroup(); }
                Mark();
            }
            finally { copying = false; source = -1; }
        }
        Schedule();
    }

    private void Mark() => renderer.SetHighlights(ranges.Select(r => (r.Start.Offset, r.End.Offset)));

    public void Dispose()
    {
        disposed = true;
        End();
        editor.TextArea.Caret.PositionChanged -= CaretMoved;
        editor.TextArea.RemoveHandler(Avalonia.Input.InputElement.KeyDownEvent, KeyDown);
        editor.Document.Changing -= Changing;
        editor.Document.TextChanged -= TextChanged;
        settings.PropertyChanged -= SettingsChanged;
        editor.TextArea.TextView.BackgroundRenderers.Remove(renderer);
    }
}
