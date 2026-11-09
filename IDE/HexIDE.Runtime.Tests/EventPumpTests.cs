using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// <see cref="EventPump"/>'s queue on its own (#766): what waits behind a running handler, and how it is coalesced. A
/// handler that suspends is one awaiting a gate the test opens, bracketed as the interpreter brackets its own awaits.
/// </summary>
public class EventPumpTests
{
    private static Func<Task> Logs(List<string> ran, string what) => () =>
    {
        ran.Add(what);
        return Task.CompletedTask;
    };

    // A handler that suspends until the gate opens, so everything raised meanwhile has to wait for it.
    private static void RaiseBusy(EventPump pump, List<string> ran, TaskCompletionSource gate) =>
        pump.Raise("Busy_Click", async () =>
        {
            await pump.WhileSuspended(new ValueTask(gate.Task));
            ran.Add("busy");
        });

    // The real pump runs on one UI thread. Keep its continuations serialized here too (#3516).
    private sealed class PumpContext : SynchronizationContext
    {
        public readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> Pending = new();
        public override void Post(SendOrPostCallback callback, object? state) => Pending.Add((callback, state));
    }

    private static void OnPump(Func<Task> test)
    {
        var previous = SynchronizationContext.Current;
        var context = new PumpContext();
        SynchronizationContext.SetSynchronizationContext(context);
        try
        {
            var finished = test();
            while (!finished.IsCompleted)
            {
                context.Pending.TryTake(out var work, TimeSpan.FromSeconds(10)).Should().BeTrue("the test must finish");
                work.Callback(work.State);
            }
            finished.GetAwaiter().GetResult();
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
            context.Pending.Dispose();
        }
    }

    private static Task WhenIdle(EventPump pump)
    {
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        pump.WhenIdle(() =>
        {
            finished.SetResult();
            return Task.CompletedTask;
        });
        return finished.Task;
    }

    [Fact]
    public void Sibling_events_from_one_statement_wait_for_the_first_suspended_handler() => OnPump(async () =>
    {
        var pump = new EventPump();
        var ran = new List<string>();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        // Run completes after the outer walk unwinds; opening the gate alone does not (#3516).
        var finished = pump.Run("Click", () =>
        {
            pump.Raise("SelChange", async () =>
            {
                await pump.WhileSuspended(new ValueTask(gate.Task));
                ran.Add("SelChange");
            });
            pump.Raise("Change", Logs(ran, "Change"));
            return Task.FromResult(0);
        });

        ran.Should().BeEmpty();
        gate.SetResult();
        await finished.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        ran.Should().Equal("SelChange", "Change");
        pump.IsIdle.Should().BeTrue();
    });

    [Fact]
    public void Events_that_come_while_a_handler_is_suspended_run_after_it_in_the_order_they_came() => OnPump(async () =>
    {
        var pump = new EventPump();
        var ran = new List<string>();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        RaiseBusy(pump, ran, gate);
        pump.Raise("Command1_Click", Logs(ran, "one"));
        pump.Raise("Command2_Click", Logs(ran, "two"));

        ran.Should().BeEmpty("both wait for the suspended handler");
        var finished = WhenIdle(pump);
        gate.SetResult();
        await finished.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        ran.Should().Equal("busy", "one", "two");
    });

    [Fact]
    public void A_move_updates_only_a_move_at_the_back_of_the_queue() => OnPump(async () =>
    {
        // Inferred from how Windows coalesces WM_MOUSEMOVE: a move never reaches past a press, but a move of another
        // control right behind one is folded into it, since the program's input is one queue.
        var pump = new EventPump();
        var ran = new List<string>();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        RaiseBusy(pump, ran, gate);
        pump.Raise("Command1_MouseMove", Logs(ran, "move 1"), EventCoalescing.Latest);
        pump.Raise("Command1_MouseDown", Logs(ran, "down"));
        pump.Raise("Command1_MouseMove", Logs(ran, "move 2"), EventCoalescing.Latest);
        pump.Raise("Command1_MouseMove", Logs(ran, "move 3"), EventCoalescing.Latest);
        pump.Raise("Label1_MouseMove", Logs(ran, "label move"), EventCoalescing.Latest);
        var finished = WhenIdle(pump);
        gate.SetResult();
        await finished.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        ran.Should().Equal("busy", "move 1", "down", "label move");
    });

    [Fact]
    public void Ticks_of_one_Timer_wait_as_one_and_ticks_of_another_wait_beside_them() => OnPump(async () =>
    {
        var pump = new EventPump();
        var ran = new List<string>();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        RaiseBusy(pump, ran, gate);
        pump.Raise("Timer1_Timer", Logs(ran, "timer 1"), EventCoalescing.Timer);
        pump.Raise("Timer2_Timer", Logs(ran, "timer 2"), EventCoalescing.Timer);
        pump.Raise("Timer1_Timer", Logs(ran, "timer 1 again"), EventCoalescing.Timer);
        var finished = WhenIdle(pump);
        gate.SetResult();
        await finished.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        ran.Should().Equal("busy", "timer 1", "timer 2");
    });

    [Fact]
    public void A_closed_pump_forgets_what_waits_and_takes_nothing_more() => OnPump(async () =>
    {
        // The end of a run: what it left waiting must not run beside the next one.
        var pump = new EventPump();
        var ran = new List<string>();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var finished = pump.Run("Busy_Click", async () =>
        {
            await pump.WhileSuspended(new ValueTask(gate.Task));
            ran.Add("busy");
            return 0;
        });
        pump.Raise("Command1_Click", Logs(ran, "waited"));
        pump.Close();
        pump.Raise("Command2_Click", Logs(ran, "after the end"));
        gate.SetResult();
        await finished.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        ran.Should().Equal("busy");
    });

    // A reply that named a handler running says when the action let it end; a Timer's walks are left out, since one ticks on
    // its own and would be named on most replies (#2720).
    [Fact]
    public void FinishedSince_names_the_walks_that_ended_and_leaves_out_a_Timer_s() => OnPump(async () =>
    {
        var pump = new EventPump();
        var ran = new List<string>();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var timerGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        RaiseBusy(pump, ran, gate);
        pump.Raise("Timer1_Timer", async () => await pump.WhileSuspended(new ValueTask(timerGate.Task)), EventCoalescing.Timer);
        var before = pump.Live.ToList();

        pump.FinishedSince(before).Should().BeEmpty("nothing has ended yet");
        var finished = WhenIdle(pump);
        gate.SetResult();
        timerGate.SetResult();
        await finished.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        pump.FinishedSince(before).Should().Equal("Busy_Click");
    });
}
