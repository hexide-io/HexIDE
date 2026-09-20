using System.ComponentModel;
using HexIDE.Addins;
using HexIDE.Events;
using HexIDE.IDE;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Addins;

public sealed class AddinEventService : IAddinEvents, IDisposable
{
    public event Action<AddinProjectEventArgs>? ProjectLoaded;
    public event Action<AddinProjectEventArgs>? ProjectUnloaded;
    public event Action<AddinFileEventArgs>? FileOpened;
    public event Action<AddinFileEventArgs>? FileClosed;
    public event Action? RunStarted;
    public event Action? RunStopped;

    private readonly IProjectManager _projectManager;
    private readonly IProjectRunnerService _projectRunnerService;
    private readonly IDisposable _fileOpenedSub;
    private readonly IDisposable _fileClosedSub;

    public AddinEventService(IProjectManager projectManager, IProjectRunnerService projectRunnerService, IEventBus eventBus)
    {
        _projectManager = projectManager;
        _projectRunnerService = projectRunnerService;

        projectManager.ProjectLoaded   += OnProjectLoaded;
        projectManager.ProjectUnloaded += OnProjectUnloaded;

        projectRunnerService.PropertyChanged += OnRunnerPropertyChanged;

        _fileOpenedSub = eventBus.Subscribe<FileOpenedEvent>(e =>
            FileOpened?.Invoke(Describe(e.Document, e.Title)));
        _fileClosedSub = eventBus.Subscribe<FileClosedEvent>(e =>
            FileClosed?.Invoke(Describe(e.Document, e.Title)));
    }

    /// <summary>
    /// What an add-in is told about a tab that opened or closed.
    /// </summary>
    /// <remarks>
    /// Both fields used to be the tab's <em>title</em> — which is localized, carries the project name and a
    /// "(Code)" suffix, and is not a path by any reading. An add-in matching it against a name it had from
    /// anywhere else never matched. A tab with no document keeps the title as its name, because there is
    /// nothing else to call it, but its path is now empty rather than a title pretending to be one.
    /// </remarks>
    private static AddinFileEventArgs Describe(DocumentIdentity? document, string title) =>
        document is null
            ? new AddinFileEventArgs(string.Empty, title)
            : new AddinFileEventArgs(document.AbsolutePath ?? string.Empty, document.Name,
                document.Project.Name);

    private void OnProjectLoaded(ProjectDefinition p) =>
        ProjectLoaded?.Invoke(new AddinProjectEventArgs(p.AbsolutePath ?? string.Empty, p.Name));

    private void OnProjectUnloaded(ProjectDefinition p) =>
        ProjectUnloaded?.Invoke(new AddinProjectEventArgs(p.AbsolutePath ?? string.Empty, p.Name));

    private void OnRunnerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(IProjectRunnerService.IsRunning)) return;
        if (_projectRunnerService.IsRunning)
            RunStarted?.Invoke();
        else
            RunStopped?.Invoke();
    }

    public void Dispose()
    {
        _projectManager.ProjectLoaded   -= OnProjectLoaded;
        _projectManager.ProjectUnloaded -= OnProjectUnloaded;
        _projectRunnerService.PropertyChanged -= OnRunnerPropertyChanged;
        _fileOpenedSub.Dispose();
        _fileClosedSub.Dispose();
    }
}
