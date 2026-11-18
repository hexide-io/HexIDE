using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using Serilog;

namespace HexIDE.Infrastructure;

/// <summary>
/// Carries Windows' end-of-session messages to <see cref="SessionEnd"/>, ahead of Avalonia (#4646).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a subclass of Avalonia's own window.</b> Avalonia 12.1.6 answers <c>WM_QUERYENDSESSION</c> on the hidden
/// window its Win32 platform creates (<c>Win32Platform.WndProc</c>). It raises the query as
/// <c>ShutdownRequested</c>, and replies 0 when any handler cancels. Not cancelling makes the lifetime shut the app
/// down inside the query, before Windows says whether the session really ends. Nothing in Avalonia handles
/// <c>WM_ENDSESSION</c>.
/// </para>
/// <para>
/// Avalonia's one public hook, <c>Win32Properties.AddWndProcHookCallback</c>, takes a <c>TopLevel</c>, so it can't reach
/// that window. So this replaces the window's procedure (<c>SetWindowLongPtr</c>, <c>GWLP_WNDPROC</c>) and answers the
/// two messages itself. Every other message goes on to Avalonia.
/// </para>
/// <para>
/// <b>The internal it depends on:</b> the window's class name, <see cref="PlatformWindowClassPrefix"/>.
/// <c>PlatformWindowProbeTests</c> fails if an Avalonia upgrade stops creating that window. If it can't be found at
/// run time, <see cref="Install"/> logs it once and falls back to clearing the cancel from a late subscriber to the
/// platform's <c>ShutdownRequested</c>. That fallback answers 1 and lets the IDE close itself after its usual exit,
/// even if another app later vetoes the session. A missing hook must never mean a veto.
/// </para>
/// <para>
/// <b>Upstream:</b> Avalonia's tracker had nothing for Windows' end-of-session messages on 2026-11-18:
/// </para>
/// <list type="bullet">
/// <item>#6310 (2021) added today's query handling.</item>
/// <item>PR #22711, still open, adds the same kind of deferral for macOS termination only.</item>
/// </list>
/// </remarks>
[SupportedOSPlatform("windows")]
internal sealed class Win32SessionEndHook
{
    /// <summary>Avalonia's Win32 platform names its hidden window "AvaloniaMessageWindow " and a GUID.</summary>
    internal const string PlatformWindowClassPrefix = "AvaloniaMessageWindow";

    private const uint WmQueryEndSession = 0x0011;
    private const uint WmEndSession = 0x0016;
    private const long EndSessionCritical = 0x40000000;
    private const int GwlpWndProc = -4;

    private readonly SessionEnd _sessionEnd;
    private readonly Func<nint> _mainWindow;
    // Held so the delegate Windows calls is never collected.
    private readonly WndProc _proc;
    private nint _previous;
    private bool _blocking;

    private Win32SessionEndHook(SessionEnd sessionEnd, Func<nint> mainWindow)
    {
        _sessionEnd = sessionEnd;
        _mainWindow = mainWindow;
        _proc = Proc;
    }

    /// <summary>The hook in place for this process, kept alive with it.</summary>
    private static Win32SessionEndHook? s_installed;

    /// <summary>
    /// Installs the hook on the calling thread, which must be Avalonia's UI thread. Returns how it is answering.
    /// </summary>
    /// <param name="mainWindow">The main window's handle, which <c>ShutdownBlockReasonCreate</c> needs: the reason
    /// is shown only for a visible window.</param>
    internal static string Install(SessionEnd sessionEnd, Func<nint> mainWindow,
        IClassicDesktopStyleApplicationLifetime desktop)
    {
        var hwnd = FindPlatformWindow();
        if (hwnd != 0)
        {
            var hook = new Win32SessionEndHook(sessionEnd, mainWindow);
            var procPointer = Marshal.GetFunctionPointerForDelegate(hook._proc);
            Marshal.SetLastPInvokeError(0);
            var previous = SetWindowProc(hwnd, procPointer);
            if (previous != 0)
            {
                hook._previous = previous;
                s_installed = hook;
                Log.Information("Windows session end: answered on Avalonia's platform window {Window}", hwnd);
                return "subclass";
            }

            Log.Error("Windows session end: replacing the procedure of Avalonia's platform window {Window} failed " +
                      "with error {Error}", hwnd, Marshal.GetLastPInvokeError());
        }
        else
        {
            Log.Error("Windows session end: no {Prefix} window on the UI thread, so Avalonia's handling has changed " +
                      "(#4646)", PlatformWindowClassPrefix);
        }

        return InstallFallback(sessionEnd) ? "fallback" : "none";
    }

