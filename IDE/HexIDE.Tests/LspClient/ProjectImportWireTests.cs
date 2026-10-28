using System.Globalization;
using System.Text;
using System.Text.Json;
using HexIDE.Lsp;
using HexIDE.Lsp.Messages;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Nerdbank.Streams;
using StreamJsonRpc;

namespace HexIDE.Tests.LspClient;

public sealed class ProjectImportWireTests : IAsyncDisposable
{
    private readonly CancellationTokenSource _cts = new(TimeSpan.FromSeconds(20));
    private readonly List<JsonElement> _frames = [];
    private readonly SemaphoreSlim _write = new(1);
    private readonly string _root = Path.Combine(Path.GetTempPath(), "hexide-import-wire-" + Guid.NewGuid().ToString("N"));
    private Stream? _server;
    private LspClientRegistry? _registry;
    private Task? _loop;
    private readonly Workspace _workspace = new();
    private bool _useImport = true;
    private bool _failImport;
    private string _folderNotifications = "true";
    private bool _declareCommand = true;
    private int _pulls;
    private readonly List<string> _notes = [];
    private readonly OutputLogger _output = new();
    private string _replyNotes = "[\"Imported project\"]";

    private JsonElement[] Frames(string method)
    {
        lock (_frames) return _frames.Where(f => f.TryGetProperty("method", out var m) && m.GetString() == method).ToArray();
    }

