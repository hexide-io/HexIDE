namespace HexIDE.Lsp;

public interface IWorkspaceArtifactService
{
    /// <summary>Runs folder updates on the project-change debounce, before any imports.</summary>
    Task TrackFoldersAsync(LanguageServerRegistration server, Func<CancellationToken, Task> update,
        CancellationToken cancellationToken) => Task.CompletedTask;
    Task ImportAsync(LanguageServerRegistration server, Func<WorkspaceArtifactContext, CancellationToken, Task> import,
        CancellationToken cancellationToken);
    Task PrepareAsync(LanguageServerRegistration server, CancellationToken cancellationToken);

    /// <summary>
    /// Forgets a server taken out while the IDE runs: its artefact is not regenerated, its project not imported and its
    /// folders not tracked again. Only this registration: a later one under the same id is its own.
    /// </summary>
    Task ForgetAsync(LanguageServerRegistration server) => Task.CompletedTask;

    /// <summary>
    /// An opted-in project membership change or artefact regeneration requires a restart. Raised with the server's
    /// registration once per debounce, never for the write before a launch or a refused write alone. An identical
    /// artefact does not cause a restart; changed membership can, independently of the file. The registration, not
    /// its id: a server taken out while the IDE runs can have its id taken by another.
    /// </summary>
    event Action<LanguageServerRegistration>? RestartRequested;
    event Action<string, int>? FileChanged { add { } remove { } }
}

public interface IWorkspaceArtifactWorkspace : ILspWorkspace
{
    IReadOnlyList<WorkspaceArtifactContext> Projects { get; }
    event Action? ProjectChanged;
}

public enum WorkspaceArtifactDecision { Deny, Allow, AllowAlways }

public interface IWorkspaceArtifactConsent
{
    /// <summary>
    /// Whether this server may write this artefact in this workspace. A decision covers the server, the workspace, and the
    /// declaration's path and provider, so a changed declaration asks again rather than inheriting a grant for another file.
    /// </summary>
    Task<bool> AllowAsync(string serverId, string serverName, string workspace, string path, WorkspaceArtifactSpec spec);
}
