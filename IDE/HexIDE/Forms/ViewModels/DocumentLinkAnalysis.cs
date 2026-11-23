using System;
using System.Threading;
using System.Threading.Tasks;
using AvaloniaEdit.Document;
using HexIDE.Lsp.Messages;

namespace HexIDE.Forms.ViewModels;

internal sealed class DocumentLinkAnalysis : IDisposable
{
    private object? version;
    private string? uri;
    private Task<DocumentLink[]>? answer;
    private CancellationTokenSource lifetime = new();
    private bool disposed;

    internal Task<DocumentLink[]> GetAsync(TextDocument document, string? documentUri,
        Func<CancellationToken, Task<DocumentLink[]>> request, CancellationToken token)
    {
        if (!ReferenceEquals(version, document.Version) || uri != documentUri)
        {
            Invalidate();
            version = document.Version;
            uri = documentUri;
        }
        return (answer ??= request(lifetime.Token)).WaitAsync(token);
    }

    internal void Invalidate()
    {
        if (disposed) return;
        lifetime.Cancel();
        lifetime.Dispose();
        lifetime = new();
        version = null;
        answer = null;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        lifetime.Cancel();
        lifetime.Dispose();
    }
}
