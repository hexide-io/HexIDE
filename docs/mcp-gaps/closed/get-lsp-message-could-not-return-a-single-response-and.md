# `get_lsp_message` could not return a single response, and nothing said so — **CLOSED** (moved from the live file 2026-09-22)

> **Closed by #431 (#429).**

**Symptom.** Pointing HexIDE at a foreign server and trying to read what it advertised. `list_lsp_messages`
showed the `initialize` request answered in 41 ms; `get_lsp_message` on that sequence returned the
**request**. There was no sequence that returned the reply, no field naming one, and no explanation — the
tool had exactly one address per exchange and it was the half already known.

**How it bit.** The whole investigation was about an `InitializeResult`: which capabilities the server
declared, and therefore which later messages were legal. Getting it meant writing a client outside the IDE
and driving the server over its own transport by hand — reintroducing, inside the tool built to destroy
the ambiguity, the exact work that tool exists to remove. Two other things went with it: an unarmed
connection could say a request was answered and not how large the answer was, and
`export_lsp_conversation` — a format whose claim is that it replays into a client — contained not one
reply, so the requests in it would hang.

**Why it survived.** `ConversationLog.TryComplete` pairs a response with its request, stamps the outcome
and the latency onto that request's envelope, and returned. Its comment explains half the decision
correctly and stops one step short: *"The response is not itself an entry. It is the second half of one,
and a timeline that showed both would double every request."* True about the **entry**, and taken as
license to drop the **body**. Everything downstream then read as working: the timeline was right, the
latencies were right, and the one thing missing was missing uniformly, so it looked like a design rather
than a hole. A caller who has never seen the record cannot tell "responses are not kept" from "this
response was not kept" from "I have asked the wrong question", which is the three-way ambiguity this
surface exists to destroy.

**Fixed** (hexide-io/HexIDE#429). A response keeps the sequence it was already allocated — responses have
always consumed one, which is why a listing has always had gaps and why this tool's description has always
had to explain that those gaps are not dropped frames. The request's row now names it, so the apology
becomes an address: `answerSequence` when a body was kept, and `answerSizeBytes` always, since a size is
metadata and belongs to the tier that runs unarmed. Passing an answer sequence to `get_lsp_message` returns
`isAnswer`, `answerTo`, and the request's method, because a response carries none on the wire. Both tool
descriptions say all of this, including which state a row with an outcome and no `answerSequence` is in.

**The general lesson is the one this file keeps re-learning.** The author knew responses completed their
requests; a first-time caller sees a sequence number that answers with the wrong half. Judge the tool by
what a model that has never seen it would do on first contact — and a reply that is structurally
unreachable must at minimum say so, rather than returning something plausible and adjacent.
