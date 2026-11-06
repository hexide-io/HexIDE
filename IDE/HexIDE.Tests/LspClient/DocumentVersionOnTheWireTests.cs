using System.Text.Json;
using AvaloniaEdit.Document;
using HexIDE.Forms.ViewModels;
using HexIDE.Events;
using HexIDE.IDE;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Lsp;
using Microsoft.Extensions.Logging;
using Nerdbank.Streams;
using StreamJsonRpc;

// NB: namespace deliberately avoids a `Lsp` segment — see VBLspClientTests.
namespace HexIDE.Tests.LspClient;

/// <summary>
/// The versions a server receives for one document, from its open through its first change, with the open
/// sent by the client and the changes by a real document session.
///
/// <para>
/// <b>Both halves real, because the defect was in the seam between them</b> (hexide-io/HexIDE#470). The
/// client hardcoded the open's version and the session counted its changes from 0, so the first change
/// repeated the open's 1. Every existing test saw half of that: the session's own tests compared one change
/// with the next through a substitute, and the wire tests called <c>ChangeDocumentAsync</c> with a version
/// chosen by hand. Neither ever recorded an open and a change from the same document.
/// </para>
/// <para>
/// The last test asks what that cost rather than what was sent. A server that orders by version discards a
/// change that is not later than what it holds, and one measured in #470 does, so the assertion that matters
/// is on the text the server ends up holding.
/// </para>
/// </summary>
public class DocumentVersionOnTheWireTests : IAsyncDisposable
{
    private const string Uri = "file:///c:/proj/Module1.bas";
    private const string SyncCapabilities = """{"textDocumentSync":{"openClose":true,"change":1,"save":true}}""";

    private readonly List<IAsyncDisposable> _disposables = [];
    private readonly List<IDisposable> _sessions = [];

    private sealed record Received(string Method, int? Version, string Text);

    /// <summary>
    /// Records every open and change. Holds a document as a server that orders by version does: a change
    /// whose version is not later than the one held is discarded.
    /// </summary>
    private sealed class VersionRecordingServer
    {
        private readonly object gate = new();
        private readonly List<Received> received = [];
        private readonly TaskCompletionSource opened = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource changed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource saved = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int heldVersion;

        public Task Opened => opened.Task;
        public Task Changed => changed.Task;
        public Task Saved => saved.Task;
        private readonly TaskCompletionSource closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task Closed => closed.Task;
        public string HeldText { get; private set; } = "";

        public IReadOnlyList<Received> Messages
        {
            get { lock (gate) return [.. received]; }
        }

        [JsonRpcMethod("initialize", UseSingleObjectParameterDeserialization = true)]
        public JsonElement Initialize(JsonElement _) =>
            JsonDocument.Parse($$"""{"capabilities":{{SyncCapabilities}}}""").RootElement.Clone();

        [JsonRpcMethod("initialized")]
        public void Initialized(JsonElement _) { }

        [JsonRpcMethod("textDocument/didOpen", UseSingleObjectParameterDeserialization = true)]
        public void DidOpen(JsonElement p)
        {
            var item = p.GetProperty("textDocument");
            lock (gate)
            {
                var version = item.GetProperty("version").GetInt32();
                var text = item.GetProperty("text").GetString()!;
                received.Add(new Received("didOpen", version, text));
                heldVersion = version;
                HeldText = text;
            }
            opened.TrySetResult();
        }

        [JsonRpcMethod("textDocument/didChange", UseSingleObjectParameterDeserialization = true)]
        public void DidChange(JsonElement p)
        {
            lock (gate)
            {
                var version = p.GetProperty("textDocument").GetProperty("version").GetInt32();
                var text = p.GetProperty("contentChanges")[0].GetProperty("text").GetString()!;
                received.Add(new Received("didChange", version, text));
                if (version > heldVersion)
                {
                    heldVersion = version;
                    HeldText = text;
                }
            }
            changed.TrySetResult();
        }

        [JsonRpcMethod("textDocument/didSave", UseSingleObjectParameterDeserialization = true)]
        public void DidSave(JsonElement p)
        {
            var item = p.GetProperty("textDocument");
            int? version = item.TryGetProperty("version", out var value) ? value.GetInt32() : null;
            lock (gate) received.Add(new Received("didSave", version, ""));
            saved.TrySetResult();
        }

        [JsonRpcMethod("textDocument/didClose", UseSingleObjectParameterDeserialization = true)]
        public void DidClose(JsonElement _)
        {
            lock (gate) received.Add(new Received("didClose", null, ""));
            closed.TrySetResult();
        }

    }

