using System.Text.Json;
using HexIDE.Lsp;
using Microsoft.Extensions.Logging;
using Nerdbank.Streams;
using StreamJsonRpc;

// NB: namespace deliberately avoids a `Lsp` segment — see VBLspClientTests.
namespace HexIDE.Tests.LspClient;

/// <summary>
/// What actually happens when a server sends <c>client/registerCapability</c>.
///
/// <para>
/// Written before any fix, because the issue that prompted it (hexide-io/HexIDE#288) asserted a
/// consequence nobody here had observed. The client registers no handler for that request, and what a
/// server gets back was assumed to be a JSON-RPC error on StreamJsonRpc's account rather than measured.
/// "Very probably an error" is not a basis for changing a protocol seam.
/// </para>
/// </summary>
public class DynamicRegistrationProbe : IAsyncDisposable
{
    private readonly List<IAsyncDisposable> _clients = [];
    private readonly List<IDisposable> _disposables = [];

    public async ValueTask DisposeAsync()
    {
        foreach (var c in _clients)
        {
            try { await c.DisposeAsync(); } catch { /* teardown is best effort */ }
        }
        foreach (var d in _disposables)
        {
            try { d.Dispose(); } catch { /* teardown is best effort */ }
        }
        foreach (var path in _directories) Directory.Delete(path, true);
        GC.SuppressFinalize(this);
    }

    private sealed class RegisteringServer
    {
        public JsonRpc? Rpc;
        public JsonElement ClientCapabilities;
        public readonly System.Collections.Concurrent.ConcurrentQueue<JsonElement> Changes = new();

        [JsonRpcMethod("workspace/didChangeWatchedFiles", UseSingleObjectParameterDeserialization = true)]
        public void Changed(JsonElement p) => Changes.Enqueue(p.Clone());

        [JsonRpcMethod("workspace/didChangeWorkspaceFolders", UseSingleObjectParameterDeserialization = true)]
        public void FoldersChanged(JsonElement _) { }

        public Task<JsonElement> RegisterAsync(object watchers, string id = "files-1") =>
            Rpc!.InvokeWithParameterObjectAsync<JsonElement>("client/registerCapability", new
            {
                registrations = new[] { new { id, method = "workspace/didChangeWatchedFiles", registerOptions = new { watchers } } },
            });

        public Task<JsonElement> UnregisterAsync(string id = "files-1") =>
            Rpc!.InvokeWithParameterObjectAsync<JsonElement>("client/unregisterCapability", new
            {
                unregisterations = new[] { new { id, method = "workspace/didChangeWatchedFiles" } },
            });

        [JsonRpcMethod("initialize", UseSingleObjectParameterDeserialization = true)]
        public JsonElement Initialize(JsonElement p)
        {
            ClientCapabilities = p.TryGetProperty("capabilities", out var caps)
                ? caps.Clone()
                : default;
            // Watched files have no static capability; the server registers them dynamically.
            return JsonDocument.Parse("""{"capabilities":{"textDocumentSync":1,"workspace":{"workspaceFolders":{"supported":true,"changeNotifications":true}}}}""").RootElement.Clone();
        }

        [JsonRpcMethod("initialized")]
        public void Initialized(JsonElement _) { }

        /// <summary>Asks the client to register hover, the way a server built on the reference library would.</summary>
        public Task<JsonElement> RegisterHoverAsync() =>
            Rpc!.InvokeWithParameterObjectAsync<JsonElement>("client/registerCapability", new
            {
                registrations = new[]
                {
                    new { id = "hover-1", method = "textDocument/hover", registerOptions = new { } },
                },
            });
    }

    private sealed class RecordingLogger : ILogger<VBLspClient>
    {
        private readonly List<(LogLevel Level, string Message)> _entries = [];

        public IReadOnlyList<(LogLevel Level, string Message)> Entries
        {
            get { lock (_entries) return _entries.ToArray(); }
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            lock (_entries) _entries.Add((logLevel, formatter(state, exception)));
        }
    }

