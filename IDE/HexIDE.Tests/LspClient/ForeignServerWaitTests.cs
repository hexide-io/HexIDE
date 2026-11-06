using HexIDE.Conversations;

namespace HexIDE.Tests.LspClient;

public class ForeignServerWaitTests
{
    [Theory]
    [InlineData("initialize", false)]
    [InlineData("textDocument/diagnostic", true)]
    [InlineData(null, false)]
    public async Task ANonZeroExitNamesTheProcessAndPendingRequest(string? method, bool abandoned)
    {
        await using var capture = new ConversationLog();
        if (method is not null)
        {
            capture.Record("foreign", ConversationDirection.Sent, ConversationEntryKind.Request,
                method, "1", 0, null);
            if (abandoned) capture.EndRequests("foreign");
        }
        var pending = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

        var waiting = ForeignServerWait.ForDiagnostics(
            pending.Task, capture, "foreign", TimeSpan.FromSeconds(30));
        capture.Record("foreign", ConversationDirection.Local, ConversationEntryKind.Lifecycle,
            null, null, 0, null, "process exited with code 141", fault: true);
        var act = () => waiting.WaitAsync(TimeSpan.FromSeconds(2));

        var failure = (await act.Should().ThrowAsync<InvalidOperationException>()).Which;
        failure.Message.Should().StartWith("Foreign server 'foreign' process exited with code 141");
        failure.Message.Should().Contain(method is null ? "no outstanding request recorded" : $"during request '{method}'");
        failure.Message.Should().Contain("The conversation was:");
        pending.Task.IsCompleted.Should().BeFalse();
    }

    [Fact]
    public async Task AnUnrelatedExitAndACleanExitDoNotReplaceSilence()
    {
        await using var capture = new ConversationLog();
        capture.Record("other", ConversationDirection.Local, ConversationEntryKind.Lifecycle,
            null, null, 0, null, "process exited with code 141", fault: true);
        capture.Record("foreign", ConversationDirection.Local, ConversationEntryKind.Lifecycle,
            null, null, 0, null, "process exited with code 0");
        var pending = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var act = () => ForeignServerWait.ForDiagnostics(
            pending.Task, capture, "foreign", TimeSpan.FromMilliseconds(100));
        await act.Should().ThrowAsync<TimeoutException>();
    }

    [Fact]
    public async Task DiagnosticsStillReachTheCaller()
    {
        await using var capture = new ConversationLog();
        (await ForeignServerWait.ForDiagnostics(
            Task.FromResult(42), capture, "foreign", TimeSpan.FromSeconds(1))).Should().Be(42);
    }
}
