# There is no way to ask what is being recorded without changing it — **CLOSED** (moved from the live file 2026-09-22)

> **Closed by #393 (#369 phase 3), which added `get_lsp_capture_state`.**

**Symptom.** To find out which connections existed and which were armed, the only tools were
`arm_lsp_capture` and `clear_lsp_capture` — both of which mutate. Reading the state meant arming something
first.

**Workaround used.** Call `arm_lsp_capture` with the state it already had, and read the reply.

**Fixed** by adding `get_lsp_capture_state`, which is the same reply with nothing changed.