    private async Task<(VBLspClient Client, RegisteringServer Server)> ConnectedAsync(
        ILogger<VBLspClient>? logger = null, string? root = null, ILspWorkspace? workspace = null)
    {
        var server = new RegisteringServer();
        var (clientSide, serverSide) = FullDuplexStream.CreatePair();

        var serverRpc = new JsonRpc(
            new HeaderDelimitedMessageHandler(serverSide, serverSide, new SystemTextJsonFormatter()), server);
        server.Rpc = serverRpc;
        serverRpc.StartListening();
        _disposables.Add(serverRpc);

        var transport = Substitute.For<ILspTransport>();
        transport.IsAlive.Returns(true);
        transport.ConnectAsync(Arg.Any<IJsonRpcMessageFormatter>(), Arg.Any<CancellationToken>())
            .Returns(ci => Task.FromResult<IJsonRpcMessageHandler?>(
                new HeaderDelimitedMessageHandler(clientSide, clientSide, ci.Arg<IJsonRpcMessageFormatter>())));

        if (workspace is null)
        {
            workspace = Substitute.For<ILspWorkspace>();
            workspace.Directory.Returns(root);
            workspace.Folders.Returns(root is null ? [] : new[] { new LspWorkspaceFolder("test", root) });
        }
        var client = new VBLspClient(
            transport, logger ?? Substitute.For<ILogger<VBLspClient>>(), DocumentLanguage.Vb6, workspace: workspace);
        _clients.Add(client);
        await client.StartAsync();
        return (client, server);
    }

    [Fact]
    public async Task TheClientClaimsDynamicRegistrationOnlyForWatchedFiles()
    {
        var (_, server) = await ConnectedAsync();
        var caps = server.ClientCapabilities;
        var files = caps.GetProperty("workspace").GetProperty("didChangeWatchedFiles");
        files.GetProperty("dynamicRegistration").GetBoolean().Should().BeTrue();
        files.GetProperty("relativePatternSupport").GetBoolean().Should().BeTrue();
        System.Text.RegularExpressions.Regex.Matches(caps.GetRawText(), "dynamicRegistration").Count.Should().Be(1);
    }

    [Fact]
    public async Task AServerRegisteringAnywayGetsAnErrorRatherThanSilence()
    {
        // The half that is ours, and the reason this file exists: measured, not assumed. A server that
        // registers regardless — non-conformant, but they exist — must get an answer. Silence would leave
        // it awaiting a reply forever, which is the failure shape this client has already been bitten by
        // (hexide-io/HexIDE#231).
        var (_, server) = await ConnectedAsync();

        var register = server.RegisterHoverAsync();
        // xUnit1051 suppressed deliberately: this Task.Delay is the TIMEOUT ARM of the race, not
        // work the test is waiting on. Giving it the test's cancellation token would make the
        // timeout itself cancellable — the guard would vanish exactly when a cancelled run most
        // needs it to fire, and WhenAny would settle on a faulted task rather than a timeout.
#pragma warning disable xUnit1051
        var finished = await Task.WhenAny(register, Task.Delay(TimeSpan.FromSeconds(10)));
#pragma warning restore xUnit1051

        finished.Should().BeSameAs(register, "an unanswered request would hang the server, not degrade it");

        var thrown = await Record.ExceptionAsync(() => register);
        thrown.Should().BeOfType<RemoteMethodNotFoundException>(
            "the honest answer to a request we do not implement is a JSON-RPC error naming that, which "
          + "lets a conformant server fall back rather than wait");
    }

    [Fact]
    public async Task RefusingIsWrittenDown_SoMissingFeaturesHaveATrace()
    {
        // The only thing this change actually altered. The wire answer was already right; what was absent
        // was any local record, so a server that asked, was refused, and served less left the user with
        // fewer features and nothing to explain them. That is the same silence #289 closed on a different
        // channel, and it is the whole of #288 once the spec question is settled.
        var logger = new RecordingLogger();
        var (_, server) = await ConnectedAsync(logger);

        await Record.ExceptionAsync(() => server.RegisterHoverAsync());

        logger.Entries.Should().ContainSingle(e =>
                e.Level == LogLevel.Warning && e.Message.Contains("register capabilities dynamically"),
            "a refusal nobody can see is the bug this issue was actually about");
    }

