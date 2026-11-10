using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using HexIDE.Processes;
using Microsoft.Extensions.Logging;
using StreamJsonRpc;

namespace HexIDE.Lsp;

public enum TcpRole
{
    Connect,
    Listen,
}

/// <summary>Content-Length framed LSP over TCP, optionally launching the server.</summary>
public sealed class TcpLspTransport : ILspTransport
{
    private const int DefaultConnectTimeoutSeconds = 30;

    private readonly string _endpoint;
    private readonly TcpRole _role;
    private readonly NamedPipeLaunch? _launch;
    private readonly ILspWorkspace? _workspace;
    private readonly TimeSpan _connectTimeout;
    private readonly ILogger<TcpLspTransport> _logger;

    private NetworkStream? _stream;
    private TcpClient? _client;
    private TcpListener? _listener;
    private readonly Uri _address;
    internal IPAddress? BoundAddress => (_listener?.LocalEndpoint as IPEndPoint)?.Address;
    internal int BoundPort => (_listener?.LocalEndpoint as IPEndPoint)?.Port ?? _address.Port;
    private Process? _process;
    private readonly Lock _exitGate = new();
    private bool _exitReported;
    private volatile bool _shutdownCompleted;
    private volatile bool _endedAfterShutdown;

    private bool _disposing;
    private ChildProcessJob? _job;

    private bool _launchedWithoutHandle;

    private string? _launchCwd;

    private string LaunchCwdForMessage() => _launchCwd is null
        ? ""
        : $" (server cwd: {(_launchCwd.Length == 0 ? "<inherited>" : _launchCwd)})";

    public TcpLspTransport(
        string endpoint,
        TcpRole role,
        ILogger<TcpLspTransport> logger,
        NamedPipeLaunch? launch = null,
        ILspWorkspace? workspace = null,
        TimeSpan? connectTimeout = null)
    {
        if (!TryAddress(endpoint, out var address))
            throw new ArgumentException("Expected tcp://host:port (port 0 is allowed for listening).", nameof(endpoint));
        _address = address!;
        _endpoint = endpoint;
        _role = role;
        _logger = logger;
        _launch = launch;
        _workspace = workspace;
        _connectTimeout = connectTimeout ?? TimeSpan.FromSeconds(DefaultConnectTimeoutSeconds);
    }

    public bool IsAlive => _client is { Connected: true } && _process is not { HasExited: true };

    public void ShutdownCompleted()
    {
        _shutdownCompleted = true;
    }

    public string? LastFailure { get; private set; }
    public bool WaitingForWorkspace { get; private set; }

    public string? Unobservable =>
        _launch is null
            ? "This server was already running when " + HexIDE.IDE.Distribution.DisplayName + " connected to it, so its start, its exit code and "
              + "anything it writes to standard error belong to whoever launched it. Messages are captured in full."
        : _launchedWithoutHandle
            ? HexIDE.IDE.Distribution.DisplayName + " started this server but the operating system gave back no handle for it — the request "
              + "went to an instance that was already running — so its exit code and standard error cannot be "
              + "read. Messages are captured in full."
            : null;

    public bool CanReconnect => _launch is null;

    public bool LeaveServerRunning => _process is null;

    public event EventHandler? Closed;

    public event EventHandler<TransportNotice>? Notice;

