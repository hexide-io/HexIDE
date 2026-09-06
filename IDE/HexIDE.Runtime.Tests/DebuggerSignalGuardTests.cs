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
    public async Task ASignalThatNeverArrives_NamesItselfAndItsTest()
    {
        var neverArrives = new TaskCompletionSource<int>().Task;

        var thrown = await FluentActions
            .Awaiting(() => neverArrives.Guarded(TimeSpan.FromMilliseconds(50)))
            .Should().ThrowAsync<TimeoutException>();

        // The expression text is the point: a debugger test awaits three or four signals, and a bare
        // TimeoutException leaves you guessing which one expired.
        var message = thrown.Which.Message;
        message.Should().Contain("neverArrives");
        message.Should().Contain(nameof(ASignalThatNeverArrives_NamesItselfAndItsTest));
        message.Should().Contain("#102", "the message routes the next occurrence to the open issue");
    }

    [Fact]
    public async Task ASignalThatArrives_IsReturnedUntouched()
    {
        var arrived = Task.FromResult(42);

        (await arrived.Guarded(TimeSpan.FromSeconds(30))).Should().Be(42);
    }
}
