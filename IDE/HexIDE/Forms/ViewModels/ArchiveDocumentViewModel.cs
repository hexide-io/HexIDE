using System;
using System.Collections.Generic;
using System.Linq;
using AvaloniaEdit.Document;
using HexIDE.Language;
using HexIDE.Lsp;
using HexIDE.Controls;
using HexIDE.Localization;
using HexIDE.TwinBasic;
using HexIDE.Projects;
using PropertyChanged.SourceGenerator;

namespace HexIDE.Forms.ViewModels;

public partial class ArchiveDocumentViewModel : BaseEditorWindowViewModel, ISearchableDocument
{
    private readonly ILocalizationService localization;
    private readonly IConditionalCodeReader conditionalCode;
    private string? buildType;
    private string? languageId;
    private readonly ILspClient? lspClient;
    private LspDocumentSession? session;
    public IReadOnlyList<LspMarker> Markers { get; private set; } = [];
    public event Action<IReadOnlyList<LspMarker>>? MarkersChanged;
    public string DocumentUri => Entry.DocumentUri;
    public ContainerEntry Entry { get; private set; }
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

    public ArchiveDocumentViewModel(ContainerEntry entry, ILocalizationService localization,
        IConditionalCodeReader conditionalCode, string? buildType = null, ILspClient? lspClient = null, string? languageId = null)
    {
        this.lspClient = lspClient;
        this.localization = localization;
        this.conditionalCode = conditionalCode;
        Entry = entry;
        Update(entry, buildType, languageId);
    }

    public ArchiveDocumentViewModel(ArchiveProjectEntry entry, ILocalizationService localization,
        IConditionalCodeReader conditionalCode, string? buildType = null, ILspClient? lspClient = null, string? languageId = null)
        : this(ContainerEntry.FromArchive(entry), localization, conditionalCode, buildType, lspClient, languageId) { }

    /// <param name="entry">The entry, from the project's latest snapshot.</param>
    /// <param name="buildType">The project's build type from its Settings, which <c>TWINBASIC_BUILD_TYPE</c> holds; null when
    /// the project names none, and conditions on it are then left ungreyed.</param>
    public void Update(ContainerEntry entry, string? buildType = null, string? languageId = null)
    {
        session?.Dispose();
        session = null;
        Markers = [];
        MarkersChanged?.Invoke(Markers);
        this.buildType = buildType;
        this.languageId = languageId;
        Entry = entry;
        var preview = entry.ReadText();
        var buffer = new TextDocument(preview?.Text ?? "");
        buffer.UndoStack.ClearAll();
        // Protect programmatic edits as well as keyboard, paste and Replace operations.
        buffer.Changing += RejectEdit;
        Document = buffer;
        CaretOffset = SelectionStart = SelectionLength = 0;
        Notice = localization.GetString("Str.Archive.ReadOnly") + " " +
            (preview is null ? "" : preview.Status == ArchiveTextStatus.Available ? "" :
                localization.GetString("Str.Archive." + preview.Status));
        if (entry.NoticeKey is { } noticeKey) Notice = localization.GetString(noticeKey);
        Metadata = entry.Archive is { } archive ? string.Format(localization.GetString("Str.Archive.Metadata"),
            entry.Identity.ContainerPath, string.Join(" / ", entry.Identity.Segments),
            archive.Source.Category, archive.Source.Flags, archive.Source.Revision,
            (archive as ArchiveProjectDocument)?.File.Content.Length ?? 0) : "";
        InactiveLines = entry.Archive != null && preview?.Status == ArchiveTextStatus.Available && entry.Name.EndsWith(".twin", StringComparison.OrdinalIgnoreCase)
            ? InactiveLinesOf(preview.Text!, buildType)
            : [];
        Title = ComputeTitle();
        if (lspClient != null && preview?.Status == ArchiveTextStatus.Available)
        {
            session = new LspDocumentSession(lspClient, Document, DocumentUri, isProjectMember: entry.Source != null, languageId: languageId);
            session.MarkersChanged += markers =>
            {
                Markers = markers;
                MarkersChanged?.Invoke(markers);
            };
            session.Start();
        }
    }

    internal ArchiveDocumentViewModel Reopen()
    {
        var reopened = new ArchiveDocumentViewModel(Entry, localization, conditionalCode, buildType, lspClient, languageId);
        reopened.CaretOffset = Math.Clamp(CaretOffset, 0, reopened.Document.TextLength);
        return reopened;
    }

    public override void Dispose()
    {
        session?.Dispose();
        session = null;
        base.Dispose();
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
