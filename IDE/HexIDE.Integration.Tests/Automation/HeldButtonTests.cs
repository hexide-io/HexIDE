using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HexIDE.Automation;
using HexIDE.Controls;

namespace HexIDE.Integration.Tests.Automation;

/// <summary>
/// <c>click … to x,y hold</c> leaves the button down at the end of a drag, and <c>release</c> lets it go in a later call
/// (#2231). A drag pressed, moved and released in one call, so nothing that shows only while the button is down could be
/// looked at: the designer's draw preview, Dock's drop targets, a control's outline as it moves.
/// </summary>
/// <remarks>
/// The held button is static, as the mouse is one, so every test lets go of what it held, however it ends. A resize
/// handle's abandon is checked live: the handles come from a template this harness cannot load (DesignerOverlayTreeTests).
/// </remarks>
public class HeldButtonTests : IDisposable
{
    public void Dispose()
    {
        HeldButton.Current?.End();
        HeldButton.TakeNote();
    }

    private static Canvas Spawner(DesignerOverlayTreeTests.Designer designer) =>
        designer.View.FindControl<Canvas>("NewControlSpawner")!;

    private static async Task<InteractOutcome> Act(Control target, string action, string? value) =>
        await UiAutomationDriver.FinishAsync(await UiAutomationDriver.InteractAsync(target, action, value));

    // The control a draw placed, as its view model has it.
    private static Rect Placed(DesignerOverlayTreeTests.Designer designer)
    {
        var placed = designer.Model.Components.Last(c => c != designer.Model.Form);
        return new Rect(placed.Left, placed.Top, placed.Width, placed.Height);
    }

    private static int Placements(DesignerOverlayTreeTests.Designer designer) =>
        designer.Model.Components.Count(c => c != designer.Model.Form);

    // What an unheld drag places, to compare a held one against.
    private static async Task<Rect> DrawnUnheld(string value)
    {
        using var designer = DesignerOverlayTreeTests.Shown();
        var drawn = await Act(Spawner(designer), "click", value);
        drawn.Success.Should().BeTrue(drawn.Error);
        return Placed(designer);
    }

    [AvaloniaFact]
    public async Task A_held_draw_shows_the_preview_and_its_release_places_what_an_unheld_drag_does()
    {
        var expected = await DrawnUnheld("20,20 to 100,60");
        using var designer = DesignerOverlayTreeTests.Shown();

        var held = await Act(Spawner(designer), "click", "20,20 to 100,60 hold");

        held.Success.Should().BeTrue(held.Error);
        held.Detail.Should().Contain("and moved to (100, 60) on 'Canvas")
            .And.Contain("the left button is still held there, and 'Canvas")
            .And.Contain("has the pointer capture; interact 'release' lets it go");
        HeldButton.Current.Should().NotBeNull();
        HeldButton.TakeNote().Should().StartWith("The left button is held at (100, 60)").And.Contain("interact 'release' lets it go");
        DesignerOverlayTreeTests.Settle(designer.Window);
        var phantom = designer.View.FindControl<Border>("NewControlSpawnerPhantom")!;
        phantom.IsVisible.Should().BeTrue("the preview shows while the button is held");
        phantom.Bounds.Width.Should().BeGreaterThan(0);
        Placements(designer).Should().Be(0, "nothing is placed until the release");

        var released = await Act(Spawner(designer), "release", null);

        released.Success.Should().BeTrue(released.Error);
        released.Detail.Should().StartWith("dragged the left button at (20, 20)")
            .And.Contain("; released at (100, 60) on 'Canvas").And.EndWith("(pointer capturer)");
        HeldButton.Current.Should().BeNull();
        HeldButton.TakeNote().Should().BeNull("nothing is held, and nothing ended without a release");
        Placed(designer).Should().Be(expected);
    }

    [AvaloniaFact]
    public async Task A_release_with_a_point_moves_there_before_letting_go()
    {
        var expected = await DrawnUnheld("20,20 to 140,90");
        using var designer = DesignerOverlayTreeTests.Shown();
        (await Act(Spawner(designer), "click", "20,20 to 100,60 hold")).Success.Should().BeTrue();

        var released = await Act(Spawner(designer), "release", "140,90");

        released.Success.Should().BeTrue(released.Error);
        released.Detail.Should().Contain("; released at (140, 90) on 'Canvas");
        Placed(designer).Should().Be(expected);
    }

