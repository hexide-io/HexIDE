using System.Text.Json;
using HexIDE.Lsp;
using HexIDE.Lsp.Messages;
using Microsoft.Extensions.Logging;
using Nerdbank.Streams;
using StreamJsonRpc;
using LspRange = HexIDE.Lsp.Messages.Range;   // `Range` collides with System.Range

// NB: namespace deliberately avoids a `Lsp` segment — see VBLspClientTests.
namespace HexIDE.Tests.LspClient;

/// <summary>
/// Where a server thinks it is working, and which server wins when only one can answer.
///
/// <para>
/// Both are section 4 of #255, and both exist to make the bundled server genuinely replaceable rather than
/// merely abstracted: a user's server has to outrank ours without them learning a field exists, and any
/// server has to be rooted where the project is or it silently reads none of the user's settings for it.
/// </para>
/// </summary>
public class WorkspaceAndPriorityTests : IAsyncDisposable
{
    private readonly List<IAsyncDisposable> _disposables = [];

    private sealed class FixedWorkspace(string? directory) : ILspWorkspace
    {
        public string? Directory { get; set; } = directory;
    }

    // ── Priority: a user's entry outranks a default ───────────────────────────────────────────────────

    private static ILspClient FormattingServer(string marker)
    {
        var c = Substitute.For<ILspClient>();
        c.IsRunning.Returns(true);
        c.AdvertisedCapabilities.Returns(JsonDocument.Parse(
            """{"textDocumentSync":{"openClose":true,"change":1},"documentFormattingProvider":true}""")
            .RootElement.Clone());
        c.RequestFormattingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns([new TextEdit(new LspRange(new Position(0, 0), new Position(0, 1)), marker)]);
        return c;
    }

    [Fact]
    public async Task AUserEntryStatingNoPriorityStillOutranksABundledOne()
    {
        // The point of the constant. A user who attaches their own VB6 server should win formatting without
        // discovering that a priority field exists — otherwise "replaceable" means "replaceable if you read
        // the documentation", which is not the same thing.
        var bundled = FormattingServer("bundled");
        var mine = FormattingServer("mine");
        var sut = new LspClientRegistry(
            [
                new LanguageServerRegistration("hexide.vb6", "bundled", [".bas"], "vb6", () => bundled,
                    LanguageServerRegistration.BundledPriority),
                new LanguageServerRegistration("mine", "mine", [".bas"], "vb6", () => mine),
            ],
            Substitute.For<ILogger<LspClientRegistry>>());
        _disposables.Add(sut);

        await sut.OpenDocumentAsync("file:///c:/p/M.bas", "code");
        var edits = await sut.RequestFormattingAsync("file:///c:/p/M.bas");

        edits.Should().ContainSingle().Which.NewText.Should().Be("mine");
    }

    [Fact]
    public async Task AUserCanStillRankTheirOwnServerBelowTheBundledOne()
    {
        // Why the floor is not int.MinValue. Someone attaching a supplementary server — extra diagnostics,
        // say — must be able to say "but not for formatting", and no value can be written below a floor.
        var bundled = FormattingServer("bundled");
        var mine = FormattingServer("mine");
        var sut = new LspClientRegistry(
            [
                new LanguageServerRegistration("hexide.vb6", "bundled", [".bas"], "vb6", () => bundled,
                    LanguageServerRegistration.BundledPriority),
                new LanguageServerRegistration("mine", "mine", [".bas"], "vb6", () => mine,
                    LanguageServerRegistration.BundledPriority - 1),
            ],
            Substitute.For<ILogger<LspClientRegistry>>());
        _disposables.Add(sut);

        await sut.OpenDocumentAsync("file:///c:/p/M.bas", "code");
        var edits = await sut.RequestFormattingAsync("file:///c:/p/M.bas");

        edits.Should().ContainSingle().Which.NewText.Should().Be("bundled");
    }

    // ── Workspace: the server is told where it is ─────────────────────────────────────────────────────

    /// <summary>Records the <c>rootUri</c> of the initialize it is sent.</summary>
    private sealed class RootRecordingServer
    {
        private readonly TaskCompletionSource<string?> _rootUri =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<string?> RootUri => _rootUri.Task;

        [JsonRpcMethod("initialize", UseSingleObjectParameterDeserialization = true)]
        public JsonElement Initialize(JsonElement p)
        {
            _rootUri.TrySetResult(
                p.TryGetProperty("rootUri", out var r) && r.ValueKind == JsonValueKind.String
                    ? r.GetString()
                    : null);
            return JsonDocument.Parse("""{"capabilities":{}}""").RootElement.Clone();
        }

