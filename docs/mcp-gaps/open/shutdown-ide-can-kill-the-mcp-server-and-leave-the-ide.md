# shutdown_ide can kill the MCP server and leave the IDE running

**Symptom.** `shutdown_ide` replied `Unable to connect. Is the computer able to access the url?`. The
process was still alive afterwards with its window intact (`MainWindowTitle` unchanged), but `/health` now
answered `connection actively refused` — the web host had gone down while the application had not.

**Consequence.** The documented recovery for a failed shutdown is to call the tool again, and there is no
tool left to call. Worse, the state reads as "the IDE is gone" from the automation side and "the IDE is
fine" from the desktop, so the next build fails on a file lock for a process the agent believes it closed.

**Workaround used.** `Stop-Process -Name HexIDE.Desktop -Force`, then relaunch — then the documented
fallback for an MCP disconnect, but reached here *because of* the shutdown rather than before it. The fallback is
now `pwsh scripts/dev-ide.ps1 -Stop`, which finds the IDE by the `--server-port` on its command line, so it finds
one in this state, with no server listening, and stops it only if it was started for this session. Stopping by
name would end every session's IDE on a machine running several.

**Suggested fix.** Send the reply before tearing the host down (or stop the host last), so the caller gets
the confirmation the tool promises. Either way, do not leave a window up with no server behind it: if the
shutdown cannot complete, keep the server alive so the next call can say why. Not reproduced since 2026-09-22, so
what sets it off is unknown. [#644](https://github.com/hexide-io/HexIDE/issues/644).

**Ordering hardened for #644 (2026-10-25; live verification pending).** The main-window close now waits
for HTTP response completion, and server/context cleanup waits for desktop `Exit`, rather than the
cancellable `ShutdownRequested` event. Headless regression coverage checks response ordering and the
exit subscription. The original trigger remains unknown; keep this entry open until a running IDE check
confirms a complete reply followed by exit, and a cancelled close leaves `/health` and tools working.
