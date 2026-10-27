using System.Text.Json;
using HexIDE.Conversations;
using HexIDE.Lsp.Messages;
using StreamJsonRpc;

namespace HexIDE.Tests.LspClient;

public partial class PullDiagnosticsReportKindTests
{
    private sealed class RetryServer(int code, bool? retrigger)
    {
        private int _asked;
        public int TimesAsked => Volatile.Read(ref _asked);

        [JsonRpcMethod("initialize", UseSingleObjectParameterDeserialization = true)]
        public JsonElement Initialize(JsonElement _) => JsonDocument.Parse("""
            {"capabilities":{"textDocumentSync":{"openClose":true,"change":1},"diagnosticProvider":true}}
            """).RootElement.Clone();

        [JsonRpcMethod("textDocument/diagnostic", UseSingleObjectParameterDeserialization = true)]
        public JsonElement Diagnostic(JsonElement _)
        {
            if (Interlocked.Increment(ref _asked) == 1)
                throw new LocalRpcException("try again")
                {
                    ErrorCode = code,
                    ErrorData = JsonDocument.Parse(retrigger == false
                        ? "{\"retriggerRequest\":false}"
                        : retrigger == true ? "{\"retriggerRequest\":true}" : "{}").RootElement.Clone()
                };
            return JsonDocument.Parse("""
                {"kind":"full","items":[{"range":{"start":{"line":0,"character":0},"end":{"line":0,"character":1}},"message":"retried"}]}
                """).RootElement.Clone();
        }
    }

    [Theory]
    [InlineData(-32801, null)]
    [InlineData(-32802, null)]
    [InlineData(-32802, true)]
    public async Task ATransientDiagnosticFailureIsAskedAgain(int code, bool? retrigger)
    {
        await using var capture = new ConversationLog();
        var server = new RetryServer(code, retrigger);
        var sut = ClientTalkingTo(server, capture);
        var report = new TaskCompletionSource<PublishDiagnosticsParams>(TaskCreationOptions.RunContinuationsAsynchronously);
        sut.DiagnosticsPublished += (_, p) => report.TrySetResult(p);
        await sut.StartAsync(TestContext.Current.CancellationToken);
        await sut.OpenDocumentAsync(Uri, "x", TestContext.Current.CancellationToken);

        var result = await report.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        result.Diagnostics.Should().ContainSingle().Which.Message.Should().Be("retried");
        server.TimesAsked.Should().Be(2);
        await capture.DrainAsync();
        capture.Snapshot("scripted.pull").Where(e =>
            e.Method == "textDocument/diagnostic" && e.Kind == ConversationEntryKind.Request
            && e.Direction == ConversationDirection.Sent).Should().HaveCount(2);
    }

    [Fact]
    public async Task AChangeStartsAFreshPullAndStopsTheOldRetry()
    {
        var server = new RetryServer(-32801, null);
        var sut = ClientTalkingTo(server);
        await sut.StartAsync(TestContext.Current.CancellationToken);
        await sut.OpenDocumentAsync(Uri, "x", TestContext.Current.CancellationToken);
        await Until(() => server.TimesAsked == 1, "the first request must reach the server");
        await sut.ChangeDocumentAsync(Uri, 2, "y", TestContext.Current.CancellationToken);
        await Until(() => server.TimesAsked >= 2, "the change must start a fresh pull");
        await Task.Delay(800, TestContext.Current.CancellationToken);
        server.TimesAsked.Should().Be(2);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ARetryStopsWhenRefusedOrClosed(bool close)
    {
        var server = new RetryServer(close ? -32801 : -32802, false);
        var sut = ClientTalkingTo(server);
        await sut.StartAsync(TestContext.Current.CancellationToken);
        await sut.OpenDocumentAsync(Uri, "x", TestContext.Current.CancellationToken);
        await Until(() => server.TimesAsked == 1, "the first request must reach the server");
        if (close) await sut.CloseDocumentAsync(Uri, TestContext.Current.CancellationToken);
        await Task.Delay(800, TestContext.Current.CancellationToken);
        server.TimesAsked.Should().Be(1);
    }
}
