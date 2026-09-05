using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;
using AvaloniaEdit.Document;
using HexIDE.Controls;
using HexIDE.Lsp;
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
public partial class RelatedDocumentEditorViewModel(ILspClient lspClient) : BaseEditorWindowViewModel
{
    private RelatedDocumentDefinition? document;
    private bool hadByteOrderMark;
    private string savedText = string.Empty;

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

    public RelatedDocumentDefinition? RelatedDocument => document;

    public override object? Icon => null;

    [Notify] private bool isDirty;

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
                hadByteOrderMark = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
                savedText = new UTF8Encoding(false).GetString(
                    hadByteOrderMark ? bytes.AsSpan(3) : bytes.AsSpan());
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
    /// Identified by a <c>file:</c> URI, not by the <c>vb6://</c> scheme the IDE's own documents use.
    /// Routing keys on the extension, and that is the entire basis on which a server claims this file;
    /// a scheme URI carries none. It is also a real file on disk, which a server may want to read itself.
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

        var session = AutoDispose(new LspDocumentSession(lspClient, Document, LspDocumentUri.ForFile(path)));
        session.MarkersChanged += markers =>
        {
            Markers = markers;
            MarkersChanged?.Invoke(markers);
        };
        session.Start();
    }

    /// <summary>
    /// Writes the buffer back. Bound to Save, unlike the VB6 code editor — where the only Save binding
    /// routes to the form path and silently does nothing for a module (#244).
    /// </summary>
    public void Save() => SaveAsync().ListenErrors();

    public async Task SaveAsync()
    {
        if (document?.AbsolutePath is not { } path || LoadError is not null) return;

        var text = Document.Text;
        // Encoding is preserved rather than normalised: this file belongs to something else — a build
        // script, a docs pipeline, another editor — and quietly adding or dropping a byte-order mark is
        // the kind of diff that shows up in someone's review with no explanation.
        var encoding = new UTF8Encoding(hadByteOrderMark);

        // Written to a sibling temp file and moved into place, so an interrupted save cannot leave a
        // truncated document where the original was.
        var temporary = path + ".hexide-tmp";
        await File.WriteAllTextAsync(temporary, text, encoding);
        File.Move(temporary, path, overwrite: true);

        savedText = text;
        IsDirty = false;
        Log.Debug("Saved related document {Path}", path);
    }
}
