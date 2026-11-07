using System.Globalization;
using System.Text;
using System.Text.Json;
using HexIDE.IDE;
using HexIDE.Lsp;
using Microsoft.Extensions.Logging;
using Nerdbank.Streams;
using StreamJsonRpc;

namespace HexIDE.Tests.LspClient;

public class ShowMessageRequestWireTests : IAsyncDisposable
{
    private readonly CancellationTokenSource _timeout = new(TimeSpan.FromSeconds(10));
    private readonly Func<HexIDE.Lsp.Messages.ShowMessageRequestParams, CancellationToken, Task<HexIDE.Lsp.Messages.MessageActionItem?>> _show =
        Substitute.For<Func<HexIDE.Lsp.Messages.ShowMessageRequestParams, CancellationToken, Task<HexIDE.Lsp.Messages.MessageActionItem?>>>();
    private Stream? _peer;
    private VBLspClient? _client;
    private CancellationToken Token => _timeout.Token;

    private async Task<JsonElement> ConnectAsync()
    {

        var (local, peer) = FullDuplexStream.CreatePair();
        _peer = peer;
        var transport = Substitute.For<ILspTransport>();
        transport.IsAlive.Returns(true);
        transport.LeaveServerRunning.Returns(true);
        transport.ConnectAsync(Arg.Any<IJsonRpcMessageFormatter>(), Arg.Any<CancellationToken>())
            .Returns(ci => Task.FromResult<IJsonRpcMessageHandler?>(
                new HeaderDelimitedMessageHandler(local, local, ci.Arg<IJsonRpcMessageFormatter>())));
        _client = new VBLspClient(transport, Substitute.For<ILogger<VBLspClient>>(), "vb6", showMessageRequest: _show);
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
    public async Task InitializeDeclaresMessageActionItems()
    {
        var frame = await ConnectAsync();
        frame.GetProperty("params").GetProperty("capabilities").GetProperty("window")
            .GetProperty("showMessage").GetProperty("messageActionItem")
            .GetProperty("additionalPropertiesSupport").GetBoolean().Should().BeTrue();
    }

    private Task RequestAsync(int id = 1) => WriteFrameAsync(_peer!,
        "{\"jsonrpc\":\"2.0\",\"id\":" + id + """
        ,"method":"window/showMessageRequest","params":{"type":3,"message":"Choose",
        "actions":[{"title":"First","extra":42},{"title":"Second"}]}}
        """, Token);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AnswersChosenItemOrNullOnDismissal(bool dismissed)
    {
        _show(Arg.Any<HexIDE.Lsp.Messages.ShowMessageRequestParams>(), Arg.Any<CancellationToken>())
            .Returns(ci => Task.FromResult(dismissed ? null : ci.Arg<HexIDE.Lsp.Messages.ShowMessageRequestParams>().Actions![0]));
        await ConnectAsync();
        await RequestAsync();
        var frame = await ReadAsync();
        frame.GetProperty("id").GetInt32().Should().Be(1);
        frame.TryGetProperty("result", out var result).Should().BeTrue(frame.GetRawText());
        if (dismissed) result.ValueKind.Should().Be(JsonValueKind.Null);
        else
        {
            result.GetProperty("title").GetString().Should().Be("First");
            result.GetProperty("extra").GetInt32().Should().Be(42);
        }
    }

    [Fact]
    public async Task PendingPromptDoesNotHoldUpDiagnostics()
    {
        var entered = new TaskCompletionSource();
        var answer = new TaskCompletionSource<HexIDE.Lsp.Messages.MessageActionItem?>();
        _show(Arg.Any<HexIDE.Lsp.Messages.ShowMessageRequestParams>(), Arg.Any<CancellationToken>())
            .Returns(_ => { entered.SetResult(); return answer.Task; });
        await ConnectAsync();
        var diagnostics = new TaskCompletionSource();
        _client!.DiagnosticsPublished += (_, _) => diagnostics.TrySetResult();
        await RequestAsync();
        await entered.Task.WaitAsync(Token);
        await WriteFrameAsync(_peer!, """
            {"jsonrpc":"2.0","method":"textDocument/publishDiagnostics",
             "params":{"uri":"untitled:P/M.bas","diagnostics":[]}}
            """, Token);
        await diagnostics.Task.WaitAsync(Token);
        answer.SetResult(null);
        (await ReadAsync()).GetProperty("result").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task CancellationClosesPromptAndAnswersRequestCancelled()
    {
        var entered = new TaskCompletionSource();
        var closed = new TaskCompletionSource();
        _show(Arg.Any<HexIDE.Lsp.Messages.ShowMessageRequestParams>(), Arg.Any<CancellationToken>())
            .Returns(async ci =>
            {
                entered.SetResult();
                try { await Task.Delay(Timeout.Infinite, ci.Arg<CancellationToken>()); }
                finally { closed.SetResult(); }
                return null;
            });
        await ConnectAsync();
        await RequestAsync();
        await entered.Task.WaitAsync(Token);
        await WriteFrameAsync(_peer!, """
            {"jsonrpc":"2.0","method":"$/cancelRequest","params":{"id":1}}
            """, Token);
        await closed.Task.WaitAsync(Token);
        (await ReadAsync()).GetProperty("error").GetProperty("code").GetInt32().Should().Be(-32800);
    }

    [Fact]
    public async Task TwoPromptsShowOneAfterTheOther()
    {
        var entered = new TaskCompletionSource();
        var first = new TaskCompletionSource<HexIDE.Lsp.Messages.MessageActionItem?>();
        var second = new TaskCompletionSource<HexIDE.Lsp.Messages.MessageActionItem?>();
        var count = 0;
        var queue = new ServerMessagePrompt((_, _, _, _) =>
        {
            var pending = Interlocked.Increment(ref count) == 1 ? first.Task : second.Task;
            entered.TrySetResult();
            return Task.FromResult<Task<HexIDE.Lsp.Messages.MessageActionItem?>?>(pending);
        });
        _show(Arg.Any<HexIDE.Lsp.Messages.ShowMessageRequestParams>(), Arg.Any<CancellationToken>())
            .Returns(ci => queue.ShowAsync("Server", ci.Arg<HexIDE.Lsp.Messages.ShowMessageRequestParams>(), ci.Arg<CancellationToken>()));
        await ConnectAsync();
        await RequestAsync();
        await entered.Task.WaitAsync(Token);
        await RequestAsync(2);
        // A following configuration request is a wire barrier: the second prompt has reached the receiver.
        await WriteFrameAsync(_peer!, """
            {"jsonrpc":"2.0","id":3,"method":"workspace/configuration","params":{"items":[]}}
            """, Token);
        (await ReadAsync()).GetProperty("id").GetInt32().Should().Be(3);
        count.Should().Be(1);
        first.SetResult(null);
        (await ReadAsync()).GetProperty("id").GetInt32().Should().Be(1);
        second.SetResult(null);
        (await ReadAsync()).GetProperty("id").GetInt32().Should().Be(2);
        count.Should().Be(2);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RunningProgramOrModalDefersPromptWhileMessagesFlow(bool running)
    {
        var held = true;
        var checkedState = new TaskCompletionSource();
        var shown = new TaskCompletionSource();
        var queue = new ServerMessagePrompt((_, _, _, _) =>
        {
            var blocked = ServerMessagePrompt.IsBlocked(running && held, [], !running && held);
            checkedState.TrySetResult();
            if (blocked) return Task.FromResult<Task<HexIDE.Lsp.Messages.MessageActionItem?>?>(null);
            shown.TrySetResult();
            return Task.FromResult<Task<HexIDE.Lsp.Messages.MessageActionItem?>?>(Task.FromResult<HexIDE.Lsp.Messages.MessageActionItem?>(null));
        });
        _show(Arg.Any<HexIDE.Lsp.Messages.ShowMessageRequestParams>(), Arg.Any<CancellationToken>())
            .Returns(ci => queue.ShowAsync("Server", ci.Arg<HexIDE.Lsp.Messages.ShowMessageRequestParams>(), ci.Arg<CancellationToken>()));
        await ConnectAsync();
        await RequestAsync();
        await checkedState.Task.WaitAsync(Token);
        await WriteFrameAsync(_peer!, """
            {"jsonrpc":"2.0","id":2,"method":"workspace/configuration","params":{"items":[]}}
            """, Token);
        (await ReadAsync()).GetProperty("id").GetInt32().Should().Be(2);
        shown.Task.IsCompleted.Should().BeFalse();
        held = false;
        (await ReadAsync()).GetProperty("id").GetInt32().Should().Be(1);
        shown.Task.IsCompleted.Should().BeTrue();
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
