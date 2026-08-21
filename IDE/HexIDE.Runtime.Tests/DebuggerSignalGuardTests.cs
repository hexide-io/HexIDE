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

        var ex = await Assert.ThrowsAsync<TimeoutException>(
            () => neverArrives.Guarded(TimeSpan.FromMilliseconds(50)));

        // The expression text is the point: a debugger test awaits three or four signals, and a bare
        // TimeoutException leaves you guessing which one expired.
        ex.Message.Should().Contain("neverArrives");
        ex.Message.Should().Contain(nameof(ASignalThatNeverArrives_NamesItselfAndItsTest));
        ex.Message.Should().Contain("#102", "the message routes the next occurrence to the open issue");
    }

    [Fact]
    public async Task ASignalThatArrives_IsReturnedUntouched()
    {
        var arrived = Task.FromResult(42);

        (await arrived.Guarded(TimeSpan.FromSeconds(30))).Should().Be(42);
    }
}