    public async Task<IJsonRpcMessageHandler?> ConnectAsync(
        IJsonRpcMessageFormatter formatter, CancellationToken cancellationToken = default)
    {
        await DisposeAsync();
        lock (_exitGate)
        {
            _disposing = false;
            _exitReported = false;
            _shutdownCompleted = false;
            _endedAfterShutdown = false;
        }

        WaitingForWorkspace = false;
        LastFailure = null;
        string? arguments = null;
        string[]? argumentList = null;
        if (_launch?.ArgumentList is { } list)
        {
            var resolved = new List<string>();
            foreach (var argument in list)
            {
                if (ResolveArguments(argument) is not { } value) return null;
                resolved.Add(value);
            }
            argumentList = resolved.ToArray();
        }
        else if (_launch is not null && (arguments = ResolveArguments(_launch.Arguments)) is null)
            return null;

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(_connectTimeout);

        try
        {
            if (_role == TcpRole.Listen)
            {
                var host = _address.Host;
                var bind = host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                    ? IPAddress.Loopback : IPAddress.Parse(host.Trim('[', ']'));
                _listener = new TcpListener(bind, _address.Port);
                _listener.Start(1);
            }
            StartServerIfConfigured(arguments, argumentList);
            if (_listener is not null)
            {
                _client = await _listener.AcceptTcpClientAsync(timeoutCts.Token);
                // One server per connection: nothing else on this machine gets to queue behind it.
                _listener.Stop();
            }
            else
            {
                while (true)
                {
                    _client = new TcpClient();
                    try
                    {
                        await _client.ConnectAsync(_address.Host.Trim('[', ']'), _address.Port, timeoutCts.Token);
                        break;
                    }
                    catch (SocketException) when (_launch is not null && !timeoutCts.IsCancellationRequested)
                    {
                        _client.Dispose();
                        await Task.Delay(50, timeoutCts.Token);
                    }
                }
            }
            _stream = _client.GetStream();
        }
        catch (Exception ex)
        {
            LastFailure = $"could not establish TCP '{_endpoint}'{LaunchCwdForMessage()}: {ex.Message}";
            _logger.LogWarning(ex, "{Failure}", LastFailure);
            await DisposeAsync();
            return null;
        }

        var tapped = new ChannelTapStream(_stream!,
            text => Notice?.Invoke(this, new TransportNotice(TransportNoticeKind.Lifecycle, text)));
        return new HeaderDelimitedMessageHandler(tapped, tapped, formatter);
    }

    internal static bool TryAddress(string endpoint, out Uri? address)
    {
        return Uri.TryCreate(endpoint, UriKind.Absolute, out address)
            && address.Scheme == "tcp" && address.Port is >= 0 and <= 65535
            && string.IsNullOrEmpty(address.UserInfo) && address.AbsolutePath is "" or "/"
            && string.IsNullOrEmpty(address.Query) && string.IsNullOrEmpty(address.Fragment);
    }

    private string? ResolveArguments(string configured)
    {
        var arguments = configured
            .Replace("{host}", _address.Host.Trim('[', ']'), StringComparison.Ordinal)
            .Replace("{pid}", Environment.ProcessId.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);

        var wantsUri = arguments.Contains("{workspaceUri}", StringComparison.Ordinal);
        var wantsDir = arguments.Contains("{workspaceDir}", StringComparison.Ordinal);
        if (!wantsUri && !wantsDir) return arguments;

        var directory = WorkspaceDirectory();
        if (string.IsNullOrWhiteSpace(directory))
        {
            WaitingForWorkspace = true;
            LastFailure =
                $"'{_launch!.FileName}' is configured with a workspace placeholder and "
                + "no project is open or the project has not been saved yet, so there is no workspace to give it";
            return null;
        }

        if (wantsUri)
        {
            var uri = LspWorkspaceUri.For(directory);
            if (uri is null)
            {
                LastFailure =
                    $"the workspace directory '{directory}' cannot be expressed as a URI, "
                    + $"which '{_launch!.FileName}' was configured to require";
                _logger.LogWarning("{Failure}.", LastFailure);
                return null;
            }

            arguments = arguments.Replace("{workspaceUri}", uri, StringComparison.Ordinal);
        }

        return wantsDir
            ? arguments.Replace("{workspaceDir}", directory, StringComparison.Ordinal)
            : arguments;
    }

    private string WorkingDirectory() =>
        LspLaunchDirectory.For(_launch?.WorkingDirectory, _workspace, _logger);

    private string? WorkspaceDirectory() =>
        _workspace?.Directory is { } d && !string.IsNullOrWhiteSpace(d) ? d : null;

    private string WithPort(string argument) =>
        argument.Replace("{port}", BoundPort.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);

