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
/// The Run menu's part in the launches add-ins contribute: F5 Runs, which sets what F5 runs for the startup project,
/// and Start Once With, which runs a launch once.
/// </summary>
/// <remarks>
/// Built when the Run menu opens (<see cref="Refresh"/>), so a launch is asked whether it runs the project then, and not
/// when a project loads or the startup project changes. F5 Runs is shown while a launch runs the project, or while the
/// project has a choice, a gone launch's included, so the user can always set it back.
/// </remarks>
public sealed class RunWithMenuViewModel(DebugLaunchService launches, IRunChoiceStore choices, IProjectManager projects,
    IProjectRunnerService runner, ILocalizationService localization) : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>What F5 can run for the startup project: the IDE first, then each launch that runs it.</summary>
    public ObservableCollection<RunMenuItemViewModel> Choices { get; } = [];

    /// <summary>Each launch that runs the startup project, to start once.</summary>
    public ObservableCollection<RunMenuItemViewModel> Starts { get; } = [];

    /// <summary>Whether F5 Runs is shown: a launch runs the startup project, or the project has a choice.</summary>
    public bool HasChoices => Choices.Count > 0;

    /// <summary>Whether Start Once With is shown: a launch runs the startup project.</summary>
    public bool HasLaunches => Starts.Count > 0;

    /// <summary>Builds the lists for the startup project as it is now; called as the Run menu opens.</summary>
    public void Refresh()
    {
        Choices.Clear();
        Starts.Clear();
        if (projects.StartupProject is { } project)
        {
            var offered = launches.For(project);
            var chosen = choices.Get(project);
            if (offered.Count > 0 || chosen is not null)
            {
                Choices.Add(new RunMenuItemViewModel(localization.GetString("Str.Menu.Run.RunWith.Interpreter"),
                    new DelegateCommand(() => choices.Set(project, null)), chosen is null));
                foreach (var launch in offered)
                {
                    var header = string.Format(localization.GetString("Str.Menu.Run.RunWith.Launch"), launch.Title, launch.Origin);
                    Choices.Add(new RunMenuItemViewModel(header,
                        new DelegateCommand(() => choices.Set(project, launch.Key)), chosen == launch.Key));
                    Starts.Add(new RunMenuItemViewModel(header,
                        new DelegateCommand(() => Start(launch, project), () => !runner.IsRunning)));
                }

                // A choice whose launch is gone, or no longer runs the project: shown, checked and disabled, beside the IDE.
                if (chosen is not null && offered.All(launch => launch.Key != chosen))
                    Choices.Add(new RunMenuItemViewModel(
                        string.Format(localization.GetString("Str.Menu.Run.RunWith.Unavailable"), ContributedDebugLaunch.TitleOf(chosen)),
                        new DelegateCommand(() => { }, () => false), isChecked: true));
            }
        }
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasChoices)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasLaunches)));
    }

    // For the project the menu was built for, if it is still the startup project: the menu may be older than a change.
    private void Start(ContributedDebugLaunch launch, ProjectDefinition project)
    {
        if (!ReferenceEquals(projects.StartupProject, project)) return;
        runner.StartContributedLaunch(launch, project);
    }
}