    private async Task StartAsync()
    {
        Directory.CreateDirectory(_root);
        _workspace.Directory = _root;
        _workspace.Folders = [new("Probe", _root)];
        _workspace.Projects = [new(_root, new("Probe", Path.Combine(_root, "Probe.vbp"), [], []), ProjectText: _ => Task.FromResult("Name=\"Unsaved\"\r\n"))];
        var (local, remote) = FullDuplexStream.CreatePair();
        _server = remote;
        _loop = Task.Run(async () =>
        {
            try
            {
                while (await ReadFrameAsync(remote, _cts.Token) is { } text)
                {
                    using var document = JsonDocument.Parse(text);
                    var frame = document.RootElement.Clone();
                    lock (_frames) _frames.Add(frame);
                    if (!frame.TryGetProperty("method", out var method) || !frame.TryGetProperty("id", out var id)) continue;
                    var name = method.GetString();
                    var result = name switch
                    {
                        "initialize" => "{\"capabilities\":{\"workspace\":{\"workspaceFolders\":{\"supported\":true,\"changeNotifications\":" + _folderNotifications + "}},\"textDocumentSync\":1,\"diagnosticProvider\":{\"interFileDependencies\":true,\"workspaceDiagnostics\":false},\"executeCommandProvider\":{\"commands\":" + (_declareCommand ? "[\"probe.import\"]" : "[]") + "}}}",
                        "workspace/executeCommand" => "{\"notes\":" + _replyNotes + "}",
                        "textDocument/diagnostic" => "{\"kind\":\"full\",\"items\":[],\"resultId\":\"" + Interlocked.Increment(ref _pulls) + "\"}",
                        _ => "null"
                    };
                    var reply = "{\"jsonrpc\":\"2.0\",\"id\":" + id.GetRawText() +
                        (name == "workspace/executeCommand" && _failImport ? ",\"error\":{\"code\":-32803,\"message\":\"Import refused\"}}" : ",\"result\":" + result + "}");
                    await SendAsync(reply);
                }
            }
            catch (OperationCanceledException) { }
            catch (IOException) { }
            catch (ObjectDisposedException) { }
        }, _cts.Token);
        var transport = Substitute.For<ILspTransport>();
        transport.IsAlive.Returns(true);
        transport.ConnectAsync(Arg.Any<IJsonRpcMessageFormatter>(), Arg.Any<CancellationToken>())
            .Returns(ci => Task.FromResult<IJsonRpcMessageHandler?>(new HeaderDelimitedMessageHandler(local, local, ci.Arg<IJsonRpcMessageFormatter>())));
        var client = new VBLspClient(transport, _output, "vb6", workspace: _workspace);
        using var arguments = JsonDocument.Parse("[{\"uri\":\"{uri}\",\"text\":\"{text}\",\"overwrite\":true}]");
        var registration = new LanguageServerRegistration("probe", "Probe server", [".bas"], "vb6", () => client,
            WorkspaceArtifact: _useImport ? new("generated.json", "probe") : null, ProjectImport: _useImport ? new("probe.import", arguments.RootElement.Clone()) : null);
        var providers = new WorkspaceArtifactProviderRegistry([new Provider()]);
        var consent = Substitute.For<IWorkspaceArtifactConsent>();
        consent.AllowAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<WorkspaceArtifactSpec>()).Returns(true);
        var artifacts = new WorkspaceArtifactService(_workspace, providers, consent, NullLogger<WorkspaceArtifactService>.Instance);
        _registry = new([registration], NullLogger<LspClientRegistry>.Instance, _workspace, artifacts: artifacts);
        _registry.MessageLogged += (_, p) => _notes.Add(p.Message);
        await _registry.OpenDocumentAsync(new Uri(Path.Combine(_root, "Caller.bas")).AbsoluteUri, "Sub Main()\r\nEnd Sub", true, _cts.Token);
    }

    private async Task SendAsync(string text)
    {
        await _write.WaitAsync(_cts.Token);
        try { await WriteFrameAsync(_server!, text, _cts.Token); }
        finally { _write.Release(); }
    }

    private async Task RefreshAsync()
    {
        await SendAsync("{\"jsonrpc\":\"2.0\",\"id\":\"refresh\",\"method\":\"workspace/diagnostic/refresh\"}");
        await UntilAsync(() =>
        {
            lock (_frames) return _frames.Any(f => f.TryGetProperty("id", out var id)
                && id.ValueKind == JsonValueKind.String && id.GetString() == "refresh"
                && f.TryGetProperty("result", out _));
        });
    }

    private async Task UntilAsync(Func<bool> condition)
    {
        while (!condition()) await Task.Delay(10, _cts.Token);
    }

    [Fact]
    public async Task ConnectImportsInMemoryTextAfterInitializeInsteadOfWritingAnArtifactAndReportsNotes()
    {
        await StartAsync();
        var imports = Frames("workspace/executeCommand");
        imports.Should().ContainSingle();
        var p = imports[0].GetProperty("params");
        p.GetProperty("command").GetString().Should().Be("probe.import");
        var arg = p.GetProperty("arguments")[0];
        arg.GetProperty("uri").GetString().Should().Be(new Uri(Path.Combine(_root, "Probe.vbp")).AbsoluteUri);
        arg.GetProperty("text").GetString().Should().Be("Name=\"Unsaved\"\r\n");
        arg.GetProperty("overwrite").GetBoolean().Should().BeTrue();
        JsonElement[] frames;
        lock (_frames) frames = _frames.ToArray();
        var initialized = Array.FindIndex(frames, f => f.TryGetProperty("method", out var m) && m.GetString() == "initialized");
        initialized.Should().BeGreaterThanOrEqualTo(0);
        initialized.Should().BeLessThan(Array.FindIndex(frames,
            f => f.TryGetProperty("method", out var m) && m.GetString() == "workspace/executeCommand"));
        File.Exists(Path.Combine(_root, "generated.json")).Should().BeFalse();
        _notes.Should().Contain(m => m.EndsWith("Imported project", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("[\"Imported project\"]")]
    [InlineData("[{\"kind\":\"Startup\",\"subject\":\"Sub Main\",\"message\":\"Imported project\"}]")]
    public async Task ImportNotesReachTheServersLogAsText(string notes)
    {
        _replyNotes = notes;
        await StartAsync();
        _output.Messages.Should().Contain("[vb6 server] Imported project");
        _output.Messages.Should().NotContain(m => m.Contains("\"kind\""));
    }

    [Fact]
    public async Task InitializeDeclaresDiagnosticRefreshSupport()
    {
        await StartAsync();
        Frames("initialize").Single().GetProperty("params").GetProperty("capabilities")
            .GetProperty("workspace").GetProperty("diagnostics").GetProperty("refreshSupport")
            .GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task ARefreshOnAnotherConnectionDoesNotDisableImportRepulls()
    {
        await using var other = new ProjectImportWireTests();
        await other.StartAsync();
        await StartAsync();
        await UntilAsync(() => Volatile.Read(ref _pulls) == 1);
        await other.UntilAsync(() => Volatile.Read(ref other._pulls) == 1);
        await other.RefreshAsync();
        _workspace.Change();
        await UntilAsync(() => Frames("workspace/executeCommand").Length == 2 && Volatile.Read(ref _pulls) == 2);
        await _registry!.StopAsync();
        Frames("textDocument/diagnostic").Should().HaveCount(2);
    }

    [Fact]
    public async Task ProjectChangesDebounceAndRepullOpenDocumentsWithoutPreviousResultIds()
    {
        await StartAsync();
        await UntilAsync(() => Volatile.Read(ref _pulls) == 1);
        _workspace.Projects = [_workspace.Projects[0] with { ProjectText = _ => Task.FromResult("Name=\"Changed\"\r\n") }];
        for (var i = 0; i < 5; i++) _workspace.Change();
        await UntilAsync(() => Frames("workspace/executeCommand").Length == 2 && Volatile.Read(ref _pulls) == 2);
        var import = Frames("workspace/executeCommand")[1];
        import.GetProperty("params").GetProperty("arguments")[0].GetProperty("text").GetString().Should().Be("Name=\"Changed\"\r\n");
        var diagnostic = Frames("textDocument/diagnostic")[1].GetProperty("params");
        (diagnostic.TryGetProperty("previousResultId", out var previous) && previous.ValueKind == JsonValueKind.String)
            .Should().BeFalse();
        await _registry!.StopAsync();
        Frames("workspace/executeCommand").Should().HaveCount(2);
    }

    [Theory]
    [InlineData("true")]
    [InlineData("\"folder-registration\"")]
    [InlineData("\"\"")]
    public async Task FolderChangesAreDeduplicatedAndPrecedeTheDebouncedImport(string capability)
    {
        _folderNotifications = capability;
        await StartAsync();
        var outside = new LspWorkspaceFolder("Shared", Path.Combine(_root, "..", "Shared"));
        _workspace.Folders = [new("Probe", _root), outside, outside];
        for (var i = 0; i < 5; i++) _workspace.Change();
        await UntilAsync(() => Frames("workspace/executeCommand").Length == 2);
        var changes = Frames("workspace/didChangeWorkspaceFolders");
        changes.Should().ContainSingle();
        var change = changes[0].GetProperty("params").GetProperty("event");
        change.GetProperty("added").GetArrayLength().Should().Be(1);
        change.GetProperty("added")[0].GetProperty("uri").GetString().Should().Be(new Uri(outside.Directory).AbsoluteUri);
        change.GetProperty("removed").GetArrayLength().Should().Be(0);
        JsonElement[] frames;
        lock (_frames) frames = _frames.ToArray();
        Array.FindIndex(frames, f => f.TryGetProperty("method", out var m) && m.GetString() == "workspace/didChangeWorkspaceFolders")
            .Should().BeLessThan(Array.FindLastIndex(frames, f => f.TryGetProperty("method", out var m) && m.GetString() == "workspace/executeCommand"));
        _workspace.Folders = [new("Probe", _root)];
        _workspace.Change();
        await UntilAsync(() => Frames("workspace/executeCommand").Length == 3);
        change = Frames("workspace/didChangeWorkspaceFolders")[1].GetProperty("params").GetProperty("event");
        change.GetProperty("removed")[0].GetProperty("uri").GetString().Should().Be(new Uri(outside.Directory).AbsoluteUri);
        change.GetProperty("removed").GetArrayLength().Should().Be(1);
        change.GetProperty("added").GetArrayLength().Should().Be(0);
        _workspace.Change();
        await UntilAsync(() => Frames("workspace/executeCommand").Length == 4);
        await _registry!.StopAsync();
        Frames("workspace/didChangeWorkspaceFolders").Should().HaveCount(2);
        Frames("workspace/executeCommand").Should().HaveCount(4);
    }

    [Fact]
    public async Task FolderChangesAlsoReachConnectionsWithoutProjectImport()
    {
        _useImport = false;
        await StartAsync();
        _workspace.Folders = [new("Probe", _root), new("Shared", Path.Combine(_root, "Shared"))];
        _workspace.Change();
        await UntilAsync(() => Frames("workspace/didChangeWorkspaceFolders").Length == 1);
        Frames("workspace/didChangeWorkspaceFolders")[0].GetProperty("params").GetProperty("event")
            .GetProperty("added").GetArrayLength().Should().Be(1);
        Frames("workspace/executeCommand").Should().BeEmpty();
        _workspace.Projects = [];
        _workspace.Folders = [];
        _workspace.Change();
        await UntilAsync(() => Frames("workspace/didChangeWorkspaceFolders").Length == 2);
        Frames("workspace/didChangeWorkspaceFolders")[1].GetProperty("params").GetProperty("event")
            .GetProperty("removed").GetArrayLength().Should().Be(2);
    }

    [Theory]
    [InlineData("false")]
    [InlineData("null")]
    public async Task FolderChangesRequireServerConsent(string capability)
    {
        _folderNotifications = capability;
        await StartAsync();
        _workspace.Folders = [new("Probe", _root), new("Shared", Path.Combine(_root, "Shared"))];
        _workspace.Change();
        await UntilAsync(() => Frames("workspace/executeCommand").Length == 2);
        await _registry!.StopAsync();
        Frames("workspace/didChangeWorkspaceFolders").Should().BeEmpty();
        // A consenting connection is the positive control: silence alone cannot prove capability gating.
        await using var consenting = new ProjectImportWireTests();
        await consenting.StartAsync();
        consenting._workspace.Folders = [new("Probe", consenting._root),
            new("Shared", Path.Combine(consenting._root, "Shared"))];
        consenting._workspace.Change();
        await consenting.UntilAsync(() => consenting.Frames("workspace/executeCommand").Length == 2);
        consenting.Frames("workspace/didChangeWorkspaceFolders").Should().ContainSingle();
    }

    [Fact]
    public async Task AServerRefreshDisablesTheFallbackAfterImport()
    {
        await StartAsync();
        await UntilAsync(() => Volatile.Read(ref _pulls) == 1);
        await RefreshAsync();
        await UntilAsync(() => Volatile.Read(ref _pulls) == 2);
        _workspace.Change();
        await UntilAsync(() => Frames("workspace/executeCommand").Length == 2);
        // A shutdown reply drains preceding outbound frames; duplicate pulls would be before it.
        await _registry!.StopAsync();
        Frames("textDocument/diagnostic").Should().HaveCount(2);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task FailedOrUnadvertisedImportsAreConfigurationProblems(bool advertised)
    {
        _declareCommand = advertised;
        _failImport = true;
        await StartAsync();
        _registry!.ConfigurationProblems.Should().Contain(p => p.EntryId == "probe" && !p.EntryRejected
            && p.Message.Contains(advertised ? "Import refused" : "does not advertise"));
        Frames("workspace/executeCommand").Should().HaveCount(advertised ? 1 : 0);
        await UntilAsync(() => Frames("textDocument/didOpen").Length == 1);
    }

    [Fact]
    public async Task AProjectWithoutASavedPathReportsAProblemWithoutInventingAnImportUri()
    {
        await StartAsync();
        _workspace.Projects = [new(_root, new("Unsaved", null, [], []))];
        _workspace.Change();
        await UntilAsync(() => _registry!.ConfigurationProblems.Count > 0);
        _registry!.ConfigurationProblems.Should().Contain(p => p.Message.Contains("saved project path"));
        await _registry.StopAsync();
        Frames("workspace/executeCommand").Should().ContainSingle();
    }

    public async ValueTask DisposeAsync()
    {
        if (_registry is not null) await _registry.DisposeAsync();
        await _cts.CancelAsync();
        if (_loop is not null) await _loop;
        if (_server is not null) await _server.DisposeAsync();
        _cts.Dispose();
        _write.Dispose();
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    private sealed class OutputLogger : ILogger<VBLspClient>
    {
        public System.Collections.Concurrent.ConcurrentQueue<string> Messages { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Messages.Enqueue(formatter(state, exception));
    }

    private sealed class Provider : IWorkspaceArtifactProvider
    {
        public string Name => "probe";
        public string Produce(WorkspaceArtifactContext context) => "{}";
    }

    private sealed class Workspace : IWorkspaceArtifactWorkspace
    {
        public string? Directory { get; set; }
        public IReadOnlyList<WorkspaceArtifactContext> Projects { get; set; } = [];
        public IReadOnlyList<LspWorkspaceFolder> Folders { get; set; } = [];
        public event Action? ProjectChanged;
        public void Change() => ProjectChanged?.Invoke();
    }
    private static async Task<string?> ReadFrameAsync(Stream stream, CancellationToken ct)
    {
        var header = new List<byte>(64);
        var one = new byte[1];

        while (true)
        {
            if (await stream.ReadAsync(one.AsMemory(0, 1), ct) == 0) return null;
            header.Add(one[0]);
            if (header.Count >= 4
                && header[^4] == (byte)'\r' && header[^3] == (byte)'\n'
                && header[^2] == (byte)'\r' && header[^1] == (byte)'\n')
            {
                break;
            }
        }

        var length = 0;
        foreach (var line in Encoding.ASCII.GetString([.. header]).Split("\r\n", StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
            {
                length = int.Parse(line["Content-Length:".Length..].Trim(), CultureInfo.InvariantCulture);
            }
        }

        if (length <= 0) return null;

        var body = new byte[length];
        var read = 0;
        while (read < length)
        {
            var n = await stream.ReadAsync(body.AsMemory(read, length - read), ct);
            if (n == 0) return null;
            read += n;
        }

        return Encoding.UTF8.GetString(body);
    }

    private static async Task WriteFrameAsync(Stream stream, string json, CancellationToken ct)
    {
        // Collapse the interpolated literals' incidental newlines; the header must state the real length.
        var compact = JsonSerializer.Serialize(JsonDocument.Parse(json).RootElement);
        var body = Encoding.UTF8.GetBytes(compact);
        await stream.WriteAsync(Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n"), ct);
        await stream.WriteAsync(body, ct);
        await stream.FlushAsync(ct);
    }
}
