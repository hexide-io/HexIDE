# A mid-session relaunch DOES pick up new tools, if the server was attached when the session started — **CLOSED** (superseded by #763, 2026-10-12)

> **Corrected 2026-10-12 (#763): the mechanism was not the attachment, and it no longer applies.**
>
> - **How new tools arrived.** A relaunched stateful server answered the session's next call with 404.
>   Claude Code re-initialised on that 404, and re-listed the tools as part of it.
> - **"Attached at session start" was incidental.** What mattered was that the session survived the
>   shutdown at all, which on Claude Code 2.1.282 and later depends on the age of its GET stream (entry 3).
> - **Now.** The server is stateless, so there is no 404, no re-initialise and no re-list. A tool added in
>   a rebuild needs a `/mcp` reconnect to appear.
>
> The measurements below stand as measurements.

**Measured, and it refines a rule this file and `CLAUDE.md` both state more strongly than is true.** The
documented rule is that MCP tools are discovered at session start and a schema change needs a session
restart. Both of these happened in one afternoon:

- Four new tools were added while **no IDE was running**. Building and launching did not surface them, and
  a session restart was needed. Consistent with the rule.
- A fifth was added later, with the IDE **running and attached since session start**. Shutting it down,
  rebuilding and relaunching surfaced the new tool immediately, as a deferred-tool notification, and it
  worked on the first call.

**What it concluded, now superseded.** "The distinguishing condition is whether the server was attached
when the session began", with the advice to try the relaunch before asking. Since #763 a relaunch brings in
no new tool, attached or not. After a rebuild that adds one, ask for a `/mcp` reconnect straight away.
