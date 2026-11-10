using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using HexIDE.Lsp;
using StreamJsonRpc;

namespace HexIDE.Tests.LspClient;

public class TcpLspTransportTests
{
    private static TcpLspTransport Transport(string endpoint, string role, NamedPipeLaunch? launch = null) =>
        new(endpoint, Enum.Parse<TcpRole>(role),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<TcpLspTransport>.Instance,
            launch, connectTimeout: TimeSpan.FromSeconds(5));

    [Theory]
    [InlineData("Connect")]
    [InlineData("Listen")]
    public async Task InitializeUsesContentLengthFramesInBothDirections(string role)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        if (role == "Connect") listener.Start();
        var port = role == "Connect" ? ((IPEndPoint)listener.LocalEndpoint).Port : 0;
        await using var transport = Transport($"tcp://localhost:{port}", role);
        var connecting = transport.ConnectAsync(new SystemTextJsonFormatter(), timeout.Token);
        using var peer = role == "Connect" ? await listener.AcceptTcpClientAsync(timeout.Token) : new TcpClient();
        if (role == "Listen")
        {
            transport.BoundAddress.Should().Be(IPAddress.Loopback);
            await peer.ConnectAsync(IPAddress.Loopback, transport.BoundPort, timeout.Token);
        }
        var handler = await connecting;
        handler.Should().NotBeNull();
        using var rpc = new JsonRpc(handler!);
        rpc.StartListening();
        var reply = rpc.InvokeWithParameterObjectAsync<JsonElement>("initialize", new Dictionary<string, object?>(), cancellationToken: timeout.Token);
        var stream = peer.GetStream();
        using var frame = JsonDocument.Parse(await ReadFrame(stream, timeout.Token));
        frame.RootElement.GetProperty("method").GetString().Should().Be("initialize");
        frame.RootElement.GetProperty("jsonrpc").GetString().Should().Be("2.0");
        var body = Encoding.UTF8.GetBytes($"{{\"jsonrpc\":\"2.0\",\"id\":{frame.RootElement.GetProperty("id")},\"result\":{{\"capabilities\":{{}}}}}}");
        await stream.WriteAsync(Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n"), timeout.Token);
        await stream.WriteAsync(body, timeout.Token);
        (await reply.WaitAsync(timeout.Token)).GetProperty("capabilities").ValueKind.Should().Be(JsonValueKind.Object);
        transport.CanReconnect.Should().BeTrue();
        transport.LeaveServerRunning.Should().BeTrue();
    }

    [Fact]
    public async Task LaunchedServerReceivesTheBoundPortAndReportsItsProcess()
    {
        var server = typeof(global::HexIDE.Lsp.TestServer.Program).Assembly.Location;
        var launch = new NamedPipeLaunch("dotnet", "", ArgumentList: [server, "tcp", "{host}", "{port}"]);
        await using var transport = Transport("tcp://localhost:0", "Listen", launch);
        var received = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        transport.Notice += (_, n) =>
        {
            if (n.Kind == TransportNoticeKind.StandardOutput && n.Text == "tcp-received:initialize") received.TrySetResult();
        };
        var handler = await transport.ConnectAsync(new SystemTextJsonFormatter(), TestContext.Current.CancellationToken);
        handler.Should().NotBeNull();
        transport.CanReconnect.Should().BeFalse();
        transport.LeaveServerRunning.Should().BeFalse();
        transport.Unobservable.Should().BeNull();
        using var rpc = new JsonRpc(handler!);
        rpc.StartListening();
        var reply = await rpc.InvokeWithParameterObjectAsync<JsonElement>("initialize", new Dictionary<string, object?>(), cancellationToken: TestContext.Current.CancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        reply.GetProperty("capabilities").ValueKind.Should().Be(JsonValueKind.Object);
        await received.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        await rpc.InvokeWithParameterObjectAsync<JsonElement>("shutdown", null, cancellationToken: TestContext.Current.CancellationToken);
        await rpc.NotifyWithParameterObjectAsync("exit", null);
        transport.ShutdownCompleted();
    }

    private static async Task<string> ReadFrame(Stream stream, CancellationToken token)
    {
        var header = new List<byte>();
        var one = new byte[1];
        while (!Encoding.ASCII.GetString(header.ToArray()).EndsWith("\r\n\r\n", StringComparison.Ordinal))
        {
            await stream.ReadExactlyAsync(one, token);
            header.Add(one[0]);
            header.Count.Should().BeLessThan(4096);
        }
        var text = Encoding.ASCII.GetString(header.ToArray());
        text.Should().StartWith("Content-Length:");
        var length = int.Parse(text.Split("\r\n")[0].Split(':')[1], System.Globalization.CultureInfo.InvariantCulture);
        var body = new byte[length];
        await stream.ReadExactlyAsync(body, token);
        return Encoding.UTF8.GetString(body);
    }

    [Fact]
    public async Task MissingServerNamesTheHostAndPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        await using var transport = Transport($"tcp://127.0.0.1:{port}", "Connect");
        (await transport.ConnectAsync(new SystemTextJsonFormatter(), TestContext.Current.CancellationToken)).Should().BeNull();
        transport.LastFailure.Should().Contain("127.0.0.1").And.Contain(port.ToString(System.Globalization.CultureInfo.InvariantCulture));
        transport.IsAlive.Should().BeFalse();
    }

