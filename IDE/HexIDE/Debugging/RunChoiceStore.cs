using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Debugging;

/// <summary>
/// What F5 runs, per project: the interpreter, which is every project's until its user says otherwise, or a launch an
/// add-in contributed, by its <see cref="ContributedDebugLaunch.Key"/>. Kept in the project's user sidecar.
/// </summary>
public interface IRunChoiceStore
{
    /// <summary>The launch F5 runs for this project, or null for the interpreter.</summary>
    string? Get(ProjectDefinition project);

    /// <summary>Remembers what F5 runs for this project; null is the interpreter.</summary>
    void Set(ProjectDefinition project, string? launch);

    /// <summary>Forgets a project's choice, as the project unloads.</summary>
    void ClearProject(ProjectDefinition project);

    /// <summary>Raised when a choice is set or cleared, with its project.</summary>
    event Action<ProjectDefinition>? ChoiceChanged;
}

public sealed class RunChoiceStore : IRunChoiceStore
{
    private readonly object _gate = new();

    // By instance, as the sidecar's projects are: two projects of one group can share a name, and one never saved has
    // no path.
    private readonly Dictionary<ProjectDefinition, string> _choices = new(ReferenceEqualityComparer.Instance);

    public event Action<ProjectDefinition>? ChoiceChanged;

    public string? Get(ProjectDefinition project)
    {
        lock (_gate) return _choices.GetValueOrDefault(project);
    }

    public void Set(ProjectDefinition project, string? launch)
    {
        ArgumentNullException.ThrowIfNull(project);
        bool changed;
        lock (_gate)
        {
            var before = _choices.GetValueOrDefault(project);
            changed = !string.Equals(before, launch, StringComparison.Ordinal);
            if (launch is null) _choices.Remove(project);
            else _choices[project] = launch;
        }
        if (changed) ChoiceChanged?.Invoke(project);
    }

    public void ClearProject(ProjectDefinition project)
    {
        bool removed;
        lock (_gate) removed = _choices.Remove(project);
        if (removed) ChoiceChanged?.Invoke(project);
    }
}
