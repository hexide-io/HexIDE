using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using AvaloniaEdit.Document;
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
public partial class RelatedDocumentEditorViewModel : BaseEditorWindowViewModel
{
    private RelatedDocumentDefinition? document;
    private bool hadByteOrderMark;
    private string savedText = string.Empty;

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
        Title = ComputeTitle();
        return this;
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
