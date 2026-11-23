using System.Net;
using System.Net.Sockets;
using HexIDE.Lsp;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using StreamJsonRpc;

namespace HexIDE.Tests.LspClient;

/// <summary>
/// An entry that only connects is announced and confirmed as a new command is (#5465). It used to slip past both,
/// because the seen store's identity was the launched command alone. Measured on main <c>49c175c48</c>: a connect-only
/// tcp entry dialled a non-loopback address with no notice, and sent <c>initialize</c> with the project folder,
/// a path under the user's profile. A tcp listener is also refused off this machine, as a WebSocket listener is.
/// </summary>
public sealed class ConnectIdentityTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("hexide-connect-").FullName;
    private readonly ILoggerFactory _logging = LoggerFactory.Create(_ => { });

    public void Dispose()
    {
        _logging.Dispose();
        try { Directory.Delete(_dir, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private LanguageServerCommandStore Store() => new(Path.Combine(_dir, "seen.json"));

    private LanguageServerConfigResult Load(string servers, LanguageServerCommandStore store)
    {
        var path = Path.Combine(_dir, "lsp-servers.json");
        File.WriteAllText(path, "{\"version\":1,\"servers\":[" + servers + "]}");
        return new LanguageServerConfigLoader(path, NullLogger<LanguageServerConfigLoader>.Instance).Load([], store);
    }

    private static string Tcp(string endpoint, string? role = null, string? command = null) =>
        "{\"id\":\"remote\",\"extensions\":[\".cls\"],\"languageId\":\"vb6\",\"transport\":\"tcp\",\"endpoint\":\""
        + endpoint + "\"" + (role is null ? "" : ",\"tcpRole\":\"" + role + "\"")
        + (command is null ? "" : ",\"command\":\"" + command + "\"") + "}";

    private static string WebSocket(string endpoint, string? role = null) =>
        "{\"id\":\"ws\",\"extensions\":[\".cls\"],\"languageId\":\"vb6\",\"transport\":\"websocket\",\"endpoint\":\""
        + endpoint + "\"" + (role is null ? "" : ",\"websocketRole\":\"" + role + "\"") + "}";

    [Fact]
    public void A_connect_only_tcp_entry_is_announced_once_and_still_loads()
    {
        var store = Store();

        var first = Load(Tcp("tcp://192.168.0.100:47321"), store);

        first.Entries.Should().ContainSingle(e => e.Id == "remote");
        var problem = first.Problems.Should().ContainSingle().Subject;
        problem.Kind.Should().Be(LanguageServerConfigProblemKind.UnseenCommand);
        problem.Code.Should().Be("UnseenEndpoint");
        problem.Arguments.Should().Equal("remote", "tcp://192.168.0.100:47321");
        problem.Message.Should().Contain("will connect to an address").And.Contain("tcp://192.168.0.100:47321");
        problem.EntryRejected.Should().BeFalse();
        store.IsNewOrChanged("remote", "connect tcp://192.168.0.100:47321").Should().BeFalse();

        Load(Tcp("tcp://192.168.0.100:47321"), store).Problems.Should().BeEmpty();
        Load(Tcp("tcp://192.168.0.101:47321"), store).Problems.Should().ContainSingle(p => p.Code == "UnseenEndpoint");
    }

    [Fact]
    public void A_connect_only_websocket_entry_is_announced_and_a_listener_is_not()
    {
        Load(WebSocket("ws://192.168.0.100:9000/"), Store())
            .Problems.Should().ContainSingle(p => p.Code == "UnseenEndpoint" && p.Arguments[1].Equals("ws://192.168.0.100:9000/"));

        LanguageServerConfigLoader.SeenIdentityOf(new LanguageServerEntry
            { Id = "ws", Transport = "websocket", Endpoint = "ws://localhost:9000/", WebSocketRole = "listen" })
            .Should().BeNull("a listener dials nothing");
    }

    [Fact]
    public void A_command_and_its_connect_address_are_one_identity()
    {
        var store = Store();
        Load(Tcp("tcp://localhost:6005", command: "example-tcp-server"), store)
            .Problems.Should().ContainSingle(p => p.Code == "UnseenCommand"
                && p.Arguments[1].Equals("example-tcp-server; connect tcp://localhost:6005"));

        // The same command, now dialling elsewhere: what it reaches changed, so it's announced again.
        Load(Tcp("tcp://192.168.0.100:6005", command: "example-tcp-server"), store)
            .Problems.Should().ContainSingle(p => p.Code == "UnseenCommand");
    }

    [Theory]
    [InlineData("stdio", null, null)]
    [InlineData("pipe", null, "lsp-pipe")]
    public void An_entry_that_only_launches_keeps_the_identity_it_had(string transport, string? endpoint, string? pipe)
    {
        var entry = new LanguageServerEntry { Id = "s", Transport = transport, Command = "server", Endpoint = endpoint,
            PipeName = pipe, Arguments = new ServerArguments("--stdio", null) };

        LanguageServerConfigLoader.SeenIdentityOf(entry).Should().Be(LanguageServerConfigLoader.LaunchedCommandOf(entry))
            .And.Be("server --stdio", "a command already recorded must stay seen");
    }

    [Theory]
    [InlineData("tcp://0.0.0.0:6005")]
    [InlineData("tcp://192.168.0.100:6005")]
    [InlineData("tcp://[::]:6005")]
    public void A_tcp_listener_off_this_machine_is_refused(string endpoint)
    {
        var result = Load(Tcp(endpoint, role: "listen"), Store());

        result.Entries.Should().NotContain(e => e.Id == "remote");
        result.Problems.Should().ContainSingle(p => p.Code == "Tcp" && p.EntryRejected)
            .Which.Message.Should().Contain("127.0.0.1");
    }

    [Theory]
    [InlineData("tcp://localhost:0")]
    [InlineData("tcp://127.0.0.1:0")]
    [InlineData("tcp://127.0.0.2:0")]
    [InlineData("tcp://[::1]:0")]
    public void A_tcp_listener_on_loopback_is_accepted_and_not_announced(string endpoint)
    {
        var result = Load(Tcp(endpoint, role: "listen"), Store());

        result.Entries.Should().ContainSingle(e => e.Id == "remote");
        result.Problems.Should().BeEmpty();
    }

    [Fact]
    public async Task The_transport_refuses_a_listener_off_this_machine_too()
    {
        await using var transport = new TcpLspTransport("tcp://0.0.0.0:0", TcpRole.Listen,
            NullLogger<TcpLspTransport>.Instance, connectTimeout: TimeSpan.FromSeconds(5));

        var handler = await transport.ConnectAsync(new SystemTextJsonFormatter(), TestContext.Current.CancellationToken);

        handler.Should().BeNull();
        transport.LastFailure.Should().Contain("must be on this machine");
    }

    private static ILanguageServerLaunchConfirmation Answering(bool agree)
    {
        var confirmation = Substitute.For<ILanguageServerLaunchConfirmation>();
        confirmation.ConfirmLaunchAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ReturnsForAnyArgs(agree);
        return confirmation;
    }

    private static LanguageServerEntry ConnectTo(int port) => new()
    {
        Id = "remote", Transport = "tcp", Endpoint = $"tcp://127.0.0.1:{port}", Extensions = [".cls"], LanguageId = "vb6",
    };

    [Fact]
    public async Task A_trial_of_an_unseen_address_asks_first_and_declining_dials_nothing()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var entry = ConnectTo(((IPEndPoint)listener.LocalEndpoint).Port);
        var confirmation = Answering(false);
        var store = Store();

        var result = await new LanguageServerTrial(_logging, store, confirmation).RunAsync(entry, TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(LanguageServerTrialOutcome.Declined);
        await confirmation.Received(1).ConfirmLaunchAsync("remote", "remote", $"connect {entry.Endpoint}", Arg.Any<CancellationToken>());
        await Task.Delay(300, TestContext.Current.CancellationToken);
        listener.Pending().Should().BeFalse("declining must not dial");
        store.IsNewOrChanged("remote", $"connect {entry.Endpoint}").Should().BeTrue();
    }

    [Fact]
    public async Task A_trial_of_an_unseen_address_dials_once_agreed_and_records_it()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var entry = ConnectTo(((IPEndPoint)listener.LocalEndpoint).Port);
        var store = Store();
        var accepted = listener.AcceptTcpClientAsync(TestContext.Current.CancellationToken).AsTask();

        // The listener never answers initialize, so the trial ends Failed; what matters is that it dialled.
        var run = new LanguageServerTrial(_logging, store, Answering(true)).RunAsync(entry, TestContext.Current.CancellationToken);
        using (var client = await accepted.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken))
            client.Close();
        await run;

        new LanguageServerCommandStore(Path.Combine(_dir, "seen.json"))
            .IsNewOrChanged("remote", $"connect {entry.Endpoint}").Should().BeFalse();
    }

    [Fact]
    public void Saving_a_connect_entry_in_the_window_records_its_address()
    {
        var path = Path.Combine(_dir, "lsp-servers.json");
        var store = Store();
        var editor = new LanguageServerEditorStore(path, [], NullLogger<LanguageServerConfigLoader>.Instance, store);
        var entry = new LanguageServerEntry { Id = "remote", Transport = "tcp", Endpoint = "tcp://192.168.0.100:6005" };

        editor.Save(editor.Read(), "{\"version\":1,\"servers\":[" + Tcp("tcp://192.168.0.100:6005") + "]}", entry).Should().BeTrue();

        store.IsNewOrChanged("remote", "connect tcp://192.168.0.100:6005").Should().BeFalse();
    }
}
