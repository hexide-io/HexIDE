using System.Globalization;
using System.Text;
using System.Text.Json;
using HexIDE.IDE;
using HexIDE.Lsp;
using Microsoft.Extensions.Logging;
using Nerdbank.Streams;
using StreamJsonRpc;

namespace HexIDE.Tests.LspClient;

public class WorkspaceConfigurationWireTests : IAsyncDisposable
{
    private readonly CancellationTokenSource _timeout = new(TimeSpan.FromSeconds(10));
    private readonly ISettingsService _settings = Substitute.For<ISettingsService>();
    private Stream? _peer;
    private VBLspClient? _client;
    private CancellationToken Token => _timeout.Token;

    private async Task<JsonElement> ConnectAsync()
    {
        _settings.TabWidth.Returns(2);
        var (local, peer) = FullDuplexStream.CreatePair();
        _peer = peer;
        var transport = Substitute.For<ILspTransport>();
        transport.IsAlive.Returns(true);
        transport.LeaveServerRunning.Returns(true);
        transport.ConnectAsync(Arg.Any<IJsonRpcMessageFormatter>(), Arg.Any<CancellationToken>())
            .Returns(ci => Task.FromResult<IJsonRpcMessageHandler?>(
                new HeaderDelimitedMessageHandler(local, local, ci.Arg<IJsonRpcMessageFormatter>())));
        _client = new VBLspClient(transport, Substitute.For<ILogger<VBLspClient>>(), "vb6", settings: _settings);
        var starting = _client.StartAsync(Token);
        var initialize = await ReadAsync();
        await WriteFrameAsync(peer, "{\"jsonrpc\":\"2.0\",\"id\":" + initialize.GetProperty("id").GetRawText()
            + ",\"result\":{\"capabilities\":{}}}", Token);
        await starting;
        (await ReadAsync()).GetProperty("method").GetString().Should().Be("initialized");
        return initialize;
    }

    private async Task<JsonElement> ReadAsync()
    {
        using var document = JsonDocument.Parse((await ReadFrameAsync(_peer!, Token))!);
        return document.RootElement.Clone();
    }

    [Fact]
    public async Task InitializeDeclaresWorkspaceConfiguration()
    {
        var frame = await ConnectAsync();
        frame.GetProperty("params").GetProperty("capabilities").GetProperty("workspace")
            .GetProperty("configuration").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task ConfigurationReturnsTabWidthAndNullForUnknownItemsInOrder()
    {
        await ConnectAsync();
        await WriteFrameAsync(_peer!, """
            {"jsonrpc":"2.0","id":"config","method":"workspace/configuration","params":{"items":[
              {"section":"unknown"},{"scopeUri":"file:///M.bas","section":"editor.tabSize"},{},
              {"section":"editor.tabSize"}]}}
            """, Token);
        var frame = await ReadAsync();
        frame.GetProperty("id").GetString().Should().Be("config");
        frame.TryGetProperty("error", out _).Should().BeFalse();
        frame.GetProperty("result").EnumerateArray().Select(e => e.GetRawText())
            .Should().Equal("null", "2", "null", "2");
    }

    [Fact]
    public async Task TabWidthChangesNotifyTheServerAndConfigurationReadsTheNewValue()
    {
        await ConnectAsync();
        _settings.TabWidth.Returns(8);
        _settings.PropertyChanged += Raise.Event<PropertyChangedEventHandler>(_settings,
            new PropertyChangedEventArgs(nameof(ISettingsService.TabWidth)));
        var frame = await ReadAsync();
        frame.GetProperty("method").GetString().Should().Be("workspace/didChangeConfiguration");
        frame.TryGetProperty("id", out _).Should().BeFalse();
        frame.GetProperty("params").GetProperty("settings").ValueKind.Should().Be(JsonValueKind.Object);
        await WriteFrameAsync(_peer!, """
            {"jsonrpc":"2.0","id":9,"method":"workspace/configuration","params":{"items":[{"section":"editor.tabSize"}]}}
            """, Token);
        (await ReadAsync()).GetProperty("result")[0].GetInt32().Should().Be(8);
    }

    public async ValueTask DisposeAsync()
    {
        if (_client is not null) await _client.DisposeAsync();
        if (_peer is not null) await _peer.DisposeAsync();
        _timeout.Dispose();
    }

    private static async Task<string?> ReadFrameAsync(Stream stream, CancellationToken ct)
    {
        var header = new List<byte>(64);
        var one = new byte[1];

        while (true)
        {
            if (await stream.ReadAsync(one.AsMemory(0, 1), ct) == 0) return null;
            header.Add(one[0]);
            if (header.Count >= 4
                && header[^4] == (byte)'\r' && header[^3] == (byte)'\n'
                && header[^2] == (byte)'\r' && header[^1] == (byte)'\n')
            {
                break;
            }
        }

        var length = 0;
        foreach (var line in Encoding.ASCII.GetString([.. header]).Split("\r\n", StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
            {
                length = int.Parse(line["Content-Length:".Length..].Trim(), CultureInfo.InvariantCulture);
            }
        }

        if (length <= 0) return null;

        var body = new byte[length];
        var read = 0;
        while (read < length)
        {
            var n = await stream.ReadAsync(body.AsMemory(read, length - read), ct);
            if (n == 0) return null;
            read += n;
        }

        return Encoding.UTF8.GetString(body);
    }

    private static async Task WriteFrameAsync(Stream stream, string json, CancellationToken ct)
    {
        // Collapse the interpolated literals' incidental newlines; the header must state the real length.
        var compact = JsonSerializer.Serialize(JsonDocument.Parse(json).RootElement);
        var body = Encoding.UTF8.GetBytes(compact);
        await stream.WriteAsync(Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n"), ct);
        await stream.WriteAsync(body, ct);
        await stream.FlushAsync(ct);
    }
}