    [AvaloniaFact]
    public async Task While_the_button_is_held_a_second_press_or_pointer_is_refused_and_nothing_is_pressed()
    {
        using var designer = DesignerOverlayTreeTests.Shown();
        var spawner = Spawner(designer);
        (await Act(spawner, "click", "20,20 to 100,60 hold")).Success.Should().BeTrue();

        foreach (var (action, value) in new[] { ("click", "150,150"), ("double_click", (string?)null) })
        {
            var refused = await Act(spawner, action, value);
            refused.Success.Should().BeFalse(action);
            refused.Mechanism.Should().Be("pointer", "the refusal is about the pointer, as hover's is");
            refused.Error.Should().StartWith("the left button is still held from click … hold, at (100, 60)")
                .And.Contain($"{action} would press again").And.EndWith("interact 'release' lets it go first");
        }
        UiAutomationDriver.Hover(spawner, 5, 5).Error.Should().Contain("hover would press again or move another pointer");
        HeldButton.Current.Should().NotBeNull("a refusal leaves the hold as it was");
        Placements(designer).Should().Be(0);
    }

    [AvaloniaFact]
    public async Task A_release_with_nothing_held_and_a_hold_without_a_drag_are_refused()
    {
        using var designer = DesignerOverlayTreeTests.Shown();
        var spawner = Spawner(designer);

        (await Act(spawner, "release", null)).Error.Should().StartWith("no button is held, so there is nothing to release");
        (await Act(spawner, "click", "20,20 hold")).Error.Should().StartWith("hold keeps the button down at the end of a drag");
        (await Act(spawner, "click", "20,20 to 100,60 hold double")).Error.Should().StartWith("double cannot be combined with to");
        HeldButton.Current.Should().BeNull();
        Placements(designer).Should().Be(0);
    }

    [AvaloniaFact]
    public async Task A_release_point_that_cannot_be_read_leaves_the_button_held()
    {
        using var designer = DesignerOverlayTreeTests.Shown();
        var spawner = Spawner(designer);
        (await Act(spawner, "click", "20,20 to 100,60 hold")).Success.Should().BeTrue();

        var refused = await Act(spawner, "release", "here");

        refused.Error.Should().Contain("'here' is not a release point; release takes x,y relative to the target")
            .And.EndWith("nothing was done, and the button is still held");
        HeldButton.Current.Should().NotBeNull();
    }

    // The designer has no Escape, and a draw let go where it began still places a control: the abandon says so plainly,
    // naming what the designer recorded, rather than calling the drag cancelled.
    [AvaloniaFact]
    public async Task An_abandoned_draw_still_places_a_control_and_the_note_says_so()
    {
        var expected = await DrawnUnheld("20,20 to 20,20");
        using var designer = DesignerOverlayTreeTests.Shown();
        (await Act(Spawner(designer), "click", "20,20 to 100,60 hold")).Success.Should().BeTrue();

        await UiAutomationDriver.AbandonHeldAsync();

        HeldButton.Current.Should().BeNull();
        Placed(designer).Should().Be(expected, "the release where the press was is a draw that ends where it began");
        HeldButton.TakeNote().Should().StartWith("The held left button was abandoned at ")
            .And.Contain(": Escape went to '")
            .And.Contain("then it moved back to where it was pressed, (20, 20) on 'Canvas")
            .And.Contain("the release reports: dragged the left button at (20, 20)")
            .And.EndWith("The designer recorded 'Add: Command1' anyway, so it did not cancel; Undo takes it back.");
        HeldButton.TakeNote().Should().BeNull("the note is given once");
    }

