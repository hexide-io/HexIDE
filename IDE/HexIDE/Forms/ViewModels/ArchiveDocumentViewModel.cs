using System;
using AvaloniaEdit.Document;
using HexIDE.Localization;
using HexIDE.TwinBasic;
using PropertyChanged.SourceGenerator;

namespace HexIDE.Forms.ViewModels;

public partial class ArchiveDocumentViewModel : BaseEditorWindowViewModel, ISearchableDocument
{
    private readonly ILocalizationService localization;
    public ArchiveProjectEntry Entry { get; private set; }
    [Notify] private TextDocument document = new();
    [Notify] private string notice = "";
    [Notify] private string metadata = "";
    [Notify] private int caretOffset;
    [Notify] private int selectionStart;
    [Notify] private int selectionLength;
    public override object? Icon => null;
    protected override string ComputeTitle() => Entry.Name;
    public bool IsReadOnlyDocument => true;
    public override bool CanHoldUnsavedChanges => false;
    public bool IsReadOnlyRegion(int offset, int length) => false;
    public Func<int, int, bool> SnapshotReadOnlyRegions() => static (_, _) => true;

    public ArchiveDocumentViewModel(ArchiveProjectEntry entry, ILocalizationService localization)
    {
        this.localization = localization;
        Entry = entry;
        Update(entry);
    }

    public void Update(ArchiveProjectEntry entry)
    {
        Entry = entry;
        var preview = (entry as ArchiveProjectDocument)?.ReadText();
        var buffer = new TextDocument(preview?.Text ?? "");
        buffer.UndoStack.ClearAll();
        // Protect programmatic edits as well as keyboard, paste and Replace operations.
        buffer.Changing += RejectEdit;
        Document = buffer;
        CaretOffset = SelectionStart = SelectionLength = 0;
        Notice = localization.GetString("Str.Archive.ReadOnly") + " " +
            (preview is null ? "" : preview.Status == ArchiveTextStatus.Available ? "" :
                localization.GetString("Str.Archive." + preview.Status));
        Metadata = string.Format(localization.GetString("Str.Archive.Metadata"),
            entry.Identity.ContainerPath, string.Join(" / ", entry.Identity.Segments),
            entry.Source.Category, entry.Source.Flags, entry.Source.Revision,
            (entry as ArchiveProjectDocument)?.File.Content.Length ?? 0);
        Title = ComputeTitle();
    }

    internal ArchiveDocumentViewModel Reopen()
    {
        var reopened = new ArchiveDocumentViewModel(Entry, localization);
        reopened.CaretOffset = Math.Clamp(CaretOffset, 0, reopened.Document.TextLength);
        return reopened;
    }

    private static void RejectEdit(object? sender, DocumentChangeEventArgs e) =>
        throw new InvalidOperationException("Archive documents are read-only.");
}
