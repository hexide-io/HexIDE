using System.Diagnostics;
using System.IO.Pipes;
using Microsoft.Extensions.Logging;
using StreamJsonRpc;

namespace HexIDE.Lsp;

/// <summary>Which side of a named pipe HexIDE takes.</summary>
/// <remarks>
/// There is no single convention, which is why both are supported. The widespread
/// <c>--pipe</c> arrangement (as used by the vscode-languageserver family) has the <em>editor</em>
/// create the pipe and the server dial in — that is <see cref="Listen"/>. A server that creates its
/// own endpoint and waits to be dialled needs <see cref="Connect"/>. Picking the wrong one does not
/// fail loudly; both sides simply wait, which is precisely the silent-hang shape this transport's
/// connect timeout exists to convert into a log line.
/// </remarks>
public enum NamedPipeRole
{
    /// <summary>HexIDE creates the pipe and waits for the server to connect to it.</summary>
    Listen,

    /// <summary>The server owns the pipe; HexIDE connects to it.</summary>
    Connect,
}

/// <summary>
/// How to start a server that needs launching before the pipe can be used. <paramref name="Arguments"/>
/// may contain the placeholder <c>{pipe}</c>, which is replaced with the agreed pipe name.
/// </summary>
/// <param name="FileName">Executable to start.</param>
/// <param name="Arguments">Command line; <c>{pipe}</c> is substituted.</param>
/// <param name="WorkingDirectory">
/// Working directory for the child. Not cosmetic: a server may resolve its own configuration
/// relative to the process CWD, in which case launching from the wrong directory fails in a way that
/// looks like a transport problem.
/// </param>
public sealed record NamedPipeLaunch(string FileName, string Arguments, string? WorkingDirectory = null);

/// <summary>
/// Desktop/IPC transport: speaks LSP as JSON-RPC over a named pipe with Content-Length framing —
/// the same framing as stdio, a different channel. Named pipes are the usual same-machine transport
/// for a language server that does not want its stdio consumed (and are how a server that logs to
/// stdout avoids corrupting the protocol stream).
///
/// <para>
/// The server may already be running (<see cref="NamedPipeRole.Connect"/> with no launch) or be
/// started by this transport (<paramref name="launch"/>), and HexIDE may own either end of the pipe.
/// </para>
/// </summary>
/// <remarks>
/// <para>
/// <b>The connect timeout is not the initialize timeout.</b> This bounds only "the channel came up".
/// A server can connect its pipe promptly and then take seconds to answer <c>initialize</c> — one
/// real server was measured at <b>6.35 s</b> for the round trip, because it spawns its own children
/// inside the initialize handler. Bounding the handshake is <see cref="VBLspClient"/>'s job, and any
/// value chosen there has to clear that mark or it will cut off a server that is merely slow.
/// </para>
/// <para>
/// A pipe that drops mid-session is surfaced through <c>JsonRpc.Disconnected</c> rather than
/// <see cref="Closed"/>, because StreamJsonRpc owns the read loop once the handler is bound.
/// <see cref="Closed"/> fires only for a launched child that exits, which this transport can observe
/// directly.
/// </para>
/// </remarks>
public sealed class NamedPipeLspTransport : ILspTransport
{
    private const int DefaultConnectTimeoutSeconds = 15;

    private readonly string _pipeName;
    private readonly NamedPipeRole _role;
    private readonly NamedPipeLaunch? _launch;
    private readonly TimeSpan _connectTimeout;
    private readonly ILogger<NamedPipeLspTransport> _logger;

    private PipeStream? _pipe;
    private Process? _process;

    public NamedPipeLspTransport(
        string pipeName,
        NamedPipeRole role,
        ILogger<NamedPipeLspTransport> logger,
        NamedPipeLaunch? launch = null,
        TimeSpan? connectTimeout = null)
    {
        _pipeName = pipeName;
        _role = role;
        _logger = logger;
        _launch = launch;
        _connectTimeout = connectTimeout ?? TimeSpan.FromSeconds(DefaultConnectTimeoutSeconds);
    }

    public bool IsAlive => _pipe is { IsConnected: true } && _process is not { HasExited: true };

    // A server this transport launched is one-shot, matching StdioProcessLspTransport: a crash does
    // not auto-respawn. A pre-existing endpoint we merely dialled can be re-dialled.
    public bool CanReconnect => _launch is null;

