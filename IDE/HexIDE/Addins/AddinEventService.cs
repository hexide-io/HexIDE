using System.ComponentModel;
using HexIDE.Addins;
using HexIDE.Events;
using HexIDE.IDE;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Addins;

public sealed class AddinEventService : IAddinEvents, IDisposable
{
    private event Action<AddinProjectEventArgs>? _projectLoaded;
    public event Action<AddinProjectEventArgs>? ProjectLoaded
    {
        add
        {
            _projectLoaded += value;
            AddinInitializationScope.Record(() => _projectLoaded -= value);
        }
        remove => _projectLoaded -= value;
    }
    private event Action<AddinProjectEventArgs>? _projectUnloaded;
    public event Action<AddinProjectEventArgs>? ProjectUnloaded
    {
        add
        {
            _projectUnloaded += value;
            AddinInitializationScope.Record(() => _projectUnloaded -= value);
        }
        remove => _projectUnloaded -= value;
    }
    private event Action<AddinFileEventArgs>? _fileOpened;
    public event Action<AddinFileEventArgs>? FileOpened
    {
        add
        {
            _fileOpened += value;
            AddinInitializationScope.Record(() => _fileOpened -= value);
        }
        remove => _fileOpened -= value;
    }
    private event Action<AddinFileEventArgs>? _fileClosed;
    public event Action<AddinFileEventArgs>? FileClosed
    {
        add
        {
            _fileClosed += value;
            AddinInitializationScope.Record(() => _fileClosed -= value);
        }
        remove => _fileClosed -= value;
    }
    private event Action? _runStarted;
    public event Action? RunStarted
    {
        add
        {
            _runStarted += value;
            AddinInitializationScope.Record(() => _runStarted -= value);
        }
        remove => _runStarted -= value;
    }
    private event Action? _runStopped;
    public event Action? RunStopped
    {
        add
        {
            _runStopped += value;
            AddinInitializationScope.Record(() => _runStopped -= value);
        }
        remove => _runStopped -= value;
    }

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
            _fileOpened?.Invoke(Describe(e.Document, e.Title)));
        _fileClosedSub = eventBus.Subscribe<FileClosedEvent>(e =>
            _fileClosed?.Invoke(Describe(e.Document, e.Title)));
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
        _projectLoaded?.Invoke(new AddinProjectEventArgs(p.AbsolutePath ?? string.Empty, p.Name));

    private void OnProjectUnloaded(ProjectDefinition p) =>
        _projectUnloaded?.Invoke(new AddinProjectEventArgs(p.AbsolutePath ?? string.Empty, p.Name));

    private void OnRunnerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(IProjectRunnerService.IsRunning)) return;
        if (_projectRunnerService.IsRunning)
            _runStarted?.Invoke();
        else
            _runStopped?.Invoke();
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
