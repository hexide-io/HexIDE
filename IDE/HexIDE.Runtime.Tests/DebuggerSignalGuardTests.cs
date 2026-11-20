using System;
using System.Threading.Tasks;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// The guard's whole value is what it says when it fires, so that is what is asserted here. A diagnostic
/// nobody has ever seen produce output is a diagnostic you find out is wrong at the worst moment.
/// </summary>
public class DebuggerSignalGuardTests
{
    [Fact]
    public async Task ASignalThatNeverArrives_NamesItselfItsTestAndTheRunnersLoad()
    {
        var neverArrives = new TaskCompletionSource<int>().Task;

        var thrown = await FluentActions
            .Awaiting(() => neverArrives.Guarded(TimeSpan.FromMilliseconds(50)))
            .Should().ThrowAsync<TimeoutException>();

        // The expression text is the point: a debugger test awaits three or four signals, and a bare
        // TimeoutException leaves you guessing which one expired.
        var message = thrown.Which.Message;
        message.Should().Contain("neverArrives");
        message.Should().Contain(nameof(ASignalThatNeverArrives_NamesItselfItsTestAndTheRunnersLoad));
        message.Should().Contain("had still not arrived 0.05 s after that", "the guard waits as long again to tell late from never");
        message.Should().Contain("Runner load over the last 5.05 s:", "the load is read from before the guard's window began");
        message.Should().Contain("issue labelled").And.Contain("`flaky`", "the message routes the occurrence to the test's own issue");
    }

    [Fact]
    public async Task ASignalThatArrivesLate_SaysSoAndWhatTheTestWasDoing()
    {
        var signal = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        // The state is read the moment the guard runs out, so completing the signal there makes it late
        // without depending on a clock.
        string Doing()
        {
            signal.SetResult(1);
            return "depth 7, descending";
        }

        var thrown = await FluentActions
            .Awaiting(() => signal.Task.Guarded(TimeSpan.FromMilliseconds(50), Doing))
            .Should().ThrowAsync<TimeoutException>();

        thrown.Which.Message.Should().Contain("ms after the guard ran out, so it came late.")
            .And.Contain("When the guard ran out: depth 7, descending.");
    }

    [Fact]
    public async Task ASignalsOwnTimeout_IsItsOwn()
    {
        var timedOut = Task.FromException<int>(new TimeoutException("the signal's own"));

        var thrown = await FluentActions
            .Awaiting(() => timedOut.Guarded(TimeSpan.FromSeconds(30)))
            .Should().ThrowAsync<TimeoutException>();

        thrown.Which.Message.Should().Be("the signal's own");
    }

    [Fact]
    public async Task ASignalThatArrives_IsReturnedUntouched()
    {
        var arrived = Task.FromResult(42);

        (await arrived.Guarded(TimeSpan.FromSeconds(30))).Should().Be(42);
    }
}