    public event EventHandler? Closed;

    public async Task<IJsonRpcMessageHandler?> ConnectAsync(
        IJsonRpcMessageFormatter formatter, CancellationToken cancellationToken = default)
    {
        // Drop any previous connection (reconnect) so the far side observes the close and frees its
        // endpoint, rather than leaking a half-open pipe that the next dial would collide with.
        await DisposeAsync();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(_connectTimeout);

        try
        {
            // Order matters in Listen mode: the pipe must exist BEFORE the child starts, or a server
            // that dials immediately races us and fails to find it.
            PipeStream pipe = _role == NamedPipeRole.Listen ? CreateListener() : CreateDialler();
            _pipe = pipe;

            StartServerIfConfigured();

            _logger.LogInformation(
                "Connecting to VB LSP server over named pipe '{Pipe}' ({Role}).", _pipeName, _role);

            switch (pipe)
            {
                case NamedPipeServerStream server:
                    await server.WaitForConnectionAsync(timeoutCts.Token);
                    break;
                case NamedPipeClientStream client:
                    await client.ConnectAsync(timeoutCts.Token);
                    break;
            }
        }
        catch (Exception ex)
        {
            // Graceful absence, exactly as the other transports do: the IDE runs with LSP features
            // disabled rather than failing startup. A timeout lands here as OperationCanceledException.
            var timedOut = ex is OperationCanceledException && !cancellationToken.IsCancellationRequested;
            _logger.LogWarning(
                ex,
                timedOut
                    ? "Named pipe '{Pipe}' did not connect within {Timeout:g} — LSP unavailable. If the "
                    + "server is running, check which side is expected to create the pipe."
                    : "Named pipe LSP connect failed ('{Pipe}', timeout {Timeout:g}) — LSP unavailable.",
                _pipeName,
                _connectTimeout);
            await DisposeAsync();
            return null;
        }

        // Same stream both ways — a named pipe opened InOut is duplex. The 3-arg ctor is required:
        // the shorter overloads default to the Newtonsoft formatter, bypassing the shared AOT-safe
        // SystemTextJsonFormatter + LspJsonContext.
        return new HeaderDelimitedMessageHandler(_pipe!, _pipe!, formatter);
    }

    private NamedPipeServerStream CreateListener() =>
        new(_pipeName, PipeDirection.InOut, maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

    private NamedPipeClientStream CreateDialler() =>
        new(serverName: ".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);

    private void StartServerIfConfigured()
    {
        if (_launch is null)
            return;

        var startInfo = new ProcessStartInfo
        {
            FileName = _launch.FileName,
            Arguments = _launch.Arguments.Replace("{pipe}", _pipeName, StringComparison.Ordinal),
            WorkingDirectory = _launch.WorkingDirectory ?? string.Empty,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        _logger.LogInformation(
            "Starting VB LSP server: {Exe} {Args} (cwd: {Cwd})",
            startInfo.FileName,
            startInfo.Arguments,
            string.IsNullOrEmpty(startInfo.WorkingDirectory) ? "<inherited>" : startInfo.WorkingDirectory);

        var process = Process.Start(startInfo);
        if (process is null)
        {
            // Process.Start returning null means the OS reused an existing instance; there is no child
            // to own or observe, so leave _process null and let the connect attempt decide the outcome.
            _logger.LogWarning("Process.Start returned no handle for {Exe}.", startInfo.FileName);
            return;
        }

        _process = process;
        process.EnableRaisingEvents = true;
        process.Exited += OnServerExited;
    }

    private void OnServerExited(object? sender, EventArgs e) => Closed?.Invoke(this, EventArgs.Empty);

    public async ValueTask DisposeAsync()
    {
        var pipe = _pipe;
        var process = _process;
        _pipe = null;
        _process = null;

        if (pipe is not null)
        {
            try { await pipe.DisposeAsync(); } catch { /* best effort */ }
        }

        if (process is null)
            return;

        // Unsubscribe first: killing the child would otherwise raise Closed during teardown and
        // invite the client to reconnect to a transport that is being disposed.
        process.Exited -= OnServerExited;
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch { /* already gone */ }

        process.Dispose();
    }
}
