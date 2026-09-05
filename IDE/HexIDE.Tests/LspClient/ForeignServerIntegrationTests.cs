using HexIDE.Lsp;
using HexIDE.Lsp.Messages;
using Microsoft.Extensions.Logging;

// NB: namespace deliberately avoids a `Lsp` segment — see VBLspClientTests.
namespace HexIDE.Tests.LspClient;

/// <summary>
/// Drives HexIDE's language layer against a <b>real third-party server we did not write</b>.
///
/// <para>
/// Everything else in this suite tests the layer against itself. That is necessary and not sufficient: our
/// own server advertised nothing while our own client called everything unconditionally, and the pair
/// worked only because each was wrong in the way the other expected. These tests are the ones that can
/// catch that class of defect, because the far end has no idea we exist.
/// </para>
///
/// <para>
/// Skipped, visibly, when no server is installed — see <see cref="ForeignServerFactAttribute"/>.
/// </para>
/// </summary>
public class ForeignServerIntegrationTests : IAsyncDisposable
{
    private const string MarkdownUri = "file:///c:/proj/notes/README.md";

    // Deliberately violates common Markdown lint rules: no top-level heading, a heading with no space
    // after the hashes, and trailing blank lines. Which rules fire is the server's business — the test
    // asserts that SOMETHING well-formed comes back, not that a particular rule exists.
    private const string SloppyMarkdown = "##Heading without a space\n\n\n\nsome text\n\n\n";

    private LspClientRegistry? _registry;

    /// <summary>
    /// A registry holding one connection to the foreign server, claiming Markdown. No VB6 server — this
    /// isolates the foreign path so a failure cannot be masked by ours answering instead.
    /// </summary>
    private LspClientRegistry ForeignMarkdownRegistry()
    {
        // No locator, mocked or otherwise. The transport is told what to launch, which is the whole point:
        // a locator that answers "where is THE server" cannot describe a second one, so faking it was the
        // test admitting the shipping path could not express what the test was proving.
        var serverInfo = new LspServerInfo(
            ForeignServer.Markdown.Find()!, ForeignServer.Markdown.ServerArguments, Path.GetTempPath());

        var loggerFactory = LoggerFactory.Create(b => { });
        var registration = new LanguageServerRegistration(
            Id: "foreign.markdown",
            DisplayName: "Foreign Markdown server",
            Extensions: ForeignServer.Markdown.Extensions,
            LanguageId: ForeignServer.Markdown.LanguageId,
            CreateClient: () => new VBLspClient(
                new StdioProcessLspTransport(serverInfo, loggerFactory.CreateLogger<StdioProcessLspTransport>()),
                loggerFactory.CreateLogger<VBLspClient>(),
                ForeignServer.Markdown.LanguageId));

        _registry = new LspClientRegistry([registration], loggerFactory.CreateLogger<LspClientRegistry>());
        return _registry;
    }

    [ForeignServerFact]
    public async Task AForeignServerStartsLazilyAndItsDiagnosticsReachUs()
    {
        // The whole proof in one test: routing by language id, lazy start on first document, a real stdio
        // subprocess we did not write, a `file://` URI (every URI HexIDE has ever sent is `vb6://`), and a
        // capability handshake with a server that advertises honestly rather than advertising nothing.
        var sut = ForeignMarkdownRegistry();
        var received = new TaskCompletionSource<PublishDiagnosticsParams>(TaskCreationOptions.RunContinuationsAsynchronously);
        sut.DiagnosticsPublished += (_, p) => received.TrySetResult(p);

        await sut.StartAsync();
        sut.IsRunning.Should().BeFalse("nothing has been opened, so nothing should have started");

        await sut.OpenDocumentAsync(MarkdownUri, SloppyMarkdown);

        var published = await received.Task.WaitAsync(TimeSpan.FromSeconds(30));

        published.Uri.Should().Be(MarkdownUri, "a foreign server may normalise the URI — #236 is why this is checked");
        published.Diagnostics.Should().NotBeEmpty("the document deliberately violates common Markdown lint rules");
        published.Diagnostics.Should().OnlyContain(
            d => d.Range.Start.Line >= 0 && d.Range.End.Line >= d.Range.Start.Line,
            "ranges must be well-formed, or the editor cannot place a marker");
    }

    [ForeignServerFact]
    public async Task TellingAForeignServerADocumentWasSavedMakesItLookAgain()
    {
        // The honest end-to-end proof, and the one our own server cannot give: it does not ask for saves,
        // so a test against it can only show the gate refusing. This server asks, and demonstrably
        // re-analyses when told.
        //
        // Asserts the INCREMENT, never an absolute count. This server publishes more than once for a
        // single open — measured, not assumed — so a test pinned to "one publication becomes two" would
        // fail for a reason that has nothing to do with saves.
        var sut = ForeignMarkdownRegistry();
        var publications = 0;
        var settled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        sut.DiagnosticsPublished += (_, _) =>
        {
            Interlocked.Increment(ref publications);
            settled.TrySetResult();
        };

        await sut.OpenDocumentAsync(MarkdownUri, SloppyMarkdown);
        await settled.Task.WaitAsync(TimeSpan.FromSeconds(30));
        await Task.Delay(1500);                       // let the opening burst finish
        var afterOpen = Volatile.Read(ref publications);

        await sut.SaveDocumentAsync(MarkdownUri);

        // Polled rather than awaited on a completion source: what is being measured is that MORE arrive,
        // so there is no single event to wait for.
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (Volatile.Read(ref publications) <= afterOpen && DateTime.UtcNow < deadline)
            await Task.Delay(100);

        Volatile.Read(ref publications).Should().BeGreaterThan(
            afterOpen,
            "this server re-lints on save, so being told of one must produce a further publication — and "
          + "that is the behaviour a server which defers its analysis to save depends on entirely");
    }

