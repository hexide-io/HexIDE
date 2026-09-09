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

    private sealed class FixedWorkspace(string? directory, params LspWorkspaceFolder[] folders) : ILspWorkspace
    {
        public string? Directory { get; set; } = directory;

        public IReadOnlyList<LspWorkspaceFolder> Folders { get; set; } = folders;
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

        await sut.OpenDocumentAsync("file:///c:/p/M.bas", "code", TestContext.Current.CancellationToken);
        var edits = await sut.RequestFormattingAsync("file:///c:/p/M.bas", TestContext.Current.CancellationToken);

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

        await sut.OpenDocumentAsync("file:///c:/p/M.bas", "code", TestContext.Current.CancellationToken);
        var edits = await sut.RequestFormattingAsync("file:///c:/p/M.bas", TestContext.Current.CancellationToken);

        edits.Should().ContainSingle().Which.NewText.Should().Be("bundled");
    }

    // ── Workspace: the server is told where it is ─────────────────────────────────────────────────────

    /// <summary>Records the initialize frame it is sent, so the wire can be asserted rather than the call.</summary>
    private sealed class RootRecordingServer
    {
        private readonly TaskCompletionSource<string?> _rootUri =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<JsonElement> _frame =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<string?> RootUri => _rootUri.Task;

        /// <summary>The whole <c>initialize</c> params, cloned — the frame, not our idea of it.</summary>
        public Task<JsonElement> Frame => _frame.Task;

        [JsonRpcMethod("initialize", UseSingleObjectParameterDeserialization = true)]
        public JsonElement Initialize(JsonElement p)
        {
            _frame.TrySetResult(p.Clone());
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

            await sut.StartAsync(TestContext.Current.CancellationToken);

            var rootUri = await server.RootUri.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
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
    public async Task EveryLoadedProjectBecomesAWorkspaceFolder()
    {
        // A .vbg group names its members by relative path, so its projects routinely live in different
        // directories. One root meant every server believed the workspace was wherever the STARTUP project
        // happened to be, and switching startup project silently re-rooted every server (#261).
        var a = Path.Combine(Path.GetTempPath(), "hexide-ws-a-" + Guid.NewGuid().ToString("N"));
        var b = Path.Combine(Path.GetTempPath(), "hexide-ws-b-" + Guid.NewGuid().ToString("N"));
        var server = new RootRecordingServer();
        var sut = ClientRootedAt(
            new FixedWorkspace(a, new LspWorkspaceFolder("Orders", a), new LspWorkspaceFolder("Shared", b)),
            server);

        await sut.StartAsync(TestContext.Current.CancellationToken);
        var frame = await server.Frame.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        frame.TryGetProperty("workspaceFolders", out var folders).Should().BeTrue();
        folders.ValueKind.Should().Be(JsonValueKind.Array);
        folders.GetArrayLength().Should().Be(2, "one folder per loaded project, not one for the group");

        var names = folders.EnumerateArray().Select(f => f.GetProperty("name").GetString()).ToList();
        names.Should().BeEquivalentTo(["Orders", "Shared"], "a folder is named as the user sees the project");

        foreach (var folder in folders.EnumerateArray())
            folder.GetProperty("uri").GetString().Should().StartWith("file:///",
                "a folder is addressed by URI, not by path");
    }

    [Fact]
    public async Task RootUriIsStillSentAlongsideTheFolders()
    {
        // Not a compatibility shrug. Measured across the four servers this suite drives: clangd never
        // parses workspaceFolders at all, so rootUri is its ONLY root channel — dropping it in favour of
        // the modern field would silently un-scope that server while looking like a tidy-up.
        var directory = Path.Combine(Path.GetTempPath(), "hexide-ws-" + Guid.NewGuid().ToString("N"));
        var server = new RootRecordingServer();
        var sut = ClientRootedAt(
            new FixedWorkspace(directory, new LspWorkspaceFolder("Orders", directory)), server);

        await sut.StartAsync(TestContext.Current.CancellationToken);
        var frame = await server.Frame.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        frame.GetProperty("rootUri").ValueKind.Should().Be(JsonValueKind.String);
        frame.GetProperty("workspaceFolders").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task TheClientDeclaresItUnderstandsWorkspaceFolders()
    {
        // Declaring and sending are two halves of one negotiation. A server that composes its behaviour
        // from what the client claimed ignores folders it was handed when nothing declared support —
        // the same trap already recorded for `save` and `codeLens`.
        var directory = Path.Combine(Path.GetTempPath(), "hexide-ws-" + Guid.NewGuid().ToString("N"));
        var server = new RootRecordingServer();
        var sut = ClientRootedAt(new FixedWorkspace(directory, new LspWorkspaceFolder("Orders", directory)), server);

        await sut.StartAsync(TestContext.Current.CancellationToken);
        var frame = await server.Frame.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        frame.GetProperty("capabilities").GetProperty("workspace")
             .GetProperty("workspaceFolders").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task WithNoProjectOpenTheFoldersAreAbsentRatherThanEmpty()
    {
        // The protocol distinguishes them: null is "no folders are open", an empty array is a workspace
        // that has folders and happens to have none right now. With nothing loaded the first is true.
        var server = new RootRecordingServer();
        var sut = ClientRootedAt(new FixedWorkspace(null), server);

        await sut.StartAsync(TestContext.Current.CancellationToken);
        var frame = await server.Frame.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        if (frame.TryGetProperty("workspaceFolders", out var folders))
            folders.ValueKind.Should().Be(JsonValueKind.Null, "an empty array would claim something different");
    }

    [Fact]
    public async Task WithNoProjectOpenNoRootIsSentRatherThanAnInventedOne()
    {
        // Null is the honest answer and the protocol allows it. Inventing a root — the current directory, a
        // temp path — points every workspace-relative lookup the server makes at somewhere the user has
        // never heard of, which is a wrong answer dressed as a working one.
        var server = new RootRecordingServer();
        var sut = ClientRootedAt(new FixedWorkspace(null), server);

        await sut.StartAsync(TestContext.Current.CancellationToken);

        (await server.RootUri.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken)).Should().BeNull();
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
            await sut.StartAsync(TestContext.Current.CancellationToken);

            (await server.RootUri.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken)).Should().NotBeNull();
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

    // ── A server must not keep serving a project that is gone ─────────────────────────────────────────

    private static ILspClient RunningServer()
    {
        var c = Substitute.For<ILspClient>();
        c.IsRunning.Returns(true);
        c.AdvertisedCapabilities.Returns(
            JsonDocument.Parse("""{"textDocumentSync":{"openClose":true,"change":1}}""").RootElement.Clone());
        return c;
    }

    [Fact]
    public async Task ClosingOneProjectAndOpeningAnotherRestartsTheServers()
    {
        // rootUri is sent once, at initialize, and never revised — so without this a server started for
        // project A goes on serving project B, reading A's configuration and reporting results with nothing
        // to indicate why. This is NOT the first-save transition: switching projects is ordinary, permanent,
        // and unrelated to a project acquiring a location.
        var workspace = new FixedWorkspace("/projects/a");
        var created = new List<ILspClient>();
        var sut = new LspClientRegistry(
            [new LanguageServerRegistration("s", "s", [".bas"], "vb6", () =>
            {
                var c = RunningServer();
                created.Add(c);
                return c;
            })],
            Substitute.For<ILogger<LspClientRegistry>>(),
            workspace);
        _disposables.Add(sut);

        await sut.OpenDocumentAsync("file:///projects/a/M.bas", "code", TestContext.Current.CancellationToken);
        created.Should().HaveCount(1);

        workspace.Directory = "/projects/b";
        await sut.OpenDocumentAsync("file:///projects/b/M.bas", "code", TestContext.Current.CancellationToken);

        created.Should().HaveCount(2, "the server was rebuilt so it could be told the new root");
        await created[0].Received(1).StopAsync();
    }

    [Fact]
    public async Task AWorkspaceThatHasNotMovedDoesNotRestartAnything()
    {
        // The control. Without it a bug that restarted on every document open would pass the test above,
        // while throwing away every server's state on each file the user touches.
        var workspace = new FixedWorkspace("/projects/a");
        var created = new List<ILspClient>();
        var sut = new LspClientRegistry(
            [new LanguageServerRegistration("s", "s", [".bas"], "vb6", () =>
            {
                var c = RunningServer();
                created.Add(c);
                return c;
            })],
            Substitute.For<ILogger<LspClientRegistry>>(),
            workspace);
        _disposables.Add(sut);

        await sut.OpenDocumentAsync("file:///projects/a/One.bas", "code", TestContext.Current.CancellationToken);
        await sut.OpenDocumentAsync("file:///projects/a/Two.bas", "code", TestContext.Current.CancellationToken);

        created.Should().HaveCount(1);
        await created[0].DidNotReceive().StopAsync();
    }

    [Fact]
    public async Task AServerThatFailedToStartIsNotRetriedJustBecauseTheProjectChanged()
    {
        // A workspace changing is not a reason to believe a command that would not launch will launch now.
        // Retrying on every project switch turns one broken entry into a cost paid over and over.
        var workspace = new FixedWorkspace("/projects/a");
        var attempts = 0;
        var sut = new LspClientRegistry(
            [new LanguageServerRegistration("s", "s", [".bas"], "vb6", () =>
            {
                attempts++;
                var c = Substitute.For<ILspClient>();
                c.IsRunning.Returns(false);   // started, but never came up
                return c;
            })],
            Substitute.For<ILogger<LspClientRegistry>>(),
            workspace);
        _disposables.Add(sut);

        await sut.OpenDocumentAsync("file:///projects/a/M.bas", "code", TestContext.Current.CancellationToken);
        workspace.Directory = "/projects/b";
        await sut.OpenDocumentAsync("file:///projects/b/M.bas", "code", TestContext.Current.CancellationToken);

        attempts.Should().Be(1);
    }
}
