# 10. A crashed IDE is indistinguishable from a slow tool call (narrowed 2026-09-22)

> **Narrowed by #643.** An exception nothing caught is now written to the IDE log with its stack, and the log
> is flushed before the process ends, so the post-mortem no longer needs the Windows event log. Checked live
> with `HEXIDE_DEBUG_CRASH=thread` and `=ui` in a Debug build. **What remains** is the caller's side: once the
> process is dead it cannot answer, so the client still says `Unable to connect`, and the reader must know to
> look in the log. The four IDE-owned timer callbacks named in #652 now catch and log exceptions with
> their purpose and stack, remaining nonfatal. This covers dock-layout capture, the language revert
> countdown, the minimap redraw and the designer code-navigation follow-up; the DEBUG crash hook remains
> an unwrapped framework probe. VB6 Timer events keep their existing interpreter runtime-error handling
> (checked in the later #652 comment). WSL/Ubuntu headless probes on Avalonia 12 confirm that throwing Tick and Run
> callbacks execute without reaching `Dispatcher.UIThread.UnhandledException`; the three logging regressions
> (Tick, Run, RunOnce) each fail with an empty log when the guard is reduced to calling the body directly.

**Symptom.** Driving the designer to compose a screenshot, `add_control` returned success, then the next
call hung for the full 120 s timeout and every subsequent call failed with `Unable to connect. Is the
computer able to access the url?`. That message reads like a networking problem. The IDE had in fact
died — the tool call was fine, the process wasn't. The background-task notification said only
`transport dropped mid-call; response for tool "add_control" was lost`.

**Workaround.** When any tool starts failing to connect, check the process before changing approach:

```sh
curl -s -m 4 -o /dev/null -w '%{http_code}' "http://localhost:${HEXIDE_MCP_PORT:-5123}/health"
```

A 200 says it is alive. Anything else, with your own IDE's PID from `scripts/dev-ide.ps1` in hand, is checked with
`Get-Process -Id <pid>`. Not `Get-Process HexIDE.Desktop`: other sessions' IDEs share the name.

If it's gone, the IDE log (`%LOCALAPPDATA%\HexIDE\logs\ide\`) will end cleanly with no exception,
because an unhandled render-thread exception terminates the process before Serilog flushes. The actual
stack is in the Windows Application event log:

```sh
powershell -NoProfile -Command "Get-WinEvent -FilterHashtable @{LogName='Application'; ProviderName='.NET Runtime'; StartTime=(Get-Date).AddMinutes(-25)} | Where-Object { $_.Message -match 'HexIDE' } | Select-Object -First 1 -ExpandProperty Message"
```

That is how the `VBOptionButton` render crash was identified — nothing else surfaced it.

**Fix consideration.** Have the MCP layer distinguish "server unreachable" from "server was reachable and
has now gone", and surface the last few lines of the IDE log with the failure. A `--crash-log` style
handler that flushes Serilog on `AppDomain.UnhandledException` would also make the IDE log self-sufficient.
