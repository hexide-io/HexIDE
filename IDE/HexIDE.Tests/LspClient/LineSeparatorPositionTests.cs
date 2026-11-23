using System.Globalization;
using System.Text;
using System.Text.Json;
using AvaloniaEdit.Document;
using HexIDE.Bookmarks;
using HexIDE.Debugging;
using HexIDE.Events;
using HexIDE.Forms.ViewModels;
using HexIDE.Localization;
using HexIDE.Lsp;
using HexIDE.Lsp.Messages;
using Microsoft.Extensions.Logging;
using Nerdbank.Streams;
using StreamJsonRpc;

namespace HexIDE.Tests.LspClient;

// Regression guards for https://github.com/hexide-io/HexIDE/issues/5287.
public class LineSeparatorPositionTests : IAsyncDisposable
{
    private const string Source = "Const A = \"x\u2028y\"\r\n' a\u2029b\r\n' c\u0085d\rSub Target()\r\nEnd Sub";
    private readonly List<IAsyncDisposable> _disposables = [];
    private readonly CancellationTokenSource _cts = new(TimeSpan.FromSeconds(30));

    private Task? _serverLoop;

    public async ValueTask DisposeAsync()
    {
        foreach (var d in _disposables.AsEnumerable().Reverse())
        {
            try { await d.DisposeAsync(); } catch { /* teardown is best effort */ }
        }
        await _cts.CancelAsync();
        if (_serverLoop is not null)
        {
            try { await _serverLoop; } catch { /* observe the fake server before releasing its lifetime */ }
        }
        _cts.Dispose();
        GC.SuppressFinalize(this);
    }

    private readonly List<JsonDocument> _received = [];
    private static string? MethodOf(JsonElement frame) => frame.TryGetProperty("method", out var m) ? m.GetString() : null;

    private async Task<VBLspClient> ConnectedAsync()
    {
        var (clientSide, serverSide) = FullDuplexStream.CreatePair();

        _disposables.Add(clientSide);
        _disposables.Add(serverSide);
        _serverLoop = Task.Run(async () =>
        {
            try
            {
                while (!_cts.IsCancellationRequested)
                {
                    var json = await ReadFrameAsync(serverSide, _cts.Token);
                    if (json is null) return;

                    var doc = JsonDocument.Parse(json);
                    lock (_received)
                    {
                        _received.Add(doc);
                    }

                    if (!doc.RootElement.TryGetProperty("method", out var m)) continue;
                    if (!doc.RootElement.TryGetProperty("id", out var id)) continue;   // notification

                    var head = "{\"jsonrpc\":\"2.0\",\"id\":" + id.GetRawText() + ",\"result\":";
                    var reply = m.GetString() switch
                    {
                        "initialize" => head + "{\"capabilities\":{\"definitionProvider\":true,\"renameProvider\":{\"prepareProvider\":true},"
                            + "\"textDocumentSync\":{\"openClose\":true,\"change\":1}}}}",
                        "textDocument/prepareRename" => head
                            + "{\"start\":{\"line\":3,\"character\":4},\"end\":{\"line\":3,\"character\":10}}}",
                        _ => head + "null}"
                    };
                    await WriteFrameAsync(serverSide, reply, _cts.Token);
                }
            }
            catch (OperationCanceledException) { /* the test finished */ }
            catch (IOException) { /* the client hung up */ }
        }, _cts.Token);

        var transport = Substitute.For<ILspTransport>();
        transport.IsAlive.Returns(true);
        transport.ConnectAsync(Arg.Any<IJsonRpcMessageFormatter>(), Arg.Any<CancellationToken>())
            .Returns(ci => Task.FromResult<IJsonRpcMessageHandler?>(
                new LspMessageHandler(clientSide, clientSide, ci.Arg<IJsonRpcMessageFormatter>())));

        var client = new VBLspClient(transport, Substitute.For<ILogger<VBLspClient>>(), "vb6");
        _disposables.Add(client);
        await client.StartAsync();
        return client;
    }

    [AvaloniaFact]
    public void EditorModelEndsLinesOnlyAtCrAndLf()
    {
        var document = new TextDocument(Source);
        var offset = Source.IndexOf("Target", StringComparison.Ordinal);
        document.LineCount.Should().Be(5);
        var location = document.GetLocation(offset);
        location.Line.Should().Be(4);
        location.Column.Should().Be(5);
        document.GetOffset(3 + 1, 4 + 1).Should().Be(offset);
    }

    [AvaloniaFact]
    public async Task WirePositionsAndPrepareRenameUseTheEditorsLineModel()
    {
        var client = await ConnectedAsync();
        var windows = Substitute.For<IWindowManager>();
        windows.InputBox(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>()).Returns(Task.FromResult<string?>(null));
        var bus = Substitute.For<IEventBus>();
        bus.Subscribe<CreateOrNavigateToSubEvent>(Arg.Any<Action<CreateOrNavigateToSubEvent>>()).Returns(Substitute.For<IDisposable>());
        bus.Subscribe<ApplyAllUnsavedChangesEvent>(Arg.Any<Action<ApplyAllUnsavedChangesEvent>>()).Returns(Substitute.For<IDisposable>());
        bus.Subscribe<FormUnloadedEvent>(Arg.Any<Action<FormUnloadedEvent>>()).Returns(Substitute.For<IDisposable>());
        using var editor = new CodeEditorViewModel(windows, Substitute.For<IEditorService>(),
            Substitute.For<IProjectService>(), bus, client, Substitute.For<ISettingsService>(),
            Substitute.For<IStatusBarService>(), Substitute.For<IBookmarkService>(),
            Substitute.For<IBreakpointService>(), Substitute.For<HexIDE.Runtime.Debugging.IDebugController>(),
            Substitute.For<IRunScope>(), Substitute.For<ILocalizationService>());
        var module = TestHelpers.CreateModule(name: "Module1");
        module.RecordOriginalHeader("");
        module.UpdateCode(Source);
        editor.Initialize(module);
        await editor.FlushDocumentAsync(TestContext.Current.CancellationToken);
        var location = editor.Document.GetLocation(Source.IndexOf("Target", StringComparison.Ordinal));
        var position = new Position(location.Line - 1, location.Column - 1);
        await editor.RequestDefinitionAsync(position, TestContext.Current.CancellationToken);
        (await editor.StartRenameAtAsync(position)).Should().BeTrue();
        await windows.Received(1).InputBox(Arg.Any<string>(), Arg.Any<string>(), "Target");
        lock (_received)
        {
            var open = _received.Single(d => MethodOf(d.RootElement) == "textDocument/didOpen").RootElement;
            open.GetProperty("params").GetProperty("textDocument").GetProperty("text").GetString().Should().Be(Source);
            foreach (var method in new[] { "textDocument/definition", "textDocument/prepareRename" })
            {
                var frame = _received.Single(d => MethodOf(d.RootElement) == method).RootElement;
                var sent = frame.GetProperty("params").GetProperty("position");
                sent.GetProperty("line").GetInt32().Should().Be(3);
                sent.GetProperty("character").GetInt32().Should().Be(4);
            }
        }
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
        foreach (var line in Encoding.ASCII.GetString([.. header])
                     .Split("\r\n", StringSplitOptions.RemoveEmptyEntries))
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
        var body = Encoding.UTF8.GetBytes(json);
        var header = Encoding.ASCII.GetBytes(
            $"Content-Length: {body.Length.ToString(CultureInfo.InvariantCulture)}\r\n\r\n");
        await stream.WriteAsync(header, ct);
        await stream.WriteAsync(body, ct);
        await stream.FlushAsync(ct);
    }
}
