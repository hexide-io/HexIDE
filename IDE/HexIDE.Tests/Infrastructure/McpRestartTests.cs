using System.ComponentModel;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HexIDE.Desktop.Server;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace HexIDE.Tests.Infrastructure;

/// <summary>
/// The automation server is restarted by every rebuild in the documented dev loop, and a client attached before
/// the restart has to keep working after it.
/// </summary>
/// <remarks>
/// <para>
/// It did not, through two mechanisms, both of which come from the SDK's 1.x default transport being stateful:
/// </para>
/// <list type="bullet">
/// <item><b>The session.</b> <c>initialize</c> issues an <c>Mcp-Session-Id</c> that every later request carries,
/// and a relaunched process has never heard of it. It answers 404 "Session not found", as the specification
/// requires. The SDK's own client does not start a new session on that: the 404 cancels its connection, and
/// every call after it fails at once. Claude Code does start a new session, so for Claude Code this path
/// recovers.</item>
/// <item><b>The GET stream.</b> A stateful server also offers a long-lived GET for messages the server starts.
/// Claude Code holds one open, and from 2.1.282 it counts a refused reconnect of that stream as a terminal
/// error. On the third it closes the transport and retries at 1, 2, 4 and 8 seconds. If the IDE is still down
/// after about 15 seconds, which it always is during a rebuild, it gives up until <c>/mcp</c>. This is what
/// dropped the tools after a <c>shutdown_ide</c>; which attachment the session had was incidental.</item>
/// </list>
/// <para>
/// So the server runs stateless (<see cref="McpTransport"/>). It issues no session and offers no GET stream,
/// so neither mechanism has anything to act on. Nothing the server does needs either: it answers tool calls,
/// and sends no notification, sampling, elicitation or roots request of its own. What else stateless gives up
/// is listed on <see cref="McpTransport.Configure"/>.
/// </para>
/// <para>
/// These run a real server built like the IDE's under the SDK's real client, and restart it on the same port.
/// What Claude Code does with that is a separate question only a real relaunch answers. These prove the
/// server side: no session to go stale, no stream to drop, and a failing tool still explained.
/// </para>
/// </remarks>
public class McpRestartTests
{
    static McpRestartTests()
    {
        // ToolFailures now lives in the Desktop assembly. Supply this fixture's tool signatures
        // alongside the production signatures so binding failures still name the synthetic parameters.
        foreach (var method in typeof(Tools).GetMethods())
            if (method.GetCustomAttribute<McpServerToolAttribute>()?.Name is { } name)
                ToolFailures.ParameterTypes[name] = method.GetParameters()
                    .ToDictionary(parameter => parameter.Name!, parameter => parameter.ParameterType);
    }

    public sealed record Described(string Detail);

    [McpServerToolType]
    public sealed class Tools
    {
        [McpServerTool(Name = "echo"), Description("Returns its argument.")]
        public static string Echo(string text) => text;

        [McpServerTool(Name = "describe"), Description("Returns its argument in a JSON reply.")]
        public static Described Describe(string text) => new(text);

        [McpServerTool(Name = "fail"), Description("Throws.")]
        public static string Fail() => throw new InvalidOperationException("the tool broke");

        [McpServerTool(Name = "flag"), Description("Returns its flag.")]
        public static string Flag(bool on, int count = 0) => $"{on} {count}";

        [McpServerTool(Name = "seq"), Description("Returns its sequence and limit.")]
        public static string Seq(long after, int limit = 0) => $"{after} {limit}";

        [McpServerTool(Name = "refuse"), Description("Throws an ArgumentException of its own.")]
        public static string Refuse(string text) => throw new ArgumentException("the text was not wanted", nameof(text));

