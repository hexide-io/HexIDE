using System.ComponentModel;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using HexIDE.Desktop.Server;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace HexIDE.Tests.Infrastructure;

/// <summary>
/// An IDE started for one agent session answers no other session's tool calls (#1306).
/// </summary>
/// <remarks>
/// <para>
/// Ports keep sessions apart only while every session sets its own (#1212). Two that set nothing share 5123, and a
/// session given the port of a stale IDE drives that IDE, and either way the replies look exactly as they would from
/// the session's own build. The guard makes that loud: the IDE knows the session it was started for, the client
/// names its own in a header, and a call from anyone else is answered with what went wrong instead of being run.
/// </para>
/// <para>
/// These run a real server, built with the same registration the IDE uses, under the SDK's real client, because the
/// question that matters is one only a real request answers: whether the header reaches a tool-call filter at all in
/// a stateless server, which builds itself afresh for every request.
/// </para>
/// </remarks>
public class SessionGuardTests
{
    [McpServerToolType]
    public sealed class Tools
    {
        public static int Calls;

        [McpServerTool(Name = "echo"), Description("Returns its argument.")]
        public static string Echo(string text)
        {
            Interlocked.Increment(ref Calls);
            return text;
        }

        [McpServerTool(Name = "marks"), Description("Returns its lines.")]
        public static string Marks(string name, int[] lines)
        {
            Interlocked.Increment(ref Calls);
            return $"{name} {string.Join(",", lines)}";
        }
    }

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    /// <summary>A server registered as <c>IdeServer</c> registers its own, for an IDE started with <paramref name="session"/>.</summary>
    private static async Task<WebApplication> StartAsync(int port, string? session)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(o => o.ListenLocalhost(port));
        ToolFilters.Register(builder.Services
            .AddMcpServer()
            .WithHttpTransport(McpTransport.Configure)
            .WithTools<Tools>(ReplyJson.Options), session);
        var app = builder.Build();
        app.MapMcp("/mcp");
        await app.StartAsync(TestContext.Current.CancellationToken);
        return app;
    }

    private static Task<McpClient> ConnectAsync(int port, string? session)
    {
        var options = new HttpClientTransportOptions
        {
            Endpoint = new Uri($"http://localhost:{port}/mcp"),
            TransportMode = HttpTransportMode.StreamableHttp,
        };
        if (session is not null)
            options.AdditionalHeaders = new Dictionary<string, string> { [SessionGuard.Header] = session };
        return McpClient.CreateAsync(new HttpClientTransport(options),
            cancellationToken: TestContext.Current.CancellationToken);
    }

    private static async Task<CallToolResult> CallAsync(McpClient client, string tool, Dictionary<string, object?> arguments)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        return await client.CallToolAsync(tool, arguments, cancellationToken: timeout.Token);
    }

    private static Task<CallToolResult> EchoAsync(McpClient client, string text) =>
        CallAsync(client, "echo", new() { ["text"] = text });

    private static string TextOf(CallToolResult result) =>
        string.Concat(result.Content.OfType<TextContentBlock>().Select(c => c.Text));

    /// <summary>Runs <paramref name="body"/> against a fresh server for <paramref name="session"/>, and stops it.</summary>
    private static async Task WithServerAsync(string? session, Func<int, Task> body)
    {
        var port = FreePort();
        var app = await StartAsync(port, session);
        try
        {
            await body(port);
        }
        finally
        {
            await app.StopAsync(TestContext.Current.CancellationToken);
            await app.DisposeAsync();
        }
    }

    // ── Over the wire ──────────────────────────────────────────────────────────

    [Fact]
    public Task The_session_it_was_started_for_is_served() =>
        WithServerAsync("alpha", async port =>
        {
            await using var client = await ConnectAsync(port, "alpha");

            var result = await EchoAsync(client, "hello");

            result.IsError.Should().NotBe(true);
            TextOf(result).Should().Be("hello");
        });

    [Fact]
    public Task Another_sessions_call_is_refused_and_not_run() =>
        WithServerAsync("alpha", async port =>
        {
            await using var client = await ConnectAsync(port, "beta");
            var before = Volatile.Read(ref Tools.Calls);

            var result = await EchoAsync(client, "hello");

            result.IsError.Should().BeTrue();
            TextOf(result).Should().Contain("'alpha'").And.Contain("'beta'").And.Contain("was not run");
            Volatile.Read(ref Tools.Calls).Should().Be(before, "a refused call must not reach the tool");
        });

    [Fact]
    public Task A_client_that_names_no_session_is_refused() =>
        WithServerAsync("alpha", async port =>
        {
            await using var client = await ConnectAsync(port, session: null);

            var result = await EchoAsync(client, "hello");

            result.IsError.Should().BeTrue();
            TextOf(result).Should().Contain("'alpha'").And.Contain("named no session");
        });

    [Fact]
    public Task An_empty_session_header_counts_as_naming_none() =>
        WithServerAsync("alpha", async port =>
        {
            // What .mcp.json sends from a session started without HEXIDE_SESSION: "${HEXIDE_SESSION:-}" is empty.
            await using var client = await ConnectAsync(port, "");

            var result = await EchoAsync(client, "hello");

            result.IsError.Should().BeTrue();
            TextOf(result).Should().Contain("named no session");
        });

    [Fact]
    public Task A_refusal_is_a_result_so_the_client_stays_connected() =>
        WithServerAsync("alpha", async port =>
        {
            // Claude Code closes its transport after three consecutive connection errors, and the tools then stay
            // gone until /mcp. A refusal answered as an error RESULT is not one of those, so a session told it is on
            // the wrong IDE can still list the tools and read the reply, however often it calls.
            await using var client = await ConnectAsync(port, "beta");

            for (var i = 0; i < 4; i++)
                (await EchoAsync(client, $"call {i}")).IsError.Should().BeTrue();

            var tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
            tools.Select(t => t.Name).Should().Contain("echo", "listing the tools is not a call, and is answered");
        });

    [Fact]
    public Task Another_session_is_refused_before_its_arguments_are_checked() =>
        WithServerAsync("alpha", async port =>
        {
            // The guard comes first: a caller on the wrong IDE is told that, not how to fix arguments for a tool on
            // an IDE that is not its own.
            await using var client = await ConnectAsync(port, "beta");

            var result = await CallAsync(client, "marks", new() { ["name"] = "x" });

            result.IsError.Should().BeTrue();
            TextOf(result).Should().Contain("'alpha'");
        });

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public Task An_IDE_started_for_no_session_serves_a_client_that_names_none(string? caller) =>
        WithServerAsync(null, async port =>
        {
            // A contributor sets nothing, and nothing changes for them: .mcp.json sends the header empty.
            await using var client = await ConnectAsync(port, caller);

            var result = await EchoAsync(client, "hello");

            result.IsError.Should().NotBe(true);
            TextOf(result).Should().Be("hello");
        });

    [Fact]
    public Task An_IDE_started_for_no_session_refuses_a_client_that_names_one() =>
        WithServerAsync(null, async port =>
        {
            // The likeliest mistake of all: a session that set HEXIDE_SESSION and not HEXIDE_MCP_PORT reaches 5123,
            // where an IDE started with nothing set is waiting. Serving it would be the silence this exists to end.
            await using var client = await ConnectAsync(port, "beta");
            var before = Volatile.Read(ref Tools.Calls);

            var result = await EchoAsync(client, "hello");

            result.IsError.Should().BeTrue();
            TextOf(result).Should().Contain("for no session").And.Contain("'beta'");
            Volatile.Read(ref Tools.Calls).Should().Be(before);
        });

    // ── The rule ───────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("alpha", "alpha")]
    [InlineData("alpha", "ALPHA")]
    [InlineData("alpha", " alpha ")]
    [InlineData(null, null)]
    [InlineData(null, "")]
    [InlineData(null, "  ")]
    public void The_same_session_is_allowed(string? ide, string? caller) =>
        SessionGuard.Refusal(ide, caller).Should().BeNull();

    [Theory]
    [InlineData("alpha", "beta")]
    [InlineData("alpha", "alph")]
    [InlineData("alph", "alpha")]
    [InlineData("alpha", null)]
    [InlineData("alpha", "")]
    [InlineData("alpha", "   ")]
    [InlineData(null, "beta")]
    public void Any_other_caller_is_refused(string? ide, string? caller) =>
        SessionGuard.Refusal(ide, caller).Should().NotBeNull();

    [Fact]
    public void A_refusal_says_whose_IDE_this_is_and_what_to_check()
    {
        var refusal = SessionGuard.Refusal("alpha", "beta")!;

        refusal.Should().Contain("'alpha'").And.Contain("'beta'")
            .And.Contain($"PID {Environment.ProcessId}")
            .And.Contain("X-HexIDE-Session").And.Contain("HEXIDE_MCP_PORT").And.Contain("HEXIDE_SESSION");
    }
}
