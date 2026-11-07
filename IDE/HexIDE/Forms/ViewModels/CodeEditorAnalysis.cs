using System;
using System.Threading;
using System.Threading.Tasks;
using HexIDE.Lsp;
using HexIDE.Lsp.Messages;

namespace HexIDE.Forms.ViewModels;

// Answers belong to the document, not to a visual attachment: Dock may replace the entire view.
internal sealed class CodeEditorAnalysis : IDisposable
{
    private readonly CodeEditorViewModel owner;
    private readonly ILspClient client;

    public CodeEditorAnalysis(CodeEditorViewModel owner, ILspClient client)
    {
        this.owner = owner;
        this.client = client;
        owner.Settings.PropertyChanged += OnSettingsChanged;
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(owner.Settings.ShowCodeLenses))
            lenses?.SetEnabledAsync(owner.Settings.ShowCodeLenses).ListenErrors();
    }

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
        Invalidate(resetLenses: false);
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
            if (lenses is not null && lensUri != uri)
            {
                lenses.Dispose();
                lenses = null;
            }
            lensUri = uri;
            if (lenses is null)
            {
                lenses = new CodeLensSession(client, uri!);
                lenses.SetEnabledAsync(owner.Settings.ShowCodeLenses).ListenErrors();
            }
            return lenses;
        }
    }

    private string? lensUri;

    public void RefreshLenses()
    {
        if (lenses is not null) lenses.RefreshAsync().ListenErrors();
    }

    public void Invalidate(bool resetLenses = true)
    {
        if (disposed) return;
        version = null;
        lifetime.Cancel();
        lifetime.Dispose();
        lifetime = new();
        tokens = null;
        folds = null;
        hints = null;
        if (resetLenses)
        {
            lenses?.Dispose();
            lenses = null;
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        owner.Settings.PropertyChanged -= OnSettingsChanged;
        lifetime.Cancel();
        lifetime.Dispose();
        lenses?.Dispose();
    }
}
