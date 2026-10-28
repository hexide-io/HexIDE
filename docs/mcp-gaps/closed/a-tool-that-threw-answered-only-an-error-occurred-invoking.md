# A tool that threw answered only "An error occurred invoking" — **CLOSED** (#603, 2026-09-22)

> **Fixed.** A call-tool filter (`ToolFailures`, registered in `IdeServer`) catches any exception a tool did
> not catch itself and answers with an MCP error result naming the tool, the exception type and its message.
> It says this is a defect to report, not a refusal of the arguments, and points to the IDE log, where the
> stack is written under `MCP tool <name> failed`. It names the log folder as each platform does. The user's
> profile directory is replaced with `%USERPROFILE%` (or `~`) in the reply, in its long and 8.3 short forms,
> because the reply asks to be pasted into an issue; the log keeps the message as thrown. `McpException`, and
> a cancellation the caller asked for, are left to the SDK. A tool's own timeout surfacing as a cancellation
> is reported like any other throw. An `AggregateException` holding one exception reports that one. It is a
> backstop: each throw it reveals is still a defect in its tool, and entries recording one stay open until
> that throw is fixed.

**Symptom.** `set_window_state {"state":"Normal","width":-50}` (x, y, height left at null) on `main` at
3eb6396 answered `An error occurred invoking 'set_window_state'.` and nothing else. The same line was
recorded separately for `take_snapshot` with a menu open and for `set_breakpoints` against a stale schema.
Each time the cause had to be dug out of the log or the source.

**After.** The same call answers `'set_window_state' failed with an unhandled System.ArgumentException: -50
is not a valid value for 'Width'. This is a defect in the tool, not a refusal of your arguments: please
report it. The stack trace is in the IDE log (%LOCALAPPDATA%\HexIDE\logs\ide), under 'MCP tool
set_window_state failed'.` The log holds `[ERR] MCP tool set_window_state failed with an unhandled
ArgumentException` and the stack. That throw itself is #602.

With the IDE's `TMP` and `TEMP` pointed at a folder under the profile, and a directory standing where
`export_lsp_conversation` writes its `.jsonl`, `export_lsp_conversation {}` answers `… System.UnauthorizedAccessException:
Access to the path '%USERPROFILE%\AppData\Local\Temp\…\hexide_lsp_conversation.jsonl' is denied. …`. That
held for both the short and the long spelling of the profile, and the log line for each carried the real path.

**Exposure.** An exception message can carry local paths. The server already returns absolute paths and
whole documents to any local caller, and it is DEBUG-only and loopback-only with `Host` and `Origin`
checked. The stack trace is kept out of the reply. Authentication stays #352.
