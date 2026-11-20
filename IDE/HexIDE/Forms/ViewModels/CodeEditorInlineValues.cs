using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using HexIDE.Dap;
using HexIDE.Debugging;
using HexIDE.Lsp;
using HexIDE.Lsp.Messages;
using HexIDE.Runtime.Debugging;
using Serilog;

namespace HexIDE.Forms.ViewModels;

public partial class CodeEditorViewModel
{
    internal async Task<InlayHint[]> RequestInlineValuesAsync(HexIDE.Lsp.Messages.Range visible, CancellationToken ct)
    {
        if (ExternalDebugger?.IsActive == true) return await RequestExternalInlineValuesAsync(visible, ct);
        if (!Settings.ShowInlineValues || debugController.State != DebugState.Paused
            || debugController.CurrentStop is not { HasStatement: true } stop || !Identity.IsNamed(stop.Module)
            || (RunningProject is { } running && !Identity.IsIn(running))
            || LiveDocumentUri is not { } uri || stop.Line > Document.LineCount) return [];
        var stoppedLine = Document.GetLineByNumber(stop.Line);
        // CallStackFrame carries no DAP frame id; 0 denotes the currently paused frame at this seam.
        var context = new InlineValueContext(0, new(new(stop.Line - 1, 0), new(stop.Line - 1, stoppedLine.Length)));
        var version = Document.Version;
        await FlushDocumentAsync(ct);
        var values = await lspClient.RequestInlineValuesAsync(uri, visible, context, ct);
        ct.ThrowIfCancellationRequested();
        if (Document.Version != version || debugController.CurrentStop != stop
            || debugController.State != DebugState.Paused || !Settings.ShowInlineValues) return [];
        var locals = debugController.GetLocals()?.Locals ?? [];
        var labels = new List<(int Line, string Text)>();
        foreach (var value in values)
        {
            if (!TryInlineValueText(value.Range, out var source)) continue;
            string? label = null;
            switch (value)
            {
                case InlineValueText text:
                    label = text.Text;
                    break;
                case InlineValueVariableLookup lookup:
                    var name = lookup.VariableName ?? source;
                    var local = locals.FirstOrDefault(node => string.Equals(node.Name, name,
                        lookup.CaseSensitiveLookup ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase));
                    if (local is not null) label = $"{name} = {local.Value}";
                    break;
                case InlineValueEvaluatableExpression expression:
                    var watch = expression.Expression ?? source;
                    try
                    {
                        if (await debugController.EvaluateWatchAsync(watch) is { Ok: true } result)
                            label = $"{watch} = {result.Display}";
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception) { /* An unavailable expression contributes no label. */ }
                    break;
            }
            ct.ThrowIfCancellationRequested();
            if (!string.IsNullOrEmpty(label)) labels.Add((value.Range.End.Line, label));
        }
        if (Document.Version != version || debugController.CurrentStop != stop
            || debugController.State != DebugState.Paused || !Settings.ShowInlineValues) return [];
        return labels.GroupBy(label => label.Line).Select(group => new InlayHint(
            new(group.Key, Document.GetLineByNumber(group.Key + 1).Length),
            // Two lookups of one variable on a line (x = x + 1) read the same; show it once.
            JsonSerializer.SerializeToElement(string.Join(", ", group.Select(label => label.Text).Distinct(StringComparer.Ordinal))))
            { PaddingLeft = true }).ToArray();
    }

