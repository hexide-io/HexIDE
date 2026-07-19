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
            FileOpened?.Invoke(new AddinFileEventArgs(e.Title, e.Title)));
        _fileClosedSub = eventBus.Subscribe<FileClosedEvent>(e =>
            FileClosed?.Invoke(new AddinFileEventArgs(e.Title, e.Title)));
    }

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
