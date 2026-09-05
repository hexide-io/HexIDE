using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AvaloniaEdit.Document;
using HexIDE.Controls;
using HexIDE.Lsp;
using HexIDE.Lsp.Messages;
using HexIDE.Utils;

namespace HexIDE.Forms.ViewModels;

/// <summary>
/// One open document's conversation with the language layer: opened, kept synchronized while it is
/// edited, closed when its editor closes, and its diagnostics turned into editor markers.
///
/// <para>
/// <b>Shared rather than copied, and the reason is the subtle parts.</b> Ninety lines is small enough to
/// argue for a second copy, and copying is exactly wrong here, because what would drift is what took the
/// longest to get right: diagnostics are matched with <see cref="LspDocumentUri.AreSame"/> and never with
/// <c>!=</c>, because a server that normalises the URI it echoes back drops every diagnostic it publishes
/// and does it silently (hexide-io/HexIDE#236); and the range-to-offset conversion clamps in three places
/// because the server's copy of a document is always a little behind the buffer. A second implementation
/// would not reproduce those, and its failure would read as "diagnostics are slightly wrong in the other
/// editor", which is a defect nobody files.
/// </para>
///
/// <para>
/// It deliberately knows nothing about what kind of document it holds. The VB6 code editor and the
/// carried-file editor have almost nothing in common — one has a designer half, procedure dropdowns,
/// breakpoints and a faithfulness gate, the other is text in and text out — and sharing this one narrow
/// collaborator is what makes keeping them separate affordable rather than a step towards merging them.
/// </para>
/// </summary>
internal sealed class LspDocumentSession : IDisposable
{
    /// <summary>
    /// How long editing settles before the server is told. Long enough that a burst of typing is one
    /// message rather than one per keystroke; short enough that diagnostics do not feel detached from the
    /// edit that caused them.
    /// </summary>
    private const int DebounceMilliseconds = 300;

    private readonly ILspClient client;
    private readonly TextDocument document;
    private readonly Action<Action> postToUiThread;

    private CancellationTokenSource? debounce;
    private int version;
    private bool started;
    private bool disposed;

    /// <param name="postToUiThread">
    /// How to reach the UI thread. Defaults to the dispatcher, and is a parameter because the alternative
    /// is a static call buried in a private method — which makes the thread affinity invisible to a reader
    /// and untestable without owning the dispatcher, since only the thread that initialised Avalonia may
    /// pump it.
    /// </param>
    public LspDocumentSession(
        ILspClient client, TextDocument document, string uri, Action<Action>? postToUiThread = null)
    {
        this.client = client;
        this.document = document;
        this.postToUiThread = postToUiThread ?? (work => Avalonia.Threading.Dispatcher.UIThread.Post(work));
        Uri = uri;
    }

    /// <summary>How this document is named to servers. Fixed for the session's lifetime.</summary>
    public string Uri { get; }

    /// <summary>Diagnostics for this document, converted to offsets in this buffer. Raised on the UI thread.</summary>
    public event Action<IReadOnlyList<LspMarker>>? MarkersChanged;

    /// <summary>
    /// Raised after each batch of diagnostics is applied, on the UI thread.
    ///
    /// <para>
    /// Exists so the code editor can keep refreshing its procedure list whenever the server has evidently
    /// re-read the document, without this class having to know what a procedure is. Piggybacking on
    /// diagnostics is the editor's own choice and stays the editor's own business.
    /// </para>
    /// </summary>
    public event Action? DiagnosticsApplied;

    /// <summary>
    /// Opens the document to the language layer and begins tracking edits.
    ///
    /// <para>
    /// <b>Not gated on the client running</b>, and that is a fix rather than an omission. Opening tracks
    /// the document before it checks for a live server, and every tracked document is replayed after a
    /// (re)connect — so gating here would mean a file opened while the server was down is never tracked
    /// and never replayed. It is also what makes lazy start work at all: opening a document is the trigger
    /// that starts the server claiming its language, so a gate would leave nothing to do the starting.
    /// </para>
    /// </summary>
    public void Start()
    {
        if (started || disposed) return;
        started = true;

        client.DiagnosticsPublished += OnDiagnosticsPublished;
        client.OpenDocumentAsync(Uri, document.Text).ListenErrors();
        document.TextChanged += OnTextChanged;
    }

