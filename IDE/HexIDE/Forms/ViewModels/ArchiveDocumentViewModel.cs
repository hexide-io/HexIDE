using System;
using System.Collections.Generic;
using System.Linq;
using AvaloniaEdit.Document;
using HexIDE.Language;
using HexIDE.Localization;
using HexIDE.TwinBasic;
using PropertyChanged.SourceGenerator;

namespace HexIDE.Forms.ViewModels;

public partial class ArchiveDocumentViewModel : BaseEditorWindowViewModel, ISearchableDocument
{
    private readonly ILocalizationService localization;
    private readonly IConditionalCodeReader conditionalCode;
    private string? buildType;
    public ArchiveProjectEntry Entry { get; private set; }
    [Notify] private TextDocument document = new();
    [Notify] private string notice = "";
    [Notify] private string metadata = "";
    [Notify] private int caretOffset;
    [Notify] private int selectionStart;
    [Notify] private int selectionLength;
    // The lines a .twin entry's #If leaves out as twinBASIC compiles it at 32-bit (#1691), less any it can't decide.
    [Notify] private IReadOnlyList<LineRange> inactiveLines = [];
    public override object? Icon => null;
    protected override string ComputeTitle() => Entry.Name;
    public bool IsReadOnlyDocument => true;
    public override bool CanHoldUnsavedChanges => false;
    public bool IsReadOnlyRegion(int offset, int length) => false;
    public Func<int, int, bool> SnapshotReadOnlyRegions() => static (_, _) => true;

    public ArchiveDocumentViewModel(ArchiveProjectEntry entry, ILocalizationService localization,
        IConditionalCodeReader conditionalCode, string? buildType = null)
    {
        this.localization = localization;
        this.conditionalCode = conditionalCode;
        Entry = entry;
        Update(entry, buildType);
    }

    /// <param name="entry">The entry, from the project's latest snapshot.</param>
    /// <param name="buildType">The project's build type from its Settings, which <c>TWINBASIC_BUILD_TYPE</c> holds; null when
    /// the project names none, and conditions on it are then left ungreyed.</param>
    public void Update(ArchiveProjectEntry entry, string? buildType = null)
    {
        this.buildType = buildType;
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
        InactiveLines = preview?.Status == ArchiveTextStatus.Available && entry.Name.EndsWith(".twin", StringComparison.OrdinalIgnoreCase)
            ? InactiveLinesOf(preview.Text!, buildType)
            : [];
        Title = ComputeTitle();
    }

    internal ArchiveDocumentViewModel Reopen()
    {
        var reopened = new ArchiveDocumentViewModel(Entry, localization, conditionalCode, buildType);
        reopened.CaretOffset = Math.Clamp(CaretOffset, 0, reopened.Document.TextLength);
        return reopened;
    }

    private IReadOnlyList<LineRange> InactiveLinesOf(string text, string? buildType) =>
        conditionalCode.InactiveLines(text, new ConditionalConstants(ConditionalConstants.NoArguments)
        {
            BuiltIns = TwinBasicCompilationConstants.BuiltIns(win64: false, build: null, buildType),
            CallsReadZero = true,
            NullIsUndetermined = true,
        });

    private static void RejectEdit(object? sender, DocumentChangeEventArgs e) =>
        throw new InvalidOperationException("Archive documents are read-only.");
}