    private async Task<InlayHint[]> RequestExternalInlineValuesAsync(HexIDE.Lsp.Messages.Range visible, CancellationToken ct)
    {
        if (!Settings.ShowInlineValues || ExternalDebugger is not { IsActive: true, Stop: { } stop } external
            || !external.IsCurrent(stop) || external.SelectedFrame is not { } frame
            || external.SelectedLocation is not { } location || LiveDocumentUri is not { } uri
            || !string.Equals(location.Uri, uri,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
            || location.Line > Document.LineCount) return [];
        var frameId = frame.GetProperty("id").GetInt32();
        var locals = external.Variables;
        var version = Document.Version;
        bool IsCurrent() => external.IsActive && external.Stop == stop && external.IsCurrent(stop)
            && external.SelectedFrame?.GetProperty("id").GetInt32() == frameId
            && ReferenceEquals(external.Variables, locals) && Document.Version == version && Settings.ShowInlineValues;
        var line = Document.GetLineByNumber(location.Line);
        var context = new InlineValueContext(frameId, new(new(location.Line - 1, 0), new(location.Line - 1, line.Length)));
        await FlushDocumentAsync(ct);
        var values = await lspClient.RequestInlineValuesAsync(uri, visible, context, ct);
        ct.ThrowIfCancellationRequested();
        if (!IsCurrent()) return [];
        var labels = new List<(int Line, string Text)>();
        foreach (var value in values)
        {
            if (!TryInlineValueText(value.Range, out var source)) continue;
            string? label = null;
            switch (value)
            {
                case InlineValueText text:
                    label = text.Text;
                    break;
                case InlineValueVariableLookup lookup:
                    var name = lookup.VariableName ?? source;
                    var local = locals.FirstOrDefault(node => string.Equals(node.Name, name,
                        lookup.CaseSensitiveLookup ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase));
                    if (local is not null) label = $"{name} = {local.Value}";
                    break;
                case InlineValueEvaluatableExpression expression:
                    var watch = expression.Expression ?? source;
                    try
                    {
                        var result = await external.EvaluateWatchAsync(stop, frameId, watch);
                        label = $"{watch} = {result.Value}";
                    }
                    catch (InvalidOperationException) { /* An expired frame contributes no label. */ }
                    catch (DapRequestException) { /* An adapter refusal contributes no label. */ }
                    break;
            }
            ct.ThrowIfCancellationRequested();
            if (!IsCurrent()) return [];
            if (!string.IsNullOrEmpty(label)) labels.Add((value.Range.End.Line, label));
        }
        return labels.GroupBy(label => label.Line).Select(group => new InlayHint(
            new(group.Key, Document.GetLineByNumber(group.Key + 1).Length),
            JsonSerializer.SerializeToElement(string.Join(", ", group.Select(label => label.Text).Distinct(StringComparer.Ordinal))))
            { PaddingLeft = true }).ToArray();
    }

    private bool TryInlineValueText(HexIDE.Lsp.Messages.Range range, out string text)
    {
        text = "";
        if (range.Start.Line < 0 || range.End.Line < range.Start.Line || range.End.Line >= Document.LineCount)
            return false;
        var first = Document.GetLineByNumber(range.Start.Line + 1);
        var last = Document.GetLineByNumber(range.End.Line + 1);
        if (range.Start.Character < 0 || range.Start.Character > first.Length
            || range.End.Character < 0 || range.End.Character > last.Length) return false;
        var start = first.Offset + range.Start.Character;
        var end = last.Offset + range.End.Character;
        if (end < start) return false;
        text = Document.GetText(start, end - start);
        return true;
    }

    internal InlineValueSession CreateInlineValueSession(Action<InlayHint[]> show) => new(this, lspClient, show);
}

// Each split pane has its own viewport and lifetime. Debug and server events may arrive off the UI thread.
internal sealed class InlineValueSession : IDisposable
{
    private readonly CodeEditorViewModel owner;
    private readonly ILspClient client;
    private readonly Action<InlayHint[]> show;
    private readonly Dispatcher dispatcher = Dispatcher.UIThread;
    private HexIDE.Lsp.Messages.Range? visible;
    private CancellationTokenSource? request;
    private int revision;
    private bool disposed;
    private DapStop? externalStop;
    private int? externalFrame;
    private IReadOnlyList<ExternalVariable>? externalVariables;

    public InlineValueSession(CodeEditorViewModel owner, ILspClient client, Action<InlayHint[]> show)
    {
        this.owner = owner;
        this.client = client;
        this.show = show;
        if (owner.ExternalDebugger is { } external) external.Changed += OnExternalChanged;
        owner.DebugController.Stopped += OnStopped;
        owner.DebugController.Continued += OnContinued;
        owner.Settings.PropertyChanged += OnSettingsChanged;
        owner.Document.Changed += OnDocumentChanged;
        owner.LanguageServerStateChanged += Refresh;
        client.InlineValueRefreshRequested += OnRefresh;
    }

    public void SetVisibleRange(HexIDE.Lsp.Messages.Range range)
    {
        if (visible == range) return;
        visible = range;
        Refresh();
    }

    private void OnExternalChanged() => dispatcher.Post(() =>
    {
        if (disposed || owner.ExternalDebugger is not { } external) return;
        var stop = external.IsActive && external.Stop is { } current && external.IsCurrent(current) ? current : null;
        var frame = external.SelectedFrame?.GetProperty("id").GetInt32();
        if (externalStop == stop && externalFrame == frame && ReferenceEquals(externalVariables, external.Variables)) return;
        externalStop = stop;
        externalFrame = frame;
        externalVariables = external.Variables;
        if (stop is null) Clear();
        else Refresh();
    });

    private void OnStopped(StoppedInfo _) => dispatcher.Post(Refresh);
    private void OnContinued() => dispatcher.Post(Clear);
    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(owner.Settings.ShowInlineValues)) dispatcher.Post(Refresh);
    }
    private void OnDocumentChanged(object? sender, EventArgs args) => Clear();
    private void OnRefresh(object? sender, EventArgs args) => dispatcher.Post(() =>
    {
        if (owner.GetDocumentUriPublic() is { } uri && client.HandlesRefresh(uri, sender)) Refresh();
    });

    private void Clear()
    {
        if (disposed) return;
        ++revision;
        request?.Cancel();
        request?.Dispose();
        request = null;
        show([]);
    }

    private void Refresh()
    {
        if (disposed) return;
        Clear();
        if (visible is not { } range) return;
        request = new CancellationTokenSource();
        RequestAsync(range, revision, request.Token).ListenErrors();
    }

    private async Task RequestAsync(HexIDE.Lsp.Messages.Range range, int generation, CancellationToken ct)
    {
        try
        {
            var hints = await owner.RequestInlineValuesAsync(range, ct);
            await dispatcher.InvokeAsync(() =>
            {
                if (!disposed && !ct.IsCancellationRequested && generation == revision) show(hints);
            });
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { Log.Debug(error, "Inline debug values request failed."); }
    }

    public void Dispose()
    {
        if (disposed) return;
        Clear();
        disposed = true;
        if (owner.ExternalDebugger is { } external) external.Changed -= OnExternalChanged;
        owner.DebugController.Stopped -= OnStopped;
        owner.DebugController.Continued -= OnContinued;
        owner.Settings.PropertyChanged -= OnSettingsChanged;
        owner.Document.Changed -= OnDocumentChanged;
        owner.LanguageServerStateChanged -= Refresh;
        client.InlineValueRefreshRequested -= OnRefresh;
    }
}
