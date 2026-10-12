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
    [McpServerToolType]
    public sealed class Tools
    {
        [McpServerTool(Name = "echo"), Description("Returns its argument.")]
        public static string Echo(string text) => text;

        [McpServerTool(Name = "fail"), Description("Throws.")]
        public static string Fail() => throw new InvalidOperationException("the tool broke");
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
            .WithTools<Tools>()
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
    public void The_IDEs_server_is_built_with_this_transport()
    {
        // No test project references HexIDE.Desktop, so this reads the source, as ToolSource does. The tests
        // above prove the configuration; this proves the server uses it.
        // Without comments, so a line commented out does not count as used.
        var source = ToolSource.WithoutComments(
            File.ReadAllText(Path.Combine(RepoTree.Root(), "IDE", "HexIDE.Desktop", "Server", "IdeServer.cs")));

        source.Should().Contain(".WithHttpTransport(McpTransport.Configure)")
            .And.Contain("filters.AddCallToolFilter(ToolFailures.Filter)");
    }
}
