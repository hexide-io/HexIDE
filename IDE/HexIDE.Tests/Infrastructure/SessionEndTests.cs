using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Threading;
using HexIDE.Infrastructure;

namespace HexIDE.Tests.Infrastructure;

/// <summary>What the IDE answers when Windows ends the session, and what it does once the session ends (#4646).</summary>
public class SessionEndTests
{
    private sealed class Probe
    {
        public string? Unsaved;
        public int Asked;
        public int Prompts;
        public int Stops;
        public int AfterStops;
        public readonly List<TimeSpan> Waits = [];
        public Func<Task> Stop = () => Task.CompletedTask;
        public Action<Task, TimeSpan>? Wait;
        public List<string> Running = [];
        public readonly List<string> Restarted = [];

        public SessionEnd Create() => new(
            () =>
            {
                Asked++;
                return Unsaved;
            },
            project => $"Unsaved changes in {project}",
            () => Prompts++,
            () =>
            {
                Stops++;
                return Stop();
            },
            (task, bound) =>
            {
                Waits.Add(bound);
                (Wait ?? ((t, b) => t.Wait(b)))(task, bound);
            },
            () => AfterStops++,
            SessionEnd.EndBound,
            () => Running,
            id =>
            {
                Restarted.Add(id);
                return Task.CompletedTask;
            });
    }

    [AvaloniaFact]
    public void Nothing_unsaved_allows_the_session_to_end_at_once_without_a_prompt()
    {
        var probe = new Probe();
        var sessionEnd = probe.Create();

        sessionEnd.OnQuery(critical: false).Should().BeNull();
        Dispatcher.UIThread.RunJobs();

        probe.Prompts.Should().Be(0);
        probe.Stops.Should().Be(0, "nothing stops until Windows says the session is ending: another app may still veto");
    }

    [AvaloniaFact]
    public void Unsaved_work_blocks_with_the_project_named_and_prompts_after_the_reply()
    {
        var probe = new Probe { Unsaved = "Project1" };
        var sessionEnd = probe.Create();

        sessionEnd.OnQuery(critical: false).Should().Be("Unsaved changes in Project1");
        probe.Prompts.Should().Be(0, "the prompt is never held open inside Windows' query");
        Dispatcher.UIThread.RunJobs();

        probe.Prompts.Should().Be(1);
        probe.Stops.Should().Be(0);
    }

    [AvaloniaFact]
    public void A_forced_end_is_allowed_without_asking_about_unsaved_work()
    {
        var probe = new Probe { Unsaved = "Project1" };

        probe.Create().OnQuery(critical: true).Should().BeNull();
        Dispatcher.UIThread.RunJobs();

        probe.Asked.Should().Be(0);
        probe.Prompts.Should().Be(0);
    }

    [AvaloniaFact]
    public void The_session_ending_stops_the_servers_within_the_bound_then_closes_once()
    {
        var probe = new Probe();
        var sessionEnd = probe.Create();

        sessionEnd.OnEnd();
        sessionEnd.OnEnd();

        probe.Stops.Should().Be(1);
        probe.Waits.Should().Equal(SessionEnd.EndBound);
        probe.AfterStops.Should().Be(1);
        SessionEnd.EndBound.Should().BeLessThan(TimeSpan.FromSeconds(5), "Windows waits 5 s for WM_ENDSESSION");
        sessionEnd.OnQuery(critical: false).Should().BeNull("a query after the end has nothing left to block for");
    }

    [AvaloniaFact]
    public void A_stop_that_does_not_finish_or_throws_still_lets_the_ide_close()
    {
        var never = new Probe { Stop = () => new TaskCompletionSource().Task, Wait = (_, _) => { } };
        never.Create().OnEnd();
        never.AfterStops.Should().Be(1);

        var throws = new Probe { Stop = () => throw new InvalidOperationException("stop failed") };
        throws.Create().OnEnd();
        throws.AfterStops.Should().Be(1);
    }

    [AvaloniaFact]
    public async Task A_session_that_goes_on_restarts_the_servers_the_sign_out_ended_and_no_others()
    {
        // "crashed-earlier" stopped before the sign-out began, so it isn't running at the last look or the query and stays
        // stopped as any crash does. Windows' console handling then ends "exit-recorder" at the start of a sign-out the
        // person cancels; "rdcore-vba" survives.
        var probe = new Probe { Running = ["exit-recorder", "rdcore-vba"] };
        var sessionEnd = probe.Create();

        sessionEnd.Observe();
        sessionEnd.OnQuery(critical: false);
        probe.Running = ["rdcore-vba"];
        await sessionEnd.OnContinueAsync(TimeSpan.Zero);

        probe.Restarted.Should().Equal("exit-recorder");
        probe.Restarted.Should().NotContain("crashed-earlier");
    }

    [AvaloniaFact]
    public async Task A_server_windows_ended_just_before_the_query_is_restarted_too()
    {
        // Measured on the VM: the console kill can land before the IDE handles the query (8 ms before, in one run).
        var probe = new Probe { Running = ["exit-recorder"] };
        var sessionEnd = probe.Create();

        sessionEnd.Observe();
        probe.Running = [];
        sessionEnd.OnQuery(critical: false);
        await sessionEnd.OnContinueAsync(TimeSpan.Zero);

        probe.Restarted.Should().Equal("exit-recorder");
    }

    [AvaloniaFact]
    public async Task A_session_that_goes_on_without_a_query_or_after_ending_restarts_nothing()
    {
        var noQuery = new Probe { Running = [] };
        await noQuery.Create().OnContinueAsync(TimeSpan.Zero);
        noQuery.Restarted.Should().BeEmpty();

        var ended = new Probe { Running = ["exit-recorder"] };
        var sessionEnd = ended.Create();
        sessionEnd.OnQuery(critical: false);
        ended.Running = [];
        sessionEnd.OnEnd();
        await sessionEnd.OnContinueAsync(TimeSpan.Zero);
        ended.Restarted.Should().BeEmpty();
    }

    [AvaloniaFact]
    public void The_nested_frame_lets_a_stop_that_posts_to_the_ui_thread_finish()
    {
        // A stop whose continuation needs the UI thread: a blocking wait there would hold it to the bound.
        var done = new TaskCompletionSource();
        var stop = Task.Run(async () =>
        {
            await Task.Delay(50);
            await Dispatcher.UIThread.InvokeAsync(() => done.SetResult());
        });

        var watch = System.Diagnostics.Stopwatch.StartNew();
        SessionEnd.WaitInNestedFrame(stop, TimeSpan.FromSeconds(10));

        stop.IsCompletedSuccessfully.Should().BeTrue();
        done.Task.IsCompleted.Should().BeTrue();
        watch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5));
    }
}
