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

    private async Task<JsonElement> ConnectAsync(JsonElement? serverSettings = null, Func<Task>? start = null)
    {
        _settings.TabWidth.Returns(2);
        var (local, peer) = FullDuplexStream.CreatePair();
        _peer = peer;
        var transport = Substitute.For<ILspTransport>();
        transport.IsAlive.Returns(true);
        transport.LeaveServerRunning.Returns(true);
        transport.ConnectAsync(Arg.Any<IJsonRpcMessageFormatter>(), Arg.Any<CancellationToken>())
            .Returns(ci => Task.FromResult<IJsonRpcMessageHandler?>(
                new LspMessageHandler(local, local, ci.Arg<IJsonRpcMessageFormatter>())));
        _client = new VBLspClient(transport, Substitute.For<ILogger<VBLspClient>>(), "vb6", settings: _settings, serverSettings: serverSettings);
        var starting = start is null ? _client.StartAsync(Token) : start();
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

    [Fact]
    public async Task ServerSettingsAnswerSectionsOnTheWire()
    {
        using var settings = JsonDocument.Parse("""{"rdcore":{"Analysis":{"DisabledDiagnostics":["example"]}},"editor":{"tabSize":99}}""");
        await ConnectAsync(settings.RootElement);
        await WriteFrameAsync(_peer!, """
            {"jsonrpc":"2.0","id":42,"method":"workspace/configuration","params":{"items":[
              {"section":"rdcore"},{"section":"rdcore.Analysis"},{"section":"rdcore.missing"},
              {"section":"rdcore.Analysis.DisabledDiagnostics.child"},{"scopeUri":"file:///M.bas","section":"editor.tabSize"},
              {},{"section":""}]}}
            """, Token);
        var result = (await ReadAsync()).GetProperty("result");
        result[0].GetProperty("Analysis").GetProperty("DisabledDiagnostics")[0].GetString().Should().Be("example");
        result[1].GetProperty("DisabledDiagnostics")[0].GetString().Should().Be("example");
        result[2].ValueKind.Should().Be(JsonValueKind.Null);
        result[3].ValueKind.Should().Be(JsonValueKind.Null);
        result[4].GetInt32().Should().Be(2);
        JsonElement.DeepEquals(result[5], settings.RootElement).Should().BeTrue();
        JsonElement.DeepEquals(result[6], settings.RootElement).Should().BeTrue();
    }

    [Fact]
    public async Task EmptyObjectAndRemovalRemainDistinctAndUnchangedSettingsDoNotNotify()
    {
        await ConnectAsync();
        using var empty = JsonDocument.Parse("{}");
        await _client!.UpdateServerSettingsAsync(empty.RootElement);
        (await ReadAsync()).GetProperty("params").GetProperty("settings").ValueKind.Should().Be(JsonValueKind.Object);
        await _client.UpdateServerSettingsAsync(empty.RootElement);
        const string request = """{"jsonrpc":"2.0","id":45,"method":"workspace/configuration","params":{"items":[{}]}}""";
        await WriteFrameAsync(_peer!, request, Token);
        (await ReadAsync()).GetProperty("result")[0].ValueKind.Should().Be(JsonValueKind.Object);
        await _client.UpdateServerSettingsAsync(null);
        (await ReadAsync()).GetProperty("method").GetString().Should().Be("workspace/didChangeConfiguration");
        await WriteFrameAsync(_peer!, request, Token);
        (await ReadAsync()).GetProperty("result")[0].ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SettingsChangeNotifiesOnlyItsServerAndChangesSubsequentAnswers(bool save)
    {
        await using var other = new WorkspaceConfigurationWireTests();
        var registry = new LspClientRegistry([
            new LanguageServerRegistration("one", "One", [], "vb6", () => _client!),
            new LanguageServerRegistration("two", "Two", [], "vb6", () => other._client!)
        ], Substitute.For<ILogger<LspClientRegistry>>());
        await ConnectAsync(start: () => registry.RestartAsync("one", Token));
        await other.ConnectAsync(start: () => registry.RestartAsync("two", Token));
        using var updated = JsonDocument.Parse("""{"rdcore":{"enabled":false}}""");
        var directory = Path.Combine(Path.GetTempPath(), "hexide-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "lsp-servers.json");
        try
        {
            var store = new LanguageServerEditorStore(path, [new LanguageServerEntry
            {
                Id = "one", Extensions = [".bas"], LanguageId = "vb6", Transport = "stdio", Command = "example",
            }], Substitute.For<ILogger<LanguageServerConfigLoader>>(), new LanguageServerCommandStore(Path.Combine(directory, "seen.json")));
            store.Reloaded += async entries =>
            {
                foreach (var entry in entries) await registry.UpdateServerSettingsAsync(entry.Id!, entry.Settings);
            };
            var original = store.Read();
            var changed = store.Change(original, null, new Dictionary<string, string>
            {
                ["id"] = "\"one\"", ["settings"] = updated.RootElement.GetRawText(),
            });
            if (save) store.Save(original, changed, store.Preview(changed).Entries.Single()).Should().BeTrue();
            else
            {
                File.WriteAllText(path, changed);
                store.Load();
            }
        }
        finally { Directory.Delete(directory, recursive: true); }
        var frame = await ReadAsync();
        frame.GetProperty("method").GetString().Should().Be("workspace/didChangeConfiguration");
        JsonElement.DeepEquals(frame.GetProperty("params").GetProperty("settings"), updated.RootElement).Should().BeTrue();
        // A request/reply on the untouched connection is a wire barrier: no configuration notification precedes it.
        const string request = """{"jsonrpc":"2.0","id":44,"method":"workspace/configuration","params":{"items":[{"section":"rdcore"}]}}""";
        await WriteFrameAsync(other._peer!, request, Token);
        (await other.ReadAsync()).GetProperty("result")[0].ValueKind.Should().Be(JsonValueKind.Null);
        await WriteFrameAsync(_peer!, request, Token);
        (await ReadAsync()).GetProperty("result")[0].GetProperty("enabled").GetBoolean().Should().BeFalse();
        await registry.DisposeAsync();
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
