using HexIDE.Conversations;
using HexIDE.Lsp;
using Microsoft.Extensions.Logging;

// NB: namespace deliberately avoids a `Lsp` segment — see VBLspClientTests.
namespace HexIDE.Tests.LspClient;

/// <summary>
/// DRAFT ONLY, NOT FOR MERGE (#390). Repeats the handshake ItAdvertisesCapabilitiesAndTheyAreVisibleOnTheConnection
/// makes with rumdl, many times in one run, so CI captures a failure with the channel tap in the record. It fails on
/// macOS runners about two runs in three as a single test.
/// </summary>
public class RumdlHandshakeLoopTests
{
    private const string MarkdownUri = "file:///c:/proj/notes/README.md";

    // rumdl logs its own startup to standard error under RUST_LOG, and the record keeps standard error with its
    // time, so a stall shows the last thing rumdl did. The first loop run showed the request reaching rumdl's stdin
    // within 66 ms and nothing coming back.
    static RumdlHandshakeLoopTests() => Environment.SetEnvironmentVariable("RUST_LOG", "debug");

    public static TheoryData<int> Iterations()
    {
        var data = new TheoryData<int>();
        for (var i = 1; i <= 40; i++)
            data.Add(i);
        return data;
    }

    [Theory]
    [MemberData(nameof(Iterations))]
    public async Task The_handshake_with_rumdl_completes(int iteration)
    {
        var capture = new ConversationLog();
        var serverInfo = new LspServerInfo(
            ForeignServer.Markdown.Find()!, ForeignServer.Markdown.ServerArguments, Path.GetTempPath());
        var loggerFactory = LoggerFactory.Create(_ => { });
        var registration = new LanguageServerRegistration(
            Id: "foreign.markdown",
            DisplayName: "Foreign Markdown server",
            Extensions: ForeignServer.Markdown.Extensions,
            LanguageId: ForeignServer.Markdown.LanguageId,
            CreateClient: () => new VBLspClient(
                new StdioProcessLspTransport(serverInfo, loggerFactory.CreateLogger<StdioProcessLspTransport>()),
                loggerFactory.CreateLogger<VBLspClient>(),
                ForeignServer.Markdown.LanguageId,
                capture: capture, connectionId: "foreign.markdown"));
        var registry = new LspClientRegistry([registration], loggerFactory.CreateLogger<LspClientRegistry>());
        try
        {
            await registry.OpenDocumentAsync(MarkdownUri, "# Heading\n", TestContext.Current.CancellationToken);

            var connection = registry.Connections.Single();
            connection.State.Should().Be(LanguageConnectionState.Running,
                "iteration {0}: {1}", iteration, ConnectionDiagnostics.Explain(connection, capture));
        }
        finally
        {
            try { await registry.DisposeAsync(); } catch { /* best effort, as the class it mirrors */ }
        }
    }
}
