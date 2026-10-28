# A tool's schema, once loaded in a session, stays stale after `/mcp`, in subagents too (measured 2026-10-12)

**Symptom.** A rebuild changed two descriptions and added a tool, and the user ran `/mcp`:

```
ToolSearch {"query":"select:mcp__hexide__get_debug_state,mcp__hexide__get_window_state,mcp__hexide__zz_probe_added"}
→ get_debug_state:   the OLD description   (this session had loaded it before the rebuild)
→ get_window_state:  the NEW description   (this session had never loaded it)
→ zz_probe_added:    present               (added in the rebuild)
```

A fresh subagent then asked `ToolSearch` for `get_debug_state` and got the old description too, so the cache
belongs to the Claude Code session rather than to one conversation. The running DLL held the new text, and
`zz_probe_added` answered, so the server was the new build. Claude Code 2.1.296.

**Found by** another session, which saw the same after a compaction: `add_file` and `set_file_content` read as
they are on `main` while the running build was a branch that extends both descriptions, and the tools' replies
were the branch's. That session had loaded both schemas before its compaction.

**Why it matters.** What a session believes about a tool is how it calls it. A description edited for a
first-time caller cannot be judged in the session that edited it, and a changed signature produces calls
built from the old one. That is how the 2026-09-20 entry (archived) failed: a tool gained a parameter, and the
first call after the rebuild failed against the stale schema.

**Workaround.** Check a changed tool in a new session, or headlessly. Do not rely on `/mcp` or a subagent to
refresh it. A tool the session has not loaded yet is safe to load after `/mcp`.

**Suggested fix.** The client's: drop cached schemas for a server when it reconnects, or when its `tools/list`
changes. Nothing on the server side can reach a schema the client already holds.
[#815](https://github.com/hexide-io/HexIDE/issues/815).
Its issue, #815, is closed, and no HexIDE issue is open for what remains, which is the client's (#645).
