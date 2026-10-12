using System.ComponentModel;
using System.IO;
using System.Net;
using System.Net.Http;
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
/// It did not. The SDK's Streamable HTTP transport is stateful by default: <c>initialize</c> issues an
/// <c>Mcp-Session-Id</c>, every later request carries it, and a relaunched process has never heard of it. The
/// relaunched server answers 404 "Session not found", as the MCP specification requires for an unknown session,
/// and the SDK's own client does not start a new session on that 404. Its next call hangs. CLAUDE.md said the
/// transport was stateless ("each call is a fresh POST"). That was never true of the default, and it is how
/// a dropped attachment after every rebuild came to look like a client quirk.
/// </para>
/// <para>
/// So the server runs stateless (<see cref="McpTransport"/>). Nothing it does needs a session: it answers tool
/// calls, and sends no notification, sampling, elicitation or roots request of its own, which are what stateless
/// mode gives up.
/// </para>
/// <para>
/// These run a real server with the same transport configuration under the SDK's real client, and restart it
/// on the same port. Whether Claude Code's own client then keeps its tools is a separate question that only a
/// real relaunch answers. This proves only that the server side no longer strands a client.
/// </para>
/// </remarks>
public class McpRestartTests
{
    [McpServerToolType]
    public sealed class Tools
    {
        [McpServerTool(Name = "echo"), Description("Returns its argument.")]
        public static string Echo(string text) => text;
    }

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    /// <summary>A server shaped like the IDE's: Kestrel on loopback, the transport configured as given.</summary>
    private static async Task<WebApplication> StartAsync(int port, Action<HttpServerTransportOptions> transport)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(o => o.ListenLocalhost(port));
        builder.Services.AddMcpServer().WithHttpTransport(transport).WithTools<Tools>();
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

    private static Task<McpClient> ConnectAsync(int port) => McpClient.CreateAsync(
        new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri($"http://localhost:{port}/mcp"),
            TransportMode = HttpTransportMode.StreamableHttp,
        }),
        cancellationToken: TestContext.Current.CancellationToken);

    /// <summary>Calls the tool, bounded, so a client left hanging fails the test rather than stalling it.</summary>
    private static async Task<string> EchoAsync(McpClient client, string text)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        var result = await client.CallToolAsync("echo", new Dictionary<string, object?> { ["text"] = text },
            cancellationToken: timeout.Token);
        return string.Concat(result.Content.OfType<TextContentBlock>().Select(c => c.Text));
    }

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
    public async Task Under_the_SDKs_default_a_restart_strands_the_client()
    {
        // The case the configuration exists for. If a later SDK starts a new session on its own when it meets
        // the 404, this fails, and the reason for running stateless should be looked at again rather than kept
        // out of habit.
        var port = FreePort();
        var first = await StartAsync(port, _ => { });
        await using var client = await ConnectAsync(port);
        (await EchoAsync(client, "before")).Should().Be("before");

        await StopAsync(first);
        var second = await StartAsync(port, _ => { });
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
        var source = File.ReadAllText(Path.Combine(RepoTree.Root(), "IDE", "HexIDE.Desktop", "Server", "IdeServer.cs"));

        source.Should().Contain(".WithHttpTransport(McpTransport.Configure)");
    }
}
