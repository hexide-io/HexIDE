using System.Collections.Generic;
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

    [Fact]
    public void Events_that_come_while_a_handler_is_suspended_run_after_it_in_the_order_they_came()
    {
        var pump = new EventPump();
        var ran = new List<string>();
        var gate = new TaskCompletionSource();

        RaiseBusy(pump, ran, gate);
        pump.Raise("Command1_Click", Logs(ran, "one"));
        pump.Raise("Command2_Click", Logs(ran, "two"));

        ran.Should().BeEmpty("both wait for the suspended handler");
        gate.SetResult();
        ran.Should().Equal("busy", "one", "two");
    }

    [Fact]
    public void A_move_updates_only_a_move_at_the_back_of_the_queue()
    {
        // Inferred from how Windows coalesces WM_MOUSEMOVE: a move never reaches past a press, but a move of another
        // control right behind one is folded into it, since the program's input is one queue.
        var pump = new EventPump();
        var ran = new List<string>();
        var gate = new TaskCompletionSource();

        RaiseBusy(pump, ran, gate);
        pump.Raise("Command1_MouseMove", Logs(ran, "move 1"), EventCoalescing.Latest);
        pump.Raise("Command1_MouseDown", Logs(ran, "down"));
        pump.Raise("Command1_MouseMove", Logs(ran, "move 2"), EventCoalescing.Latest);
        pump.Raise("Command1_MouseMove", Logs(ran, "move 3"), EventCoalescing.Latest);
        pump.Raise("Label1_MouseMove", Logs(ran, "label move"), EventCoalescing.Latest);
        gate.SetResult();

        ran.Should().Equal("busy", "move 1", "down", "label move");
    }

    [Fact]
    public void Ticks_of_one_Timer_wait_as_one_and_ticks_of_another_wait_beside_them()
    {
        var pump = new EventPump();
        var ran = new List<string>();
        var gate = new TaskCompletionSource();

        RaiseBusy(pump, ran, gate);
        pump.Raise("Timer1_Timer", Logs(ran, "timer 1"), EventCoalescing.Timer);
        pump.Raise("Timer2_Timer", Logs(ran, "timer 2"), EventCoalescing.Timer);
        pump.Raise("Timer1_Timer", Logs(ran, "timer 1 again"), EventCoalescing.Timer);
        gate.SetResult();

        ran.Should().Equal("busy", "timer 1", "timer 2");
    }

    [Fact]
    public void A_closed_pump_forgets_what_waits_and_takes_nothing_more()
    {
        // The end of a run: what it left waiting must not run beside the next one.
        var pump = new EventPump();
        var ran = new List<string>();
        var gate = new TaskCompletionSource();

        RaiseBusy(pump, ran, gate);
        pump.Raise("Command1_Click", Logs(ran, "waited"));
        pump.Close();
        pump.Raise("Command2_Click", Logs(ran, "after the end"));
        gate.SetResult();

        ran.Should().Equal("busy");
    }
}