    [Fact]
    public async Task TheLogNamesWhatWasRefused_NotJustThatSomethingWas()
    {
        // "A server asked for something" is not actionable. Which method it wanted is.
        var logger = new RecordingLogger();
        var (_, server) = await ConnectedAsync(logger);

        await Record.ExceptionAsync(() => server.RegisterHoverAsync());

        logger.Entries.Should().Contain(e => e.Message.Contains("textDocument/hover"),
            "the method it wanted is what tells a reader which feature went missing");
    }
    [Fact]
    public async Task DirectWritesReachEveryRunningConnectionWithoutStartingOthers()
    {
        var first = Substitute.For<ILspClient>();
        var second = Substitute.For<ILspClient>();
        var idle = Substitute.For<ILspClient>();
        first.IsRunning.Returns(true);
        second.IsRunning.Returns(true);
        await using var registry = new LspClientRegistry([
            new("first", "First", [".bas"], "vb6", () => first),
            new("second", "Second", [".py"], "python", () => second),
            new("idle", "Idle", [".md"], "markdown", () => idle),
        ], Substitute.For<ILogger<LspClientRegistry>>());
        await registry.OpenDocumentAsync("untitled:test.bas", "code", TestContext.Current.CancellationToken);
        await registry.OpenDocumentAsync("untitled:test.py", "code", TestContext.Current.CancellationToken);
        var path = Path.Combine(DirectoryForTest(), "pyproject.toml");
        var notify = typeof(LspClientRegistry).GetMethod("NotifyWatchedFile");
        notify.Should().NotBeNull();
        notify!.Invoke(registry, new object[] { path, 2 });
        foreach (var client in new[] { first, second })
            client.ReceivedCalls().Should().ContainSingle(c => c.GetMethodInfo().Name == "NotifyWatchedFile"
                && Equals(c.GetArguments()[0], path) && Equals(c.GetArguments()[1], 2));
        idle.ReceivedCalls().Should().BeEmpty();
    }

    private readonly List<string> _directories = [];

    private string DirectoryForTest()
    {
        var path = Path.Combine(Path.GetTempPath(), "hexide-watch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        _directories.Add(path);
        return path;
    }

    private static async Task<JsonElement[]> EventsAsync(RegisteringServer server, int count)
    {
        var until = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < until)
        {
            var events = server.Changes.SelectMany(p => p.GetProperty("changes").EnumerateArray()).ToArray();
            if (events.Length >= count) return events;
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }
        throw new TimeoutException("No matching watched-file frame arrived.");
    }

