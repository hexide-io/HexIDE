using System.IO.Pipes;
using System.Text.Json;
using HexIDE.Addins;
using HexIDE.Conversations;
using HexIDE.Lsp;
using Microsoft.Extensions.Logging;
using StreamJsonRpc;

// NB: namespace deliberately avoids a `Lsp` segment — see VBLspClientTests.
namespace HexIDE.Tests.LspClient;

/// <summary>
/// AI Chat's tools and context from language servers (#4257): which servers count, the limits, and that whether a call asks
/// first is decided by the host from the server's own listing, whatever the method the server named for it.
/// </summary>
public sealed class ServerChatToolsTests : IAsyncDisposable
{
    private readonly ILoggerFactory _loggerFactory = LoggerFactory.Create(_ => { });
    private readonly List<IAsyncDisposable> _cleanup = [];

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask DisposeAsync()
    {
        for (var i = _cleanup.Count - 1; i >= 0; i--) await _cleanup[i].DisposeAsync();
        _loggerFactory.Dispose();
    }

    // ── the capability, the policy and the approval rule ─────────────────────

    [Theory]
    [InlineData("""{"experimental":{"hexide.chatTools":{"list":"a/list","call":"a/call","context":"a/context"}}}""", "a/list", "a/call", "a/context")]
    [InlineData("""{"experimental":{"hexide.chatTools":{"list":"a/list","call":"textDocument/rename"}}}""", "a/list", "textDocument/rename", null)]
    public void TheCapability_IsReadFromExperimental(string capabilities, string list, string call, string? context)
    {
        var read = ServerChatToolsCapability.Read(JsonDocument.Parse(capabilities).RootElement);

        read.Should().Be(new ServerChatToolsCapability(list, call, context));
    }

    [Theory]
    [InlineData("""{}""")]
    [InlineData("""{"experimental":{}}""")]
    [InlineData("""{"experimental":{"hexide.chatTools":true}}""")]
    [InlineData("""{"experimental":{"hexide.chatTools":{"list":"a/list"}}}""")]
    [InlineData("""{"experimental":{"hexide.chatTools":{"call":"a/call"}}}""")]
    [InlineData("""{"experimental":{"hexide.chatTools":{"list":"","call":"a/call"}}}""")]
    [InlineData("""{"experimental":{"hexide.chatTools":{"list":" a/list","call":"a/call"}}}""")]
    [InlineData("""{"experimental":{"hexide.chatTools":{"list":"a/li\u0007st","call":"a/call"}}}""")]
    [InlineData("""{"experimental":{"hexide.chatTools":{"list":1,"call":"a/call"}}}""")]
    public void ACapabilityWithoutAListAndACallHexIdeCanSend_IsNone(string capabilities)
    {
        ServerChatToolsCapability.Read(JsonDocument.Parse(capabilities).RootElement).Should().BeNull();
    }

    [Theory]
    // policy, readOnlyHint, destructiveHint, runsCode, asks
    [InlineData(ServerChatToolsPolicy.Hints, true, false, false, false)]
    [InlineData(ServerChatToolsPolicy.Hints, true, true, false, true)]
    [InlineData(ServerChatToolsPolicy.Hints, true, null, false, true)]
    [InlineData(ServerChatToolsPolicy.Hints, false, false, false, true)]
    [InlineData(ServerChatToolsPolicy.Hints, null, false, false, true)]
    [InlineData(ServerChatToolsPolicy.Hints, null, null, false, true)]
    [InlineData(ServerChatToolsPolicy.Hints, true, false, true, true)]
    [InlineData(ServerChatToolsPolicy.AskEveryCall, true, false, false, true)]
    public void ATool_AsksUnlessItSaysItOnlyReads_AndDoesntRunCode_UnderHints(
        ServerChatToolsPolicy policy, bool? readOnly, bool? destructive, bool runsCode, bool asks)
    {
        ServerChatToolsApproval.Asks(policy, readOnly, destructive, runsCode).Should().Be(asks);
    }

    [Theory]
    [InlineData(null, ServerChatToolsPolicy.Hints)]
    [InlineData("", ServerChatToolsPolicy.Hints)]
    [InlineData("hints", ServerChatToolsPolicy.Hints)]
    [InlineData(" ASK ", ServerChatToolsPolicy.AskEveryCall)]
    [InlineData("off", ServerChatToolsPolicy.Off)]
    public void TheEntrysSetting_NamesAPolicy(string? value, ServerChatToolsPolicy policy)
    {
        ServerChatToolsSetting.Parse(value).Should().Be(policy);
    }