    private void StartServerIfConfigured(string? arguments, string[]? argumentList)
    {
        if (_launch is null)
            return;

        var startInfo = new ProcessStartInfo
        {
            FileName = _launch.FileName,
            Arguments = argumentList is null ? WithPort(arguments ?? _launch.Arguments) : "",
            WorkingDirectory = WorkingDirectory(),
            UseShellExecute = false,
            CreateNoWindow = true,

            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };

        if (argumentList is not null)
            foreach (var argument in argumentList) startInfo.ArgumentList.Add(WithPort(argument));
        if (_launch.Environment is not null)
            foreach (var (key, value) in _launch.Environment) startInfo.Environment[key] = value;

        _launchCwd = startInfo.WorkingDirectory;

        _logger.LogInformation(
            "Starting VB LSP server: {Exe} {Args} (cwd: {Cwd})",
            startInfo.FileName,
            startInfo.Arguments,
            string.IsNullOrEmpty(startInfo.WorkingDirectory) ? "<inherited>" : startInfo.WorkingDirectory);

        _job = new ChildProcessJob();
        LaunchedServerGuard.Wrap(startInfo);
        var process = Process.Start(startInfo);
        if (process is null)
        {
            _launchedWithoutHandle = true;
            _logger.LogWarning("Process.Start returned no handle for {Exe}.", startInfo.FileName);
            return;
        }

        _process = process;
        LaunchedServerGuard.NoteStarted(process);
        _job.Assign(process);
        process.EnableRaisingEvents = true;

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not { Length: > 0 }) return;

            _logger.LogDebug("[lsp stderr] {Data}", e.Data);
            Notice?.Invoke(this, new TransportNotice(TransportNoticeKind.StandardError, e.Data));
        };

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not { Length: > 0 }) return;

            _logger.LogDebug("[lsp stdout] {Data}", e.Data);
            Notice?.Invoke(this, new TransportNotice(TransportNoticeKind.StandardOutput, e.Data));
        };

        process.Exited += OnServerExited;
        process.BeginErrorReadLine();
        process.BeginOutputReadLine();
    }

    private void OnServerExited(object? sender, EventArgs e)
    {
        var code = ExitCode();
        bool disposing;
        lock (_exitGate)
        {
            if (!ReferenceEquals(sender, _process)) return;
            ReportExit(code);
            disposing = _disposing;
        }
        if (!disposing) Closed?.Invoke(this, EventArgs.Empty);
    }

    private void ReportExit(int? code)
    {
        if (_exitReported) return;
        _exitReported = true;
        if (_endedAfterShutdown) return;
        _logger.Log(_disposing ? LogLevel.Information : LogLevel.Warning,
            "[{Server}] Language server process exited{Code}", _launch!.FileName,
            code is null ? "" : $" with code {code}");

        Notice?.Invoke(this, new TransportNotice(
            TransportNoticeKind.Lifecycle,
            code is null ? "process exited" : $"process exited with code {code}",
            Fault: code is not (null or 0)));
    }

    private int? ExitCode()
    {
        try { return _process?.HasExited == true ? _process.ExitCode : null; }
        catch (Exception) { return null; }
    }

    public async ValueTask DisposeAsync()
    {
        var stream = _stream;
        var process = _process;
        _stream = null;
        _listener?.Stop();
        _listener = null;
        _client?.Dispose();
        _client = null;
        lock (_exitGate) _disposing = true;

        if (stream is not null)
        {
            try { await stream.DisposeAsync(); } catch { /* best effort */ }
        }

        if (process is null)
        {
            _job?.Dispose();
            _job = null;
            return;
        }

        try
        {
            await process.WaitForExitAsync().WaitAsync(LaunchedServerGuard.ExitTimeout);
        }
        catch { /* grace period elapsed, or already gone */ }

        try
        {
            if (!process.HasExited)
            {
                _endedAfterShutdown = _shutdownCompleted;
                LaunchedServerGuard.Kill(process);
                if (_endedAfterShutdown)
                {
                    var detail = $"stopped after shutdown; still exiting after {LaunchedServerGuard.ExitTimeout.TotalSeconds:g} s, so ended";
                    Notice?.Invoke(this, new TransportNotice(TransportNoticeKind.Lifecycle, detail));
                    _logger.LogInformation("[{Server}] {Detail}", _launch!.FileName, detail);
                }
                if (!_endedAfterShutdown)
                {
                    _logger.LogInformation("[{Server}] Language server process killed after waiting {Timeout:g} for exit.",
                        _launch!.FileName, LaunchedServerGuard.ExitTimeout);
                    Notice?.Invoke(this, new TransportNotice(TransportNoticeKind.Lifecycle,
                        "process killed after waiting for exit"));
                }
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(2));
            }
        }
        catch { /* already gone */ }

        var hasExited = process.HasExited;
        var code = ExitCode();
        lock (_exitGate)
        {
            if (hasExited) ReportExit(code);
            _process = null;
        }
        process.Exited -= OnServerExited;
        process.Dispose();
        _job?.Dispose();
        _job = null;
    }
}
