namespace HexIDE.Lsp;

/// <summary>Information about where to find and launch the LSP server executable.</summary>
public record LspServerInfo(string FileName, string Arguments, string WorkingDirectory);

/// <summary>Finds the VB LSP server executable.</summary>
public interface ILspServerLocator
{
    /// <summary>Returns launch info for the VB LSP server, or null if the executable is not found.</summary>
    LspServerInfo? FindLspServer();
}
