using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;

namespace HexIDE.Automation;

/// <summary>
/// A button <c>click … to x,y hold</c> left down, for a later <c>release</c> to let go (#2231). A drag that pressed, moved
/// and released in one call could show nothing that exists only while the button is down: the designer's draw preview and
/// rubber band, a control's outline as it moves, Dock's drop targets, a running form's MouseMove with a button held.
/// </summary>
/// <remarks>
/// <para>
/// There is one, as there is one mouse. While it is down a second press, or a move from another pointer, is input no mouse
/// can make, so <c>click</c>, <c>double_click</c> and <c>hover</c> are refused; keys still go, since Escape in mid-drag is
/// worth testing. Every tool's reply says it is held, through the server's call filter, so no tool has to remember to.
/// </para>
/// <para>
/// After <see cref="IdleLimit"/> with no call it is abandoned, not released where it is: a release then would commit a drop
/// nobody asked for, such as placing a control or docking a window long after anyone was looking (decided on #2231). The
/// abandon sends Escape, moves back to where the press was and lets go there, and the next reply says what the surface did
/// with that, which is not always nothing (<see cref="UiAutomationDriver.AbandonHeldAsync"/>).
/// </para>
/// </remarks>
public sealed class HeldButton
{
    private readonly Func<Point, Point, IReadOnlyList<Action>> movesBetween;
    private readonly Func<Point, string, InteractOutcome> releaseAt;
    private readonly Func<string> capture;
    private readonly long since = Stopwatch.GetTimestamp();

    internal HeldButton(TopLevel window, IPointer pointer, string button, Point pressedAt, string pressedWhere, Point at,
        string atWhere, Func<Point, Point, IReadOnlyList<Action>> movesBetween, Func<Point, string, InteractOutcome> releaseAt,
        Func<string> capture, object? surface)
    {
        Window = window;
        Pointer = pointer;
        Button = button;
        PressedAt = pressedAt;
        PressedWhere = pressedWhere;
        At = at;
        AtWhere = atWhere;
        Surface = surface;
        this.movesBetween = movesBetween;
        this.releaseAt = releaseAt;
        this.capture = capture;
    }

    /// <summary>The window the button is held in. A release's point must be in it.</summary>
    public TopLevel Window { get; }

    internal IPointer Pointer { get; }

    /// <summary>left, right or middle.</summary>
    public string Button { get; }

    /// <summary>Where the press was, in window coordinates, and in words. An abandoned hold is let go here.</summary>
    public Point PressedAt { get; }

    internal string PressedWhere { get; }

    /// <summary>Where the button is held now, in window coordinates, and in words.</summary>
    public Point At { get; }

    internal string AtWhere { get; }

    /// <summary>What the press landed in that can say what a release did, such as the designer's view model; or null.</summary>
    internal object? Surface { get; }

    /// <summary>The button held now, or null. Read and written on the UI thread.</summary>
    public static HeldButton? Current { get; private set; }

    /// <summary>How long a hold lasts with no tool call before it is abandoned. Shortened by tests.</summary>
    public static TimeSpan IdleLimit { get; internal set; } = TimeSpan.FromSeconds(120);

    /// <summary>The idle clock and its timer. Replaced by tests, to make the timer fire before the clock agrees (#2514).</summary>
    internal static TimeProvider Time
    {
        get => time;
        set
        {
            lock (Gate)
            {
                idle?.Dispose();
                idle = null;
                time = value;
            }
        }
    }

    private static readonly Lock Gate = new();
    private static TimeProvider time = TimeProvider.System;
    private static ITimer? idle;
    private static long lastCall;
    // What became of a hold that ended without a release, for the next reply, once.
    private static string? ended;

    internal static void Begin(HeldButton held)
    {
        Current = held;
        if (held.Window is Window window)
            window.Closed += held.OnWindowClosed;
        lock (Gate)
        {
            lastCall = time.GetTimestamp();
            Schedule();
        }
    }

    /// <summary>
    /// Restarts the idle clock. The server's call filter calls this at the start and the end of every tool call, so a
    /// hold is abandoned only once nothing at all has been asked of the IDE for <see cref="IdleLimit"/>.
    /// </summary>
    public static void Touch()
    {
        lock (Gate)
        {
            lastCall = time.GetTimestamp();
            if (Current is not null)
                Schedule();
        }
    }

