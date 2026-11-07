# Every parameter of a capture tool was required, including the ones that mean "no filter" — **CLOSED** (moved from the live file 2026-09-22)

> **Closed by #393 (#369 phase 3), which gave every optional parameter a C# default.**

**Symptom.** The first call to `list_lsp_messages` had to pass five arguments to ask the simplest possible
question. `connectionId`, `method`, `failuresOnly`, `afterSequence` and `limit` were all in the schema's
`required` array, so "list everything" could not be expressed as an empty call.

**Cause, and it is a C# detail with a schema consequence.** A nullable parameter with no default value is
still a *required* parameter to the MCP schema generator. `string? connectionId` is optional-looking in C#
and mandatory on the wire. `dump_visual_tree` got this right by accident of having defaults
(`string? root = null, int maxDepth = 20`), and the new tools did not.

**Workaround used.** Pass `null` explicitly for each. It works, and it is five arguments of noise on every
call, which is exactly the friction that makes an agent reach for a different tool.

**Fixed** by giving every optional parameter a C# default. Worth knowing for the next tool: check the
generated schema's `required` array, not the C# signature — they disagree, and only one of them is what an
agent sees.