        [McpServerTool(Name = "marks"), Description("Returns its lines.")]
        public static string Marks(string name, int[] lines, string? note = null) => $"{name} {string.Join(",", lines)} {note}";
    }

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    /// <summary>
    /// A server built as <c>IdeServer</c> builds its own: Kestrel on loopback, the transport configured as given,
    /// the loopback gate, and the filter that explains a failing tool (#603).
    /// </summary>
    private static async Task<WebApplication> StartAsync(int port, Action<HttpServerTransportOptions> transport)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(o => o.ListenLocalhost(port));
        ToolFilters.Register(builder.Services
            .AddMcpServer()
            .WithHttpTransport(transport)
            .WithTools<Tools>(ReplyJson.Options), null);
        var app = builder.Build();
        app.UseLoopbackOnly(port);
        app.MapGet("/health", () => "ok");
        app.MapMcp("/mcp");
        await app.StartAsync(TestContext.Current.CancellationToken);
        return app;
    }

    private static async Task StopAsync(WebApplication app)
    {
        await app.StopAsync(TestContext.Current.CancellationToken);
        await app.DisposeAsync();
    }

    private static Task<McpClient> ConnectAsync(int port, int? maxReconnectionAttempts = null)
    {
        var options = new HttpClientTransportOptions
        {
            Endpoint = new Uri($"http://localhost:{port}/mcp"),
            TransportMode = HttpTransportMode.StreamableHttp,
        };
        if (maxReconnectionAttempts is { } attempts)
            options.MaxReconnectionAttempts = attempts;
        var transport = new HttpClientTransport(options);
        return McpClient.CreateAsync(transport,
            cancellationToken: TestContext.Current.CancellationToken);
    }

    /// <summary>Calls a tool, bounded, so a client left hanging fails the test rather than stalling it.</summary>
    private static async Task<CallToolResult> CallAsync(McpClient client, string tool, string? text = null)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        var arguments = text is null ? new Dictionary<string, object?>() : new() { ["text"] = text };
        return await client.CallToolAsync(tool, arguments, cancellationToken: timeout.Token);
    }

    private static string TextOf(CallToolResult result) =>
        string.Concat(result.Content.OfType<TextContentBlock>().Select(c => c.Text));

    private static async Task<string> EchoAsync(McpClient client, string text) =>
        TextOf(await CallAsync(client, "echo", text));

    [Theory]
    [InlineData("POST", "/mcp", "evil.example", null)]
    [InlineData("POST", "/mcp", "localhost", "http://evil.example")]
    [InlineData("GET", "/health", "evil.example", null)]
    public async Task A_foreign_host_or_origin_is_refused_before_the_endpoint(
        string method, string path, string host, string? origin)
    {
        var port = FreePort();
        var app = await StartAsync(port, McpTransport.Configure);
        try
        {
            using var http = new HttpClient();
            using var request = new HttpRequestMessage(new HttpMethod(method), $"http://localhost:{port}{path}");
            request.Headers.Host = $"{host}:{port}";
            if (origin is not null)
                request.Headers.Add("Origin", origin);

            using var response = await http.SendAsync(request, TestContext.Current.CancellationToken);
            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }
        finally
        {
            await StopAsync(app);
        }
    }

    [Fact]
    public async Task A_loopback_host_reaches_the_health_endpoint()
    {
        var port = FreePort();
        var app = await StartAsync(port, McpTransport.Configure);
        try
        {
            using var http = new HttpClient();
            using var response = await http.GetAsync($"http://localhost:{port}/health", TestContext.Current.CancellationToken);
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).Should().Be("ok");
        }
        finally
        {
            await StopAsync(app);
        }
    }

    [Fact]
    public async Task Discovery_advertises_the_sessionless_protocol_without_initializing()
    {
        var port = FreePort();
        var app = await StartAsync(port, McpTransport.Configure);
        try
        {
            using var http = new HttpClient();
            using var request = new HttpRequestMessage(HttpMethod.Post, $"http://localhost:{port}/mcp");
            request.Headers.Accept.ParseAdd("application/json, text/event-stream");
            request.Headers.Add("MCP-Protocol-Version", "2026-07-28");
            request.Headers.Add("Mcp-Method", "server/discover");
            request.Content = new StringContent(
                """
                {"jsonrpc":"2.0","id":1,"method":"server/discover","params":{"_meta":{
                  "io.modelcontextprotocol/protocolVersion":"2026-07-28",
                  "io.modelcontextprotocol/clientCapabilities":{},
                  "io.modelcontextprotocol/clientInfo":{"name":"HexIDE.Tests","version":"1"}
                }}}
                """,
                Encoding.UTF8, "application/json");

            using var response = await http.SendAsync(request, TestContext.Current.CancellationToken);
            var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            response.StatusCode.Should().Be(HttpStatusCode.OK, body);
            response.Headers.Contains("Mcp-Session-Id").Should().BeFalse();
            var json = response.Content.Headers.ContentType?.MediaType == "text/event-stream"
                ? body.ReplaceLineEndings("\n").Split('\n').Single(line => line.StartsWith("data: "))[6..]
                : body;
            using var reply = JsonDocument.Parse(json);
            reply.RootElement.TryGetProperty("error", out _).Should().BeFalse(json);
            reply.RootElement.GetProperty("result").GetProperty("supportedVersions")
                .EnumerateArray().Select(version => version.GetString()).Should().Contain("2026-07-28");
        }
        finally
        {
            await StopAsync(app);
        }
    }

    [Fact]
    public async Task A_client_attached_before_a_restart_is_answered_after_it()
    {
        var output = TestContext.Current.TestOutputHelper;
        var trace = new McpRestartDiagnostics((level, message) => output?.WriteLine($"[{level}] {message}"));
        var port = FreePort();
        var first = await StartAsync(port, McpTransport.Configure);
        await using var client = await trace.PhaseAsync("first connect attempt", () => ConnectAsync(port));
        (await EchoAsync(client, "before")).Should().Be("before");

        await trace.PhaseAsync("stop", () => StopAsync(first));
        var second = await trace.PhaseAsync("restart", () => StartAsync(port, McpTransport.Configure));
        try
        {
            string after;
            try
            {
                after = await trace.PhaseAsync("call after", () => EchoAsync(client, "after"));
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException(trace.Failure(exception), exception);
            }
            after.Should().Be("after", "the restart must be invisible to the client");
            (await EchoAsync(client, "again")).Should().Be("again");
        }
        finally
        {
            await StopAsync(second);
        }
    }

    [Fact]
    public async Task There_is_no_GET_stream_for_a_shutdown_to_drop()
    {
        // The mechanism that cost Claude Code its tools. A client asks for the stream with a GET; a server that
        // offers none answers 405, which the specification names as the way to decline, and a client then holds
        // no connection between calls. Stateful, the same GET is not refused this way: the endpoint exists.
        var port = FreePort();
        var app = await StartAsync(port, McpTransport.Configure);
        try
        {
            using var http = new HttpClient();
            using var request = new HttpRequestMessage(HttpMethod.Get, $"http://localhost:{port}/mcp");
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

            using var response = await http.SendAsync(request, TestContext.Current.CancellationToken);

            response.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
        }
        finally
        {
            await StopAsync(app);
        }
    }

    [Fact]
    public async Task A_tool_that_throws_is_still_explained()
    {
        // Stateless builds the server afresh for every request, so the filter registered once at startup has to
        // reach each of those. If it did not, a failing tool would be back to the SDK's bare "An error occurred".
        var port = FreePort();
        var app = await StartAsync(port, McpTransport.Configure);
        try
        {
            await using var client = await ConnectAsync(port);

            var result = await CallAsync(client, "fail");

            result.IsError.Should().BeTrue();
            TextOf(result).Should().Contain("System.InvalidOperationException").And.Contain("the tool broke");
        }
        finally
        {
            await StopAsync(app);
        }
    }

    // ── Arguments the SDK could not bind are refusals, not defects (#1270) ──────

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Unpaired_surrogate_arguments_are_refused_as_invalid_text(bool high)
    {
        // Keep the invalid string out of theory metadata: discovery serializes that metadata as UTF-8 too.
        var surrogate = high ? "\uD800" : "\uDC00";
        var port = FreePort();
        var app = await StartAsync(port, McpTransport.Configure);
        try
        {
            await using var client = await ConnectAsync(port);
            var encoded = await CallAsync(client, "echo", surrogate);
            TextOf(encoded).Should().Be("\uFFFD", "the SDK client replaces a lone surrogate while encoding");

            using var http = new HttpClient();
            using var request = new HttpRequestMessage(HttpMethod.Post, $"http://localhost:{port}/mcp");
            request.Headers.Accept.ParseAdd("application/json");
            request.Headers.Accept.ParseAdd("text/event-stream");
            var escaped = $"\\u{(int)surrogate[0]:X4}";
            var body = "{\"jsonrpc\":\"2.0\",\"id\":1337,\"method\":\"tools/call\",\"params\":{\"name\":\"echo\",\"arguments\":{\"text\":\""
                + escaped + "\"}}}";
            request.Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
            using var response = await http.SendAsync(request, TestContext.Current.CancellationToken);
            var reply = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            reply.Should().Contain("unpaired UTF-16 surrogate");
            var data = reply.Split('\n').Single(line => line.StartsWith("data: ", StringComparison.Ordinal))[6..];
            using var json = System.Text.Json.JsonDocument.Parse(data);
            var result = json.RootElement.GetProperty("result");
            result.GetProperty("isError").GetBoolean().Should().BeTrue();
            result.GetProperty("content")[0].GetProperty("text").GetString().Should().Be(
                "'echo' refused its arguments, and nothing was done: 'text' is not valid text (an unpaired UTF-16 surrogate). "
                + "Its input schema names each parameter and its type.");
            json.RootElement.GetProperty("id").GetInt32().Should().Be(1337);
            reply.Should().NotContain("An error occurred.");
            (await EchoAsync(client, "\uD800\uDC00")).Should().Be("\uD800\uDC00", "a whole surrogate pair is valid text");
            (await EchoAsync(client, @"\uD800")).Should().Be(@"\uD800", "literal escape text is valid too");
        }
        finally
        {
            await StopAsync(app);
        }
    }

    private static async Task<CallToolResult> CallWithAsync(string tool, Dictionary<string, object?> arguments)
    {
        var port = FreePort();
        var app = await StartAsync(port, McpTransport.Configure);
        try
        {
            await using var client = await ConnectAsync(port);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            return await client.CallToolAsync(tool, arguments, cancellationToken: timeout.Token);
        }
        finally
        {
            await StopAsync(app);
        }
    }

    [Fact]
    public async Task Missing_arguments_are_refused_by_name_and_not_called_a_defect()
    {
        var result = await CallWithAsync("marks", new() { ["path"] = "x" });

        result.IsError.Should().BeTrue();
        TextOf(result).Should().Be("'marks' refused its arguments, and nothing was done: 'name' is required; 'lines' is "
            + "required; it takes no 'path' parameter. Its input schema names each parameter and its type.");
    }

    [Theory]
    [InlineData("on", "true", "'on' must be boolean, not \"true\"")]
    [InlineData("on", 1, "'on' must be boolean, not 1")]
    [InlineData("count", 1.5, "'count' must be integer, not 1.5")]
    [InlineData("count", "five", "'count' must be integer, not \"five\"")]
    [InlineData("on", null, "'on' must be boolean, not null")]
    public async Task An_argument_of_the_wrong_type_is_refused_by_name(string name, object? value, string problem)
    {
        var arguments = new Dictionary<string, object?> { ["on"] = true, [name] = value };

        var result = await CallWithAsync("flag", arguments);

        result.IsError.Should().BeTrue();
        TextOf(result).Should().Be($"'flag' refused its arguments, and nothing was done: {problem}. Its input schema "
            + "names each parameter and its type.");
    }

    [Theory]
    [InlineData(new object?[] { 1, "x" }, "'lines' item 1 must be integer, not \"x\"")]
    [InlineData(new object?[] { 1.5 }, "'lines' item 0 must be integer, not 1.5")]
    public async Task An_array_item_of_the_wrong_type_is_refused_by_position(object?[] lines, string problem)
    {
        var result = await CallWithAsync("marks", new() { ["name"] = "Form1", ["lines"] = lines });

        TextOf(result).Should().Be($"'marks' refused its arguments, and nothing was done: {problem}. Its input schema "
            + "names each parameter and its type.");
    }

    [Fact]
    public async Task Null_is_refused_where_the_parameter_cannot_hold_it_and_taken_where_it_can()
    {
        // A null the binding accepts reached the tool, which failed in its own code and asked to be reported.
        TextOf(await CallWithAsync("marks", new() { ["name"] = "Form1", ["lines"] = null })).Should().Be(
            "'marks' refused its arguments, and nothing was done: 'lines' must be array, not null. Its input schema names "
            + "each parameter and its type.");
        TextOf(await CallWithAsync("marks", new() { ["name"] = "Form1", ["lines"] = new[] { 3 }, ["note"] = null }))
            .Should().Be("Form1 3 ");
    }

    [Fact]
    public async Task A_number_in_a_string_binds_as_the_SDK_reads_it_and_is_not_named_beside_a_real_problem()
    {
        TextOf(await CallWithAsync("flag", new() { ["on"] = true, ["count"] = "5" })).Should().Be("True 5");
        TextOf(await CallWithAsync("flag", new() { ["on"] = "no", ["count"] = "5", ["cuont"] = 1 })).Should().Be(
            "'flag' refused its arguments, and nothing was done: 'on' must be boolean, not \"no\"; it takes no 'cuont' "
            + "parameter. Its input schema names each parameter and its type.");
    }

    [Fact]
    public async Task An_argument_the_tool_does_not_take_is_ignored_as_the_SDK_does_when_nothing_else_is_wrong()
    {
        var result = await CallWithAsync("flag", new() { ["on"] = true, ["extra"] = 1 });
        result.IsError.Should().NotBeTrue();
        var blocks = result.Content.OfType<TextContentBlock>().ToArray();
        blocks.Should().HaveCount(2);
        blocks[0].Text.Should().Be("True 0");
        blocks[1].Text.Should().Be("ignored 'extra': flag takes no such parameter");
    }

    [Theory]
    [InlineData("interactive_only")]
    [InlineData("interactive-only")]
    [InlineData("INTERACTIVEONLY")]
    public void Ignored_argument_suggestions_match_only_parameter_spelling(string spelling)
    {
        using var schema = System.Text.Json.JsonDocument.Parse(
            """{"properties":{"interactiveOnly":{"type":"boolean"}}}""");
        var (problems, ignored) = ArgumentCheck.Check(new Dictionary<string, System.Text.Json.JsonElement>()
        {
            [spelling] = System.Text.Json.JsonSerializer.SerializeToElement(false),
        }, schema.RootElement, "dump_visual_tree");
        problems.Should().BeEmpty();
        ignored.Should().Equal($"ignored '{spelling}': dump_visual_tree takes no such parameter (did you mean 'interactiveOnly'?)");
    }

    [Fact]
    public void An_unlocated_binding_failure_keeps_the_serializer_path()
    {
        const string message = "Cannot convert. Path: $[1] | LineNumber: 0 | BytePositionInLine: 3.";
        ToolFailures.BindingProblems(new System.Text.Json.JsonException(message), null, "unknown")
            .Should().Equal(message);
    }

    [Fact]
    public async Task Ignored_arguments_are_named_in_a_JSON_object_reply()
    {
        var result = await CallWithAsync("describe", new() { ["text"] = "hello", ["extra"] = 1 });
        result.IsError.Should().NotBeTrue();
        using var json = System.Text.Json.JsonDocument.Parse(TextOf(result));
        json.RootElement.GetProperty("detail").GetString().Should().Be("hello");
        json.RootElement.GetProperty("ignoredArguments")[0].GetString().Should().Be(
            "ignored 'extra': describe takes no such parameter");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Overflow_names_the_int_parameter_and_never_the_valid_long(bool numericString)
    {
        var result = await CallWithAsync("seq", new()
        {
            ["after"] = 5_000_000_000L,
            ["limit"] = numericString ? (object)"3000000000" : 3_000_000_000L,
        });
        result.IsError.Should().BeTrue();
        TextOf(result).Should().Contain("'limit'").And.NotContain("'after'");
    }

    [Fact]
    public async Task Overflow_in_an_array_item_names_its_parameter()
    {
        var result = await CallWithAsync("marks", new() { ["name"] = "Form1", ["lines"] = new[] { 3_000_000_000L } });
        result.IsError.Should().BeTrue();
        TextOf(result).Should().Contain("'lines'").And.Contain("Path: $[0]");
    }

    [Fact]
    public async Task An_integer_the_schema_allows_but_the_parameter_cannot_hold_is_refused_by_name()
    {
        var result = await CallWithAsync("flag", new() { ["on"] = true, ["count"] = 3_000_000_000L });

        result.IsError.Should().BeTrue();
        TextOf(result).Should().Contain("'count'").And.Contain("System.Int32");
    }

    [Fact]
    public async Task Arguments_that_bind_still_reach_the_tool()
    {
        var result = await CallWithAsync("flag", new() { ["on"] = true, ["count"] = 2 });

        result.IsError.Should().NotBeTrue();
        TextOf(result).Should().Be("True 2");
    }

    [Fact]
    public async Task An_ArgumentException_the_tool_throws_itself_is_still_a_defect()
    {
        var result = await CallWithAsync("refuse", new() { ["text"] = "x" });

        result.IsError.Should().BeTrue();
        TextOf(result).Should().Contain("System.ArgumentException").And.Contain("This is a defect in the tool");
    }

    [Fact]
    public async Task A_stateful_server_strands_the_SDKs_own_client_after_a_restart()
    {
        // The session mechanism, kept as a control. If a later SDK starts a new session by itself on the 404, this
        // fails, and the reasons for running stateless should be looked at again rather than kept out of habit.
        //
        // Stateful is set explicitly rather than taken from the SDK's default, which the SDK's 2.x line changes
        // (#764). And the client is told not to reconnect its GET stream: when the first server stops, the stream
        // ends and the client tries again a second later with the old session id. If the restart takes longer than
        // that, the retry meets the 404 first and cancels the connection, so the call below fails differently.
        var port = FreePort();
        var first = await StartAsync(port, o => o.Stateless = false);
        await using var client = await ConnectAsync(port, maxReconnectionAttempts: 0);
        (await EchoAsync(client, "before")).Should().Be("before");

        await StopAsync(first);
        var second = await StartAsync(port, o => o.Stateless = false);
        try
        {
            var act = () => EchoAsync(client, "after");
            (await act.Should().ThrowAsync<HttpRequestException>())
                .Which.Message.Should().Contain("404").And.Contain("Session not found");
        }
        finally
        {
            await StopAsync(second);
        }
    }

    [Fact]
    public async Task A_reply_carries_its_text_as_written_and_not_escaped_for_a_web_page()
    {
        // A reply is read as text, where 'Form1' had come out as \u0027Form1\u0027 and a Cyrillic name could not be
        // read at all (#753). JSON's own escapes stay: a quote, a backslash, a line break.
        // So does an escape for what would not be seen, such as a right-to-left override; an emoji is seen.
        var result = await CallWithAsync("describe", new() { ["text"] = "'Ж1' → <a & b> + 😀 \"c\\d\"\n\u202E" });

        TextOf(result).Should().Be("""{"detail":"'Ж1' → <a & b> + 😀 \"c\\d\"\n\u202E"}""");
    }

    [Fact]
    public void The_IDEs_server_is_built_with_this_transport()
    {
        // No test project references HexIDE.Desktop, so this reads the source, as ToolSource does. The tests
        // above prove the configuration; this proves the server uses it.
        // Without comments, so a line commented out does not count as used.
        var source = ToolSource.WithoutComments(
            File.ReadAllText(Path.Combine(RepoTree.Root(), "IDE", "HexIDE.Desktop", "Server", "IdeServer.cs")));

        // ToolFilters registers the session guard and then ToolFailures (#1306). It is compiled in here, so
        // SessionGuardTests drive that order for real; this proves the IDE registers through it, with the session it
        // was started for, and reports that session in /health.
        source.Should().Contain(".WithHttpTransport(McpTransport.Configure)")
            .And.MatchRegex(@"ToolFilters\.Register\([\s\S]*?ReplyJson\.Options\), session\)")
            .And.Contain("app.UseLoopbackOnly(port);")
            .And.Contain("Session: session,")
            .And.Contain(".AddCallToolFilter(OpenDialogNote.Filter)")
            .And.Contain(".WithTools<HexIdeTools>(ReplyJson.Options)");

        var startup = ToolSource.WithoutComments(
            File.ReadAllText(Path.Combine(RepoTree.Root(), "IDE", "HexIDE.Desktop", "DesktopStartup.cs")));
        startup.Should().Contain("IdeServer.RunAsync(port, ServerOptions.Session,",
            "the server is started with the session --session named, or the guard has nothing to compare");
    }
}
