# A relaunch picks up a NEW tool but not a CHANGED one (measured 2026-09-20) — **CLOSED** (superseded by #763, 2026-10-12)

> **Since #763 a relaunch picks up neither.** The server is stateless, so the re-initialise that used to
> bring a new tool in no longer happens. Both now need `/mcp`. The transferable part below, a nullable
> parameter with no default being `required` on the wire, is unaffected.

**Symptom.** Four mark tools gained an optional `project` argument. The documented rebuild cycle ran —
`shutdown_ide`, build, relaunch, `/health` — and the first call to `set_breakpoints` failed with
`An error occurred invoking 'set_breakpoints'.` and nothing else. The session's cached schema still carried
the old parameter list, so the request it built no longer matched what the server accepted.

**What this narrows.** The entry above records that a mid-session relaunch **does** pick up a tool that did
not exist before, measured both ways. This is the other half: a tool whose *signature* changed keeps the
schema the session already has. Added tools yes; changed tools no. The failure is a bare invocation error,
which reads as a broken tool rather than as a stale schema.

**The actual defect underneath it, which is the transferable part.** `string? project` with **no default
value** is `required` in the generated schema — a nullable C# parameter is not an optional JSON one. So the
new argument was mandatory on the wire and every existing caller broke, cached schema or not. Giving all six
`= null` made them genuinely optional, and the same call then worked on the first try after a rebuild.

**How to avoid paying for it again.** Read the generated schema's `required` array, not the C# signature —
this file and CLAUDE.md both say so, and it still cost a cycle. And when a *signature* changes rather than a
tool being added, expect the first call to fail against a stale schema and do not diagnose the server.

**Since #603.** A throw inside a tool is now reported with its exception type and message rather than the bare
line. Not re-measured for this case: a call that does not match the tool's parameters may fail in the SDK's
own argument binding, which can report through `McpException`, and #603 leaves that as the SDK reports it.
