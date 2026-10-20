using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Threading;
using HexIDE.Runtime.Utils;

namespace HexIDE.Runtime;

/// <summary>How an event that has to wait is queued, where it differs from first come, first served.</summary>
public enum EventCoalescing
{
    /// <summary>Every one waits its turn.</summary>
    None,

    /// <summary>
    /// A Timer's tick. At most one waits, however many came due, and none is taken while that Timer's own handler is
    /// running, even under <c>DoEvents</c>: the tick is dropped, and the next comes an interval later. All measured
    /// (#766).
    /// </summary>
    Timer,

    /// <summary>
    /// A move, as a MouseMove is: it updates the newest waiting event when that is itself a move, of any control, and is
    /// queued otherwise. The program's input is one queue, and a move never reaches past a press or anything else, so a
    /// waiting Move, Down, Move stays three. Inferred from how Windows coalesces WM_MOUSEMOVE, not measured: synthetic
    /// messages cannot drive it on the oracle.
    /// </summary>
    Latest,
}

/// <summary>
/// Runs a program's event handlers one at a time, as VB6's single thread does (#766).
/// </summary>
/// <remarks>
/// <para>
/// A handler is a <em>walk</em>: the event procedure and everything it calls, until it returns. An event raised from
/// outside any walk (input, a Timer, a window event) is top-level. It runs at once when nothing is running, and
/// otherwise waits until the running walk finishes, in the order the events came. An event the program's own code
/// raises (<c>Text1.Text = "x"</c> raising <c>Text1_Change</c>) is nested: it runs at once, inside the statement that
/// raised it, and that walk's next statement waits for it to finish, however it suspends. <c>DoEvents</c> is the one
/// place a waiting event runs inside a walk.
/// </para>
/// <para>
/// Without this a walk that gave up the UI thread, at a debugger time slice or in a <c>MsgBox</c>, let any other event
/// start and finish first: a CommandButton's MouseUp came before its Click, and a raiser ran on past the handler it
/// raised. Measured on vb6.exe, nothing runs inside a handler unless it calls <c>DoEvents</c>.
/// </para>
/// <para>
/// <b>Which walk is running</b> is <see cref="Current"/>, and it has to stay right across every await that really gives up
/// the thread. The interpreter brackets each one with <see cref="WhileSuspended"/>: the statement gate's debugger
/// pause and time slice, an awaited built-in such as <c>MsgBox</c>, and waiting for what it raised. While suspended,
/// <see cref="Current"/> is what the walk returned to: its raiser's code, or nothing, which is the dispatcher.
/// </para>
/// <para>
/// One per run, so every form of a program shares it, as VB6 serialises all of them on one thread. It does not depend
/// on a debugger: a run with none needs the order as much.
/// </para>
/// </remarks>
public sealed class EventPump
{
    /// <summary>One handler's run.</summary>
    internal sealed class Walk(string name)
    {
        public string Name { get; } = name;

        /// <summary>What runs on when this walk stops running: its raiser's walk, or null for the dispatcher. Set when the
        /// walk starts, and again each time it resumes.</summary>
        public Walk? ReturnsTo;

        /// <summary>Handlers this walk's code raised that have not finished. Its next statement waits for them.</summary>
        public List<Task>? Raised;
    }

    private sealed record Waiting(string Name, Func<Task> Work, EventCoalescing Coalescing);

    private readonly List<Waiting> queue = [];
    private readonly List<Func<Task>> whenIdle = [];
    private readonly Dictionary<string, int> running = new(StringComparer.OrdinalIgnoreCase);
    private int topLevel;
    // Walks held by a suspension WhileSuspended brackets: a time slice, a breakpoint, an awaited built-in (#1232).
    private int held;
    private int posting;
    private bool closed;

    // Completed when an event is queued or the run ends, for a modal form's wait to wake on.
    private TaskCompletionSource? arrival;