    [Theory]
    [InlineData("**/pyproject.toml", "pyproject.toml")]
    [InlineData("**/*.{bas,cls,frm}", "folder/code.bas")]
    [InlineData("folder/file?.[bc]as", "folder/file1.bas")]
    [InlineData("**/[!x]*.bas", "folder/code.bas")]
    public async Task RegisteredGlobsSendCreatedFileEvents(string glob, string relative)
    {
        var root = DirectoryForTest();
        Directory.CreateDirectory(Path.Combine(root, "folder"));
        var (_, server) = await ConnectedAsync(root: root);
        await server.RegisterAsync(new[] { new { globPattern = glob, kind = 1 } });
        var path = Path.Combine(root, relative);
        await File.WriteAllTextAsync(path, "test", TestContext.Current.CancellationToken);
        var events = await EventsAsync(server, 1);
        events.Should().ContainSingle();
        events[0].GetProperty("uri").GetString().Should().Be(new Uri(path).AbsoluteUri);
        events[0].GetProperty("type").GetInt32().Should().Be(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RelativePatternsUseTheirBaseAndStayInsideIt(bool folderObject)
    {
        var root = DirectoryForTest();
        var inside = Path.Combine(root, "inside space");
        Directory.CreateDirectory(inside);
        var (_, server) = await ConnectedAsync(root: root);
        var uri = new Uri(inside).AbsoluteUri;
        object baseUri = folderObject ? new { uri, name = "inside" } : uri;
        await server.RegisterAsync(new[] { new { globPattern = new { baseUri, pattern = "*.toml" }, kind = 1 } });
        await File.WriteAllTextAsync(Path.Combine(root, "outside.toml"), "test", TestContext.Current.CancellationToken);
        var path = Path.Combine(inside, "inside.toml");
        await File.WriteAllTextAsync(path, "test", TestContext.Current.CancellationToken);
        var events = await EventsAsync(server, 1);
        events.Should().ContainSingle();
        events[0].GetProperty("uri").GetString().Should().Be(new Uri(path).AbsoluteUri);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(7)]
    public async Task WatchKindFiltersAndBatchesDirectEvents(int kind)
    {
        var root = DirectoryForTest();
        var (client, server) = await ConnectedAsync(root: root);
        await server.RegisterAsync(new[] { new { globPattern = "**/*.toml", kind } });
        // Invoke through reflection so these tests can run against main to demonstrate the missing API.
        var notify = typeof(VBLspClient).GetMethod("NotifyWatchedFile");
        notify.Should().NotBeNull();
        for (var type = 1; type <= 3; type++)
            for (var i = 0; i < 10; i++)
                notify!.Invoke(client, new object[] { Path.Combine(root, $"file-{type}-{i}.toml"), type });
        var expected = kind == 7 ? 30 : 10;
        var events = await EventsAsync(server, expected);
        events.Should().HaveCount(expected);
        server.Changes.Should().ContainSingle("a burst is one wire notification");
        foreach (var e in events) (kind & (1 << (e.GetProperty("type").GetInt32() - 1))).Should().NotBe(0);
    }

    [Fact]
    public async Task DiskChangesAndDeletesUseDefaultKinds()
    {
        var root = DirectoryForTest();
        var path = Path.Combine(root, "pyproject.toml");
        await File.WriteAllTextAsync(path, "initial", TestContext.Current.CancellationToken);
        var (_, server) = await ConnectedAsync(root: root);
        await server.RegisterAsync(new[] { new { globPattern = "**/pyproject.toml" } });
        await File.WriteAllTextAsync(path, "changed", TestContext.Current.CancellationToken);
        var events = await EventsAsync(server, 1);
        events[0].GetProperty("type").GetInt32().Should().Be(2);
        File.Delete(path);
        events = await EventsAsync(server, 2);
        events.Should().Contain(e => e.GetProperty("type").GetInt32() == 3);
    }

    [Fact]
    public async Task AbsoluteGlobsAndMultipleRegistrationsAreIndependent()
    {
        var root = DirectoryForTest();
        var (_, server) = await ConnectedAsync(root: root);
        var glob = root.Replace(Path.DirectorySeparatorChar, '/') + "/**/*.toml";
        await server.RegisterAsync(new[] { new { globPattern = glob, kind = 1 } });
        var absolutePath = Path.Combine(root, "absolute.toml");
        await File.WriteAllTextAsync(absolutePath, "test", TestContext.Current.CancellationToken);
        (await EventsAsync(server, 1))[0].GetProperty("uri").GetString().Should().Be(new Uri(absolutePath).AbsoluteUri);
        server.Changes.Clear();
        await server.RegisterAsync(new[] { new { globPattern = "**/*.bas", kind = 1 } }, "second");
        await server.UnregisterAsync();
        await File.WriteAllTextAsync(Path.Combine(root, "ignored.toml"), "test", TestContext.Current.CancellationToken);
        var path = Path.Combine(root, "accepted.bas");
        await File.WriteAllTextAsync(path, "test", TestContext.Current.CancellationToken);
        var events = await EventsAsync(server, 1);
        events.Should().ContainSingle();
        events[0].GetProperty("uri").GetString().Should().Be(new Uri(path).AbsoluteUri);
    }

    [Fact]
    public async Task UnregisterDropsQueuedEventsAndOtherConnectionsReceiveNothing()
    {
        var root = DirectoryForTest();
        var (client, server) = await ConnectedAsync(root: root);
        var (_, other) = await ConnectedAsync(root: root);
        await server.RegisterAsync(new[] { new { globPattern = "**/*.toml", kind = 1 } });
        typeof(VBLspClient).GetMethod("NotifyWatchedFile")!.Invoke(client,
            new object[] { Path.Combine(root, "queued.toml"), 1 });
        await server.UnregisterAsync();
        await File.WriteAllTextAsync(Path.Combine(root, "ignored.toml"), "test", TestContext.Current.CancellationToken);
        await server.RegisterAsync(new[] { new { globPattern = "**/*.toml", kind = 2 } });
        var path = Path.Combine(root, "queued.toml");
        typeof(VBLspClient).GetMethod("NotifyWatchedFile")!.Invoke(client, new object[] { path, 2 });
        var events = await EventsAsync(server, 1);
        events.Should().ContainSingle();
        events[0].GetProperty("uri").GetString().Should().Be(new Uri(path).AbsoluteUri);
        events[0].GetProperty("type").GetInt32().Should().Be(2);
        other.Changes.Should().BeEmpty();
        await client.StopAsync();
        await File.WriteAllTextAsync(Path.Combine(root, "after-stop.bas"), "test", TestContext.Current.CancellationToken);
        server.Changes.Should().ContainSingle();
    }

    [Fact]
    public async Task StringGlobsFollowFoldersWhileRelativeBasesStayFixed()
    {
        var oldRoot = DirectoryForTest();
        var newRoot = DirectoryForTest();
        var fixedRoot = DirectoryForTest();
        var workspace = Substitute.For<ILspWorkspace>();
        workspace.Directory.Returns(oldRoot);
        workspace.Folders.Returns(new[] { new LspWorkspaceFolder("old", oldRoot) });
        var (client, server) = await ConnectedAsync(workspace: workspace);
        await server.RegisterAsync(new[] { new { globPattern = "**/*.toml", kind = 1 } });
        await server.RegisterAsync(new[] { new { globPattern = new { baseUri = new Uri(fixedRoot).AbsoluteUri, pattern = "*.bas" }, kind = 1 } }, "fixed");
        workspace.Folders.Returns(new[] { new LspWorkspaceFolder("new", newRoot) });
        await client.UpdateWorkspaceFoldersAsync(TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(oldRoot, "removed.toml"), "test", TestContext.Current.CancellationToken);
        var added = Path.Combine(newRoot, "added.toml");
        var fixedPath = Path.Combine(fixedRoot, "fixed.bas");
        await File.WriteAllTextAsync(added, "test", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(fixedPath, "test", TestContext.Current.CancellationToken);
        var events = await EventsAsync(server, 2);
        events.Select(e => e.GetProperty("uri").GetString()).Should().BeEquivalentTo(
            new[] { new Uri(added).AbsoluteUri, new Uri(fixedPath).AbsoluteUri });
        server.Changes.Clear();
        workspace.Folders.Returns(Array.Empty<LspWorkspaceFolder>());
        await client.UpdateWorkspaceFoldersAsync(TestContext.Current.CancellationToken);
        client.NotifyWatchedFile(Path.Combine(oldRoot, "fallback.toml"), 1);
        client.NotifyWatchedFile(Path.Combine(newRoot, "removed.toml"), 1);
        client.NotifyWatchedFile(fixedPath, 1);
        (await EventsAsync(server, 1)).Should().ContainSingle()
            .Which.GetProperty("uri").GetString().Should().Be(new Uri(fixedPath).AbsoluteUri);
    }

    [Fact]
    public async Task AGlobMatchesAFileNameWhateverItsCase()
    {
        var root = DirectoryForTest();
        var (client, server) = await ConnectedAsync(root: root);
        await server.RegisterAsync(new[] { new { globPattern = "**/*.{bas,cls}", kind = 1 } });

        var upper = Path.Combine(root, "Upper.BAS");
        client.NotifyWatchedFile(upper, 1);

        (await EventsAsync(server, 1)).Should().ContainSingle()
            .Which.GetProperty("uri").GetString().Should().Be(new Uri(upper).AbsoluteUri);
    }

    [Fact]
    public async Task AFolderThatCannotBeWatchedKeepsTheWatchesItHad()
    {
        var root = DirectoryForTest();
        var workspace = Substitute.For<ILspWorkspace>();
        workspace.Directory.Returns(root);
        workspace.Folders.Returns(new[] { new LspWorkspaceFolder("old", root) });
        var logger = new RecordingLogger();
        var (client, server) = await ConnectedAsync(logger, workspace: workspace);
        await server.RegisterAsync(new[] { new { globPattern = "**/*.toml", kind = 1 } });
        workspace.Folders.Returns(new[] { new LspWorkspaceFolder("disk", Path.GetPathRoot(root)!) });

        await client.UpdateWorkspaceFoldersAsync(TestContext.Current.CancellationToken);

        logger.Entries.Should().Contain(e => e.Level == LogLevel.Warning && e.Message.Contains("workspace folder"));
        var kept = Path.Combine(root, "kept.toml");
        client.NotifyWatchedFile(kept, 1);
        (await EventsAsync(server, 1)).Should().ContainSingle()
            .Which.GetProperty("uri").GetString().Should().Be(new Uri(kept).AbsoluteUri);
    }

    [Theory]
    [InlineData("absolute")]
    [InlineData("relative")]
    [InlineData("missing")]
    public async Task FilesystemRootWatchesAreRefusedAtomicallyAndLogged(string form)
    {
        var root = DirectoryForTest();
        var diskRoot = Path.GetPathRoot(root)!;
        var watchRoot = form == "missing" ? Path.Combine(diskRoot, "hexide-missing-" + Guid.NewGuid().ToString("N")) : diskRoot;
        // Validate before sending: a regressed implementation must not recursively watch the test host's disk.
        var create = typeof(VBLspClient).Assembly.GetType("HexIDE.Lsp.WatchedFileRegistrations")!
            .GetMethod("Create", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        var validation = Record.Exception(() => create.Invoke(null, new object[] { watchRoot, "**", 1, false }));
        validation.Should().BeOfType<System.Reflection.TargetInvocationException>()
            .Which.InnerException.Should().BeOfType<LocalRpcException>();
        var logger = new RecordingLogger();
        var (client, server) = await ConnectedAsync(logger, root);
        object glob = form == "absolute" ? diskRoot.Replace(Path.DirectorySeparatorChar, '/') + "**"
            : new { baseUri = new Uri(watchRoot).AbsoluteUri, pattern = "**" };
        var error = await Record.ExceptionAsync(() => server.RegisterAsync(new object[]
        {
            new { globPattern = "**/*.toml", kind = 1 }, new { globPattern = glob, kind = 1 },
        }));
        error.Should().BeOfType<RemoteMethodNotFoundException>().Which.ErrorCode.Should()
            .Be(StreamJsonRpc.Protocol.JsonRpcErrorCode.InvalidParams);
        logger.Entries.Should().Contain(e => e.Level == LogLevel.Warning && e.Message.Contains("register"));
        client.NotifyWatchedFile(Path.Combine(root, "refused.toml"), 1);
        await server.RegisterAsync(new[] { new { globPattern = "**/*.bas", kind = 1 } }, "accepted");
        client.NotifyWatchedFile(Path.Combine(root, "accepted.bas"), 1);
        var events = await EventsAsync(server, 1);
        events.Should().ContainSingle();
        events[0].GetProperty("uri").GetString().Should().Be(new Uri(Path.Combine(root, "accepted.bas")).AbsoluteUri);
    }

}