    /// <summary>
    /// The fallback: a subscriber added after the lifetime's own, which clears its cancel so the reply is 1 when
    /// nothing is unsaved. The IDE then closes through its usual exit, which has already begun.
    /// </summary>
    private static bool InstallFallback(SessionEnd sessionEnd)
    {
        try
        {
            // AvaloniaLocator and the platform's ShutdownRequested are left out of Avalonia's reference assemblies
            // ([PrivateApi], [Unstable]); at run time both are public. Reflection reaches them only on this path, which
            // is itself reached only once Avalonia's internals have changed.
            var locator = typeof(Avalonia.AvaloniaLocator)
                .GetProperty("Current", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            var events = locator?.GetType().GetMethod("GetService", [typeof(Type)])
                ?.Invoke(locator, [typeof(IPlatformLifetimeEventsImpl)]);
            var shutdownRequested = typeof(IPlatformLifetimeEventsImpl).GetEvent("ShutdownRequested");
            if (events is null || shutdownRequested is null)
            {
                Log.Error("Windows session end: no platform lifetime events either; the IDE will veto sign-out");
                return false;
            }

            shutdownRequested.AddEventHandler(events, new EventHandler<ShutdownRequestedEventArgs>((_, e) =>
            {
                if (e.Cancel && !sessionEnd.HasUnsavedWork) e.Cancel = false;
            }));
            Log.Warning("Windows session end: using the fallback; the IDE allows a sign-out and then closes itself");
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Windows session end: the fallback could not be installed; the IDE will veto sign-out");
            return false;
        }
    }

    /// <summary>Avalonia's hidden platform window on the calling thread, or 0.</summary>
    internal static nint FindPlatformWindow()
    {
        nint found = 0;
        var name = new StringBuilder(256);
        EnumThreadWindows(GetCurrentThreadId(), (hwnd, _) =>
        {
            name.Clear();
            if (GetClassName(hwnd, name, name.Capacity) > 0 &&
                name.ToString().StartsWith(PlatformWindowClassPrefix, StringComparison.Ordinal))
            {
                found = hwnd;
                return false;
            }

            return true;
        }, 0);
        return found;
    }

    private nint Proc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        if (msg == WmQueryEndSession)
        {
            try
            {
                var reason = _sessionEnd.OnQuery(critical: ((long)lParam & EndSessionCritical) != 0);
                if (reason is null) return 1;
                _blocking = ShutdownBlockReasonCreate(_mainWindow(), reason);
                Log.Information("Windows session end: blocked for unsaved work ({Reason})", reason);
                return 0;
            }
            catch (Exception ex)
            {
                // Never veto by accident.
                Log.Error(ex, "Windows session end: answering the query failed; allowing it");
                return 1;
            }
        }

        if (msg == WmEndSession)
        {
            try
            {
                if (wParam == 0)
                {
                    // The session goes on: another app, or the person, kept it.
                    Log.Information("Windows session end: the session goes on");
                    if (_blocking) ShutdownBlockReasonDestroy(_mainWindow());
                    _blocking = false;
                    _ = _sessionEnd.OnContinueAsync(SessionEnd.ContinueSettle);
                }
                else
                {
                    Log.Information("Windows session end: the session is ending (0x{Flags:X})", (long)lParam);
                    _sessionEnd.OnEnd();
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Windows session end: handling WM_ENDSESSION failed");
            }

            return 0;
        }

        return CallWindowProc(_previous, hwnd, msg, wParam, lParam);
    }

    private static nint SetWindowProc(nint hwnd, nint proc) =>
        IntPtr.Size == 8 ? SetWindowLongPtr64(hwnd, GwlpWndProc, proc) : SetWindowLong32(hwnd, GwlpWndProc, (int)proc);

    private delegate nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam);

    private delegate bool EnumWindowsProc(nint hwnd, nint lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumThreadWindows(uint threadId, EnumWindowsProc callback, nint lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll", EntryPoint = "GetClassNameW", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(nint hwnd, StringBuilder name, int capacity);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern nint SetWindowLongPtr64(nint hwnd, int index, nint value);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern int SetWindowLong32(nint hwnd, int index, int value);

    [DllImport("user32.dll", EntryPoint = "CallWindowProcW")]
    private static extern nint CallWindowProc(nint previous, nint hwnd, uint msg, nint wParam, nint lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool ShutdownBlockReasonCreate(nint hwnd, string reason);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool ShutdownBlockReasonDestroy(nint hwnd);
}
