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
    private readonly ILspWorkspace? _workspace;
    private Process? _process;

    /// <param name="serverInfo">
    /// What to launch. Resolving this — and deciding what to do when it cannot be resolved — belongs to
    /// whoever built the registration, because an entry naming a server that is not there should not be
    /// offered at all rather than fail at connect time.
    /// </param>
    /// <param name="workspace">
    /// Consulted at connect time when <paramref name="serverInfo"/> names no working directory of its own,
    /// so a server launched lazily runs in whichever project is open by then. An explicit working directory
    /// always wins: a user who named one meant it.
    /// </param>
    public StdioProcessLspTransport(
        LspServerInfo serverInfo, ILogger<StdioProcessLspTransport> logger, ILspWorkspace? workspace = null)
    {
        _serverInfo = serverInfo;
        _logger = logger;
        _workspace = workspace;
    }

    public bool IsAlive => _process is { HasExited: false };

    // A spawned subprocess is one-shot: a crash does not auto-respawn (preserves desktop behaviour).
    /// <summary>Why the last connect attempt failed, in this transport's words. See ILspTransport.</summary>
    public string? LastFailure { get; private set; }

    /// <summary>
    /// Nothing. This transport spawns the server, so its start, its standard error and its exit code are
    /// all HexIDE's to observe.
    /// </summary>
    public string? Unobservable => null;

    public bool CanReconnect => false;

    public event EventHandler? Closed;

    /// <summary>Standard error and the exit code, which this transport is the only thing that can see.</summary>
    public event EventHandler<TransportNotice>? Notice;

    public Task<IJsonRpcMessageHandler?> ConnectAsync(IJsonRpcMessageFormatter formatter, CancellationToken cancellationToken = default)
    {
        var serverInfo = _serverInfo;
        _logger.LogInformation("Starting language server: {Exe}", serverInfo.FileName);

        // The server is launched directly. There used to be an interposed debug proxy here, reached by
        // setting VB6_LSP_DEBUG_PROXY=1, which relaunched the server underneath a byte-forwarding process
        // that logged every frame to its own stderr. The protocol inspector reads the same traffic from
        // inside this client and reads strictly more of it — see docs/lsp-client.md.
        string fileName = serverInfo.FileName;
        string arguments = serverInfo.Arguments;

        if (!OperatingSystem.IsWindows())
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
                WorkingDirectory = WorkingDirectory(),
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
            if (e.Data is not { Length: > 0 }) return;

            _logger.LogDebug("[vb-lsp stderr] {Data}", e.Data);

            // Onto the record as well as into the log. A debug log line is not somewhere a person looks
            // when a server dies mid-conversation; the timeline beside the last message it managed to
            // send is. This is what makes Unobservable's "all HexIDE's to observe" true rather than a
            // claim (hexide-io/HexIDE#369 task 2.9).
            Notice?.Invoke(this, new TransportNotice(TransportNoticeKind.StandardError, e.Data));
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
            // The command is the first thing anyone needs: "not on PATH" and "there but not runnable"
            // are different problems and the exception distinguishes them.
            LastFailure = $"could not start '{fileName}': {ex.Message}";
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

    /// <summary>
    /// Where to run the server. An explicit setting wins; otherwise the workspace, resolved now rather than
    /// when this transport was built, because the server starts on first use.
    ///
    /// <para>
    /// Empty when there is neither — which hands the child the IDE's own working directory. That is not a
    /// good root, but it is the one .NET uses when none is given, and inventing a temp path instead would
    /// silently point a server's configuration lookup somewhere the user has never heard of.
    /// </para>
    /// </summary>
    private string WorkingDirectory()
    {
        if (!string.IsNullOrWhiteSpace(_serverInfo.WorkingDirectory))
            return _serverInfo.WorkingDirectory;

        return _workspace?.Directory is { } d && !string.IsNullOrWhiteSpace(d) ? d : "";
    }

    private void OnProcessExited(object? sender, EventArgs e)
    {
        // Read before anything else touches the process: Kill() and Dispose() are both moments after this,
        // and ExitCode throws once the handle is gone.
        var code = ExitCode();

        _logger.LogWarning("VB6 LSP server process exited{Code}", code is null ? "" : $" with code {code}");

        // BEFORE Closed, which is what tears the connection down. An exit code that arrived after the
        // record had stopped accepting entries would be the one fact nobody could see.
        Notice?.Invoke(this, new TransportNotice(
            TransportNoticeKind.Lifecycle,
            code is null ? "process exited" : $"process exited with code {code}"));

        Closed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// The exit code, or null when the process cannot tell us.
    /// </summary>
    /// <remarks>
    /// LSP gives an exit code a meaning — 0 when a shutdown preceded exit and 1 otherwise — which is how
    /// hexide-io/HexIDE#312 was found, and nothing in this codebase read it until now. It is wrapped
    /// because reading it races teardown: a disposed or already-reaped handle throws rather than
    /// answering, and a diagnostic must never be the thing that breaks a shutdown.
    /// </remarks>
    private int? ExitCode()
    {
        try { return _process?.HasExited == true ? _process.ExitCode : null; }
        catch (Exception) { return null; }
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
