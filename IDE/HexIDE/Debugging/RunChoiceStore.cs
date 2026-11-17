using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Debugging;

/// <summary>
/// What F5 runs, per project: an explicit choice of the interpreter or a launch an
/// add-in contributed, by its <see cref="ContributedDebugLaunch.Key"/>. Kept in the project's user sidecar.
/// </summary>
public interface IRunChoiceStore
{
    /// <summary>The stored launch, or null for the interpreter or no choice; see <see cref="HasChoice"/>.</summary>
    string? Get(ProjectDefinition project);

    /// <summary>Whether the user chose anything, including the interpreter.</summary>
    bool HasChoice(ProjectDefinition project);

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
    private readonly Dictionary<ProjectDefinition, string?> _choices = new(ReferenceEqualityComparer.Instance);

    public event Action<ProjectDefinition>? ChoiceChanged;

    public string? Get(ProjectDefinition project)
    {
        lock (_gate) return _choices.GetValueOrDefault(project);
    }

    public bool HasChoice(ProjectDefinition project)
    {
        lock (_gate) return _choices.ContainsKey(project);
    }

    public void Set(ProjectDefinition project, string? launch)
    {
        ArgumentNullException.ThrowIfNull(project);
        bool changed;
        lock (_gate)
        {
            var before = _choices.GetValueOrDefault(project);
            changed = !_choices.ContainsKey(project) || !string.Equals(before, launch, StringComparison.Ordinal);
            _choices[project] = launch;
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