    [AvaloniaFact]
    public async Task An_abandoned_move_puts_the_control_back_and_the_designer_records_nothing()
    {
        using var designer = DesignerOverlayTreeTests.Shown();
        (await Act(Spawner(designer), "click", "20,20 to 100,60")).Success.Should().BeTrue();
        DesignerOverlayTreeTests.Settle(designer.Window);
        var before = Placed(designer);
        var item = designer.View.GetVisualDescendants().OfType<ControlItem>().Single();

        (await Act(item, "click", "10,10 to 50,40 hold")).Success.Should().BeTrue();
        Placed(designer).Should().NotBe(before, "the held moves move the control");

        await UiAutomationDriver.AbandonHeldAsync();

        Placed(designer).Should().Be(before);
        HeldButton.TakeNote().Should().EndWith("The designer recorded nothing, so nothing was placed, moved or resized.");
    }

    // A move snaps its offset, not the control's position, so even a control off the grid, as one loaded from a file may
    // be, ends where it began. A resize is different: it returns the size to the grid, which the live check measured.
    [AvaloniaFact]
    public async Task An_abandoned_move_of_a_control_off_the_grid_puts_it_back_where_it_was()
    {
        using var designer = DesignerOverlayTreeTests.Shown();
        (await Act(Spawner(designer), "click", "20,20 to 100,60")).Success.Should().BeTrue();
        var placed = designer.Model.Components.Last(c => c != designer.Model.Form);
        placed.Left = 27;
        placed.Top = 29;
        DesignerOverlayTreeTests.Settle(designer.Window);
        var before = Placed(designer);
        var item = designer.View.GetVisualDescendants().OfType<ControlItem>().Single();

        (await Act(item, "click", "10,10 to 50,40 hold")).Success.Should().BeTrue();
        await UiAutomationDriver.AbandonHeldAsync();

        Placed(designer).Should().Be(before);
        HeldButton.TakeNote().Should().EndWith("The designer recorded nothing, so nothing was placed, moved or resized.");
    }

    [AvaloniaFact]
    public async Task An_abandoned_dock_drag_docks_nothing_and_says_so()
    {
        var layout = DragDockTests.Shown();
        try
        {
            var from = DragDockTests.ChromeOf(layout, layout.Right);
            var onto = DragDockTests.ChromeOf(layout, layout.Left);
            var centre = onto.TranslatePoint(new Point(onto.Bounds.Width / 2, onto.Bounds.Height / 2), from)!.Value;
            (await Act(from, "click", FormattableString.Invariant($"40,8 to {centre.X:0.#},{centre.Y:0.#} hold")))
                .Success.Should().BeTrue();

            await UiAutomationDriver.AbandonHeldAsync();

            layout.Right.VisibleDockables.Should().Contain(layout.Beta);
            layout.Left.VisibleDockables.Should().NotContain(layout.Beta);
            HeldButton.TakeNote().Should().Contain("then it moved back to where it was pressed")
                .And.EndWith("; Dock's layout is unchanged, so nothing was docked: the release was not over one of Dock's "
                             + "drop targets, or dropped what it dragged where it was.");
        }
        finally { layout.Window.Close(); }
    }

    [AvaloniaFact]
    public async Task A_held_dock_drag_released_over_another_dock_docks_there()
    {
        var layout = DragDockTests.Shown();
        try
        {
            var from = DragDockTests.ChromeOf(layout, layout.Right);
            var onto = DragDockTests.ChromeOf(layout, layout.Left);
            var centre = onto.TranslatePoint(new Point(onto.Bounds.Width / 2, onto.Bounds.Height / 2), from)!.Value;
            (await Act(from, "click", FormattableString.Invariant($"40,8 to {centre.X:0.#},{centre.Y:0.#} hold")))
                .Success.Should().BeTrue();
            layout.Left.VisibleDockables.Should().NotContain(layout.Beta, "nothing docks before the release");

            var released = await Act(from, "release", null);

            released.Success.Should().BeTrue(released.Error);
            layout.Left.VisibleDockables.Should().Contain(layout.Beta);
            released.Detail.Should().EndWith("; Dock moved 'Beta' from a dock of its own to the dock with 'Alpha'");
        }
        finally { layout.Window.Close(); }
    }

