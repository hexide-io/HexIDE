using System.Diagnostics;
using Microsoft.Extensions.Logging;
using StreamJsonRpc;

namespace HexIDE.Lsp;

/// <summary>
/// Desktop transport: launches a language server as a child process and speaks LSP over its stdio using
/// Content-Length framing. This is the only place in the LSP client that depends on
/// <see cref="System.Diagnostics.Process"/>; keeping it here lets <see cref="VBLspClient"/> stay
/// transport-agnostic (and platform-agnostic for future WebSocket / in-process transports).
///
/// <para>
/// <b>The command is given, not looked up.</b> This used to take <see cref="ILspServerLocator"/> and ask it
/// where "the" server was — which described a world with exactly one, bundled, server. A transport that
/// finds its own server can only ever launch that one, so every server the IDE could speak to had to be the
/// same server. The locator still exists and still solves a real problem (the bundled server's path differs
/// between a dev build and a publish), but it now computes the <em>default entry's</em> command rather than
/// being a dependency of the transport.
/// </para>
/// </summary>
public sealed class StdioProcessLspTransport : ILspTransport
{
    private readonly LspServerInfo _serverInfo;
    private readonly ILogger<StdioProcessLspTransport> _logger;
    private Process? _process;

    /// <param name="serverInfo">
    /// What to launch. Resolving this — and deciding what to do when it cannot be resolved — belongs to
    /// whoever built the registration, because an entry naming a server that is not there should not be
    /// offered at all rather than fail at connect time.
    /// </param>
    public StdioProcessLspTransport(LspServerInfo serverInfo, ILogger<StdioProcessLspTransport> logger)
    {
        _serverInfo = serverInfo;
        _logger = logger;
    }

    public bool IsAlive => _process is { HasExited: false };

    // A spawned subprocess is one-shot: a crash does not auto-respawn (preserves desktop behaviour).
    public bool CanReconnect => false;

    public event EventHandler? Closed;

    public Task<IJsonRpcMessageHandler?> ConnectAsync(IJsonRpcMessageFormatter formatter, CancellationToken cancellationToken = default)
    {
        var serverInfo = _serverInfo;
        _logger.LogInformation("Starting language server: {Exe}", serverInfo.FileName);

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
        else if (!OperatingSystem.IsWindows())
        {
            // Unix apphosts have historically shipped without the execute bit (neither the Content-copy
            // into the IDE output nor the publish tar sets it), so Process.Start on the apphost fails and
            // SILENTLY disables all LSP intelligence off-Windows. The previous fix was to ALWAYS launch the
            // managed dll through the shared `dotnet` host — correct for a framework-dependent layout, but
            // it reintroduces the very failure it was written to prevent in a SELF-CONTAINED bundle, which
            // carries its own runtime and has no `dotnet` on PATH at all.
            //
            // So: prefer the apphost whenever it is genuinely executable, and fall back to `dotnet <dll>`
            // only when it is not. The release pipeline chmod +x's the server apphost, so a published
            // tarball takes the first path and works with or without a machine-wide .NET install.
            //
            // This is not VB6-specific machinery even though a .NET apphost is what motivated it: the whole
            // branch is skipped for anything already executable, so an ordinary third-party server binary
            // never reaches it. When one DOES land here it is because the user named something they cannot
            // execute, and saying so is the right answer.
            var apphostIsExecutable = false;
            try
            {
                const UnixFileMode anyExecute =
                    UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
                apphostIsExecutable = File.Exists(serverInfo.FileName)
                    && (File.GetUnixFileMode(serverInfo.FileName) & anyExecute) != 0;
            }
            catch (IOException) { /* unreadable mode — fall through to the dotnet host */ }
            catch (UnauthorizedAccessException) { /* ditto */ }

            if (!apphostIsExecutable)
            {
                var dll = Path.ChangeExtension(serverInfo.FileName, ".dll");
                if (File.Exists(dll))
                {
                    arguments = string.IsNullOrEmpty(serverInfo.Arguments) ? $"\"{dll}\"" : $"\"{dll}\" {serverInfo.Arguments}";
                    fileName = "dotnet";
                }
                else
                {
                    _logger.LogWarning("LSP server apphost is not executable and no dll was found next to it ({Dll}); launching the apphost directly.", dll);
                }
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

        try
        {
            _process.Start();
            _process.BeginErrorReadLine();
        }
        catch (Exception ex)
        {
            // The server exe is missing or can't launch (Win32Exception / FileNotFoundException). Don't let it escape
            // as a faulted/unobserved task, and don't leave a never-started Process whose HasExited access throws —
            // IsAlive would then throw on every poll. Tear it down and report "no transport"; LSP features simply
            // degrade off (no diagnostics/definition/rename) rather than crashing the IDE.
            _logger.LogError(ex, "Failed to start the VB6 LSP server process ({File})", fileName);
            _process.Exited -= OnProcessExited;
            _process.Dispose();
            _process = null;
            return Task.FromResult<IJsonRpcMessageHandler?>(null);
        }

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
