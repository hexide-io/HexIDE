using System;
using System.Threading;
using System.Threading.Tasks;
using HexIDE.Lsp;
using HexIDE.Lsp.Messages;

namespace HexIDE.Forms.ViewModels;

// Answers belong to the document, not to a visual attachment: Dock may replace the entire view.
internal sealed class CodeEditorAnalysis(CodeEditorViewModel owner, ILspClient client) : IDisposable
{
    private bool disposed;
    private object? version;
    private string? uri;
    private bool showHints;
    private CancellationTokenSource lifetime = new();
    private Task<SemanticTokenResult?>? tokens;
    private Task<FoldingRange[]>? folds;
    private Task<InlayHint[]>? hints;
    private CodeLensSession? lenses;

    private void CheckDocument()
    {
        var currentUri = owner.GetDocumentUriPublic();
        if (ReferenceEquals(version, owner.Document.Version) && uri == currentUri
            && showHints == owner.Settings.ShowInlayHints) return;
        Invalidate();
        version = owner.Document.Version;
        uri = currentUri;
        showHints = owner.Settings.ShowInlayHints;
    }

    public Task<SemanticTokenResult?> Tokens(CancellationToken token)
    {
        CheckDocument();
        return (tokens ??= owner.RequestSemanticTokensAsync(lifetime.Token)).WaitAsync(token);
    }

    public Task<FoldingRange[]> Folds(CancellationToken token)
    {
        CheckDocument();
        return (folds ??= owner.RequestFoldingRangesAsync(lifetime.Token)).WaitAsync(token);
    }

    public Task<InlayHint[]> Hints(CancellationToken token)
    {
        CheckDocument();
        return (hints ??= owner.RequestInlayHintsAsync(lifetime.Token)).WaitAsync(token);
    }

    public CodeLensSession Lenses
    {
        get
        {
            CheckDocument();
            return lenses ??= new CodeLensSession(client, uri!);
        }
    }

    public void Invalidate()
    {
        if (disposed) return;
        version = null;
        lifetime.Cancel();
        lifetime.Dispose();
        lifetime = new();
        tokens = null;
        folds = null;
        hints = null;
        lenses?.Dispose();
        lenses = null;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        lifetime.Cancel();
        lifetime.Dispose();
        lenses?.Dispose();
    }
}
