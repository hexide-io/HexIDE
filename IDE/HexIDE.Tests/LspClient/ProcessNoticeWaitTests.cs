namespace HexIDE.Tests.LspClient;

public class ProcessNoticeWaitTests
{
    [Fact]
    public async Task ACallbackScheduledAfterThirtySecondsStillCompletesTheWait()
    {
        var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var waiting = ProcessNoticeWait.ForAsync(signal.Task, "the child stderr echo",
            TestContext.Current.CancellationToken);

        await Task.Delay(TimeSpan.FromSeconds(31), TestContext.Current.CancellationToken);
        signal.SetResult();
        await waiting;
        signal.Task.IsCompletedSuccessfully.Should().BeTrue();
    }

    [Fact]
    public async Task ATimeoutNamesTheCallbackAndExplainsTheSchedulingAmbiguity()
    {
        var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Func<Task> waiting = () => ProcessNoticeWait.ForAsync(signal.Task, "the child stderr echo",
            TestContext.Current.CancellationToken, TimeSpan.Zero);

        await waiting.Should().ThrowAsync<TimeoutException>()
            .WithMessage("*the child stderr echo*may not have been scheduled*does not establish a missing exit code*");
    }

    [Fact]
    public async Task ATimeoutListsWhatDidArrive()
    {
        var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var seen = new List<HexIDE.Lsp.TransportNotice>
        {
            new(HexIDE.Lsp.TransportNoticeKind.StandardError, "one"),
            new(HexIDE.Lsp.TransportNoticeKind.Lifecycle, "process exited with code 3", Fault: true),
        };
        var gate = new Lock();
        Func<Task> waiting = () => ProcessNoticeWait.ForAsync(signal.Task, "the exit-code notice",
            TestContext.Current.CancellationToken, TimeSpan.Zero, () => ProcessNoticeWait.Describe(seen, gate));

        await waiting.Should().ThrowAsync<TimeoutException>()
            .WithMessage("*Received meanwhile: StandardError 'one'; Lifecycle 'process exited with code 3'");
    }
}