    [AvaloniaFact]
    public async Task With_no_call_for_the_idle_limit_the_hold_is_abandoned_and_a_call_in_time_keeps_it()
    {
        var limit = HeldButton.IdleLimit;
        HeldButton.IdleLimit = TimeSpan.FromMilliseconds(300);
        try
        {
            using var designer = DesignerOverlayTreeTests.Shown();
            (await Act(Spawner(designer), "click", "20,20 to 100,60 hold")).Success.Should().BeTrue();

            // Calls come in more often than the limit, as the server's filter touches the clock on each.
            for (var i = 0; i < 6; i++)
            {
                await Task.Delay(100);
                HeldButton.Touch();
                Dispatcher.UIThread.RunJobs();
            }
            HeldButton.Current.Should().NotBeNull("each call restarts the clock");

            // Wait for the completed abandonment, not just End(), which clears Current before the release.
            // The timer and dispatcher can both be delayed when the full suite runs under load (#2368).
            using var timeout = System.Threading.CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            string? note;
            do
            {
                await Task.Delay(50, timeout.Token);
                note = HeldButton.TakeNote();
            } while (note is null || !note.StartsWith("The held left button was abandoned at ", StringComparison.Ordinal));

            HeldButton.Current.Should().BeNull();
            Placements(designer).Should().Be(1);
            note.Should().Contain("with no call for 0.3 s");
        }
        finally { HeldButton.IdleLimit = limit; }
    }

    [AvaloniaFact]
    public async Task A_timer_that_fires_before_the_limit_has_passed_is_armed_again_and_the_hold_is_still_abandoned()
    {
        // A real timer fired 14 µs before the clock agreed the limit had passed, and nothing armed it again, so the hold
        // was never abandoned (#2514): 3 failures in 41 runs of the test above. This one fires 100 ms early every time.
        var limit = HeldButton.IdleLimit;
        HeldButton.IdleLimit = TimeSpan.FromMilliseconds(300);
        HeldButton.Time = new EarlyTimer(TimeSpan.FromMilliseconds(100));
        try
        {
            using var designer = DesignerOverlayTreeTests.Shown();
            (await Act(Spawner(designer), "click", "20,20 to 100,60 hold")).Success.Should().BeTrue();

            // Not TestContext.Current's token: on the headless dispatcher it can already be disposed (#2486).
            var deadline = System.Diagnostics.Stopwatch.StartNew();
            string? note = null;
            while (deadline.Elapsed < TimeSpan.FromSeconds(10)
                   && (note is null || !note.StartsWith("The held left button was abandoned at ", StringComparison.Ordinal)))
            {
                await Task.Delay(50, CancellationToken.None);
                note = HeldButton.TakeNote();
            }

            note.Should().StartWith("The held left button was abandoned at ",
                "the early timer found 100 ms still to go, and the timer armed again for them ends the hold");
            HeldButton.Current.Should().BeNull();
        }
        finally
        {
            HeldButton.IdleLimit = limit;
            HeldButton.Time = TimeProvider.System;
        }
    }

    // A clock whose timer fires early by a set margin the first time it is armed, as the system's did by 14 µs (#2514).
    private sealed class EarlyTimer(TimeSpan early) : TimeProvider
    {
        public override ITimer CreateTimer(
            TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
            new Early(TimeProvider.System.CreateTimer(callback, state, dueTime, period), early);

        private sealed class Early(ITimer inner, TimeSpan early) : ITimer
        {
            private bool firedEarly;

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                if (!firedEarly && dueTime > early && dueTime != Timeout.InfiniteTimeSpan)
                {
                    firedEarly = true;
                    dueTime -= early;
                }
                return inner.Change(dueTime, period);
            }

            public void Dispose() => inner.Dispose();

            public ValueTask DisposeAsync() => inner.DisposeAsync();
        }
    }

    [AvaloniaFact]
    public async Task Closing_the_window_drops_the_hold_without_a_release_and_the_next_note_says_why()
    {
        var designer = DesignerOverlayTreeTests.Shown();
        designer.Window.Title = "Held";
        (await Act(Spawner(designer), "click", "20,20 to 100,60 hold")).Success.Should().BeTrue();

        designer.Dispose();

        HeldButton.Current.Should().BeNull();
        HeldButton.TakeNote().Should().StartWith("The held left button was dropped without a release at ")
            .And.EndWith(": its window 'Held' closed.");
    }
}
