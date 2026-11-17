using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using AvaloniaEdit;
using HexIDE.Controls;
using HexIDE.Lsp.Messages;
using Serilog;

namespace HexIDE.Forms.Views;

/// <summary>The shared LSP hover delay, request and diagnostic/Markdown tooltip.</summary>
internal sealed class EditorHoverFeatures : IDisposable
{
    private readonly TextEditor editor;
    private readonly IEditorDocumentQueries queries;
    private readonly Func<IReadOnlyList<LspMarker>> markers;
    private readonly Func<LspMarker, Control>? diagnosticContent;
    private readonly Action<Uri> openUri;
    private readonly Action? cancelHover;
    private CancellationTokenSource? hoverCts;
    private readonly CancellationTokenSource lifetime = new();
    private Point lastPoint;
    private bool disposed;

    public EditorHoverFeatures(TextEditor editor, IEditorDocumentQueries queries,
        Func<IReadOnlyList<LspMarker>> markers, Func<LspMarker, Control>? diagnosticContent,
        Action<Uri> openUri, Action? cancelHover, bool handlePointer)
    {
        this.editor = editor;
        this.queries = queries;
        this.markers = markers;
        this.diagnosticContent = diagnosticContent;
        this.openUri = openUri;
        this.cancelHover = cancelHover;
        ToolTip.SetPlacement(editor, PlacementMode.Pointer);
        ToolTip.SetShowDelay(editor, 0);
        ToolTip.SetServiceEnabled(editor, false);
        if (handlePointer)
        {
            editor.PointerMoved += OnPointerMoved;
            editor.PointerExited += OnPointerExited;
        }
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        var point = e.GetPosition(editor.TextArea.TextView);
        if (hoverCts is not null && Math.Abs(point.X - lastPoint.X) < 4 && Math.Abs(point.Y - lastPoint.Y) < 4) return;
        lastPoint = point;
        CancelHover();
        hoverCts = new CancellationTokenSource();
        if (ToolTip.GetTip(editor) is not ToolTip) CloseTip();
        var position = editor.TextArea.TextView.GetPosition(point + editor.TextArea.TextView.ScrollOffset);
        if (position is { } pos)
            _ = ShowAfterDelayAsync(new Position(pos.Line - 1, pos.Column - 1), hoverCts.Token);
    }

    private void OnPointerExited(object? sender, PointerEventArgs e)
    {
        CancelHover();
        if (ToolTip.GetTip(editor) is ToolTip tip) _ = CloseRichTipAfterExitAsync(tip);
        else CloseTip();
    }

    private void CancelHover()
    {
        hoverCts?.Cancel();
        hoverCts?.Dispose();
        hoverCts = null;
        cancelHover?.Invoke();
    }

    public void CloseTip()
    {
        ToolTip.SetIsOpen(editor, false);
        ToolTip.SetTip(editor, null);
    }

    public void Dismiss() { CancelHover(); CloseTip(); }

    public async Task CloseRichTipAfterExitAsync(ToolTip tip)
    {
        await Task.Delay(200);
        if (!disposed && ReferenceEquals(ToolTip.GetTip(editor), tip) && !tip.IsPointerOver && !editor.IsPointerOver)
            CloseTip();
    }

    public async Task ShowAfterDelayAsync(Position pos, CancellationToken token, Func<bool>? requestQuickInfo = null)
    {
        try
        {
            await Task.Delay(400, token);
            await ShowAsync(pos, token, requestQuickInfo?.Invoke() ?? true);
        }
        catch (OperationCanceledException) { }
    }

    public async Task ShowAsync(Position pos, CancellationToken token, bool requestQuickInfo = true)
    {
        if (disposed) return;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token);
        token = linked.Token;
        try
        {
            ShowDiagnosticTip(pos, null);
            var result = requestQuickInfo ? await queries.RequestHoverAsync(pos, token) : null;
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (!token.IsCancellationRequested) ShowDiagnosticTip(pos, result?.Contents);
            });
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Log.Debug("[hover] {ErrorMessage}", ex.Message); }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        lifetime.Cancel();
        editor.PointerMoved -= OnPointerMoved;
        editor.PointerExited -= OnPointerExited;
        Dismiss();
        lifetime.Dispose();
    }

    private void ShowDiagnosticTip(Position pos, MarkupContent? quickInfo)
    {
        var offset = editor.Document.GetOffset(pos.Line + 1, pos.Character + 1);
        var atPoint = markers().Where(m => offset >= m.StartOffset && offset < m.EndOffset).ToArray();
        if (quickInfo?.Kind != "markdown" && !atPoint.Any(m => m.RelatedInformation is { Length: > 0 }))
        {
            var parts = atPoint.Select(m => string.IsNullOrEmpty(m.Source) ? m.Message : $"{m.Source}: {m.Message}").ToList();
            if (!string.IsNullOrEmpty(quickInfo?.Value)) parts.Add(quickInfo.Value);
            if (parts.Count == 0) { CloseTip(); return; }
            ToolTip.SetTip(editor, string.Join(Environment.NewLine + Environment.NewLine, parts));
            ToolTip.SetIsOpen(editor, true);
            return;
        }
        var panel = new StackPanel { Spacing = 8 };
        if (diagnosticContent is not null)
        {
            foreach (var marker in markers())
                if (offset >= marker.StartOffset && offset < marker.EndOffset)
                    panel.Children.Add(diagnosticContent(marker));
        }
        if (quickInfo is { Value.Length: > 0 })
            panel.Children.Add(HoverContentView.Render(quickInfo, editor, openUri));
        if (panel.Children.Count == 0) { CloseTip(); return; }
        var richTip = new ToolTip { Content = panel };
        richTip.PointerEntered += (_, _) => CancelHover();
        richTip.PointerExited += (_, _) => _ = CloseRichTipAfterExitAsync(richTip);
        // Replacing the tip instance does not update the popup that is already open.
        ToolTip.SetIsOpen(editor, false);
        ToolTip.SetTip(editor, richTip);
        ToolTip.SetIsOpen(editor, true);
    }

}