    [Fact]
    public void ARegistration_OffersNoTools_UnlessItsRouteSaysItMay()
    {
        new LanguageServerRegistration("x", "X", [".x"], "x", () => Substitute.For<ILspClient>())
            .ChatTools.Should().BeNull("only the factory's routes, the file's and a consented add-in's, let a server offer tools");
    }

    [Theory]
    [InlineData("on", ServerChatToolsPolicy.Hints)]
    [InlineData(" ASK ", ServerChatToolsPolicy.AskEveryCall)]
    [InlineData("off", ServerChatToolsPolicy.Off)]
    [InlineData("hints", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void ThePersonsSetting_NamesAPolicy_OrNone(string? value, ServerChatToolsPolicy? policy)
    {
        ServerChatToolsSetting.ParseUser(value).Should().Be(policy);
        if (policy is { } named) ServerChatToolsSetting.ParseUser(ServerChatToolsSetting.UserValue(named)).Should().Be(named);
    }

    [Theory]
    // entry, person, effective
    [InlineData(ServerChatToolsPolicy.Hints, null, ServerChatToolsPolicy.Hints)]
    [InlineData(ServerChatToolsPolicy.Hints, ServerChatToolsPolicy.AskEveryCall, ServerChatToolsPolicy.AskEveryCall)]
    [InlineData(ServerChatToolsPolicy.Off, ServerChatToolsPolicy.Hints, ServerChatToolsPolicy.Hints)]
    [InlineData(null, ServerChatToolsPolicy.Hints, ServerChatToolsPolicy.Off)]
    [InlineData(null, null, ServerChatToolsPolicy.Off)]
    public void ThePersonsChoice_WinsOverTheEntry_ButNeverOverARouteThatCantOfferTools(
        ServerChatToolsPolicy? entry, ServerChatToolsPolicy? person, ServerChatToolsPolicy effective)
    {
        ServerChatToolsSetting.Effective(entry, person).Should().Be(effective);
    }

    [Theory]
    [InlineData(null, ServerChatToolsPolicy.Hints)]
    [InlineData("ask", ServerChatToolsPolicy.AskEveryCall)]
    [InlineData("off", ServerChatToolsPolicy.Off)]
    [InlineData("always", ServerChatToolsPolicy.AskEveryCall)]
    public void TheFactory_TakesTheEntrysSetting_AndAMisspeltOneAsksEveryCall(string? setting, ServerChatToolsPolicy policy)
    {
        var made = new LanguageServerRegistrationFactory(_loggerFactory).Create([Entry("probe", "unused", ChatTools: setting)]);

        made.Should().ContainSingle().Which.ChatTools.Should().Be(policy);
    }

    [Fact]
    public void AMisspeltSetting_IsReported_AndTheServerStillLoads()
    {
        var directory = Path.Combine(Path.GetTempPath(), "hexide-chattools-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "lsp-servers.json");
        File.WriteAllText(path, """
            {"version":1,"servers":[{"id":"probe","extensions":[".md"],"languageId":"markdown","transport":"stdio","command":"probe","chatTools":"always"}]}
            """);
        LanguageServerConfigResult result;
        try { result = new LanguageServerConfigLoader(path, _loggerFactory.CreateLogger<LanguageServerConfigLoader>()).Load([]); }
        finally { Directory.Delete(directory, true); }

        result.Entries.Should().ContainSingle(e => e.Id == "probe", string.Join(" | ", result.Problems.Select(p => p.Message)));
        result.Problems.Should().ContainSingle(p => p.Code == "ChatTools").Which.Arguments.Should()
            .Equal("probe", "always", ServerChatToolsSetting.Hints, ServerChatToolsSetting.Ask, ServerChatToolsSetting.Off);
        result.Problems.Should().NotContain(p => p.Code == "UnknownField" || p.Code == "UnknownFields");
    }

    // ── on the wire ─────────────────────────────────────────────────────────

    [Fact]
    public async Task TheList_TakesOnlyToolsHexIdeCanName_AndAsksAsTheirHintsSay()
    {
        var (tools, server) = await StartAsync();

        var listed = await tools.ListAsync(Token);

        listed.Select(t => (t.Name, t.Asks)).Should().Equal(("read", false), ("write", true), ("runner", true), ("rdcore.ns/tool", false));
        listed.Should().OnlyContain(t => t.ServerId == "probe" && t.ServerName == "Probe");
        server.Calls.Should().BeEmpty("listing calls nothing");
        server.Methods.Should().Equal(ChatToolServer.ListMethod);
    }

    [Fact]
    public async Task ACallThatAsks_SendsNothing_UntilThePersonSaysYes_EvenWithTextDocumentRenameAsItsMethod()
    {
        var (tools, server) = await StartAsync();
        var write = (await tools.ListAsync(Token)).Single(t => t.Name == "write");
        var asked = new List<ServerChatTool>();

        var declined = await tools.CallAsync(write, Arguments("""{"x":1}"""), (tool, _) =>
        {
            asked.Add(tool);
            server.Calls.Should().BeEmpty("nothing is sent before the answer");
            return Task.FromResult(false);
        }, Token);

        declined.Declined.Should().BeTrue();
        declined.IsError.Should().BeTrue();
        server.Calls.Should().BeEmpty("a no sends nothing");

        var done = await tools.CallAsync(write, Arguments("""{"x":1}"""), (tool, _) => { asked.Add(tool); return Task.FromResult(true); }, Token);

        asked.Should().HaveCount(2).And.OnlyContain(t => t.Name == "write" && t.Asks);
        done.Should().Be(new ServerChatToolResult(false, "write ran with {\"x\":1}"));
        server.Calls.Should().ContainSingle().Which.Should().Be((ChatToolServer.CallMethod, "write", """{"x":1}"""),
            "the call goes as the server's own method, with MCP's {name, arguments}, exactly once");
    }

    [Fact]
    public async Task WhetherACallAsks_IsTheHostsReading_NotTheCallers()
    {
        var (tools, server) = await StartAsync();
        var write = (await tools.ListAsync(Token)).Single(t => t.Name == "write");
        var forged = write with { Asks = false };
        var asked = 0;

        await tools.CallAsync(forged, null, (_, _) => { asked++; return Task.FromResult(false); }, Token);

        asked.Should().Be(1, "the host lists the server again and reads its hints itself");
        server.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task AToolThatOnlyReads_RunsWithoutAsking()
    {
        var (tools, server) = await StartAsync();
        var read = (await tools.ListAsync(Token)).Single(t => t.Name == "read");

        var result = await tools.CallAsync(read, null, (_, _) => throw new InvalidOperationException("asked"), Token);

        result.Text.Should().Be("read ran with {}");
        server.Calls.Should().ContainSingle().Which.Should().Be((ChatToolServer.CallMethod, "read", "{}"));
    }

    [Fact]
    public async Task AToolThatSaysItRunsCode_AlwaysAsks_WhateverElseItSays()
    {
        var (tools, server) = await StartAsync();
        var runner = (await tools.ListAsync(Token)).Single(t => t.Name == "runner");
        var asked = 0;

        await tools.CallAsync(runner, null, (_, _) => { asked++; return Task.FromResult(false); }, Token);

        asked.Should().Be(1);
        server.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task AServerSetToAsk_AsksForEveryTool_AndGivesNoContext()
    {
        var (tools, server) = await StartAsync(chatTools: "ask", context: "Context that would go without asking.");
        var listed = await tools.ListAsync(Token);
        var asked = 0;

        listed.Should().OnlyContain(t => t.Asks);
        await tools.CallAsync(listed.Single(t => t.Name == "read"), null, (_, _) => { asked++; return Task.FromResult(false); }, Token);
        var context = await tools.GetContextAsync(Token);

        asked.Should().Be(1);
        server.Calls.Should().BeEmpty();
        context.Should().BeEmpty("context goes on every send with nothing to ask, so a server set to ask gives none");
        server.Methods.Should().NotContain(ChatToolServer.ContextMethod);
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    [InlineData("\"yes\"", true)]
    [InlineData("\"false\"", true)]
    [InlineData("1", true)]
    [InlineData("null", true)]
    public async Task ARunsCodeHintThatIsntFalse_Asks(string runsCode, bool asks)
    {
        var (tools, _) = await StartAsync(toolsJson: $$$"""
            {"tools":[{"name":"t","annotations":{"readOnlyHint":true,"destructiveHint":false,"hexide.runsCode":{{{runsCode}}}}}]}
            """);

        (await tools.ListAsync(Token)).Should().ContainSingle().Which.Asks.Should().Be(asks);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("bad name")]
    public async Task ANameTheServerDoesntList_IsRefused_AndNothingIsCalled(string name)
    {
        var (tools, server) = await StartAsync();
        var read = (await tools.ListAsync(Token)).Single(t => t.Name == "read");

        var result = await tools.CallAsync(read with { Name = name }, null, (_, _) => Task.FromResult(true), Token);

        result.IsError.Should().BeTrue();
        result.Text.Should().Contain(name);
        server.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task AServerSetOff_OrWithoutTheCapability_OrNotMadeByTheFactory_IsNeverAsked()
    {
        var off = await ServeAsync(new ChatToolServer(declares: true));
        var silent = await ServeAsync(new ChatToolServer(declares: false));
        var unrouted = await ServeAsync(new ChatToolServer(declares: true));
        var factory = new LanguageServerRegistrationFactory(_loggerFactory);
        var made = factory.Create(
        [
            Entry("off", off.PipeName, ".md", ChatTools: "off"),
            Entry("silent", silent.PipeName, ".txt"),
            Entry("unrouted", unrouted.PipeName, ".ini"),
        ]);
        // As a route that isn't the factory's would make it: the registration's own default.
        var registrations = made.Select(r => r.Id == "unrouted" ? r with { ChatTools = default } : r).ToArray();
        var (tools, router) = Tools(registrations);
        await OpenAsync(router, "README.md", "notes.txt", "settings.ini");
        await Task.WhenAll(off.Server.Initialized, silent.Server.Initialized, unrouted.Server.Initialized).WaitAsync(TimeSpan.FromSeconds(30), Token);

        (await tools.ListAsync(Token)).Should().BeEmpty();
        (await tools.GetContextAsync(Token)).Should().BeEmpty();

        off.Server.Methods.Should().BeEmpty();
        silent.Server.Methods.Should().BeEmpty();
        unrouted.Server.Methods.Should().BeEmpty();
    }

    [Fact]
    public async Task ThePersonsChoice_AppliesToTheNextRequest_AndWinsOverTheEntry()
    {
        ServerChatToolsPolicy? choice = null;
        var (tools, server) = await StartAsync(context: "Context.", user: _ => choice);
        var read = (await tools.ListAsync(Token)).Single(t => t.Name == "read");
        read.Asks.Should().BeFalse("the entry trusts the tools' hints");

        choice = ServerChatToolsPolicy.AskEveryCall;
        var asked = 0;
        await tools.CallAsync(read, null, (_, _) => { asked++; return Task.FromResult(false); }, Token);
        (await tools.GetContextAsync(Token)).Should().BeEmpty();

        choice = ServerChatToolsPolicy.Off;
        server.Forget();
        (await tools.ListAsync(Token)).Should().BeEmpty();
        var off = await tools.CallAsync(read, null, (_, _) => Task.FromResult(true), Token);

        asked.Should().Be(1, "asking for every call wins over an entry that trusts hints");
        server.Calls.Should().BeEmpty();
        off.IsError.Should().BeTrue();
        server.Methods.Should().BeEmpty("a server the person turned off is asked for nothing");
    }

    [Fact]
    public async Task ThePersonsOn_TurnsOnAServerWhoseEntryIsOff()
    {
        var (tools, _) = await StartAsync(chatTools: "off", user: _ => ServerChatToolsPolicy.Hints);

        (await tools.ListAsync(Token)).Select(t => t.Name).Should().Contain("read");
    }

    [Fact]
    public async Task ThePersonsOn_CannotTurnOnARouteThatCantOfferTools()
    {
        var unrouted = await ServeAsync(new ChatToolServer(declares: true));
        var made = new LanguageServerRegistrationFactory(_loggerFactory).Create([Entry("unrouted", unrouted.PipeName)]);
        var (tools, router) = Tools(made.Select(r => r with { ChatTools = null }).ToArray(), _ => ServerChatToolsPolicy.Hints);
        await OpenAsync(router, "README.md");
        await unrouted.Server.Initialized.WaitAsync(TimeSpan.FromSeconds(30), Token);

        (await tools.ListAsync(Token)).Should().BeEmpty();
        unrouted.Server.Methods.Should().BeEmpty();
    }

    [Fact]
    public async Task WithTwoServers_EachRequestGoesOnlyToTheServerThatOfferedTheTool()
    {
        var first = await ServeAsync(new ChatToolServer(declares: true));
        var second = await ServeAsync(new ChatToolServer(declares: true, tag: "second"));
        var (tools, router) = Tools(new LanguageServerRegistrationFactory(_loggerFactory).Create(
            [Entry("first", first.PipeName, ".md"), Entry("second", second.PipeName, ".txt", displayName: "Second")]));
        await OpenAsync(router, "README.md", "notes.txt");
        await WaitForAsync(tools, 2);

        var read = (await tools.ListAsync(Token)).Single(t => t is { Name: "read", ServerId: "second" });
        var result = await tools.CallAsync(read, null, (_, _) => Task.FromResult(true), Token);

        result.Text.Should().Be("second: read ran with {}");
        second.Server.Calls.Should().ContainSingle();
        first.Server.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task TheContext_IsTheServers_CutToTheLimit_WithoutFormatCharacters()
    {
        var context = "keep\u202Ethis" + new string('c', ServerChatToolLimits.MaxContext);
        var (tools, _) = await StartAsync(context: context);

        var given = (await tools.GetContextAsync(Token)).Should().ContainSingle().Subject;

        given.ServerName.Should().Be("Probe");
        given.Truncated.Should().BeTrue();
        given.Text.Should().StartWith("keepthis").And.HaveLength(ServerChatToolLimits.MaxContext);
    }

    [Fact]
    public async Task AResult_IsCutToTheLimit_AndSaysSo()
    {
        var (tools, _) = await StartAsync();
        var read = (await tools.ListAsync(Token)).Single(t => t.Name == "read");
        var large = JsonSerializer.Serialize(new { pad = new string('r', ServerChatToolLimits.MaxResult) });

        var result = await tools.CallAsync(read, Arguments(large), (_, _) => Task.FromResult(true), Token);

        result.Truncated.Should().BeTrue();
        result.Text.Should().HaveLength(ServerChatToolLimits.MaxResult);
    }

    [Fact]
    public async Task MoreToolsThanTheLimit_AreLeftOut()
    {
        var many = string.Join(",", Enumerable.Range(0, ServerChatToolLimits.MaxToolsPerServer + 5)
            .Select(i => $$"""{"name":"t{{i}}"}"""));
        var (tools, _) = await StartAsync(toolsJson: "{\"tools\":[" + many + "]}");

        (await tools.ListAsync(Token)).Should().HaveCount(ServerChatToolLimits.MaxToolsPerServer);
    }

    // ── harness ────────────────────────────────────────────────────────────

    private static JsonElement Arguments(string json) => JsonDocument.Parse(json).RootElement.Clone();

    internal static LanguageServerEntry Entry(string id, string pipeName, string extension = ".md", string? ChatTools = null,
        string displayName = "Probe") => new()
    {
        Id = id,
        DisplayName = displayName,
        Extensions = [extension],
        LanguageId = extension.TrimStart('.'),
        Transport = "pipe",
        PipeName = pipeName,
        ChatTools = ChatTools,
    };

    private async Task<(IServerChatTools Tools, ChatToolServer Server)> StartAsync(
        string? chatTools = null, string? context = null, string? toolsJson = null, Func<string, ServerChatToolsPolicy?>? user = null)
    {
        var served = await ServeAsync(new ChatToolServer(declares: true, context: context, toolsJson: toolsJson));
        var (tools, router) = Tools(new LanguageServerRegistrationFactory(_loggerFactory).Create([Entry("probe", served.PipeName, ChatTools: chatTools)]), user);
        await OpenAsync(router, "README.md");
        await WaitForAsync(tools, 1);
        served.Server.Forget();
        return (tools, served.Server);
    }

    private (IServerChatTools Tools, LspClientRegistry Router) Tools(IReadOnlyList<LanguageServerRegistration> registrations,
        Func<string, ServerChatToolsPolicy?>? user = null)
    {
        var router = new LspClientRegistry(registrations, _loggerFactory.CreateLogger<LspClientRegistry>());
        _cleanup.Add(router);
        var contributions = new LanguageServerContributions(Substitute.For<HexIDE.IDE.ISettingsService>(), router,
            new LanguageServerRegistrationFactory(_loggerFactory),
            new LanguageServerConfigLoader(_loggerFactory.CreateLogger<LanguageServerConfigLoader>()),
            new HashSet<string>(StringComparer.Ordinal), _loggerFactory.CreateLogger<LanguageServerContributions>(), null)
        {
            ChatToolsSetting = user,
        };
        return (contributions.ChatTools, router);
    }

    [Fact]
    public async Task An_addin_writing_its_servers_settings_cant_change_how_its_tools_are_approved()
    {
        // #4307 lets an add-in write its own server's settings into the person's settings file, beside Options' own map
        // of AI Chat approvals. The write goes to that server's slot alone, and the policy is read from the map alone, so
        // an add-in that puts policy-shaped keys in its settings changes nothing about how its tools are approved.
        var directory = Path.Combine(Path.GetTempPath(), "hexide-chat-policy-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "settings.json");
            var settings = new HexIDE.IDE.SettingsService(path);
            var served = await ServeAsync(new ChatToolServer(declares: true));
            var router = new LspClientRegistry([], _loggerFactory.CreateLogger<LspClientRegistry>());
            _cleanup.Add(router);
            var contributions = new LanguageServerContributions(settings, router, new LanguageServerRegistrationFactory(_loggerFactory),
                new LanguageServerConfigLoader(_loggerFactory.CreateLogger<LanguageServerConfigLoader>()),
                new HashSet<string>(StringComparer.Ordinal), _loggerFactory.CreateLogger<LanguageServerContributions>(), null)
            {
                ChatToolsSetting = ServerChatToolsSetting.FromUserSettings(settings),
            };
            var connection = contributions.Register(
                new AddinLanguageServer(Entry("probe", served.PipeName, ChatTools: ServerChatToolsSetting.Ask)), "Owner");
            await OpenAsync(router, "README.md");
            await WaitForAsync(contributions.ChatTools, 1);
            (await contributions.ChatTools.ListAsync(Token)).Should().OnlyContain(t => t.Asks, "the entry asks before every call");

            using (var written = JsonDocument.Parse("""
                {"rdcore":{"enabled":true},"chatTools":"on","ServerChatTools":{"probe":"on"},
                 "serverChatTools":{"probe":"on"},"hexide.chatTools":"on"}
                """))
                await connection.SetSettingsAsync(written.RootElement, Token);

            settings.GetLanguageServerSettings("probe")!.Value.GetProperty("chatTools").GetString().Should().Be("on");
            (await contributions.ChatTools.ListAsync(Token)).Should().OnlyContain(t => t.Asks,
                "under the person's 'on' the read-only tools wouldn't ask, so none of these keys reached the policy");
            settings.ServerChatTools.Should().BeEmpty();
            new HexIDE.IDE.SettingsService(path).ServerChatTools.Should().BeEmpty();
            served.Server.Forget();
        }
        finally { Directory.Delete(directory, true); }
    }

    private static async Task OpenAsync(LspClientRegistry router, params string[] names)
    {
        foreach (var name in names)
            await router.OpenDocumentAsync("file:///c:/p/" + name, "x", Token);
    }

    // Until the running servers that may offer tools have started: listing them is how the host finds them.
    private static async Task WaitForAsync(IServerChatTools tools, int servers)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        while ((await tools.ListAsync(timeout.Token)).Select(t => t.ServerId).Distinct().Count() < servers)
            await Task.Delay(20, timeout.Token);
    }

    private async Task<ChatToolPipe> ServeAsync(ChatToolServer server)
    {
        var pipe = new ChatToolPipe(AddinServerRegistrationTests.PipeName(), server);
        _cleanup.Add(pipe);
        await Task.Yield();
        return pipe;
    }
}

/// <summary>
/// A scripted language server for #4257's tests: it declares <c>experimental.hexide.chatTools</c> with
/// <c>textDocument/rename</c> as its call method, lists four tools a host can take and one it can't, and records every
/// chat request it receives.
/// </summary>
internal sealed class ChatToolServer(bool declares, string? tag = null, string? context = null, string? toolsJson = null)
{
    public const string ListMethod = "probe/tools/list";
    public const string CallMethod = "textDocument/rename";
    public const string ContextMethod = "probe/context";

    private const string DefaultTools = """
        {"tools":[
          {"name":"read","description":"Reads.","inputSchema":{"type":"object","properties":{}},"annotations":{"readOnlyHint":true,"destructiveHint":false}},
          {"name":"write","description":"Writes.","inputSchema":{"type":"object","properties":{"x":{"type":"number"}}}},
          {"name":"runner","description":"Runs code.","annotations":{"readOnlyHint":true,"destructiveHint":false,"hexide.runsCode":true}},
          {"name":"rdcore.ns/tool","annotations":{"readOnlyHint":true,"destructiveHint":false}},
          {"name":"bad name","annotations":{"readOnlyHint":true,"destructiveHint":false}},
          {"name":"\u202Ehidden","annotations":{"readOnlyHint":true,"destructiveHint":false}},
          {"name":"schema","inputSchema":"not an object"}
        ]}
        """;

    private readonly TaskCompletionSource _initialized = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly List<string> _methods = [];
    private readonly List<(string Method, string Name, string Arguments)> _calls = [];

    public Task Initialized => _initialized.Task;

    /// <summary>Every chat request received, by method, in order.</summary>
    public IReadOnlyList<string> Methods { get { lock (_methods) return [.. _methods]; } }

    /// <summary>Every call received: the method it came as, and the name and arguments it carried.</summary>
    public IReadOnlyList<(string Method, string Name, string Arguments)> Calls { get { lock (_methods) return [.. _calls]; } }

    /// <summary>Forgets what was received so far, so a test sees only what it caused.</summary>
    public void Forget() { lock (_methods) { _methods.Clear(); _calls.Clear(); } }

    [JsonRpcMethod("initialize", UseSingleObjectParameterDeserialization = true)]
    public JsonElement Initialize(JsonElement _) => JsonSerializer.SerializeToElement(new
    {
        capabilities = declares
            ? (object)new
            {
                textDocumentSync = new { openClose = true, change = 1 },
                experimental = new Dictionary<string, object>
                {
                    ["hexide.chatTools"] = new { list = ListMethod, call = CallMethod, context = ContextMethod },
                },
            }
            : new { textDocumentSync = new { openClose = true, change = 1 } },
    });

    [JsonRpcMethod("initialized", UseSingleObjectParameterDeserialization = true)]
    public void OnInitialized(JsonElement _) => _initialized.TrySetResult();

    [JsonRpcMethod(ListMethod, UseSingleObjectParameterDeserialization = true)]
    public JsonElement List(JsonElement? _)
    {
        lock (_methods) _methods.Add(ListMethod);
        return JsonDocument.Parse(toolsJson ?? DefaultTools).RootElement.Clone();
    }

    [JsonRpcMethod(CallMethod, UseSingleObjectParameterDeserialization = true)]
    public JsonElement Call(JsonElement p)
    {
        var name = p.GetProperty("name").GetString()!;
        var arguments = p.GetProperty("arguments").GetRawText();
        lock (_methods)
        {
            _methods.Add(CallMethod);
            _calls.Add((CallMethod, name, arguments));
        }
        var text = (tag is null ? "" : tag + ": ") + $"{name} ran with {arguments}";
        return JsonSerializer.SerializeToElement(new { content = new[] { new { type = "text", text } }, isError = false });
    }

    [JsonRpcMethod(ContextMethod, UseSingleObjectParameterDeserialization = true)]
    public JsonElement Context(JsonElement? _)
    {
        lock (_methods) _methods.Add(ContextMethod);
        return JsonSerializer.SerializeToElement(context ?? "");
    }
}

/// <summary>Serves one <see cref="ChatToolServer"/> on a named pipe, to the first connection HexIDE makes.</summary>
internal sealed class ChatToolPipe : IAsyncDisposable
{
    private readonly NamedPipeServerStream _pipe;
    private readonly Task<JsonRpc?> _serving;
    private readonly CancellationTokenSource _stop = new();

    public ChatToolPipe(string pipeName, ChatToolServer server)
    {
        PipeName = pipeName;
        Server = server;
        _pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        _serving = Task.Run(async () =>
        {
            try { await _pipe.WaitForConnectionAsync(_stop.Token); }
            catch (OperationCanceledException) { return null; }
            var rpc = new JsonRpc(new HeaderDelimitedMessageHandler(_pipe, _pipe, new SystemTextJsonFormatter()), server);
            rpc.StartListening();
            return rpc;
        });
    }

    public string PipeName { get; }
    public ChatToolServer Server { get; }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        (await _serving)?.Dispose();
        await _pipe.DisposeAsync();
        _stop.Dispose();
    }
}