    [ForeignServerFact]
    public async Task ItAdvertisesCapabilitiesAndTheyAreVisibleOnTheConnection()
    {
        // The mirror of #242. Ours advertised `{}` for months and nothing noticed, because our own client
        // ignored the answer. A foreign server advertises for real, so this asserts the negotiation
        // actually happened rather than being tolerated.
        var sut = ForeignMarkdownRegistry();
        await sut.OpenDocumentAsync(MarkdownUri, SloppyMarkdown);

        var connection = sut.Connections.Single();

        connection.State.Should().Be(LanguageConnectionState.Running);
        connection.LanguageId.Should().Be(ForeignServer.Markdown.LanguageId);
        connection.Extensions.Should().Contain(".md");
        connection.Capabilities.Should().NotBeNull("a conformant server advertises what it can do");
        ServerCapabilities.AcceptsOpenClose(connection.Capabilities)
            .Should().BeTrue("it accepted didOpen, so it must have advertised document sync");

        // Observed rather than assumed, because the entire save gate depends on this one field and nothing
        // else in the suite looks at what a real server puts in it.
        //
        // This server asks for saves without the text. An earlier version of this comment added "which is
        // the common choice — it intends to read the file itself", and that was an inference written as an
        // observation. It is FALSE for this very server: probed directly over stdio with a clean file on
        // disk and a sloppy buffer sent to it, a text-less didSave still reported the buffer's problems.
        // For this server `includeText: false` means "I still hold your text, run it again", not "I will
        // go and read the file". Servers that do read from disk exist; this is not one of them, and the
        // suite should not be quoted as evidence that it is.
        ServerCapabilities.ReadSave(connection.Capabilities)
            .Should().Be(SaveNotification.WithoutText);
    }

    [ForeignServerFact]
    public async Task AVb6DocumentIsNotRoutedToTheMarkdownServer()
    {
        // Routing is only meaningful if it EXCLUDES. Without this the suite would pass with a registry
        // that sent every document to every server.
        var sut = ForeignMarkdownRegistry();

        await sut.OpenDocumentAsync("vb6://module/Module1", "Sub Main()\nEnd Sub\n");

        sut.Connections.Single().State.Should().Be(
            LanguageConnectionState.NotStarted,
            "a VB6 document must not start a server that claims only Markdown");
    }

    [ForeignServerFact]
    public async Task AFeatureTheForeignServerDoesNotAdvertiseIsNotRequested()
    {
        // Gating against a real advertisement rather than our own server's. Whatever this server does or
        // does not implement, the result must be the ordinary empty one rather than an error surfacing.
        var sut = ForeignMarkdownRegistry();
        await sut.OpenDocumentAsync(MarkdownUri, SloppyMarkdown);

        var act = async () => await sut.RequestFoldingRangesAsync(MarkdownUri);

        (await act.Should().NotThrowAsync()).Which.Should().NotBeNull(
            "an unadvertised feature degrades to empty, exactly as an absent server does");
    }

    [ForeignServerFact]
    public async Task AServerAttachedOnlyByAConfigurationFileAnswersForReal()
    {
        // THE proof of #255, and the one path nothing had ever exercised. Every other foreign-server test
        // constructs its registration in test code — which is the test asserting that a shape HexIDE can
        // build works, not that the shape a USER can produce does. Here the only input is a file on disk.
        var directory = Path.Combine(Path.GetTempPath(), "hexide-cfg-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var configPath = Path.Combine(directory, "lsp-servers.json");
            File.WriteAllText(configPath, $$"""
                // Attaching a server HexIDE has never heard of, with no rebuild.
                {
                  "version": 1,
                  "servers": [
                    {
                      "id": "rumdl",
                      "displayName": "rumdl",
                      "extensions": [".md", ".markdown"],
                      "languageId": "{{ForeignServer.Markdown.LanguageId}}",
                      "transport": "stdio",
                      "command": {{System.Text.Json.JsonSerializer.Serialize(ForeignServer.Markdown.Find()!)}},
                      "arguments": "{{ForeignServer.Markdown.ServerArguments}}"
                    },
                  ]
                }
                """);

            var loggerFactory = LoggerFactory.Create(b => { });
            var configuration =
                new LanguageServerConfigLoader(configPath, loggerFactory.CreateLogger<LanguageServerConfigLoader>())
                    .Load([]);
            configuration.Problems.Should().BeEmpty("the file is well-formed");

            var registrations = new LanguageServerRegistrationFactory(loggerFactory).Create(configuration.Entries);
            _registry = new LspClientRegistry(registrations, loggerFactory.CreateLogger<LspClientRegistry>());

            var received = new TaskCompletionSource<PublishDiagnosticsParams>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _registry.DiagnosticsPublished += (_, p) => received.TrySetResult(p);

            await _registry.OpenDocumentAsync(MarkdownUri, SloppyMarkdown);

            var published = await received.Task.WaitAsync(TimeSpan.FromSeconds(30));
            published.Diagnostics.Should().NotBeEmpty(
                "a server named only in a configuration file produced real diagnostics for a real document");
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); } catch { /* best effort */ }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_registry is not null)
        {
            try { await _registry.DisposeAsync(); } catch { /* teardown is best effort */ }
        }
    }
}
