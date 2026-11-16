using System;
using System.IO;
using System.Threading.Tasks;
using AvaloniaEdit.Document;
using HexIDE.Events;
using HexIDE.IDE;
using HexIDE.Projects;
using PropertyChanged.SourceGenerator;

namespace HexIDE.Forms.ViewModels;

public partial class ProjectFileDocumentViewModel : BaseEditorWindowViewModel, ISearchableDocument
{
    public ProjectFileSource Source { get; private set; }
    private readonly IProjectService? projects;
    private byte[] originalBytes = [];
    private string savedText = "";
    internal bool Applying { get; private set; }
    public void Save() => SaveAsync().ListenErrors();
    public Task<bool> SaveAsync() => projects is null || Applying ? Task.FromResult(false) : projects.SaveProjectFileText(this);
    public Task<bool> ConfirmCloseAsync() => Applying ? Task.FromResult(false)
        : projects is null ? Task.FromResult(!IsModified) : projects.ConfirmProjectFileClose(this);
    internal byte[] OriginalBytes => originalBytes;
    internal async Task<T> DuringApply<T>(Func<Task<T>> apply)
    {
        Applying = true;
        try { return await apply(); }
        finally { Applying = false; }
    }
    internal void Rebind(ProjectDefinition project, bool keepEdits = false)
    {
        var previous = (Document, CaretOffset, SelectionStart, SelectionLength);
        Source = new ProjectFileSource(project);
        OnPropertyChanged(nameof(Source));
        Refresh();
        if (!keepEdits) return;
        Document = previous.Document;
        CaretOffset = previous.CaretOffset;
        SelectionStart = previous.SelectionStart;
        SelectionLength = previous.SelectionLength;
        IsModified = Document.Text != savedText;
    }
    [Notify] private TextDocument document = new();
    [Notify] private string? loadError;
    private void OnLoadErrorChanged() => OnPropertyChanged(nameof(IsReadOnlyDocument));
    [Notify] private int caretOffset;
    [Notify] private int selectionStart;
    [Notify] private int selectionLength;
    public override object? Icon => null;
    public override bool CanHoldUnsavedChanges => !IsReadOnlyDocument;
    public bool IsReadOnlyDocument => LoadError is not null || projects is null || Source.Owner is not ProjectDefinition
        { FormFormat: null, ProjectFileWriter: null } || Source.Path is not { } path
        || !path.EndsWith(".vbp", StringComparison.OrdinalIgnoreCase);
    public Func<int, int, bool> SnapshotReadOnlyRegions() => (_, _) => IsReadOnlyDocument;
    protected override string ComputeTitle() => Path.GetFileName(Source.Path) ?? "";

    public ProjectFileDocumentViewModel(ProjectFileSource source, IEventBus? events, IProjectService? projects = null)
    {
        Source = source;
        this.projects = projects;
        if (events != null)
            AutoDispose(events.Subscribe<ProjectFileSavedEvent>(saved =>
            {
                if (saved.Owner == Source.Owner && !IsModified) Refresh();
            }));
        Refresh();
    }

    public void Refresh()
    {
        string text;
        try
        {
            originalBytes = Source.Path is { } path ? File.ReadAllBytes(path) : [];
            text = Vb6TextFile.Decode(originalBytes, Source.Encoding);
            LoadError = null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.DecoderFallbackException)
        {
            text = "";
            LoadError = ex.Message;
        }
        var buffer = new TextDocument(text);
        savedText = text;
        buffer.Changing += (_, _) =>
        {
            if (IsReadOnlyDocument) throw new InvalidOperationException("Project files are read-only.");
        };
        buffer.TextChanged += (_, _) => IsModified = buffer.Text != savedText;
        Document = buffer;
        IsModified = false;
        CaretOffset = SelectionStart = SelectionLength = 0;
        Title = ComputeTitle();
    }
}