    /// <summary>A started client connected to a fresh server.</summary>
    private async Task<(VBLspClient Client, VersionRecordingServer Server)> Connected(bool start = true)
    {
        var server = new VersionRecordingServer();
        var (clientSide, serverSide) = FullDuplexStream.CreatePair();
        var serverRpc = new JsonRpc(
            new HeaderDelimitedMessageHandler(serverSide, serverSide, new SystemTextJsonFormatter()), server);
        serverRpc.StartListening();

        var transport = Substitute.For<ILspTransport>();
        transport.IsAlive.Returns(true);
        transport.ConnectAsync(Arg.Any<IJsonRpcMessageFormatter>(), Arg.Any<CancellationToken>())
            .Returns(ci => Task.FromResult<IJsonRpcMessageHandler?>(
                new HeaderDelimitedMessageHandler(clientSide, clientSide, ci.Arg<IJsonRpcMessageFormatter>())));

        var client = new VBLspClient(transport, Substitute.For<ILogger<VBLspClient>>(), "vb6");
        _disposables.Add(client);
        if (start) await client.StartAsync(TestContext.Current.CancellationToken);
        return (client, server);
    }

    /// <summary>
    /// A started session for one document, returned once its open has reached the server, as an edit or a
    /// save comes after the open for anyone typing into a window.
    /// </summary>
    private async Task<(LspDocumentSession Session, TextDocument Document)> Opened(
        VBLspClient client, VersionRecordingServer server, string text)
    {
        var document = new TextDocument(text);
        var session = new LspDocumentSession(client, document, Uri, postToUiThread: work => work());
        _sessions.Add(session);
        session.Start();

        await server.Opened.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        return (session, document);
    }

    private static async Task FirstChange(VersionRecordingServer server) =>
        await server.Changed.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

    [AvaloniaFact]
    public async Task TheDocumentIsOpenedAtTheOpeningVersion()
    {
        var (client, server) = await Connected();
        await Opened(client, server, "Sub Main()\r\nEnd Sub\r\n");

        server.Messages.Should().ContainSingle()
            .Which.Should().Be(new Received("didOpen", LspDocumentVersion.Opening, "Sub Main()\r\nEnd Sub\r\n"));
    }

    [AvaloniaFact]
    public async Task ASingleEditIsSentAtAVersionAfterTheOpens()
    {
        // One edit, then the debounce's quiet: the shape #470 named. A second keystroke inside the window
        // would raise the count before the one send goes out, which is why ordinary typing hid it.
        var (client, server) = await Connected();
        var (_, document) = await Opened(client, server, "Sub Main()\r\nEnd Sub\r\n");

        document.Insert(0, "'");
        await FirstChange(server);

        var (open, change) = (server.Messages[0], server.Messages[1]);
        change.Method.Should().Be("didChange");
        change.Version.Should().BeGreaterThan(open.Version!.Value, "a change carries the version after the change, and the open's is already taken");
        change.Text.Should().Be("'Sub Main()\r\nEnd Sub\r\n");
    }

    [AvaloniaFact]
    public async Task ASaveBeforeAnyEditSendsOnlyTheSave()
    {
        // #470 protects the first change's version. PR #2019 makes a flush send only pending edits:
        // saving an unchanged document now sends didSave alone, which carries no version to repeat.
        var cancellationToken = TestContext.Current.CancellationToken;
        var (client, server) = await Connected();
        var (session, _) = await Opened(client, server, "Sub Main()\r\nEnd Sub\r\n");

        await session.NotifySavedAsync(cancellationToken);
        await server.Saved.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);

