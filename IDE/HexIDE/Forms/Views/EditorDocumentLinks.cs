using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using HexIDE.Lsp.Messages;
using Serilog;

namespace HexIDE.Forms.Views;

internal sealed class EditorDocumentLinks : DocumentColorizingTransformer, IDisposable
{
    private readonly TextEditor editor;
    private readonly Func<CancellationToken, Task<DocumentLink[]>> request;
    private readonly Func<string, Task> follow;
    private readonly Action? dismissHover;
    private readonly Action<Position>? configurePlacement;
    private CancellationTokenSource pending = new();
    private DocumentLink[] links = [];
    private DocumentLink? hovered;
    private Cursor? previousCursor;
    private Point? pointer;
    private KeyModifiers modifiers;
    internal Task PendingRequest { get; private set; } = Task.CompletedTask;

    internal EditorDocumentLinks(TextEditor editor, Func<CancellationToken, Task<DocumentLink[]>> request,
        Func<string, Task> follow, Action? dismissHover = null, Action<Position>? configurePlacement = null)
    {
        this.editor = editor;
        this.request = request;
        this.follow = follow;
        this.dismissHover = dismissHover;
        this.configurePlacement = configurePlacement;
        editor.TextArea.TextView.LineTransformers.Add(this);
        editor.TextChanged += TextChanged;
        editor.AddHandler(InputElement.PointerMovedEvent, Moved, RoutingStrategies.Tunnel);
        editor.AddHandler(InputElement.PointerPressedEvent, Pressed, RoutingStrategies.Tunnel);
        editor.PointerExited += Exited;
        editor.AddHandler(InputElement.KeyDownEvent, KeyChanged, RoutingStrategies.Tunnel);
        editor.AddHandler(InputElement.KeyUpEvent, KeyChanged, RoutingStrategies.Tunnel);
        Refresh();
    }

    private void TextChanged(object? sender, EventArgs args) => Refresh();

    internal void Refresh()
    {
        pending.Cancel();
        pending.Dispose();
        pending = new();
        links = [];
        SetHovered(null);
        PendingRequest = RequestAsync(pending.Token);
    }

    private async Task RequestAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(300, token);
            var document = editor.Document;
            var version = document.Version;
            var answer = await request(token);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (token.IsCancellationRequested || editor.Document != document || !ReferenceEquals(version, document.Version)) return;
                links = answer;
                UpdateHovered();
            });
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { Log.Debug(error, "Document links were not applied."); }
    }

    private void Moved(object? sender, PointerEventArgs args)
    {
        pointer = args.GetPosition(editor);
        modifiers = args.KeyModifiers;
        UpdateHovered();
        if (hovered is not null) args.Handled = true;
    }

    private void KeyChanged(object? sender, KeyEventArgs args)
    {
        modifiers = args.KeyModifiers;
        UpdateHovered();
    }

    private void Exited(object? sender, PointerEventArgs args)
    {
        pointer = null;
        SetHovered(null);
    }

    private void UpdateHovered()
    {
        var command = Application.Current?.PlatformSettings?.HotkeyConfiguration.CommandModifiers ?? KeyModifiers.Control;
        DocumentLink? found = null;
        if ((modifiers & command) == command && pointer is { } point && editor.GetPositionFromPoint(point) is { } at)
        {
            var offset = editor.Document.GetOffset(at.Line, at.Column);
            foreach (var link in links)
                if (Offsets(link) is { } span && offset >= span.Start && offset < span.End)
                {
                    found = link;
                    configurePlacement?.Invoke(new Position(at.Line - 1, at.Column - 1));
                    break;
                }
        }
        SetHovered(found);
    }

    private (int Start, int End)? Offsets(DocumentLink link)
    {
        var range = link.Range;
        var document = editor.Document;
        if (range.Start.Line < 0 || range.End.Line < range.Start.Line || range.End.Line >= document.LineCount
            || range.Start.Character < 0 || range.End.Character < 0) return null;
        var first = document.GetLineByNumber(range.Start.Line + 1);
        var last = document.GetLineByNumber(range.End.Line + 1);
        if (range.Start.Character > first.Length || range.End.Character > last.Length) return null;
        var start = first.Offset + range.Start.Character;
        var end = last.Offset + range.End.Character;
        return end > start ? (start, end) : null;
    }

    private void SetHovered(DocumentLink? link)
    {
        if (ReferenceEquals(hovered, link)) return;
        if (hovered is null && link is not null) previousCursor = editor.TextArea.Cursor;
        if (link is not null) dismissHover?.Invoke();
        hovered = link;
        editor.TextArea.Cursor = link is null ? previousCursor : new Cursor(StandardCursorType.Hand);
        ToolTip.SetIsOpen(editor, false);
        ToolTip.SetTip(editor, link?.Tooltip ?? link?.Target);
        if (link is not null && (link.Tooltip ?? link.Target) is not null) ToolTip.SetIsOpen(editor, true);
        editor.TextArea.TextView.Redraw();
    }

    private void Pressed(object? sender, PointerPressedEventArgs args)
    {
        if (!args.GetCurrentPoint(editor).Properties.IsLeftButtonPressed) return;
        pointer = args.GetPosition(editor);
        modifiers = args.KeyModifiers;
        UpdateHovered();
        if (hovered is not { } link) return;
        args.Handled = true;
        if (link.Target is { Length: > 0 } target) FollowAsync(target).ListenErrors();
    }

    private async Task FollowAsync(string target)
    {
        SetHovered(null);
        await follow(target);
    }

    protected override void ColorizeLine(DocumentLine line)
    {
        if (hovered is not { } link || Offsets(link) is not { } span) return;
        var start = Math.Max(line.Offset, span.Start);
        var end = Math.Min(line.EndOffset, span.End);
        if (end > start) ChangeLinePart(start, end, element =>
        {
            var decorations = new TextDecorationCollection();
            if (element.TextRunProperties.TextDecorations is { } existing) decorations.AddRange(existing);
            decorations.AddRange(TextDecorations.Underline);
            element.TextRunProperties.SetTextDecorations(decorations);
        });
    }

    public void Dispose()
    {
        pending.Cancel();
        pending.Dispose();
        SetHovered(null);
        editor.TextChanged -= TextChanged;
        editor.RemoveHandler(InputElement.PointerMovedEvent, Moved);
        editor.RemoveHandler(InputElement.PointerPressedEvent, Pressed);
        editor.PointerExited -= Exited;
        editor.RemoveHandler(InputElement.KeyDownEvent, KeyChanged);
        editor.RemoveHandler(InputElement.KeyUpEvent, KeyChanged);
        editor.TextArea.TextView.LineTransformers.Remove(this);
    }
}
