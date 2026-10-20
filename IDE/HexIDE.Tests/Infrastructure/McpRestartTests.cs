using System.ComponentModel;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
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
/// It did not, through two mechanisms, both of which come from the SDK's default transport being stateful:
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
    /// and the filter that explains a failing tool (#603).
    /// </summary>
    private static async Task<WebApplication> StartAsync(int port, Action<HttpServerTransportOptions> transport)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(o => o.ListenLocalhost(port));
        builder.Services
            .AddMcpServer()
            .WithHttpTransport(transport)
            .WithTools<Tools>(ReplyJson.Options)
            .WithRequestFilters(filters => filters.AddCallToolFilter(ToolFailures.Filter));
        var app = builder.Build();
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
        return McpClient.CreateAsync(new HttpClientTransport(options),
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

    [Fact]
    public async Task A_client_attached_before_a_restart_is_answered_after_it()
    {
        var port = FreePort();
        var first = await StartAsync(port, McpTransport.Configure);
        await using var client = await ConnectAsync(port);
        (await EchoAsync(client, "before")).Should().Be("before");

        await StopAsync(first);
        var second = await StartAsync(port, McpTransport.Configure);
        try
        {
            (await EchoAsync(client, "after")).Should().Be("after", "the restart must be invisible to the client");
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
        TextOf(await CallWithAsync("flag", new() { ["on"] = true, ["extra"] = 1 })).Should().Be("True 0");
    }

    [Fact]
    public async Task An_integer_the_schema_allows_but_the_parameter_cannot_hold_is_refused_by_name()
    {
        var result = await CallWithAsync("flag", new() { ["on"] = true, ["count"] = 3_000_000_000L });

        result.IsError.Should().BeTrue();
        TextOf(result).Should().Be("'flag' refused its arguments, and nothing was done: 'count' is 3000000000, outside "
            + "the range of a 32-bit integer. Its input schema names each parameter and its type.");
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
        var result = await CallWithAsync("describe", new() { ["text"] = "'Ж1' → <a & b> + \"c\\d\"\n" });

        TextOf(result).Should().Be("""{"detail":"'Ж1' → <a & b> + \"c\\d\"\n"}""");
    }

    [Fact]
    public void The_IDEs_server_is_built_with_this_transport()
    {
        // No test project references HexIDE.Desktop, so this reads the source, as ToolSource does. The tests
        // above prove the configuration; this proves the server uses it.
        // Without comments, so a line commented out does not count as used.
        var source = ToolSource.WithoutComments(
            File.ReadAllText(Path.Combine(RepoTree.Root(), "IDE", "HexIDE.Desktop", "Server", "IdeServer.cs")));

        source.Should().Contain(".WithHttpTransport(McpTransport.Configure)")
            .And.Contain("filters.AddCallToolFilter(ToolFailures.Filter)")
            .And.Contain(".AddCallToolFilter(OpenDialogNote.Filter)")
            .And.Contain(".WithTools<HexIdeTools>(ReplyJson.Options)");
    }
}
