using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Serilog;

namespace HexIDE.Infrastructure;

/// <summary>
/// What the IDE does when Windows ends the session, at a sign-out, shutdown or restart (#4646).
/// <see cref="Win32SessionEndHook"/> carries Windows' two messages here.
/// </summary>
/// <remarks>
/// <para>
/// <b>Microsoft's contract</b> (the WM_QUERYENDSESSION and WM_ENDSESSION pages, and "Shutdown Changes for Windows
/// Vista"):
/// </para>
/// <list type="bullet">
/// <item>Answer the query at once.</item>
/// <item>Block only when the person has something to decide, and say why with <c>ShutdownBlockReasonCreate</c>.</item>
/// <item>Do the cleanup when <c>WM_ENDSESSION</c> says the session is ending. The process can be ended at any time
/// after that returns.</item>
/// </list>
/// <para>
/// <b>Why this isn't <c>ShutdownRequested</c>.</b> Avalonia raises the query as <c>ShutdownRequested</c>, and a request
/// can only veto or shut the app down inside the query. <see cref="ExitStop"/> must cancel the first request so it can
/// stop the language servers before exit, and on Windows that cancel was the reply: a veto of every sign-out (#4646).
/// </para>
/// <para>
/// <b>Unsaved work:</b> the IDE blocks with a reason naming the project, then shows the save prompt that File > Exit
/// shows. The prompt comes after the reply and is never held open inside it: after 5 s Windows covers such a prompt
/// with its own screen of the apps preventing sign-out.
/// </para>
/// <list type="bullet">
/// <item>Yes or No closes the IDE as File > Exit does.</item>
/// <item>Cancel keeps it open.</item>
/// <item><b>The limit:</b> a person who picks Windows' "Sign out anyway" while the prompt is up has chosen to lose the
/// unsaved work. It is still backed up for the next start (#499), as on a terminate signal.</item>
/// </list>
/// <para>
/// <b>Another limit: at a real sign-out, Windows may end a server first.</b> A launched server is started with a hidden
/// console (<c>CreateNoWindow</c>), and Windows ends a console's processes through it, independently of this window. On a
/// Win10 VM sign-out, the stand-in server ended with 0x40010004 8 ms after <c>WM_ENDSESSION</c> arrived, before anything was
/// sent. So the stop reaches only the servers still running. Before #4554 none of them got <c>shutdown</c> or
/// <c>exit</c> at sign-out either. Launching servers without a console is #4666.
/// </para>
/// <para>
/// <b>It ends them even when the sign-out doesn't happen.</b> On the VM, a person who chose Cancel on Windows' screen kept
/// the session, and the IDE with its unsaved work, but its servers were already gone, 15 ms after the IDE answered. So
/// <see cref="OnQuery"/> notes which servers are running, and when <c>WM_ENDSESSION</c> says the session goes on,
/// <see cref="OnContinueAsync"/> restarts those of them that have stopped since. Windows' kill can also come first: on
/// the VM a server ended 8 ms before the IDE handled the query. So the note also takes in the servers that were running
/// at the last <see cref="Observe"/>, made every <see cref="ObserveEvery"/>. The exception stays bounded by the sign-out
/// attempt: a server that crashed before that observation stays stopped under the usual rule, so that one that crashes
/// on start is not started over and over.
/// </para>
/// </remarks>
internal sealed class SessionEnd(
    Func<string?> unsavedProjectName,
    Func<string, string> blockReason,
    Action showSavePrompt,
    Func<Task> stop,
    Action<Task, TimeSpan> waitOnUiThread,
    Action afterStop,
    TimeSpan bound,
    Func<IReadOnlyList<string>> runningServers,
    Func<string, Task> restartServer)
{
    /// <summary>
    /// Windows waits 5 s for <c>WM_ENDSESSION</c> before it offers to end the app anyway, so the stop finishes inside
    /// that. A wedged server is ended when the process is.
    /// </summary>
    internal static readonly TimeSpan EndBound = TimeSpan.FromSeconds(4.5);

    /// <summary>
    /// How long a continuing session waits before it looks for servers Windows ended: they went 15 to 17 ms after the
    /// IDE answered on the VM, and the wait keeps a late one from being missed.
    /// </summary>
    internal static readonly TimeSpan ContinueSettle = TimeSpan.FromSeconds(1);

    private bool _ended;
    private IReadOnlyList<string>? _runningAtQuery;
    private IReadOnlyList<string> _runningAtLastLook = [];

    /// <summary>How often <see cref="Observe"/> notes the running servers.</summary>
    internal static readonly TimeSpan ObserveEvery = TimeSpan.FromSeconds(2);

    /// <summary>Notes which servers are running, so a query just after Windows ended one still knows it was.</summary>
    internal void Observe()
    {
        if (!_ended) _runningAtLastLook = runningServers();
    }

    /// <summary>Whether any loaded project has work the save prompt would offer to save.</summary>
    internal bool HasUnsavedWork => unsavedProjectName() is not null;

    /// <summary><c>WM_QUERYENDSESSION</c>. Returns null to let the session end, or the reason the IDE blocks it.</summary>
    /// <param name="critical">
    /// <c>ENDSESSION_CRITICAL</c>: the session ends whatever the reply is, so a prompt would only be cut off.
    /// </param>
    internal string? OnQuery(bool critical)
    {
        if (_ended) return null;
        _runningAtQuery = _runningAtLastLook.Union(runningServers()).ToList();
        if (critical) return null;
        if (unsavedProjectName() is not { } project) return null;
        Avalonia.Threading.Dispatcher.UIThread.Post(showSavePrompt);
        return blockReason(project);
    }

    /// <summary>
    /// <c>WM_ENDSESSION</c> with the session ending. Stops the language servers before it returns, within
    /// <see cref="EndBound"/>, and then lets the IDE close. Nothing is done when the session goes on.
    /// </summary>
    internal void OnEnd()
    {
        if (_ended) return;
        _ended = true;
        Task task;
        try
        {
            task = stop();
        }
        catch (Exception ex)
        {
            task = Task.FromException(ex);
        }

        try
        {
            waitOnUiThread(task, bound);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Waiting for the language servers to stop at the end of the Windows session failed");
        }

        if (!task.IsCompleted)
            Log.Warning("The language servers had not stopped {Bound} after the Windows session ended; closing anyway",
                bound);
        else if (task.Exception is { } failure)
            Log.Error(failure, "Stopping the language servers at the end of the Windows session failed");
        else
            Log.Information("Language servers stopped for the end of the Windows session");

        afterStop();
    }

    /// <summary>
    /// <c>WM_ENDSESSION</c> with the session going on: after <paramref name="settle"/>, restarts each server that was
    /// running at the query and has stopped since, as Windows' console handling ends them at the start of a sign-out the
    /// person or another app then cancelled.
    /// </summary>
    internal async Task OnContinueAsync(TimeSpan settle)
    {
        if (_ended || _runningAtQuery is not { } wanted) return;
        _runningAtQuery = null;
        await Task.Delay(settle);
        if (_ended) return;
        var running = runningServers();
        var ended = wanted.Where(id => !running.Contains(id)).ToList();
        foreach (var id in ended)
        {
            try
            {
                await restartServer(id);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Restarting language server {Id} after a sign-out that didn't happen failed", id);
            }
        }

        if (ended.Count > 0)
            Log.Information("Windows session end: the session went on; restarted {Count} language server(s) it ended: {Ids}",
                ended.Count, ended);
    }

    /// <summary>
    /// Waits for <paramref name="task"/> on the UI thread for up to <paramref name="bound"/>, running the dispatcher in a
    /// nested frame meanwhile. A stop that posts back to the UI thread then finishes; a blocking wait would hold it to
    /// the bound.
    /// </summary>
    internal static void WaitInNestedFrame(Task task, TimeSpan bound)
    {
        if (task.IsCompleted) return;
        var dispatcher = Avalonia.Threading.Dispatcher.UIThread;
        var frame = new Avalonia.Threading.DispatcherFrame();
        _ = Task.WhenAny(task, Task.Delay(bound)).ContinueWith(
            _ => dispatcher.Post(() => frame.Continue = false, Avalonia.Threading.DispatcherPriority.Send),
            TaskScheduler.Default);
        dispatcher.PushFrame(frame);
    }
}
