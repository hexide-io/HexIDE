using System;
using System.IO;
using AvaloniaEdit.Document;
using HexIDE.Events;
using HexIDE.IDE;
using PropertyChanged.SourceGenerator;

namespace HexIDE.Forms.ViewModels;

public partial class ProjectFileDocumentViewModel : BaseEditorWindowViewModel, ISearchableDocument
{
    public ProjectFileSource Source { get; }
    [Notify] private TextDocument document = new();
    [Notify] private string? loadError;
    [Notify] private int caretOffset;
    [Notify] private int selectionStart;
    [Notify] private int selectionLength;
    public override object? Icon => null;
    public override bool CanHoldUnsavedChanges => false;
    public bool IsReadOnlyDocument => true;
    public Func<int, int, bool> SnapshotReadOnlyRegions() => static (_, _) => true;
    protected override string ComputeTitle() => Path.GetFileName(Source.Path) ?? "";

    public ProjectFileDocumentViewModel(ProjectFileSource source, IEventBus? events)
    {
        Source = source;
        if (events != null)
            AutoDispose(events.Subscribe<ProjectFileSavedEvent>(saved =>
            {
                if (saved.Owner == Source.Owner) Refresh();
            }));
        Refresh();
    }

    public void Refresh()
    {
        string text;
        try
        {
            text = Source.Path is { } path ? Vb6TextFile.Decode(File.ReadAllBytes(path), Source.Encoding) : "";
            LoadError = null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.DecoderFallbackException)
        {
            text = "";
            LoadError = ex.Message;
        }
        var buffer = new TextDocument(text);
        buffer.Changing += (_, _) => throw new InvalidOperationException("Project files are read-only.");
        Document = buffer;
        CaretOffset = SelectionStart = SelectionLength = 0;
        Title = ComputeTitle();
    }
}