        server.Messages.Select(m => m.Method).Should().Equal("didOpen", "didSave");
        server.Messages.Skip(1).Should().OnlyContain(m => m.Version == null);
    }

    [AvaloniaFact]
    public async Task ASaveAfterAnUnsentEditSendsOneChangeBeforeTheSaveAtAVersionAfterTheOpens()
    {
        // Keep #470's guarantee on the save/flush path as well as the debounced edit path.
        var cancellationToken = TestContext.Current.CancellationToken;
        var (client, server) = await Connected();
        var (session, document) = await Opened(client, server, "Sub Main()\r\nEnd Sub\r\n");

        document.Insert(0, "'");
        await session.NotifySavedAsync(cancellationToken);
        await server.Saved.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);

        server.Messages.Select(m => m.Method).Should().Equal("didOpen", "didChange", "didSave");
        server.Messages[2].Version.Should().BeNull("didSave carries no version");
        var change = server.Messages[1];
        change.Version.Should().BeGreaterThan(server.Messages[0].Version!.Value);
        change.Text.Should().Be("'Sub Main()\r\nEnd Sub\r\n");
        server.HeldText.Should().Be(change.Text);
    }

    [AvaloniaFact]
    public async Task AServerThatOrdersByVersionKeepsTheFirstEdit()
    {
        // What the repeated version cost. The server's copy is the one its diagnostics, folds and symbols
        // describe, so a discarded edit is analysis of text the window no longer shows, until the next edit.
        var (client, server) = await Connected();
        var (_, document) = await Opened(client, server, "Sub Main()\r\nEnd Sub\r\n");

        document.Text = "Sub Main()\r\n    Debug.Print 1\r\nEnd Sub\r\n";
        await FirstChange(server);

        server.HeldText.Should().Be(
            "Sub Main()\r\n    Debug.Print 1\r\nEnd Sub\r\n",
            "a server that discards a change no later than what it holds must still be given the edit");
    }

    private static DocumentIdentity ProjectDocument()
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "Project1");
        var module = new ModuleDefinition(project, "Module1", ModuleKind.StandardModule)
        {
            AbsolutePath = OperatingSystem.IsWindows() ? @"C:\proj\Module1.bas" : "/c:/proj/Module1.bas"
        };
        return DocumentIdentity.For(module);
    }

    private static async Task Settle() => await Task.Delay(250, TestContext.Current.CancellationToken);

    [AvaloniaFact]
    public async Task ClosingADirtyWindowKeepsItsPendingTextAndTheServerDocument()
    {
        var (client, server) = await Connected();
        var (session, document) = await Opened(client, server, "before");
        var identity = ProjectDocument();
        var events = new EventBus();
        document.Text = "after";
        identity.Module!.RecordOriginalHeader("");
        identity.Module.UpdateCode("after");
        ProjectLspDocument.CloseWindow(client, identity, session, events, dirty: true);
        await Settle();
        server.Messages.Should().NotContain(m => m.Method == "didClose");
        events.Publish(new DocumentSavedEvent(null, identity.Module));
        await server.Closed.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        server.Messages.Select(m => m.Method).Should().Equal("didOpen", "didChange", "didSave", "didClose");
        server.HeldText.Should().Be("after");
    }

    [AvaloniaFact]
    public async Task ReopeningADirtyWindowKeepsTheOpenAndVersionSequence()
    {
        var (client, server) = await Connected();
        var (session, document) = await Opened(client, server, "before");
        var identity = ProjectDocument();
        document.Text = "after";
        await session.FlushAsync(TestContext.Current.CancellationToken);
        ProjectLspDocument.CloseWindow(client, identity, session, new EventBus(), dirty: true);
        var reopened = new LspDocumentSession(client, new TextDocument("after"), Uri, postToUiThread: work => work());
        _sessions.Add(reopened);
        reopened.Start();
        ProjectLspDocument.Reopened(client, identity);
        await Settle();

        server.Messages.Select(m => m.Method).Should().Equal("didOpen", "didChange");
        LspDocumentSession.VersionsFor(client, Uri)!.Value.Current.Should().Be(2);
    }

    [AvaloniaFact]
    public async Task AServerStartingAfterTheDirtyWindowClosesGetsTheProjectText()
    {
        var (client, server) = await Connected(start: false);
        var document = new TextDocument("before");
        var session = new LspDocumentSession(client, document, Uri, postToUiThread: work => work());
        _sessions.Add(session);
        session.Start();
        document.Text = "after";
        var identity = ProjectDocument();
        var events = new EventBus();
        ProjectLspDocument.CloseWindow(client, identity, session, events, dirty: true);
        await client.StartAsync(TestContext.Current.CancellationToken);
        await server.Opened.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        server.Messages.Should().ContainSingle().Which.Should().Be(new Received("didOpen", 2, "after"));
        events.Publish(new ModuleUnloadedEvent(identity.Module!));
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RemovingOrDiscardingAClosedDirtyDocumentReleasesIt(bool discard)
    {
        var (client, server) = await Connected();
        var (session, document) = await Opened(client, server, "before");
        var identity = ProjectDocument();
        var events = new EventBus();
        document.Text = "after";
        ProjectLspDocument.CloseWindow(client, identity, session, events, dirty: true);
        await Settle();
        server.Messages.Should().NotContain(m => m.Method == "didClose");

        if (discard) events.Publish(new FileReloadedFromDiskEvent(identity.AbsolutePath!));
        else events.Publish(new ModuleUnloadedEvent(identity.Module!));
        await server.Closed.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        server.Messages.Select(m => m.Method).Should().Equal("didOpen", "didChange", "didClose");
    }

    [AvaloniaFact]
    public async Task ClosingACleanWindowStillClosesTheServerDocument()
    {
        var (client, server) = await Connected();
        var (session, _) = await Opened(client, server, "before");
        ProjectLspDocument.CloseWindow(client, ProjectDocument(), session, new EventBus(), dirty: false);
        await server.Closed.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        server.Messages.Select(m => m.Method).Should().Equal("didOpen", "didClose");
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var s in _sessions) s.Dispose();
        foreach (var d in _disposables)
        {
            try { await d.DisposeAsync(); } catch { /* teardown is best effort */ }
        }
        GC.SuppressFinalize(this);
    }
}
