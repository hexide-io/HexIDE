using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using HexIDE.Debugging;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Utils;

namespace HexIDE.Forms.ViewModels;

/// <summary>An item of the Run menu's lists: a choice of what F5 runs, or a launch to start once.</summary>
public sealed class RunMenuItemViewModel(string header, ICommand command, bool isChecked = false)
{
    public string Header { get; } = header;
    public ICommand Command { get; } = command;
    public bool IsChecked { get; } = isChecked;
}

/// <summary>
/// The Run menu's part in the launches add-ins contribute: Run With, which sets what F5 runs for the startup project,
/// and Start With, which runs a launch once. Both are hidden while no add-in offers a launch for the project.
/// </summary>
public sealed class RunWithMenuViewModel : INotifyPropertyChanged
{
    private readonly DebugLaunchService _launches;
    private readonly IRunChoiceStore _choices;
    private readonly IProjectManager _projects;
    private readonly IProjectRunnerService _runner;
    private readonly ILocalizationService _localization;

    public RunWithMenuViewModel(DebugLaunchService launches, IRunChoiceStore choices, IProjectManager projects,
        IProjectRunnerService runner, ILocalizationService localization)
    {
        _launches = launches;
        _choices = choices;
        _projects = projects;
        _runner = runner;
        _localization = localization;
        launches.LaunchesChanged += Refresh;
        choices.ChoiceChanged += _ => Refresh();
        projects.ProjectLoaded += _ => Refresh();
        projects.ProjectUnloaded += _ => Refresh();
        projects.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(IProjectManager.StartupProject)) Refresh();
        };
        Refresh();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>What F5 can run for the startup project: the interpreter first, then each launch that runs it.</summary>
    public ObservableCollection<RunMenuItemViewModel> Choices { get; } = [];

    /// <summary>Each launch that runs the startup project, to start once.</summary>
    public ObservableCollection<RunMenuItemViewModel> Starts { get; } = [];

    /// <summary>Whether any add-in offers a launch for the startup project.</summary>
    public bool HasLaunches => Starts.Count > 0;

    private void Refresh()
    {
        // A choice can arrive from the sidecar's read, off the UI thread; the menu's lists change only on it.
        if (!Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(Refresh);
            return;
        }

        Choices.Clear();
        Starts.Clear();
        if (_projects.StartupProject is { } project)
        {
            var offered = _launches.For(project);
            if (offered.Count > 0)
            {
                // The key, not the launch: a choice whose launch is gone is still the project's choice, and F5 says so.
                var chosen = _choices.Get(project);
                Choices.Add(new RunMenuItemViewModel(_localization.GetString("Str.Menu.Run.RunWith.Interpreter"),
                    new DelegateCommand(() => _choices.Set(project, null)), chosen is null));
                foreach (var launch in offered)
                {
                    var header = string.Format(_localization.GetString("Str.Menu.Run.RunWith.Launch"), launch.Title, launch.Origin);
                    Choices.Add(new RunMenuItemViewModel(header,
                        new DelegateCommand(() => _choices.Set(project, launch.Key)), chosen == launch.Key));
                    Starts.Add(new RunMenuItemViewModel(header, new DelegateCommand(() => Start(launch, project))));
                }
            }
        }
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasLaunches)));
    }

    // For the project the menu was built for, if it is still the startup project: the menu may be older than a change.
    private void Start(ContributedDebugLaunch launch, ProjectDefinition project)
    {
        if (!ReferenceEquals(_projects.StartupProject, project)) return;
        _runner.StartContributedLaunch(launch, project);
    }
}
