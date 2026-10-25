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
    Task<bool> AllowAsync(string serverId, string serverName, string workspace, string path);
}
