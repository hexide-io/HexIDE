using System.Diagnostics;
using Microsoft.Extensions.Logging;
using StreamJsonRpc;

namespace HexIDE.Lsp;

/// <summary>
/// Desktop transport: launches the VB LSP server as a child process and speaks LSP over its
/// stdio using Content-Length framing. This is the only place in the LSP client that depends on
/// <see cref="System.Diagnostics.Process"/>; keeping it here lets <see cref="VBLspClient"/> stay
/// transport-agnostic (and platform-agnostic for future WebSocket / in-process transports).
/// </summary>
public sealed class StdioProcessLspTransport : ILspTransport
{
    private readonly ILspServerLocator _locator;
    private readonly ILogger<StdioProcessLspTransport> _logger;
    private Process? _process;

    public StdioProcessLspTransport(ILspServerLocator locator, ILogger<StdioProcessLspTransport> logger)
    {
        _locator = locator;
        _logger = logger;
    }

    public bool IsAlive => _process is { HasExited: false };

    // A spawned subprocess is one-shot: a crash does not auto-respawn (preserves desktop behaviour).
    public bool CanReconnect => false;

    public event EventHandler? Closed;

    public Task<IJsonRpcMessageHandler?> ConnectAsync(IJsonRpcMessageFormatter formatter, CancellationToken cancellationToken = default)
    {
        var serverInfo = _locator.FindLspServer();
        if (serverInfo is null)
        {
            _logger.LogWarning("VB LSP server not found — LSP diagnostics disabled.");
            return Task.FromResult<IJsonRpcMessageHandler?>(null);
        }

        _logger.LogInformation("Starting VB LSP server: {Exe}", serverInfo.FileName);

        // Debug proxy: if VB6_LSP_DEBUG_PROXY=1 is set, route traffic through
        // LspProxy.exe which logs all LSP frames to its stderr (visible in the
        // debug output window). The proxy forwards everything unchanged.
        string fileName = serverInfo.FileName;
        string arguments = serverInfo.Arguments;
        var useProxy = Environment.GetEnvironmentVariable("VB6_LSP_DEBUG_PROXY") == "1";
        if (useProxy)
        {
            var proxyExe = Path.Combine(AppContext.BaseDirectory, "HexIDE.LspProxy.exe");
            if (!File.Exists(proxyExe))
                proxyExe = Path.Combine(AppContext.BaseDirectory, "HexIDE.LspProxy");
            if (File.Exists(proxyExe))
            {
                _logger.LogInformation("[proxy] Debug proxy active: {Proxy}", proxyExe);
                arguments = $"\"{serverInfo.FileName}\" {serverInfo.Arguments}".TrimEnd();
                fileName = proxyExe;
            }
            else
            {
                _logger.LogWarning("[proxy] VB6_LSP_DEBUG_PROXY=1 but proxy exe not found at {Path}", proxyExe);
            }
        }

        _process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                WorkingDirectory = serverInfo.WorkingDirectory,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            },
            EnableRaisingEvents = true
        };

        _process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is { Length: > 0 })
                _logger.LogDebug("[vb-lsp stderr] {Data}", e.Data);
        };

        _process.Exited += OnProcessExited;

        _process.Start();
        _process.BeginErrorReadLine();

        var handler = new HeaderDelimitedMessageHandler(
            _process.StandardInput.BaseStream,
            _process.StandardOutput.BaseStream,
            formatter);

        return Task.FromResult<IJsonRpcMessageHandler?>(handler);
    }

    private void OnProcessExited(object? sender, EventArgs e)
    {
        _logger.LogWarning("VB6 LSP server process exited");
        Closed?.Invoke(this, EventArgs.Empty);
    }

    public ValueTask DisposeAsync()
    {
        if (_process is not null)
        {
            _process.Exited -= OnProcessExited;
            try { _process.Kill(); } catch { /* already gone */ }
            _process.Dispose();
            _process = null;
        }

        return ValueTask.CompletedTask;
    }
}
