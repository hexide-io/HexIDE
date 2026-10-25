using HexIDE.Infrastructure;

namespace HexIDE.Tests.Infrastructure;

public sealed class ForcedExitCleanupTests
{
    [Fact]
    public void Loader_is_disposed_before_exit_closes_consent_dialogs()
    {
        var loader = Substitute.For<IDisposable>();
        using var cancellation = new CancellationTokenSource();
        var context = Substitute.For<IDisposable>();
        var events = new List<string>();
        loader.When(x => x.Dispose()).Do(_ => events.Add("loader"));
        cancellation.Token.Register(() => events.Add("cancel"));
        context.When(x => x.Dispose()).Do(_ => events.Add("context"));

        ForcedExitCleanup.Run(loader, cancellation, context, 3,
            _ => events.Add("exit"), () => { });

        events.Should().Equal("loader", "cancel", "context", "exit");
    }

    [Theory]
    [InlineData("loader")]
    [InlineData("cancel")]
    [InlineData("context")]
    public void Cleanup_failure_still_exits_with_requested_code(string failingStep)
    {
        var loader = Substitute.For<IDisposable>();
        using var cancellation = new CancellationTokenSource();
        var context = Substitute.For<IDisposable>();
        if (failingStep == "loader")
            loader.When(x => x.Dispose()).Do(_ => throw new InvalidOperationException());
        if (failingStep == "cancel")
            cancellation.Token.Register(() => throw new InvalidOperationException());
        if (failingStep == "context")
            context.When(x => x.Dispose()).Do(_ => throw new InvalidOperationException());
        var exitCodes = new List<int>();

        ForcedExitCleanup.Run(loader, cancellation, context, 3, exitCodes.Add, () => { });

        exitCodes.Should().Equal(3);
    }

    [Fact]
    public void Log_closes_after_exit_has_closed_windows()
    {
        var loader = Substitute.For<IDisposable>();
        using var cancellation = new CancellationTokenSource();
        var events = new List<string>();

        ForcedExitCleanup.Run(loader, cancellation, Substitute.For<IDisposable>(), 3,
            _ => events.Add("exit"), () => events.Add("log"));

        events.Should().Equal("exit", "log");
    }
}