    /// <summary>The walk running now, or null when none is.</summary>
    internal Walk? Current { get; private set; }

    /// <summary>Whether the run is over, and the pump takes no more events.</summary>
    internal bool IsClosed => closed;

    /// <summary>How many events are waiting. For tests.</summary>
    internal int WaitingCount => queue.Count;

    /// <summary>
    /// Whether the program is waiting for input: no handler executing, none held at a time slice, a breakpoint or an
    /// awaited built-in such as MsgBox, and no event queued. A walk parked in <c>Show vbModal</c>'s loop or at
    /// <c>DoEvents</c> counts as waiting, as that is the program's message loop; work waiting for idle cannot run under
    /// one, so it counts only when nothing is parked. What an automation reply waits for (#1232). It rests on every
    /// suspension that is not a park being bracketed by <see cref="WhileSuspended(ValueTask)"/>, which the class already
    /// requires.
    /// </summary>
    public bool IsIdle => closed
        || Current is null && queue.Count == 0 && held == 0 && (topLevel > 0 || whenIdle.Count == 0);

    /// <summary>The handlers part way through, then those waiting, by name: what a reply names when it cannot wait.</summary>
    public IReadOnlyList<string> Busy =>
        [.. running.Where(r => r.Value > 0).Select(r => r.Key), .. queue.Select(w => w.Name)];

    /// <summary>
    /// Raise an event, whose handler is <paramref name="work"/>: nested in the running walk if one is, else at once if
    /// nothing runs, else queued.
    /// </summary>
    public void Raise(string name, Func<Task> work, EventCoalescing coalescing = EventCoalescing.None)
    {
        if (closed)
            return;
        // Posted: it waits its turn behind the running walk, as a message Windows posts waits for the message loop.
        if (posting > 0 && (topLevel > 0 || Current is not null))
        {
            Enqueue(new Waiting(name, work, coalescing));
            return;
        }
        if (Current is { } raiser)
        {
            var started = Start(name, work);
            if (!started.IsCompleted)
                (raiser.Raised ??= []).Add(started);
            return;
        }
        if (coalescing == EventCoalescing.Timer && IsRunning(name))
            return;
        if (topLevel > 0)
        {
            Enqueue(new Waiting(name, work, coalescing));
            return;
        }
        RunFrom(new Waiting(name, work, coalescing)).ListenErrors();
    }

    /// <summary>
    /// Run <paramref name="work"/> as a walk and give back its result: nested if a walk is running, else as a top-level
    /// event, which waits its turn if another is running. The form's start is one: Initialize and Load hold the gate as
    /// any handler does, so an event that comes during Load waits for it.
    /// </summary>
    public async Task<T> Run<T>(string name, Func<Task<T>> work)
    {
        var result = default(T)!;
        async Task Body() => result = await work();

        if (Current is { } caller)
        {
            var started = Start(name, Body);
            if (!started.IsCompleted)
                await Suspended(caller, started);
            await started;
            return result;
        }
        if (topLevel > 0)
        {
            var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Enqueue(new Waiting(name, async () =>
            {
                try { await Body(); done.SetResult(); }
                catch (Exception e) { done.SetException(e); }
            }, EventCoalescing.None));
            await done.Task;
            return result;
        }
        topLevel++;
        try
        {
            await Start(name, Body);
        }
        finally
        {
            topLevel--;
        }
        // What waited behind it runs once the caller has gone on: after the form's start has shown the form, as VB6
        // delivers what came during Load once Load has returned.
        if (queue.Count > 0 || whenIdle.Count > 0)
            Dispatcher.UIThread.Post(() =>
            {
                if (topLevel != 0 || Current is not null)
                    return;
                if (Dequeue() is { } next)
                    RunFrom(next).ListenErrors();
                else
                    RunIdle().ListenErrors();
            });
        return result;
    }

