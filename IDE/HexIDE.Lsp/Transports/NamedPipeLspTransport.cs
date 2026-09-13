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
/// How to start a server that needs launching before the pipe can be used.
/// </summary>
/// <remarks>
/// <paramref name="Arguments"/> may contain three placeholders:
/// <list type="bullet">
///   <item><c>{pipe}</c> — the agreed pipe name.</item>
///   <item><c>{workspaceUri}</c> — the open workspace as a <c>file:</c> URI, spelled exactly as the
///     client's <c>rootUri</c> spells it.</item>
///   <item><c>{workspaceDir}</c> — the same directory as a plain path, for a server that wants one.</item>
/// </list>
///
/// <para>
/// <b>The workspace ones exist because a pipe server cannot be told any other way.</b> A stdio server
/// inherits the workspace as its working directory; a pipe server that requires the workspace as an
/// argument had no route to it, so an entry could only ever hard-code one absolute path and would then
/// serve exactly one project on one machine.
/// </para>
/// </remarks>
/// <param name="FileName">Executable to start.</param>
/// <param name="Arguments">Command line; the placeholders above are substituted.</param>
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
    // Measured against a real third-party server: its pipe came up 9.6s after launch on one run, and
    // its initialize round trip is 6.35s of which ~5.0s is a hardcoded sleep. 15s left no headroom on
    // a slow machine for something that was working correctly.
    private const int DefaultConnectTimeoutSeconds = 30;

    private readonly string _pipeName;
    private readonly NamedPipeRole _role;
    private readonly NamedPipeLaunch? _launch;
    private readonly ILspWorkspace? _workspace;
    private readonly TimeSpan _connectTimeout;
    private readonly ILogger<NamedPipeLspTransport> _logger;

    private PipeStream? _pipe;
    private Process? _process;

    /// <summary>
    /// Set when a launch was configured, ran, and produced no handle to own.
    /// </summary>
    /// <remarks>
    /// <c>Process.Start</c> returns null when the OS handed the request to an existing instance. There is
    /// then a server, and it is not ours: no exit code, no standard error. Without this flag that case
    /// reads as <see cref="Unobservable"/> null — the record promising a completeness it does not have —
    /// because the only thing distinguishing it from a healthy launch is the missing handle.
    /// </remarks>
    private bool _launchedWithoutHandle;

    public NamedPipeLspTransport(
        string pipeName,
        NamedPipeRole role,
        ILogger<NamedPipeLspTransport> logger,
        NamedPipeLaunch? launch = null,
        ILspWorkspace? workspace = null,
        TimeSpan? connectTimeout = null)
    {
        _pipeName = pipeName;
        _role = role;
        _logger = logger;
        _launch = launch;
        _workspace = workspace;
        _connectTimeout = connectTimeout ?? TimeSpan.FromSeconds(DefaultConnectTimeoutSeconds);
    }

    public bool IsAlive => _pipe is { IsConnected: true } && _process is not { HasExited: true };

    // A server this transport launched is one-shot, matching StdioProcessLspTransport: a crash does
    // not auto-respawn. A pre-existing endpoint we merely dialled can be re-dialled.
    /// <summary>Why the last connect attempt failed, in this transport's words. See ILspTransport.</summary>
    public string? LastFailure { get; private set; }

    /// <summary>
    /// Nothing when HexIDE launched the server behind this pipe and can see it, and the process half when
    /// it cannot.
    /// </summary>
    /// <remarks>
    /// The same distinction <see cref="CanReconnect"/> turns on, read the other way round. A pipe HexIDE
    /// dialled has a server on the far end that somebody else started and somebody else will stop, so its
    /// lifetime is not this IDE's to report on — while the messages crossing it are captured exactly as
    /// they are anywhere else.
    ///
    /// <para>
    /// <b>The null arm is a claim, and it has to be earned.</b> It says the record is complete, so it is
    /// only true because this transport now reports standard error and the exit code of a server it
    /// launched. It did not before, and the null was a promise nothing kept — the same defect
    /// hexide-io/HexIDE#369 fixed for stdio, which is why the third arm below exists rather than being
    /// folded into the second.
    /// </para>
    /// </remarks>
    public string? Unobservable =>
        _launch is null
            ? "This server was already running when HexIDE connected to it, so its start, its exit code and "
              + "anything it writes to standard error belong to whoever launched it. Messages are captured in full."
        : _launchedWithoutHandle
            ? "HexIDE started this server but the operating system gave back no handle for it — the request "
              + "went to an instance that was already running — so its exit code and standard error cannot be "
              + "read. Messages are captured in full."
            : null;

    public bool CanReconnect => _launch is null;

    public event EventHandler? Closed;

    /// <summary>
    /// Standard error and the exit code, for a server this transport launched.
    /// </summary>
    /// <remarks>
    /// <b>This used to be a no-op, on the grounds that a spawned server goes through the stdio transport.
    /// It does not</b> — a pipe server that HexIDE starts is launched right here, and a pipe server is the
    /// case where the process half matters most: the channel is dialled rather than inherited, so a server
    /// that dies before its pipe exists presents as a bare connect timeout with no stream to attribute and
    /// nothing else to go on.
    ///
    /// <para>
    /// Silent for a pipe that was merely dialled, where there is genuinely no process here;
    /// <see cref="Unobservable"/> says so in words the reader sees.
    /// </para>
    /// </remarks>
    public event EventHandler<TransportNotice>? Notice;

    public async Task<IJsonRpcMessageHandler?> ConnectAsync(
        IJsonRpcMessageFormatter formatter, CancellationToken cancellationToken = default)
    {
        // Drop any previous connection (reconnect) so the far side observes the close and frees its
        // endpoint, rather than leaking a half-open pipe that the next dial would collide with.
        await DisposeAsync();

        // Resolved before the pipe exists and before anything is started, because a placeholder that
        // cannot be filled is a knowable precondition rather than a connect failure — and reporting it as
        // a timeout thirty seconds later would describe the wrong problem.
        string? arguments = null;
        if (_launch is not null && (arguments = ResolveArguments()) is null)
            return null;

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(_connectTimeout);

        try
        {
            // Order matters in Listen mode: the pipe must exist BEFORE the child starts, or a server
            // that dials immediately races us and fails to find it.
            PipeStream pipe = _role == NamedPipeRole.Listen ? CreateListener() : CreateDialler();
            _pipe = pipe;

            StartServerIfConfigured(arguments);

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
            // The timed-out / did-not-connect distinction was already computed here and discarded. It is
            // the useful half: a timeout means nothing was listening, and everything else means something
            // was and refused.
            LastFailure = timedOut
                ? $"nothing connected on pipe '{_pipeName}' within {_connectTimeout:g}"
                : $"could not use pipe '{_pipeName}': {ex.Message}";
            await DisposeAsync();
            return null;
        }

        // Same stream both ways — a named pipe opened InOut is duplex. The 3-arg ctor is required:
        // the shorter overloads default to the Newtonsoft formatter, bypassing the shared AOT-safe
        // SystemTextJsonFormatter + LspJsonContext.
        return new HeaderDelimitedMessageHandler(_pipe!, _pipe!, formatter);
    }

    /// <summary>
    /// <c>Asynchronous</c> is load-bearing, not a style choice: both ends wrap the one duplex handle
    /// as reader <em>and</em> writer, and on a non-overlapped handle the OS serialises I/O on the file
    /// object, so the read loop blocks every write and the handshake never completes.
    /// <c>CurrentUserOnly</c> is the defence against pipe-squatting — a pipe name is a machine-global
    /// string, so without it any local process can claim the name and impersonate a language server,
    /// or read what we send one.
    /// </summary>
    private const PipeOptions Options = PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly;

    private NamedPipeServerStream CreateListener() =>
        new(_pipeName, PipeDirection.InOut, maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte, Options);

    private NamedPipeClientStream CreateDialler() =>
        new(serverName: ".", _pipeName, PipeDirection.InOut, Options);

    /// <summary>
    /// The launch arguments with their placeholders filled in, or null when one of them cannot be.
    /// </summary>
    /// <remarks>
    /// Null is a refusal, not an empty string. Substituting nothing for <c>{workspaceUri}</c> hands the
    /// server a flag with no value, and a server whose workspace argument is mandatory then fails in its
    /// own vocabulary — a startup error the reader has to decode back into "no project was open". Saying
    /// so here costs one line and names the actual cause.
    /// </remarks>
    private string? ResolveArguments()
    {
        var arguments = _launch!.Arguments.Replace("{pipe}", _pipeName, StringComparison.Ordinal);

        var wantsUri = arguments.Contains("{workspaceUri}", StringComparison.Ordinal);
        var wantsDir = arguments.Contains("{workspaceDir}", StringComparison.Ordinal);
        if (!wantsUri && !wantsDir) return arguments;

        var directory = WorkspaceDirectory();
        if (string.IsNullOrWhiteSpace(directory))
        {
            LastFailure =
                $"'{_launch.FileName}' is configured with a workspace placeholder and no project is open, "
                + "so there is no workspace to give it";
            _logger.LogWarning("{Failure}.", LastFailure);
            return null;
        }

        if (wantsUri)
        {
            // The same spelling the client sends as rootUri. If they disagree the server loads one
            // workspace and answers about another, which does not look like a failure from here.
            var uri = LspWorkspaceUri.For(directory);
            if (uri is null)
            {
                LastFailure =
                    $"the workspace directory '{directory}' cannot be expressed as a URI, "
                    + $"which '{_launch.FileName}' was configured to require";
                _logger.LogWarning("{Failure}.", LastFailure);
                return null;
            }

            arguments = arguments.Replace("{workspaceUri}", uri, StringComparison.Ordinal);
        }

        return wantsDir
            ? arguments.Replace("{workspaceDir}", directory, StringComparison.Ordinal)
            : arguments;
    }

    /// <summary>
    /// Where the server PROCESS runs: the explicit setting if there is one, else the open workspace.
    /// </summary>
    /// <remarks>
    /// The same rule the stdio transport applies, and for the same reason — a server resolves its own
    /// configuration relative to where it runs, and servers start lazily, so the answer is not knowable
    /// when the registration is built. An explicit setting always wins: somebody who named one meant it.
    /// </remarks>
    private string WorkingDirectory()
    {
        if (!string.IsNullOrWhiteSpace(_launch?.WorkingDirectory))
            return _launch.WorkingDirectory;

        return _workspace?.Directory is { } d && !string.IsNullOrWhiteSpace(d) ? d : "";
    }

    /// <summary>
    /// Which workspace the server should ANALYSE. Always the open project, never the launch directory.
    /// </summary>
    /// <remarks>
    /// <b>Deliberately not <see cref="WorkingDirectory"/>, though the first draft of this used it.</b> The
    /// two answer different questions and only coincide by accident. A server with a required layout is
    /// launched from its own install directory — that is what <c>workingDirectory</c> is for — while the
    /// code it must analyse is wherever the user's project is. Filling <c>{workspaceUri}</c> from the
    /// launch directory would hand such a server its own installation as the workspace: it would start,
    /// report cleanly, and answer every question about the wrong tree. A wrong answer, not a failure.
    /// </remarks>
    private string? WorkspaceDirectory() =>
        _workspace?.Directory is { } d && !string.IsNullOrWhiteSpace(d) ? d : null;

    private void StartServerIfConfigured(string? arguments)
    {
        if (_launch is null)
            return;

        var startInfo = new ProcessStartInfo
        {
            FileName = _launch.FileName,
            Arguments = arguments ?? _launch.Arguments,
            WorkingDirectory = WorkingDirectory(),
            UseShellExecute = false,
            CreateNoWindow = true,

            // Standard error only. The protocol rides the pipe, so this stream carries nothing but the
            // server's own words — which for a pipe server is the only thing it has to say when it fails
            // before the pipe exists.
            //
            // Standard OUTPUT is deliberately left alone. A pipe server is exactly the kind that writes a
            // banner there, and redirecting a stream nobody reads fills its buffer and blocks the child —
            // turning a diagnostic into a hang. Redirect what is read; read what is redirected.
            RedirectStandardError = true,
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
            // Recorded, because "launched and watchable" and "launched and not" are not the same record.
            _launchedWithoutHandle = true;
            _logger.LogWarning("Process.Start returned no handle for {Exe}.", startInfo.FileName);
            return;
        }

        _process = process;
        process.EnableRaisingEvents = true;

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not { Length: > 0 }) return;

            _logger.LogDebug("[lsp stderr] {Data}", e.Data);
            Notice?.Invoke(this, new TransportNotice(TransportNoticeKind.StandardError, e.Data));
        };

        process.Exited += OnServerExited;
        process.BeginErrorReadLine();
    }

    private void OnServerExited(object? sender, EventArgs e)
    {
        // Read first: Kill() and Dispose() are both moments away, and ExitCode throws once the handle
        // is gone.
        var code = ExitCode();

        _logger.LogWarning(
            "Language server process exited{Code}", code is null ? "" : $" with code {code}");

        // BEFORE Closed, which is what tears the connection down. An exit code arriving after the record
        // stopped accepting entries would be the one fact nobody could see — and for a pipe server that
        // died during startup it is very nearly the only fact there is.
        Notice?.Invoke(this, new TransportNotice(
            TransportNoticeKind.Lifecycle,
            code is null ? "process exited" : $"process exited with code {code}"));

        Closed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The exit code, or null when the process cannot tell us.</summary>
    /// <remarks>
    /// Wrapped because reading it races teardown: a disposed or already-reaped handle throws rather than
    /// answering, and a diagnostic must never be the thing that breaks a shutdown.
    /// </remarks>
    private int? ExitCode()
    {
        try { return _process?.HasExited == true ? _process.ExitCode : null; }
        catch (Exception) { return null; }
    }

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

        // Killing the child is required, not tidy-up. A real third-party server was measured taking a
        // client PID on its command line, silently ignoring it, logging that it "will not be able to
        // automatically exit", and then outliving every client — leaving orphaned server processes on
        // each run. A transport that owns a child has to assume nothing else will reap it.
        //
        // Unsubscribe first: the kill would otherwise raise Closed during teardown and invite the
        // client to reconnect to a transport that is being disposed.
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
