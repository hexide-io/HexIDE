using System.IO.Pipes;
using System.Text.Json;
using HexIDE.Lsp;
using HexIDE.Lsp.Messages;
using Microsoft.Extensions.Logging;
using StreamJsonRpc;

// NB: namespace deliberately avoids a `Lsp` segment — see VBLspClientTests.
namespace HexIDE.Tests.LspClient;

/// <summary>
/// A configuration file becoming actual language servers — the step everything else was building towards.
///
/// <para>
/// Sections 1-5 of #255 produced a record, a loader, routing, ordering and a trust store, and connected
/// none of them: dropping a <c>lsp-servers.json</c> on disk did nothing at all. These tests drive the whole
/// path — defaults, the user's file layered over them, entries becoming registrations with their
/// transports — because every piece being individually correct is not evidence that the assembly is.
/// </para>
/// </summary>
public class ConfiguredServerTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "hexide-wire-" + Guid.NewGuid().ToString("N"));

    private readonly ILoggerFactory _loggerFactory = LoggerFactory.Create(b => { });

    public ConfiguredServerTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
        _loggerFactory.Dispose();
        GC.SuppressFinalize(this);
    }

    private static LanguageServerEntry BundledVb6() => new()
    {
        Id = LanguageServerDefaults.BundledVb6Id,
        DisplayName = "HexIDE VB6 Language Server",
        Extensions = DocumentLanguage.Vb6Extensions,
        LanguageId = DocumentLanguage.Vb6,
        Transport = "stdio",
        Command = "HexIDE.VbLspServer",
        Priority = LanguageServerRegistration.BundledPriority,
    };

    /// <summary>Loads a configuration and turns it into registrations, exactly as startup does.</summary>
    private IReadOnlyList<LanguageServerRegistration> RegistrationsFrom(
        string? json, params LanguageServerEntry[] defaults)
    {
        var path = Path.Combine(_dir, "lsp-servers.json");
        if (json is not null) File.WriteAllText(path, json);

        var configuration =
            new LanguageServerConfigLoader(path, _loggerFactory.CreateLogger<LanguageServerConfigLoader>())
                .Load(defaults);

        return new LanguageServerRegistrationFactory(_loggerFactory).Create(configuration.Entries);
    }

    [Fact]
    public void WithNoFileTheBundledServerIsRegisteredExactlyAsShipped()
    {
        // The case that must never depend on any of this working.
        var registrations = RegistrationsFrom(null, BundledVb6());

        var only = registrations.Should().ContainSingle().Subject;
        only.Id.Should().Be(LanguageServerDefaults.BundledVb6Id);
        only.LanguageId.Should().Be(DocumentLanguage.Vb6);
        only.Extensions.Should().Contain(".bas");
        only.Priority.Should().Be(LanguageServerRegistration.BundledPriority);
    }

    [Fact]
    public void AServerNamedOnlyInTheFileBecomesARegistration()
    {
        // THE point of #255, and until now impossible: a server HexIDE has never heard of, attached without
        // a rebuild.
        var registrations = RegistrationsFrom("""
            {
              "version": 1,
              "servers": [
                { "id": "rumdl", "displayName": "rumdl", "extensions": [".md", ".markdown"],
                  "languageId": "markdown", "transport": "stdio",
                  "command": "rumdl", "arguments": "server" }
              ]
            }
            """, BundledVb6());

        registrations.Select(r => r.Id).Should().Equal(["hexide.vb6", "rumdl"]);
        var rumdl = registrations.Single(r => r.Id == "rumdl");
        rumdl.LanguageId.Should().Be("markdown");
        rumdl.Extensions.Should().BeEquivalentTo([".md", ".markdown"]);
    }

    [Fact]
    public void ADisabledEntryProducesNoRegistrationAtAll()
    {
        // Not a registration that refuses to start: the router would carry a connection whose only purpose
        // is to say no, and every "is anything claiming this file" answer would have to special-case it.
        var registrations = RegistrationsFrom("""
            {"version":1,"servers":[{"id":"hexide.vb6","enabled":false}]}
            """, BundledVb6());

        registrations.Should().BeEmpty();
    }

    [Fact]
    public void AUserEntryReplacingTheBundledServerAlsoReplacesItsPriority()
    {
        // Wholesale replacement, all the way through to the registration — including the bundled priority,
        // which is why a replacement wins the pick-one features without stating one.
        var registrations = RegistrationsFrom("""
            {"version":1,"servers":[{"id":"hexide.vb6","extensions":[".bas"],"languageId":"vb6",
             "transport":"stdio","command":"my-vb6-server"}]}
            """, BundledVb6());

        registrations.Should().ContainSingle().Which.Priority.Should().Be(0);
    }

    [Fact]
    public void EachStartGetsItsOwnTransport()
    {
        // A transport is single-use — a spawned process that exits is not respawned — and a client is
        // rebuilt whenever the workspace moves. One shared instance would hand the second client a
        // disposed transport, which is a fault that only appears on the second project a user opens.
        var registrations = RegistrationsFrom("""
            {"version":1,"servers":[{"id":"s","extensions":[".md"],"languageId":"markdown",
             "transport":"stdio","command":"does-not-need-to-exist"}]}
            """);

        var create = registrations.Should().ContainSingle().Subject.CreateClient;

        create().Should().NotBeSameAs(create());
    }

    [Theory]
    [InlineData("""{"id":"s","extensions":[".md"],"languageId":"markdown","transport":"stdio","command":"x"}""")]
    [InlineData("""{"id":"s","extensions":[".md"],"languageId":"markdown","transport":"websocket","endpoint":"ws://localhost:1/"}""")]
    [InlineData("""{"id":"s","extensions":[".md"],"languageId":"markdown","transport":"pipe","pipeName":"p"}""")]
    public void EveryTransportTheLoaderAcceptsCanActuallyBeBuilt(string entryJson)
    {
        // The loader validates three transports; the factory must build all three. A transport the loader
        // accepts and the factory silently drops would be an entry that validates and never runs.
        var registrations = RegistrationsFrom($$"""{"version":1,"servers":[{{entryJson}}]}""");

        registrations.Should().ContainSingle();
        registrations[0].CreateClient().Should().NotBeNull();
    }

    [Fact]
    public void AMalformedEntryLeavesTheBundledServerRegistered()
    {
        // The failure that matters most: a user experimenting with their configuration must not lose VB6
        // support because they mistyped something in an unrelated entry.
        var registrations = RegistrationsFrom("""
            {"version":1,"servers":[{"id":"broken","extensions":[".md"],"languageId":"markdown",
             "transport":"stdio"}]}
            """, BundledVb6());

        registrations.Should().ContainSingle().Which.Id.Should().Be("hexide.vb6");
    }

    [Fact]
    public void AnUnreadableFileLeavesTheBundledServerRegistered()
    {
        var registrations = RegistrationsFrom("{ not json", BundledVb6());

        registrations.Should().ContainSingle().Which.Id.Should().Be("hexide.vb6");
    }

    [Fact]
    public void ARegistrationRoutesTheDocumentsItsEntryClaimed()
    {
        // End to end: a file on disk decides which documents reach which server. Nothing below the router
        // is mocked except the server itself.
        var registrations = RegistrationsFrom("""
            {"version":1,"servers":[{"id":"md","extensions":[".md"],"languageId":"markdown",
             "transport":"stdio","command":"whatever"}]}
            """);

        var routed = new LspClientRegistry(
            registrations.Select(r => r with { CreateClient = FakeClientFactory() }).ToList(),
            Substitute.For<ILogger<LspClientRegistry>>());

        routed.Connections.Should().ContainSingle()
            .Which.Extensions.Should().Contain(".md");
    }


    // ── The defaults themselves ───────────────────────────────────────────────────────────────────────

    [Fact]
    public void TheBundledEntryTakesItsCommandFromTheLocator()
    {
        // Not written down anywhere, because the path differs between a dev build and a publish. This is
        // the one entry whose command HexIDE computes rather than reads.
        var locator = Substitute.For<ILspServerLocator>();
        locator.FindLspServer().Returns(new LspServerInfo("/somewhere/HexIDE.VbLspServer", "--flag", "/ignored"));

        var entry = LanguageServerDefaults.For(locator, _loggerFactory.CreateLogger("t"))
            .Should().ContainSingle().Subject;

        entry.Command.Should().Be("/somewhere/HexIDE.VbLspServer");
        entry.Arguments.Should().Be("--flag");
        entry.WorkingDirectory.Should().BeEmpty(
            "left empty so the transport asks the workspace, and the server runs in whichever project is open");
    }

    [Fact]
    public void AnInstallationMissingTheBundledServerContributesNoEntry()
    {
        // Absent rather than listed-and-permanently-failing. A registration that can never connect is a row
        // in a list that lies, and #259 would render it as though something were attached.
        var locator = Substitute.For<ILspServerLocator>();
        locator.FindLspServer().Returns((LspServerInfo?)null);

        LanguageServerDefaults.For(locator, _loggerFactory.CreateLogger("t")).Should().BeEmpty();
    }

    [Fact]
    public void AMissingBundledServerLeavesAUsersOwnEntriesWorking()
    {
        // The combination that matters: a broken install of OUR server must not take a user's server with
        // it. Before the bundled row was an ordinary entry, these two shared a fate.
        var locator = Substitute.For<ILspServerLocator>();
        locator.FindLspServer().Returns((LspServerInfo?)null);
        var defaults = LanguageServerDefaults.For(locator, _loggerFactory.CreateLogger("t"));

        var registrations = RegistrationsFrom("""
            {"version":1,"servers":[{"id":"rumdl","extensions":[".md"],"languageId":"markdown",
             "transport":"stdio","command":"rumdl"}]}
            """, [.. defaults]);

        registrations.Should().ContainSingle().Which.Id.Should().Be("rumdl");
    }


    // ── The factory's own wiring, over a real channel ─────────────────────────────────────────────────

    /// <summary>Records the <c>languageId</c> of the first didOpen it is sent.</summary>
    private sealed class DidOpenRecorder
    {
        private readonly TaskCompletionSource<string> _languageId =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<string> LanguageId => _languageId.Task;

        [JsonRpcMethod("initialize", UseSingleObjectParameterDeserialization = true)]
        public JsonElement Initialize(JsonElement _) => JsonDocument.Parse(
            """{"capabilities":{"textDocumentSync":{"openClose":true,"change":1}}}""").RootElement.Clone();

        [JsonRpcMethod("initialized")]
        public void Initialized(JsonElement _) { }

        [JsonRpcMethod("textDocument/didOpen", UseSingleObjectParameterDeserialization = true)]
        public void DidOpen(JsonElement p) =>
            _languageId.TrySetResult(p.GetProperty("textDocument").GetProperty("languageId").GetString()!);
    }

    [Fact]
    public async Task AConfiguredServerIsToldTheLanguageIdItsOwnEntryDeclared()
    {
        // The factory's wiring, not the client's honouring of it — PerServerLanguageIdTests constructs a
        // VBLspClient by hand, so the factory could hand every client one hardcoded identifier and that
        // whole file would stay green. Verified: replacing `languageId` with "vb6" in the factory fails
        // this test and nothing else.
        //
        // Over a real pipe rather than a substitute, because the value under test is what goes ON THE WIRE
        // and the whole point of a foreign server is that nothing between here and it is ours.
        var pipeName = "hexide-cfg-" + Guid.NewGuid().ToString("N");
        var recorder = new DidOpenRecorder();

        using var pipe = new NamedPipeServerStream(
            pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        var serving = pipe.WaitForConnectionAsync().ContinueWith(_ =>
        {
            var rpc = new JsonRpc(
                new HeaderDelimitedMessageHandler(pipe, pipe, new SystemTextJsonFormatter()), recorder);
            rpc.StartListening();
            return rpc;
        }, TaskScheduler.Default);

        var registrations = RegistrationsFrom($$"""
            {"version":1,"servers":[{"id":"md","extensions":[".md"],"languageId":"markdown",
             "transport":"pipe","pipeName":"{{pipeName}}"}]}
            """);

        await using var client = registrations.Should().ContainSingle().Subject.CreateClient();
        await client.StartAsync();
        await client.OpenDocumentAsync("file:///c:/p/README.md", "# hi");

        (await recorder.LanguageId.WaitAsync(TimeSpan.FromSeconds(30))).Should().Be("markdown");

        (await serving).Dispose();
    }

    private static Func<ILspClient> FakeClientFactory() => () =>
    {
        var c = Substitute.For<ILspClient>();
        c.IsRunning.Returns(true);
        return c;
    };
}
