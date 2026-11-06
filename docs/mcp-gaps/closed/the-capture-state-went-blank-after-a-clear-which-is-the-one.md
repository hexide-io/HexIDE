# The capture state went blank after a clear, which is the one thing it existed to prevent — **CLOSED** (moved from the live file 2026-09-22)

> **Closed by #393 (#369 phase 3).**

**Symptom.** `clear_lsp_capture` replied:

```
{"envelopesDiscarded":13,"state":{"armsEveryConnection":true,"connections":[]}}
```

The connection was alive and armed. `arm_lsp_capture` a moment earlier had listed it correctly.

**Cause.** Both mutating tools returned a state whose connection list was derived from the envelopes
present in the record — the same answer as the real list right up until somebody empties the record.

**Consequence, and why it is worse than a cosmetic wrong field.** The state is returned by those two tools
specifically so that arming is not invisible: a tool answering only "done" would leave an agent unable to
tell an armed connection from one whose id it had misspelled. After a clear it answered exactly that.
An agent clearing `hexide.vb6` and one clearing `hexide.vb` got identical replies.

**Fixed** by having the log name its own connections (`ConversationLog.ConnectionIds`) rather than
inferring them from traffic, which also makes a connection armed before it has started visible — the case
the launch flag depends on. Two tests pin it.

**Found on the first real use of the tools**, by driving them rather than by reading them. Both defects had
passing unit tests around them; neither could have been caught by one, because both are about what the
*schema* and the *reply* look like to a caller.
