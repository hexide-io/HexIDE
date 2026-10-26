namespace HexIDE.Lsp;

public interface IWorkspaceArtifactService
{
    Task PrepareAsync(LanguageServerRegistration server, CancellationToken cancellationToken);
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