    // Under Gate. The abandon checks the hold is still idle when it runs, so a call that lands after the timer fires wins.
    private static void Schedule()
    {
        idle ??= time.CreateTimer(state => Dispatcher.UIThread.Post(
            () => _ = UiAutomationDriver.AbandonHeldAsync(idleOnly: true), DispatcherPriority.Input),
            null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        idle.Change(IdleLimit, Timeout.InfiniteTimeSpan);
    }

    /// <summary>
    /// Whether <see cref="IdleLimit"/> has passed since the last call, and if it has not, arms the timer again for what is
    /// left. A call that came in after the timer fired keeps the hold, and has armed the timer already. But a timer can also
    /// fire a few microseconds before the clock says the limit has passed, 299.986 ms against 300 in one measurement, and
    /// with nothing to arm it again that hold was never abandoned (#2514). What is left is rounded up to a whole
    /// millisecond, the timer's resolution, so a sliver is not armed as zero.
    /// </summary>
    internal static bool IdleElseRearm()
    {
        lock (Gate)
        {
            var left = IdleLimit - time.GetElapsedTime(lastCall);
            if (left <= TimeSpan.Zero)
                return true;
            if (Current is not null)
                idle?.Change(TimeSpan.FromMilliseconds(Math.Ceiling(left.TotalMilliseconds)), Timeout.InfiniteTimeSpan);
            return false;
        }
    }

    /// <summary>The moves from where the button is held to <paramref name="to"/>, for <see cref="UiAutomationDriver.FinishAsync"/>.</summary>
    internal IReadOnlyList<Action> MovesTo(Point to) => movesBetween(At, to);

    /// <summary>The moves back to where the press was, for an abandon.</summary>
    internal IReadOnlyList<Action> MovesBack() => movesBetween(At, PressedAt);

    /// <summary>Lets go at <paramref name="point"/>, as the drag's own release does. <see cref="End"/> it first.</summary>
    internal InteractOutcome ReleaseAt(Point point, string where) => releaseAt(point, where);

    /// <summary>Stops this being the held button, so nothing else lets it go or abandons it. The release itself is still to come.</summary>
    internal void End()
    {
        if (ReferenceEquals(Current, this))
            Current = null;
        if (Window is Window window)
            window.Closed -= OnWindowClosed;
        lock (Gate)
            idle?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    /// <summary>Records what became of a hold that ended without a release, for the next reply.</summary>
    internal static void Ended(string what) => ended = ended is null ? what : $"{ended} {what}";

    // A closed window takes no release, and its pointer goes with it.
    private void OnWindowClosed(object? sender, EventArgs e)
    {
        End();
        Pointer.Capture(null);
        Ended($"The held {Button} button was dropped without a release at {Clock()}: its window '{Title(Window)}' closed.");
    }

    /// <summary>The refusal for a verb that would press again, or move a second pointer, while this is held.</summary>
    internal string Refusal(string verb) =>
        $"the {Button} button is still held from click … hold, at {AtWhere}, so nothing was done: {verb} would press again "
        + "or move another pointer, which a mouse can't do while its button is down. interact 'release' lets it go first";

    /// <summary>
    /// What a reply says about a held button: that one is held, and where; or, once, what became of one that ended without
    /// a release. Null when there is nothing to say. Called on the UI thread.
    /// </summary>
    public static string? TakeNote()
    {
        var note = Current?.Note();
        if (ended is { } what)
        {
            note = note is null ? what : $"{what} {note}";
            ended = null;
        }
        return note;
    }

    private string Note() =>
        $"The {Button} button is held at {AtWhere}, in '{Title(Window)}', "
        + $"{Stopwatch.GetElapsedTime(since).TotalSeconds.ToString("0", CultureInfo.InvariantCulture)} s after click … hold pressed it; "
        + $"{capture()}. interact 'release' lets it go, with a value moving there first. Until then click, double_click and "
        + "hover are refused, and keys still go. With no call for "
        + $"{IdleLimit.TotalSeconds.ToString("0", CultureInfo.InvariantCulture)} s it is abandoned: Escape, then a release where it was pressed.";

    internal static string Clock() => DateTime.UtcNow.ToString("HH:mm:ss", CultureInfo.InvariantCulture) + " UTC";

    internal static string Title(TopLevel window) => (window as Window)?.Title is { Length: > 0 } title ? title : window.GetType().Name;
}
