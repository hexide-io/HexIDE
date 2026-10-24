using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace HexIDE.TwinBasic.Tests;

public sealed class CompilerRecordingTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static string Fixture => Path.Combine(AppContext.BaseDirectory, "Recordings", "synthetic", "preparation.jsonl");
    private static CompilerRecordingValues Values(string stage = "/tmp/synthetic-stage") => new(new Dictionary<string, byte[]>
    {
        ["{stage}"] = Encoding.UTF8.GetBytes(stage),
        ["{pid}"] = Integer(42),
        ["{port:fs}"] = Integer(1001),
        ["{port:debugger}"] = Integer(1002),
        ["{port:language}"] = Integer(1003),
        ["{port:formImage}"] = Integer(1004),
        ["{port:liveBackup}"] = Integer(1005),
        ["{instance}"] = "synthetic-instance"u8.ToArray()
    });

    [Fact]
    public async Task Scripted_preparation_records_and_replays_the_same_exchange()
    {
        using var pair = await Pair.Create();
        using var writer = new StringWriter();
        pair.Adapter.RecordTo(new(writer, new(1, "synthetic", "Community", "preparation"), Values()), "root");
        var replay = CompilerRecordingReplay.PlayAsync(new StringReader(await File.ReadAllTextAsync(Fixture, Token)),
            new Dictionary<string, CompilerWebSocketTransport> { ["root"] = pair.Peer }, Values(), Token);
        var codec = new CompilerWireCodec();
        using var language = JsonDocument.Parse("{\"language\":\"test\"}");
        using var root = await CompilerRootSession.InitializeAsync(pair.Adapter,
            new([], CompilerWireCodec.ReadLanguageData(language.RootElement)), cancellationToken: Token);
        root.Connection.ProcessId.Should().Be(42);
        root.Connection.ServerId.Should().Be("synthetic-instance");
        // Same scripted load/settings/package/readiness exchange as the preparation wire peer.
        await Send(new CompilerLoadProject("/tmp/synthetic-stage/source.twinproj"), 2);
        codec.ReadSettingsRequest(await Receive()).Should().BeOfType<CompilerSettingsRequest>();
        await Send(new CompilerKeepSettings(), 3);
        var package = codec.ReadPackageRequest(await Receive());
        package.Identifier.Should().Be("包/opaque-id");
        await Send(new CompilerProvidePackage(package, new byte[] { 0, 255, 128, 42 }), 4);
        (await Receive()).Code.Should().Be(123);
        (await Receive()).Payload.Should().Equal(new byte[] { 10 });
        (await Receive()).RequestId.Should().Be(4);
        (await Receive()).RequestId.Should().Be(3);
        await Send(new CompilerProjectsLoaded(), 5);
        (await Receive()).Payload.Should().Equal(new byte[] { 11 });
        await replay;
        writer.ToString().ReplaceLineEndings().Should().Be((await File.ReadAllTextAsync(Fixture, Token)).ReplaceLineEndings());

        Task Send(CompilerProjectRequest request, int id) => pair.Adapter.SendAsync(
            codec.EncodeProjectRequest(id, request, 8192), WebSocketMessageType.Binary, Token);
        async Task<CompilerControlMessage> Receive() => codec.DecodeControl((await pair.Adapter.ReceiveAsync(Token)).Bytes);
    }

    [Theory]
    [InlineData("root")]
    [InlineData("fs")]
    [InlineData("language")]
    [InlineData("debugger")]
    public async Task Replay_compares_json_structurally_and_substitutes_run_values(string channel)
    {
        using var pair = await Pair.Create();
        using var writer = new StringWriter();
        var recorder = new CompilerRecording(writer, new(1, "synthetic", "Community", "json"), Values());
        recorder.Record(channel, "send", new(WebSocketMessageType.Text, "{\"path\":\"/tmp/synthetic-stage/x\",\"a\":1}"u8.ToArray()));
        recorder.Record(channel, "receive", new(WebSocketMessageType.Binary, new byte[] { 0, 255, 128 }));
        writer.ToString().Should().Contain("{stage}").And.NotContain("/tmp/synthetic-stage");
        var replay = CompilerRecordingReplay.PlayAsync(new StringReader(writer.ToString()),
            new Dictionary<string, CompilerWebSocketTransport> { [channel] = pair.Peer }, Values("/tmp/other-stage"), Token);
        await pair.Adapter.SendAsync("{ \"a\":1, \"path\":\"/tmp/other-stage/x\" }"u8.ToArray(), WebSocketMessageType.Text, Token);
        (await pair.Adapter.ReceiveAsync(Token)).Bytes.Should().Equal(new byte[] { 0, 255, 128 });
        await replay;
    }

    [Fact]
    public async Task Json_run_values_preserve_escaped_paths_and_numeric_types()
    {
        using var pair = await Pair.Create();
        using var writer = new StringWriter();
        var firstStage = "C:" + "\\Users\\synthetic\\stage";
        var secondStage = "D:" + "\\Users\\replay\\stage";
        var first = JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object>
        {
            ["path"] = firstStage + "\\source", ["pid"] = 42, ["pidText"] = "42", ["port"] = 1001, ["portText"] = "localhost:1001", ["instance"] = "synthetic-instance"
        });
        new CompilerRecording(writer, new(1, "synthetic", "Community", "json-values"), Values(firstStage))
            .Record("root", "send", new(WebSocketMessageType.Text, first));
        var text = writer.ToString();
        text.Should().Contain("{stage}").And.Contain("{pid}").And.Contain("{port:fs}").And.Contain("{instance}");
        var replay = CompilerRecordingReplay.PlayAsync(new StringReader(text),
            new Dictionary<string, CompilerWebSocketTransport> { ["root"] = pair.Peer }, Values(secondStage), Token);
        var second = JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object>
        {
            ["path"] = secondStage + "\\source", ["pid"] = 42, ["pidText"] = "42", ["port"] = 1001, ["portText"] = "localhost:1001", ["instance"] = "synthetic-instance"
        });
        await pair.Adapter.SendAsync(second, WebSocketMessageType.Text, Token);
        await replay;
    }

    [Fact]
    public async Task Replay_stops_at_first_difference_with_sequence_and_both_texts()
    {
        using var pair = await Pair.Create();
        using var writer = new StringWriter();
        new CompilerRecording(writer, new(1, "synthetic", "Community", "mismatch"), Values())
            .Record("root", "send", new(WebSocketMessageType.Text, "{\"a\":1}"u8.ToArray()));
        var replay = CompilerRecordingReplay.PlayAsync(new StringReader(writer.ToString()),
            new Dictionary<string, CompilerWebSocketTransport> { ["root"] = pair.Peer }, Values(), Token);
        await pair.Adapter.SendAsync("{\"a\":2}"u8.ToArray(), WebSocketMessageType.Text, Token);
        await FluentActions.Awaiting(() => replay).Should().ThrowExactlyAsync<InvalidDataException>()
            .WithMessage("Frame 1 differs. Expected {\"a\":1}; actual {\"a\":2}.");
    }

    [Fact]
    public async Task Missing_substitution_is_refused_before_sending_a_request()
    {
        using var pair = await Pair.Create();
        using var writer = new StringWriter();
        new CompilerRecording(writer, new(1, "synthetic", "Community", "missing"), Values())
            .Record("root", "send", new(WebSocketMessageType.Text, "{\"path\":\"{stage}\"}"u8.ToArray()));
        await FluentActions.Awaiting(() => CompilerRecordingReplay.PlayAsync(new StringReader(writer.ToString()),
            new Dictionary<string, CompilerWebSocketTransport> { ["root"] = pair.Peer }, new(new Dictionary<string, byte[]>()), Token))
            .Should().ThrowExactlyAsync<InvalidDataException>().WithMessage("Unsubstituted placeholder at frame 1.");
    }

    [Theory]
    [InlineData("drive")]
    [InlineData("home")]
    [InlineData("licence")]
    public void Redaction_checks_text_and_decoded_binary(string kind)
    {
        var secret = kind switch
        {
            "drive" => "C:" + "\\Users\\someone\\project",
            "home" => "/" + "home/someone/project",
            _ => string.Join('-', Enumerable.Repeat("ABCDE", 5))
        };
        foreach (var type in new[] { WebSocketMessageType.Binary, WebSocketMessageType.Text })
        {
            using var writer = new StringWriter();
            var recorder = new CompilerRecording(writer, new(1, "synthetic", "Community", "redaction"), Values());
            FluentActions.Invoking(() => recorder.Record("root", "send", new(type, Encoding.UTF8.GetBytes(secret))))
                .Should().ThrowExactly<InvalidDataException>();
            writer.ToString().Should().NotContain(secret);
        }
    }

    [Fact]
    public void Every_recording_in_the_tree_passes_redaction()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "IDE", "HexIDE.TwinBasic.Tests"))) directory = directory.Parent;
        directory.Should().NotBeNull();
        var files = Directory.GetFiles(directory!.FullName, "*.jsonl", SearchOption.AllDirectories)
            .Where(x => !x.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar) && !x.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) &&
                        !x.Contains(Path.DirectorySeparatorChar + "artifacts" + Path.DirectorySeparatorChar) &&
                        !x.Contains(Path.DirectorySeparatorChar + ".git" + Path.DirectorySeparatorChar)).ToArray();
        files.Should().NotBeEmpty();
        foreach (var file in files)
        foreach (var line in File.ReadLines(file)) CompilerRecordingReplay.CheckRedaction(line);
    }

    private static byte[] Integer(int value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
        return bytes;
    }

    private sealed class Pair(TcpClient client, TcpClient server) : IDisposable
    {
        public CompilerWebSocketTransport Adapter { get; } = new(WebSocket.CreateFromStream(client.GetStream(), false, null, Timeout.InfiniteTimeSpan), new(MessageTimeout: TimeSpan.FromSeconds(2)));
        public CompilerWebSocketTransport Peer { get; } = new(WebSocket.CreateFromStream(server.GetStream(), true, null, Timeout.InfiniteTimeSpan), new(MessageTimeout: TimeSpan.FromSeconds(2)));
        public static async Task<Pair> Create()
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var client = new TcpClient();
            await client.ConnectAsync((IPEndPoint)listener.LocalEndpoint, Token);
            return new(client, await listener.AcceptTcpClientAsync(Token));
        }
        public void Dispose() { Adapter.Dispose(); Peer.Dispose(); client.Dispose(); server.Dispose(); }
    }
}
