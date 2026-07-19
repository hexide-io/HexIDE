using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace HexIDE.Lsp;

public class LspServerLocator : ILspServerLocator
{
    private readonly ILogger<LspServerLocator> _logger;

    public LspServerLocator(ILogger<LspServerLocator> logger)
    {
        _logger = logger;
    }

    public LspServerInfo? FindLspServer()
    {
        var exeName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "HexIDE.VbLspServer.exe"
            : "HexIDE.VbLspServer";

        var appDir = AppContext.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(appDir, exeName),
            Path.Combine(appDir, "..", exeName),
            Path.Combine(appDir, "..", "..", exeName),
            Path.Combine(appDir, "..", "..", "..", exeName),
            Path.Combine(appDir, "..", "..", "..", "..", exeName),
        };

        foreach (var candidate in candidates)
        {
            var normalized = Path.GetFullPath(candidate);
            if (File.Exists(normalized))
            {
                _logger.LogInformation("Found VB LSP server at {Path}", normalized);
                return new LspServerInfo(normalized, "", Path.GetDirectoryName(normalized)!);
            }
        }

        _logger.LogWarning("VB LSP server executable not found near {AppDir}", appDir);
        return null;
    }
}