    /// <summary>
    /// Cancels any pending debounce and sends the current text immediately.
    ///
    /// <para>
    /// For requests whose answer depends on the server holding what the user can see — signature help
    /// being the clearest case, where a debounce still in flight means the server is answering about the
    /// line before the one being typed.
    /// </para>
    /// </summary>
    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        debounce?.Cancel();
        if (disposed || !client.IsRunning) return;
        await client.ChangeDocumentAsync(Uri, ++version, document.Text, cancellationToken);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;

        debounce?.Cancel();
        if (!started) return;

        document.TextChanged -= OnTextChanged;
        client.DiagnosticsPublished -= OnDiagnosticsPublished;
        client.CloseDocumentAsync(Uri).ListenErrors();
    }

    private void OnTextChanged(object? sender, EventArgs e)
    {
        debounce?.Cancel();
        debounce = new CancellationTokenSource();
        var token = debounce.Token;

        // Captured now rather than read in the continuation: by the time it runs the buffer may have moved
        // on, and a version number paired with the wrong text is worse than a stale one.
        var pending = ++version;
        var text = document.Text;

        Task.Delay(DebounceMilliseconds, token).ContinueWith(
            _ =>
            {
                if (!token.IsCancellationRequested && !disposed && client.IsRunning)
                    client.ChangeDocumentAsync(Uri, pending, text).ListenErrors();
            },
            token,
            TaskContinuationOptions.OnlyOnRanToCompletion,
            TaskScheduler.Default);
    }

    private void OnDiagnosticsPublished(object? sender, PublishDiagnosticsParams p)
    {
        if (disposed) return;

        // NOT `!=`: a server may normalise the URI it echoes back — drive-letter case, percent-encoding —
        // and an exact comparison drops its diagnostics without a trace. See #236.
        if (!LspDocumentUri.AreSame(p.Uri, Uri)) return;

        // TextDocument refuses access from anywhere but the UI thread, so the whole conversion goes there
        // rather than only the raise.
        postToUiThread(() =>
        {
            if (disposed) return;
            MarkersChanged?.Invoke(ToMarkers(p.Diagnostics));
            DiagnosticsApplied?.Invoke();
        });
    }

    /// <summary>
    /// Diagnostic ranges as offsets into this buffer.
    ///
    /// <para>
    /// Every bound here is defensive on purpose. The server is answering about the text it was last sent,
    /// which — with a debounce in flight, or a reload from disk — is routinely not the text in the buffer
    /// now. A range past the end is therefore ordinary traffic rather than a server fault, and must
    /// produce a clamped marker rather than an exception on the UI thread.
    /// </para>
    /// </summary>
    private List<LspMarker> ToMarkers(Diagnostic[] diagnostics)
    {
        var markers = new List<LspMarker>(diagnostics.Length);

        foreach (var diagnostic in diagnostics)
        {
            var startLine = diagnostic.Range.Start.Line + 1;  // AvaloniaEdit lines are 1-based
            var startColumn = diagnostic.Range.Start.Character;
            var endLine = diagnostic.Range.End.Line + 1;
            var endColumn = diagnostic.Range.End.Character;

            if (startLine < 1 || startLine > document.LineCount) continue;

            var startOffset = document.GetOffset(startLine, startColumn + 1);
            var endOffset = endLine <= document.LineCount
                ? document.GetOffset(endLine, Math.Max(endColumn + 1, startColumn + 2))
                : startOffset + 1;

            endOffset = Math.Min(endOffset, document.TextLength);
            if (startOffset >= endOffset) endOffset = Math.Min(startOffset + 1, document.TextLength);

            // A severity a server omits is an error: the protocol leaves it to the client, and treating an
            // unstated severity as a hint would hide real problems from a server that never sets it.
            var isError = diagnostic.Severity is null || diagnostic.Severity == DiagnosticSeverity.Error;
            markers.Add(new LspMarker(startOffset, endOffset, isError, diagnostic.Message));
        }

        return markers;
    }
}
