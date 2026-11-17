using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using HexIDE.Lsp;
using Serilog;

namespace HexIDE.Forms.ViewModels;

// A document owns the subscription, including while Dock replaces its view.
internal sealed class EditorAnalysisRefresh : IDisposable
{
    private readonly ILspClient client;
    private readonly Func<string?> documentUri;
    private readonly Func<CancellationToken, Task> refresh;
    private readonly Dispatcher dispatcher = Dispatcher.UIThread;
    private readonly CancellationTokenSource lifetime = new();
    private bool pending;
    private bool disposed;

    public EditorAnalysisRefresh(ILspClient client, Func<string?> documentUri, Func<CancellationToken, Task> refresh)
    {
        this.client = client;
        this.documentUri = documentUri;
        this.refresh = refresh;
        client.InlayHintRefreshRequested += OnRefresh;
        client.SemanticTokensRefreshRequested += OnRefresh;
    }

    private void OnRefresh(object? sender, EventArgs args) => dispatcher.Post(() =>
    {
        if (disposed || pending || documentUri() is not { } uri || !client.HandlesRefresh(uri, sender)) return;
        pending = true;
        RefreshAsync().ListenErrors();
    });

    private async Task RefreshAsync()
    {
        try
        {
            await Task.Delay(300, lifetime.Token);
            await dispatcher.InvokeAsync(() => disposed ? Task.CompletedTask : refresh(lifetime.Token));
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { Log.Debug(error, "Editor analysis refresh failed."); }
        finally { await dispatcher.InvokeAsync(() => pending = false); }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        client.InlayHintRefreshRequested -= OnRefresh;
        client.SemanticTokensRefreshRequested -= OnRefresh;
        lifetime.Cancel();
        lifetime.Dispose();
    }
}
