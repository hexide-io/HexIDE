using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using HexIDE.Addins;
using HexIDE.AIChat;
using HexIDE.AIChat.Views;
using HexIDE.Lsp;
using HexIDE.Tests.LspClient;
using Microsoft.Extensions.Logging;

namespace HexIDE.Tests.AIChat;

/// <summary>
/// A language server's tool through AI Chat's own loop (#4257): offered to the assistant under the server's name, asked
/// about before anything reaches the server, and sent only on a yes, as the server's own method.
/// </summary>
public sealed class ServerChatToolsLoopTests : IAsyncDisposable
{
    private readonly ILoggerFactory _loggerFactory = LoggerFactory.Create(_ => { });
    private readonly string _directory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
    private readonly List<IAsyncDisposable> _cleanup = [];

    public ServerChatToolsLoopTests()
    {
        Directory.CreateDirectory(_directory);
        // Loopback, so no key and no first-send notice stand in the way.
        File.WriteAllText(Path.Combine(_directory, "chat-settings.json"), """{"baseUrl":"http://localhost/v1"}""");
    }

    public async ValueTask DisposeAsync()
    {
        for (var i = _cleanup.Count - 1; i >= 0; i--) await _cleanup[i].DisposeAsync();
        _loggerFactory.Dispose();
        Directory.Delete(_directory, true);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AToolThatAsks_ReachesTheServer_OnlyAfterAYes(bool approve)
    {
        new HexIDE.Localization.LocalizationService().Apply("en");
        var (tools, server) = await StartAsync();
        var handler = new ToolCallHandler("lsp2_write", """{"x":1}""");
        using var client = new ChatClient(new HttpClient(handler));
        var host = Substitute.For<IHexIdeHost>();
        host.ServerChatTools.Returns(tools);
        var chat = new ChatViewModel(host, _directory, client) { InputText = "go" };

        var send = chat.SendAsync();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        ChatMessageViewModel? question;
        while ((question = chat.Messages.FirstOrDefault(m => m.IsApproval)) is null)
            await Task.Delay(10, timeout.Token);

        server.Calls.Should().BeEmpty("nothing reaches the server while the question is open");
        question.RawContent.Should().Contain("“write”").And.Contain("Probe").And.Contain("""{"x":1}""");
        if (approve) question.Approve(); else question.Reject();
        await send.WaitAsync(timeout.Token);

        // What the assistant was offered: the server's tools after AI Chat's own, named for the server.
        var offered = handler.Bodies[0]["tools"]!.AsArray().Select(t => t!["function"]!).ToList();
        offered.Select(f => f["name"]!.GetValue<string>()).Should().Contain(["lsp1_read", "lsp2_write", "lsp3_runner"]);
        offered.Single(f => f["name"]!.GetValue<string>() == "lsp2_write")["description"]!.GetValue<string>()
            .Should().StartWith("From the language server 'Probe', not from HexIDE.");
        // The system message, read as text: serialised JSON escapes the quotes around the server's name.
        handler.Bodies[0]["messages"]![0]!["content"]!.GetValue<string>().Should()
            .Contain("Context from the language server 'Probe'").And.Contain("Server context line.");

        if (approve)
        {
            server.Calls.Should().ContainSingle().Which.Should().Be((ChatToolServer.CallMethod, "write", """{"x":1}"""));
            var result = chat.SessionLog.Single(m => m.Role == "tool").Content;
            JsonNode.Parse(result)!["server"]!.GetValue<string>().Should().Be("Probe");
            JsonNode.Parse(result)!["result"]!.GetValue<string>().Should().Be("""write ran with {"x":1}""");
        }
        else
        {
            server.Calls.Should().BeEmpty("a no sends nothing");
            chat.SessionLog.Single(m => m.Role == "tool").Content.Should().Contain("The user declined this action.");
        }
    }

    [AvaloniaFact]
    public async Task AToolThatOnlyReads_RunsWithoutAQuestion()
    {
        new HexIDE.Localization.LocalizationService().Apply("en");
        var (tools, server) = await StartAsync();
        var handler = new ToolCallHandler("lsp1_read", "{}");
        using var client = new ChatClient(new HttpClient(handler));
        var host = Substitute.For<IHexIdeHost>();
        host.ServerChatTools.Returns(tools);
        var chat = new ChatViewModel(host, _directory, client) { InputText = "go" };

        await chat.SendAsync().WaitAsync(TimeSpan.FromSeconds(30), Token);

        chat.Messages.Should().NotContain(m => m.IsApproval);
        server.Calls.Should().ContainSingle().Which.Name.Should().Be("read");
    }

    [Theory]
    [InlineData("""{"a":"b"}""", """{"a":"b"}""")]
    [InlineData("{\"a\":\"\u202Eb\u0007\"}", "{\"a\":\"\\u202Eb\\u0007\"}")]
    public void TheQuestion_ShowsArgumentsWithoutCharactersThatHideOrReorderIt(string arguments, string shown)
    {
        ChatViewModel.ArgumentsSummary(arguments).Should().Be(shown);
    }

    [Fact]
    public void LongArguments_AreCutVisibly()
    {
        var shown = ChatViewModel.ArgumentsSummary(new string('a', ChatViewModel.MaxArgumentsShown + 10));

        shown.Should().HaveLength(ChatViewModel.MaxArgumentsShown + 1).And.EndWith("…");
    }

    [Theory]
    [InlineData(1, "read", "lsp1_read")]
    [InlineData(12, "rdcore.ns/tool", "lsp12_rdcore_ns_tool")]
    public void AServersTool_IsOfferedUnderANameNoBuiltInToolCanHave(int n, string name, string offered)
    {
        ChatViewModel.OfferedName(n, name).Should().Be(offered);
    }

    private async Task<(IServerChatTools Tools, ChatToolServer Server)> StartAsync()
    {
        var pipe = new ChatToolPipe(AddinServerRegistrationTests.PipeName(), new ChatToolServer(declares: true, context: "Server context line."));
        _cleanup.Add(pipe);
        var router = new LspClientRegistry(new LanguageServerRegistrationFactory(_loggerFactory).Create(
            [ServerChatToolsTests.Entry("probe", pipe.PipeName)]), _loggerFactory.CreateLogger<LspClientRegistry>());
        _cleanup.Add(router);
        var tools = new LanguageServerContributions(Substitute.For<HexIDE.IDE.ISettingsService>(), router,
            new LanguageServerRegistrationFactory(_loggerFactory),
            new LanguageServerConfigLoader(_loggerFactory.CreateLogger<LanguageServerConfigLoader>()),
            new HashSet<string>(StringComparer.Ordinal), _loggerFactory.CreateLogger<LanguageServerContributions>(), null).ChatTools;
        await router.OpenDocumentAsync("file:///c:/p/README.md", "x", Token);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        while ((await tools.ListAsync(timeout.Token)).Count == 0)
            await Task.Delay(20, timeout.Token);
        pipe.Server.Forget();
        return (tools, pipe.Server);
    }

    // An OpenAI-shaped service: the first reply calls one tool, the next ends the turn. Every request body is kept.
    internal sealed class ToolCallHandler(string name, string arguments) : HttpMessageHandler
    {
        public List<JsonObject> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Bodies.Add(JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!.AsObject());
            var data = Bodies.Count == 1
                ? new JsonObject
                {
                    ["choices"] = new JsonArray(new JsonObject
                    {
                        ["delta"] = new JsonObject
                        {
                            ["tool_calls"] = new JsonArray(new JsonObject
                            {
                                ["index"] = 0,
                                ["id"] = "call",
                                ["function"] = new JsonObject { ["name"] = name, ["arguments"] = arguments },
                            }),
                        },
                        ["finish_reason"] = null,
                    }),
                }.ToJsonString()
                : """{"choices":[{"delta":{"content":"done"},"finish_reason":null}]}""";
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("data: " + data + "\n\ndata: [DONE]\n\n", Encoding.UTF8, "text/event-stream"),
            };
        }
    }
}
