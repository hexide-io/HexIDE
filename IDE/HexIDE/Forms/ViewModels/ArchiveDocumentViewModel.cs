using System;
using System.Collections.Generic;
using System.Linq;
using AvaloniaEdit.Document;
using HexIDE.Language;
using HexIDE.Localization;
using HexIDE.Runtime.Interpreter;
using HexIDE.Runtime.Interpreter.ConditionalCompilation;
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
    // The lines a .twin entry's #If leaves out as twinBASIC compiles it at 32-bit (#1691), less any it can't decide.
    [Notify] private IReadOnlyList<LineRange> inactiveLines = [];
    public override object? Icon => null;
    protected override string ComputeTitle() => Entry.Name;
    public bool IsReadOnlyDocument => true;
    public override bool CanHoldUnsavedChanges => false;
    public bool IsReadOnlyRegion(int offset, int length) => false;
    public Func<int, int, bool> SnapshotReadOnlyRegions() => static (_, _) => true;

    public ArchiveDocumentViewModel(ArchiveProjectEntry entry, ILocalizationService localization, string? buildType = null)
    {
        this.localization = localization;
        Entry = entry;
        Update(entry, buildType);
    }

    /// <param name="entry">The entry, from the project's latest snapshot.</param>
    /// <param name="buildType">The project's build type from its Settings, which <c>TWINBASIC_BUILD_TYPE</c> holds; null when
    /// the project names none, and conditions on it are then left ungreyed.</param>
    public void Update(ArchiveProjectEntry entry, string? buildType = null)
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
        InactiveLines = preview?.Status == ArchiveTextStatus.Available && entry.Name.EndsWith(".twin", StringComparison.OrdinalIgnoreCase)
            ? InactiveLinesOf(preview.Text!, entry.Name, buildType)
            : [];
        Title = ComputeTitle();
    }

    internal ArchiveDocumentViewModel Reopen()
    {
        var reopened = new ArchiveDocumentViewModel(Entry, localization);
        reopened.CaretOffset = Math.Clamp(CaretOffset, 0, reopened.Document.TextLength);
        return reopened;
    }

    private static IReadOnlyList<LineRange> InactiveLinesOf(string text, string name, string? buildType)
    {
        var builtIns = new Dictionary<string, Vb6Value>(StringComparer.OrdinalIgnoreCase);
        foreach (var (constant, value) in TwinBasicCompilationConstants.BuiltIns(win64: false, build: null, buildType))
            builtIns[constant] = value switch
            {
                int number => new Vb6Value(number).AsVariant(),
                string s => new Vb6Value(s).AsVariant(),
                _ => Vb6Value.Null,
            };
        var constants = new CompilationConstants(CompilationArguments.None, builtIns) { CallsReadZero = true, NullIsUndetermined = true };
        try
        {
            var result = ConditionalCompiler.Preprocess(text, constants, name);
            return result.InactiveLines.Where(range =>
                !result.UndeterminedLines.Any(unknown => unknown.First <= range.Last && unknown.Last >= range.First)).ToArray();
        }
        catch (VBCompileErrorException)
        {
            return [];
        }
    }

    private static void RejectEdit(object? sender, DocumentChangeEventArgs e) =>
        throw new InvalidOperationException("Archive documents are read-only.");
}