    /// <summary>
    /// While the returned scope is open, an event raised waits its turn behind the running walk, even one its own code
    /// raises: after the handler that raised it has returned, or at its DoEvents. That is how a message Windows posts is
    /// delivered, and how SetFocus's LostFocus and GotFocus come (measured, #1061).
    /// </summary>
    public IDisposable Posting()
    {
        posting++;
        return new PostingScope(this);
    }

    private sealed class PostingScope(EventPump pump) : IDisposable
    {
        private bool disposed;

        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;
            pump.posting--;
        }
    }

    /// <summary>
    /// Bracket an await that may give up the thread, so that while it is suspended <see cref="Current"/> is what the walk
    /// returned to, and once it resumes it is the walk again.
    /// </summary>
    public ValueTask WhileSuspended(ValueTask pending) =>
        pending.IsCompleted ? pending : new ValueTask(Held(Current, pending.AsTask()));

    /// <inheritdoc cref="WhileSuspended(ValueTask)"/>
    public Task<T> WhileSuspended<T>(Task<T> pending) =>
        pending.IsCompleted ? pending : HeldFor(Current, pending);

    // A suspension the walk did not choose as a place to wait for input: what IsIdle counts as the program still busy.
    private async Task Held(Walk? walk, Task pending)
    {
        held++;
        try
        {
            await Suspended(walk, pending);
        }
        finally
        {
            held--;
        }
    }

    private async Task<T> HeldFor<T>(Walk? walk, Task<T> pending)
    {
        await Held(walk, pending);
        return await pending;
    }

    /// <summary>
    /// Wait for every handler the running walk raised that has not finished, before its next statement: the raiser does
    /// not go on until what it raised has run, as in VB6.
    /// </summary>
    public ValueTask WaitForRaised() =>
        Current is { Raised.Count: > 0 } walk ? new ValueTask(WaitForRaised(walk)) : ValueTask.CompletedTask;

    /// <summary>
    /// VB6's <c>DoEvents</c>: one turn of the dispatcher, so pending input reaches its handlers, then every event waiting
    /// runs, inside the caller, in the order it came. Measured: a press and release posted before a handler ran inside
    /// its <c>DoEvents</c>, and another Timer ticked there, but a Timer never inside its own handler (#766).
    /// </summary>
    public async Task DoEvents()
    {
        var walk = Current;
        await Suspended(walk, YieldToInput());
        while (Dequeue() is { } next)
        {
            var started = Start(next.Name, next.Work);
            if (!started.IsCompleted)
                await Suspended(walk, started);
            await started;
        }
    }

    /// <summary>
    /// VB6's <c>Show vbModal</c>, in the walk that showed the form (#822). The walk waits, and every event that comes runs
    /// inside it one at a time, as <see cref="DoEvents"/> runs them, until <paramref name="shown"/> says the form has gone:
    /// hidden, invisible or unloaded. The statement after the Show then runs, after the handler that removed the form has
    /// returned (measured). Every form's events come, as Form1's Timer ticks under Form2's modal form, except a Timer whose
    /// own handler is the one waiting, which never runs inside itself (measured). The wait also ends with the run.
    /// </summary>
    public async Task WhileModal(Func<bool> shown)
    {
        var walk = Current;
        while (!closed && shown())
        {
            if (Dequeue() is { } next)
            {
                var started = Start(next.Name, next.Work);
                if (!started.IsCompleted)
                    await Suspended(walk, started);
                await started;
                continue;
            }
            // Nothing waits, so give the thread up until an event comes or the run ends, suspended as any wait is. The walk
            // that raised this one waits for it at its next statement, so no walk runs meanwhile, and an event that comes is
            // top-level and waits in the queue for this loop, as VB6's modal loop takes it from the message queue.
            var arrived = arrival = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            try
            {
                await Suspended(walk, arrived.Task);
            }
            finally
            {
                arrival = null;
            }
        }
    }

    /// <summary>
    /// Run <paramref name="work"/> as a walk of its own once no handler is running and nothing waits: after the handler
    /// that asked for it has returned, as a form's Terminate comes after the handler that unloaded it (measured, #973).
    /// </summary>
    public void WhenIdle(Func<Task> work)
    {
        if (closed)
            return;
        whenIdle.Add(work);
        if (topLevel == 0 && Current is null)
            RunIdle().ListenErrors();
    }

    /// <summary>
    /// The run is over: forget every waiting event, and take no more. Called where the run ends, so nothing it left
    /// waiting can run beside the next run, whatever the debugger the two runs share says by then.
    /// </summary>
    public void Close()
    {
        closed = true;
        queue.Clear();
        whenIdle.Clear();
        arrival?.TrySetResult();
    }

    private bool IsRunning(string name) => running.TryGetValue(name, out var count) && count > 0;

    private async Task RunFrom(Waiting first)
    {
        for (var next = first; next is not null; next = topLevel == 0 ? Dequeue() : null)
        {
            topLevel++;
            try
            {
                await Start(next.Name, next.Work);
            }
            finally
            {
                topLevel--;
            }
        }
        if (topLevel == 0)
            await RunIdle();
    }

    private async Task RunIdle()
    {
        while (topLevel == 0 && Current is null && queue.Count == 0 && whenIdle.Count > 0)
        {
            var work = whenIdle[0];
            whenIdle.RemoveAt(0);
            topLevel++;
            try
            {
                await Start("(idle)", work);
            }
            finally
            {
                topLevel--;
            }
        }
    }

    private async Task Start(string name, Func<Task> work)
    {
        var walk = new Walk(name) { ReturnsTo = Current };
        Current = walk;
        running[name] = (running.TryGetValue(name, out var count) ? count : 0) + 1;
        try
        {
            var body = work();
            // Every real suspension inside is bracketed, so Current is already what the walk returned to. This is the
            // backstop for one that is not: the dispatcher must never see a walk as running.
            if (!body.IsCompleted)
            {
                Current = walk.ReturnsTo;
                await body;
                Current = walk;
            }
            else
            {
                await body;
            }
            if (walk.Raised is { Count: > 0 })
                await WaitForRaised(walk);
        }
        finally
        {
            running[name]--;
            Current = walk.ReturnsTo;
        }
    }

    private async Task WaitForRaised(Walk walk)
    {
        while (walk.Raised is { Count: > 0 } raised)
        {
            var first = raised[0];
            raised.RemoveAt(0);
            if (!first.IsCompleted)
                await Suspended(walk, first);
        }
    }

    private async Task Suspended(Walk? walk, Task pending)
    {
        Current = walk?.ReturnsTo;
        try
        {
            await pending;
        }
        finally
        {
            if (walk is not null)
                walk.ReturnsTo = Current;
            Current = walk;
        }
    }

    private async Task<T> SuspendedFor<T>(Walk? walk, Task<T> pending)
    {
        await Suspended(walk, pending);
        return await pending;
    }

    private void Enqueue(Waiting item)
    {
        if (item.Coalescing == EventCoalescing.Timer
            && queue.FindIndex(w => string.Equals(w.Name, item.Name, StringComparison.OrdinalIgnoreCase)) >= 0)
            return;
        if (item.Coalescing == EventCoalescing.Latest && queue.Count > 0 && queue[^1].Coalescing == EventCoalescing.Latest)
            queue[^1] = item;
        else
            queue.Add(item);
        arrival?.TrySetResult();
    }

    private Waiting? Dequeue()
    {
        while (queue.Count > 0)
        {
            var next = queue[0];
            queue.RemoveAt(0);
            if (next.Coalescing == EventCoalescing.Timer && IsRunning(next.Name))
                continue;
            return next;
        }
        return null;
    }

    // Background, below input: so input the platform has delivered reaches its handlers, and queues, first.
    private static async Task YieldToInput() =>
        await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Background);
}