        [JsonRpcMethod("initialized")]
        public void Initialized(JsonElement _) { }
    }

    private VBLspClient ClientRootedAt(ILspWorkspace? workspace, RootRecordingServer server)
    {
        var (clientSide, serverSide) = FullDuplexStream.CreatePair();
        var serverRpc = new JsonRpc(
            new HeaderDelimitedMessageHandler(serverSide, serverSide, new SystemTextJsonFormatter()), server);
        serverRpc.StartListening();

        var transport = Substitute.For<ILspTransport>();
        transport.IsAlive.Returns(true);
        transport.ConnectAsync(Arg.Any<IJsonRpcMessageFormatter>(), Arg.Any<CancellationToken>())
            .Returns(ci => Task.FromResult<IJsonRpcMessageHandler?>(
                new HeaderDelimitedMessageHandler(clientSide, clientSide, ci.Arg<IJsonRpcMessageFormatter>())));

        var client = new VBLspClient(
            transport, Substitute.For<ILogger<VBLspClient>>(), DocumentLanguage.Vb6, workspace);
        _disposables.Add(client);
        return client;
    }

    [Fact]
    public async Task TheServerIsToldTheWorkspaceAsAFileUri()
    {
        // rootUri was hardcoded null, so no server had ever been told where it was working. A linter that
        // reads its rule file from the workspace therefore read none of the user's rules and reported
        // subtly different results with nothing to explain why.
        var directory = Path.Combine(Path.GetTempPath(), "hexide-ws-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(directory);
        try
        {
            var server = new RootRecordingServer();
            var sut = ClientRootedAt(new FixedWorkspace(directory), server);

            await sut.StartAsync();

            var rootUri = await server.RootUri.WaitAsync(TimeSpan.FromSeconds(10));
            rootUri.Should().NotBeNull();
            new Uri(rootUri!).LocalPath.TrimEnd('/', '\\')
                .Should().Be(directory.TrimEnd('/', '\\'));
        }
        finally
        {
            try { System.IO.Directory.Delete(directory, recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public async Task WithNoProjectOpenNoRootIsSentRatherThanAnInventedOne()
    {
        // Null is the honest answer and the protocol allows it. Inventing a root — the current directory, a
        // temp path — points every workspace-relative lookup the server makes at somewhere the user has
        // never heard of, which is a wrong answer dressed as a working one.
        var server = new RootRecordingServer();
        var sut = ClientRootedAt(new FixedWorkspace(null), server);

        await sut.StartAsync();

        (await server.RootUri.WaitAsync(TimeSpan.FromSeconds(10))).Should().BeNull();
    }

    [Fact]
    public async Task TheWorkspaceIsReadAtStartRatherThanWhenTheClientWasBuilt()
    {
        // Servers start lazily, on the first document of a language they claim. Which project is open by
        // then is not knowable when the registration is built, so capturing a value at construction would
        // root every server at whatever happened to be open at startup.
        var directory = Path.Combine(Path.GetTempPath(), "hexide-ws-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(directory);
        try
        {
            var workspace = new FixedWorkspace(null);
            var server = new RootRecordingServer();
            var sut = ClientRootedAt(workspace, server);

            // The project opens AFTER the client exists — the ordinary case, not a contrived one.
            workspace.Directory = directory;
            await sut.StartAsync();

            (await server.RootUri.WaitAsync(TimeSpan.FromSeconds(10))).Should().NotBeNull();
        }
        finally
        {
            try { System.IO.Directory.Delete(directory, recursive: true); } catch { /* best effort */ }
        }
    }

    // ── The transport runs the process there too ──────────────────────────────────────────────────────

    [Fact]
    public async Task AnExplicitWorkingDirectoryBeatsTheWorkspace()
    {
        // A user who named a working directory meant it. The workspace is the fallback, not an override.
        var explicitDir = Path.GetTempPath();
        var transport = new StdioProcessLspTransport(
            new LspServerInfo(
                OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/sh",
                OperatingSystem.IsWindows() ? "/c exit 0" : "-c \"exit 0\"",
                explicitDir),
            Substitute.For<ILogger<StdioProcessLspTransport>>(),
            new FixedWorkspace("/definitely/not/here"));
        await using var _ = transport;

        var handler = await transport.ConnectAsync(
            new SystemTextJsonFormatter(),
            new CancellationTokenSource(TimeSpan.FromSeconds(10)).Token);

        // It launched at all, which is the observable consequence: an unusable working directory would
        // have failed the start.
        handler.Should().NotBeNull();
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var d in _disposables)
        {
            try { await d.DisposeAsync(); } catch { /* teardown is best effort */ }
        }
        GC.SuppressFinalize(this);
    }
}
