# A taken `--server-port` exits without running the shutdown handlers (#547) — **CLOSED** (#559, 2026-09-22)

> **Closed (#559).** The forced exit now runs, by hand and in order, the cleanup `ShutdownRequested` would
> have run. The add-in loader is disposed first, so a consent dialog closed by the exit records nothing;
> then the server context is cancelled and disposed. A throw from any of that is logged and the exit still
> happens with its code, and the log is closed after the windows. `ConsentAtShutdownTests` pins both
> directions at the registry: a late refusal records nothing, and a late Allow neither records nor loads.
>
> **The entry's premise was partly wrong.** Avalonia's forced `Shutdown` still closes every window, so the
> main window's `Closing` handler runs and saves window state and layout; it only loses its veto. A failed
> launch therefore still writes the profile it shares, which is part of #557.
>
> **Regression coverage (#563).** `DesktopStartup` now delegates its posted cleanup to the internal
> `ForcedExitCleanup` helper. `ForcedExitCleanupTests` pins loader disposal before exit, exit code 3
> despite cleanup failures, and log closure after exit; deleting or reordering those steps fails the tests.

**Symptom.** From reading `DesktopStartup.cs`: exit code 3 (#525) goes through Avalonia's forced
`desktop.Shutdown(exitCode)`, which does not raise `ShutdownRequested`. The add-in loader is never disposed,
and a third-party add-in's consent prompt, opened from the same `MainWindow.Opened` as the server start, can
then be recorded as **Block** when the closing dialog returns. The IDE's own shutdown work and the server
context's disposal are skipped too.

**Workaround.** None needed for automation itself: the exit code is right. Don't launch into a taken port:
check that `/health` stops answering before relaunching, as the rebuild cycle says. A fresh `--user-data-dir`
does not help. A new profile asks about every third-party add-in again, which makes an open consent prompt
more likely, not less.

**Fix.** Exit through `TryShutdown(exitCode)`, or run the cleanup before the forced shutdown.
