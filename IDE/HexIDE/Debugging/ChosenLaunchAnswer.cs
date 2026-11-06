using System.ComponentModel;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Debugging;

/// <summary>
/// Whether the launch a project's user chose for F5 says it runs the project, as F5's state shows it. The launch's own
/// answer, kept until what it was asked about changes, as F5's state is read at every requery, and so at every edit.
/// </summary>
/// <remarks>
/// The answer is asked again when the choice, the launch or the project is another, and when what the launch is told of
/// the project changes: its name, file or startup object, its references, or its members and their names and files. A
/// start asks again whatever was kept (<see cref="Ask"/>), so a kept answer never starts a launch that says no.
/// </remarks>
internal sealed class ChosenLaunchAnswer(DebugLaunchService launches, IRunChoiceStore choices)
{
    private static readonly HashSet<string?> DescribedProperties =
    [
        nameof(ProjectDefinition.Name), nameof(ProjectDefinition.AbsolutePath), nameof(ProjectDefinition.StartupForm),
        nameof(ProjectDefinition.StartsAtSubMain), nameof(ProjectDefinition.References),
    ];

    private static readonly HashSet<string?> DescribedMemberProperties = [nameof(ModuleDefinition.Name), nameof(ModuleDefinition.AbsolutePath)];

    private readonly object _gate = new();
    private readonly List<INotifyPropertyChanged> _members = [];
    private (ProjectDefinition Project, string Key, ContributedDebugLaunch Launch, bool Claims)? _kept;
    private ProjectDefinition? _watched;

    /// <summary>Raised when a kept answer is let go of because the project changed, so F5's state is read again.</summary>
    public event Action? Forgotten;

    /// <summary>The chosen launch, while it is registered and its kept answer is that it runs the project; null otherwise.</summary>
    public ContributedDebugLaunch? Kept(ProjectDefinition project)
    {
        if (Chosen(project) is not var (key, launch)) return null;
        lock (_gate)
            // The registration itself, not its value: a launch contributed again is another, which may answer otherwise.
            if (_kept is { } kept && kept.Project == project && kept.Key == key && ReferenceEquals(kept.Launch, launch))
                return kept.Claims ? launch : null;
        return Answer(project, key, launch);
    }

    /// <summary>The chosen launch, while it is registered and says, asked now, that it runs the project; null otherwise.</summary>
    public ContributedDebugLaunch? Ask(ProjectDefinition project) =>
        Chosen(project) is var (key, launch) ? Answer(project, key, launch) : null;

    private (string Key, ContributedDebugLaunch Launch)? Chosen(ProjectDefinition project) =>
        choices.Get(project) is { } key && launches.Find(key) is { } launch ? (key, launch) : null;

    private ContributedDebugLaunch? Answer(ProjectDefinition project, string key, ContributedDebugLaunch launch)
    {
        var claims = launches.Claims(launch, project);
        lock (_gate)
        {
            Watch(project);
            _kept = (project, key, launch, claims);
        }
        return claims ? launch : null;
    }

    private void Watch(ProjectDefinition project)
    {
        if (_watched == project) return;
        Unwatch();
        _watched = project;
        project.PropertyChanged += ProjectChanged;
        project.ObjectReferencesChanged += ReferencesChanged;
        project.ModuleAdded += MemberAdded;
        project.ModuleDeleted += MemberDeleted;
        project.FormAdded += MemberAdded;
        project.FormDeleted += MemberDeleted;
        foreach (var member in project.Modules.Cast<INotifyPropertyChanged>().Concat(project.Forms))
            WatchMember(member);
    }

    private void Unwatch()
    {
        if (_watched is not { } project) return;
        project.PropertyChanged -= ProjectChanged;
        project.ObjectReferencesChanged -= ReferencesChanged;
        project.ModuleAdded -= MemberAdded;
        project.ModuleDeleted -= MemberDeleted;
        project.FormAdded -= MemberAdded;
        project.FormDeleted -= MemberDeleted;
        foreach (var member in _members) member.PropertyChanged -= MemberChanged;
        _members.Clear();
        _watched = null;
    }

    private void WatchMember(INotifyPropertyChanged member)
    {
        member.PropertyChanged += MemberChanged;
        _members.Add(member);
    }

    private void ProjectChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (DescribedProperties.Contains(e.PropertyName)) Forget();
    }

    private void ReferencesChanged(ProjectDefinition _) => Forget();

    private void MemberChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (DescribedMemberProperties.Contains(e.PropertyName)) Forget();
    }

    private void MemberAdded<T>(ProjectDefinition _, T member) where T : INotifyPropertyChanged
    {
        lock (_gate) WatchMember(member);
        Forget();
    }

    private void MemberDeleted<T>(ProjectDefinition _, T member) where T : INotifyPropertyChanged
    {
        member.PropertyChanged -= MemberChanged;
        lock (_gate) _members.Remove(member);
        Forget();
    }

    private void Forget()
    {
        lock (_gate)
        {
            if (_kept is null) return;
            _kept = null;
        }
        Forgotten?.Invoke();
    }
}
