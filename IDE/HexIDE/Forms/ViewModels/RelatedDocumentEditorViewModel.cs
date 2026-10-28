using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using AvaloniaEdit.Document;
using HexIDE.Controls;
using HexIDE.Lsp;
using HexIDE.Localization;
using HexIDE.Lsp.Messages;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Utils;
using PropertyChanged.SourceGenerator;
using Serilog;

namespace HexIDE.Forms.ViewModels;

/// <summary>
/// A plain text editor for a file the project carries but does not compile.
///
/// <para>
/// <b>Deliberately not a third branch in <see cref="CodeEditorViewModel"/>.</b> That class carries a
/// <c>FormDefinition?</c> and a <c>ModuleDefinition?</c> as sibling nullable fields, and roughly eight
/// consumers re-derive which kind they are looking at from those two. A third would multiply that, in a
/// class where the save path is <em>already</em> wrong for the second kind
/// (hexide-io/HexIDE#244) — adding to a tangle is not the way to deliver a README.
/// </para>
///
/// <para>
/// It is also a genuinely different job. There is no designer half, no procedure dropdowns, no VB6
/// language server, no breakpoints, no faithfulness gate, no companion binary. Text in, text out. Keeping
/// that honest keeps the polyglot direction a small class rather than a growing conditional.
/// </para>
/// </summary>
public partial class RelatedDocumentEditorViewModel(ILspClient lspClient, IWindowManager windowManager,
    ILocalizationService localization)
    : BaseEditorWindowViewModel, ISearchableDocument
{
    private RelatedDocumentDefinition? document;
    private Vb6TextEncoding encoding = Vb6TextEncoding.Utf8;
    private string savedText = string.Empty;

    /// <summary>
    /// This document's conversation with the language layer, once it has one.
    ///
    /// <para>
    /// A field rather than a local because the save path needs it. No event is involved, unlike the VB6
    /// documents: nothing but this editor's own Save ever writes a carried file.
    /// </para>
    /// </summary>
    private LspDocumentSession? session;

    /// <summary>Diagnostics for this document, as offsets into the buffer. The view draws these.</summary>
    public event Action<IReadOnlyList<LspMarker>>? MarkersChanged;

    /// <summary>
    /// The most recent diagnostics, kept so a view attaching later can catch up.
    ///
    /// <para>
    /// Needed because <see cref="MarkersChanged"/> is a notification, not a state: a view that subscribes
    /// after the last publish sees nothing until the next one. That is not hypothetical — moving this
    /// document to another dock re-materialises the view, and the server has no reason to re-publish for a
    /// document that has not changed, so the squiggles would simply not come back.
    /// </para>
    /// </summary>
    public IReadOnlyList<LspMarker> Markers { get; private set; } = [];

    /// <summary>The buffer the editor binds to.</summary>
    public TextDocument Document { get; } = new();

    /// <summary>
    /// Caret and selection, mirrored to and from the view — the rest of <see cref="ISearchableDocument"/>.
    ///
    /// <para>
    /// The VB6 code editor has carried these since it was written, for the navigation its own features
    /// need. This editor had no reason to until Find could reach it, which is why it did not have them and
    /// why Find silently refused a file it was perfectly able to search (hexide-io/HexIDE#363).
    /// </para>
    /// </summary>
    [Notify] private int caretOffset;

    [Notify] private int selectionStart;

    [Notify] private int selectionLength;

    public RelatedDocumentDefinition? RelatedDocument => document;

    public override object? Icon => null;

    [Notify] private bool isDirty;

    private void OnIsDirtyChanged() => IsModified = IsDirty;

    /// <summary>True when the file could not be read — the editor opens empty and read-only rather than lying.</summary>
    [Notify] private string? loadError;

    protected override string ComputeTitle() =>
        document is null ? "" : $"{document.Owner.Name} - {document.Name}";

    public RelatedDocumentEditorViewModel Initialize(RelatedDocumentDefinition relatedDocument)
    {
        document = relatedDocument;

        if (relatedDocument.AbsolutePath is { } path && File.Exists(path))
        {
            try
            {
                var bytes = File.ReadAllBytes(path);
                var marked = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
                encoding = Vb6TextEncoding.Utf8 with { Utf8Bom = marked };
                try
                {
                    savedText = Vb6TextFile.Decode(bytes, encoding);
                }
                catch (DecoderFallbackException)
                {
                    // Latin-1 maps each byte back to itself, including bytes UTF-8 cannot read.
                    encoding = Vb6TextEncoding.Latin1 with { Utf8Bom = marked };
                    savedText = Vb6TextFile.Decode(bytes, encoding);
                }
            }
            catch (Exception ex)
            {
                // A file we cannot read opens empty with the reason shown. Failing the open outright would
                // make one unreadable note stop the developer seeing the rest of the project.
                Log.Error(ex, "Could not read related document {Path}", path);
                LoadError = ex.Message;
            }
        }
        else if (relatedDocument.AbsolutePath is not null)
        {
            LoadError = "File not found.";
        }

        Document.Text = savedText;
        Document.TextChanged += (_, _) => IsDirty = !string.Equals(Document.Text, savedText, StringComparison.Ordinal);
        OpenToLanguageLayer();
        Title = ComputeTitle();
        return this;
    }

    /// <summary>
    /// Hands this document to the language layer, if there is a document to hand over.
    ///
    /// <para>
    /// This is the point of the whole change. A carried file is the one thing HexIDE opens that it has no
    /// opinion about — no grammar, no interpreter, no designer — so it is exactly the file a server
    /// attached by configuration exists to serve, and until now it was the one editor that never spoke to
    /// the language layer at all.
    /// </para>
    ///
    /// <para>
    /// Identified by its <c>file:</c> URI, as every document with a file now is. Routing keys on the extension,
    /// and that is the entire basis on which a server claims this file. It is also a real file on disk, which a
    /// server may want to read itself. This used to be contrasted with the <c>vb6://</c> scheme the IDE's own
    /// documents rode, which carried no extension; hexide-io/HexIDE#273 retired it, and a project member with
    /// no file is named <c>untitled:&lt;Project&gt;/&lt;Name&gt;.&lt;ext&gt;</c>. A carried file never takes
    /// that name: one with no path is not opened to the language layer at all (below).
    /// </para>
    /// </summary>
    private void OpenToLanguageLayer()
    {
        // Nothing to name it by. An unsaved project's documents have no path yet (#260), and inventing one
        // would have a server index a file that is not there.
        if (document?.AbsolutePath is not { Length: > 0 } path) return;

        // A file that could not be read opens empty and read-only rather than lying about its content.
        // Offering that empty buffer would have a server publish diagnostics about a document nobody has,
        // drawn over a banner that says the file could not be read.
        if (LoadError is not null) return;

        // isProjectMember: false -- a carried file is an ordinary file the project happens to reference,
        // so it goes wherever its extension leads. A carried `.cls` reaching a LaTeX server is correct, and
        // is the case that stops the #279 gate being written as "anything the project names".
        session = AutoDispose(new LspDocumentSession(
            lspClient, Document, LspDocumentUri.ForFile(path), isProjectMember: false));
        session.MarkersChanged += markers =>
        {
            Markers = markers;
            MarkersChanged?.Invoke(markers);
        };
        session.Start();
    }

    public async Task<DocumentSymbol[]> RequestSearchSymbolsAsync()
    {
        if (session is not { IsOpen: true } open) return [];
        await open.FlushAsync();
        return await lspClient.RequestDocumentSymbolsAsync(open.Uri);
    }

    /// <summary>
    /// Folding ranges for this document, from whichever attached server serves it.
    /// </summary>
    /// <remarks>
    /// A carried file is the one document kind in this IDE with a real <c>file:</c> URI, so it is the
    /// one an ordinary language server can answer structural questions about without knowing anything
    /// about VB6. It already receives diagnostics and hover on that basis; folding was simply never
    /// wired, and the registry does all the work of deciding who — if anyone — may answer.
    /// </remarks>
    public Task<FoldingRange[]> RequestFoldingRangesAsync(CancellationToken ct = default)
        => session is { IsOpen: true } open
            ? lspClient.RequestFoldingRangesAsync(open.Uri, ct)
            : Task.FromResult<FoldingRange[]>([]);

    /// <summary>
    /// Writes the buffer back. Bound to Save, unlike the VB6 code editor — where the only Save binding
    /// routes to the form path and silently does nothing for a module (#244).
    /// </summary>
    public void Save() => SaveAsync().ListenErrors();

    public async Task SaveAsync()
    {
        if (document?.AbsolutePath is not { } path || LoadError is not null) return;

        var text = Document.Text;
        if (string.Equals(text, savedText, StringComparison.Ordinal)) return;

        // Encode before touching the file: an edit the original encoding cannot hold must not lose bytes.
        byte[] bytes;
        try
        {
            bytes = Vb6TextFile.EncodeDocument(document.Name, text, encoding);
        }
        catch (SourceEncodingRefusal refusal)
        {
            Log.Warning("Cannot save {Document} in code page {CodePage}: it holds {CodePoint}",
                refusal.Document, refusal.CodePage, refusal.CodePoint);
            await windowManager.MessageBox(
                string.Format(localization.GetString("Str.Dialog.SourceEncoding.NothingWritten"),
                    refusal.Document, refusal.CodePage, refusal.Character, refusal.CodePoint,
                    refusal.Line.Length > 0 ? refusal.Line : refusal.Character),
                "HexIDE", MessageBoxButtons.Ok, MessageBoxIcon.Warning);
            throw;
        }

        AtomicFileWriter.WriteAllBytes(path, bytes);

        savedText = text;
        IsDirty = false;
        Log.Debug("Saved related document {Path}", path);

        // After the bytes are on disk, not before: a server that re-reads the file on being told must find
        // what was written, and one that is handed the text should be handed what was written too.
        if (session is { } open) await open.NotifySavedAsync();
    }
}