    [Fact]
    public void TransportInventoryIsClosed()
    {
        Enum.GetNames<LanguageConnectionTransport>().Should().BeEquivalentTo("Stdio", "Pipe", "WebSocket", "Tcp");
    }

    [Theory]
    [InlineData("connect", "tcp://localhost:6005")]
    [InlineData("listen", "tcp://localhost:0")]
    public async Task ConfigurationReachesTheFactory(string role, string endpoint)
    {
        var entry = new LanguageServerEntry { Id = "tcp-test", Transport = "tcp", Endpoint = endpoint,
            Extensions = [".tcp"], LanguageId = "tcp-test" };
        entry.TcpRole = role;
        var problems = new List<LanguageServerConfigProblem>();
        var loader = new LanguageServerConfigLoader("unused", Microsoft.Extensions.Logging.Abstractions.NullLogger<LanguageServerConfigLoader>.Instance);
        loader.ValidateContributed(entry, problems).Should().BeTrue();
        problems.Should().BeEmpty();
        var registration = new LanguageServerRegistrationFactory(Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance)
            .Create([entry]).Should().ContainSingle().Which;
        registration.Transport.ToString().Should().Be("Tcp");
        registration.Endpoint.Should().Be($"{endpoint} ({role})");
        await using var capture = new HexIDE.Conversations.ConversationLog();
        var connection = new LanguageServerConnection("tcp-test", "TCP", LanguageConnectionKind.LanguageServer,
            LanguageConnectionState.NotStarted, [".tcp"], "tcp-test", null, registration.Transport, registration.Endpoint);
        var row = new HexIDE.Tools.LanguageServers.LanguageServerRowViewModel(connection,
            Substitute.For<HexIDE.Localization.ILocalizationService>(), capture);
        row.Transport.Should().Be("tcp");

        var seenPath = Path.Combine(Path.GetTempPath(), $"hexide-tcp-seen-{Guid.NewGuid():N}.json");
        try
        {
            entry.Command = "example-tcp-server";
            problems.Clear();
            loader.ValidateContributed(entry, problems, new LanguageServerCommandStore(seenPath)).Should().BeTrue();
            problems.Should().ContainSingle(p => p.Kind == LanguageServerConfigProblemKind.UnseenCommand);
        }
        finally { File.Delete(seenPath); }

        entry.Endpoint = "tcp://localhost";
        problems.Clear();
        loader.ValidateContributed(entry, problems).Should().BeFalse();
        problems.Should().ContainSingle(p => p.Code == "Tcp" && p.EntryRejected);
        entry.Endpoint = endpoint;
        entry.TcpRole = "wrong";
        problems.Clear();
        loader.ValidateContributed(entry, problems).Should().BeFalse();
        problems.Should().ContainSingle(p => p.Code == "Tcp" && p.EntryRejected);
    }
}
