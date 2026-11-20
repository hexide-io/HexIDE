using HexIDE.Lsp.Messages;

namespace HexIDE.Addins;

/// <summary>The code-window actions a running claimed command can request from the host.</summary>
public interface IAddinCodeEditorActions
{
    Task<bool> StartRenameAsync(string uri, Position position, CancellationToken cancellationToken);
    Task<bool> GoToLocationAsync(string uri, HexIDE.Lsp.Messages.Range range, CancellationToken cancellationToken);
    Task ShowReferencesAsync(IReadOnlyList<Location> locations, CancellationToken cancellationToken);
}
