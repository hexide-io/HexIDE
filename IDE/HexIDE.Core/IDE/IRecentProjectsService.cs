namespace HexIDE.IDE;

/// <summary>
/// Tracks recently opened project file paths.
/// Implementations persist the list between sessions.
/// </summary>
public interface IRecentProjectsService
{
    /// <summary>Fired whenever the recent list changes (Add, Remove, Clear).</summary>
    event Action? Changed;

    /// <summary>
    /// Adds a project path to the recent list (moves to front if already present).
    /// </summary>
    void Add(string absolutePath);

    /// <summary>
    /// Removes a specific path from the recent list.
    /// </summary>
    void Remove(string absolutePath);

    /// <summary>
    /// Returns the recent project paths, most recent first.
    /// </summary>
    IReadOnlyList<string> GetRecent();

    /// <summary>
    /// Clears all recent project entries.
    /// </summary>
    void Clear();

    /// <summary>
    /// Maximum number of recent entries to keep.
    /// </summary>
    int MaxEntries { get; }

    /// <summary>
    /// Whether to suppress the New Project dialog on startup.
    /// </summary>
    bool SuppressNewProjectDialog { get; set; }
}
