# Polling `list_lsp_messages` with the highest sequence seen never shows a waiting request answered

**Symptom.** While re-recording [`mcp-first-contact.md`](../../mcp-first-contact.md) for #273 on 2026-10-13, in a fresh
`--newproject` session, a request seen waiting never came back answered when polled as the description advises:

```
open_file {"name":"Form1"}
list_lsp_messages {}
→ six rows; sequence 4 is initialize, with no outcome; 6 is the highest
list_lsp_messages {"afterSequence":6,"limit":3}
→ rows 12, 13, 15 ("matched":7); initialize is not among them
list_lsp_messages {"afterSequence":3}
→ row 4 comes back "outcome":"Answered", "answerSequence":8
```

The description says to pass "the highest one already seen to poll for what is new". A reply does not add a row:
it completes its request's row and uses up a sequence. That row's sequence is below the one a poller passes, so
the filter (`CaptureQueries`, `envelope.Sequence <= after`) drops it. The poller sees what followed the request
and never sees the request answered.

**Workaround.** Pass a sequence below the oldest request still waiting, and read each request's row again.

**Suggested fix.** Match a row that was completed after `afterSequence` as well as one recorded after it, keyed
on the sequence its reply consumed, which exists whether or not the body was kept. Say in the description that
a completed request comes back on its own row. [#961](https://github.com/hexide-io/HexIDE/issues/961).

> **Fixed.** A request recorded before `afterSequence` and answered, cancelled or abandoned after it now comes back,
> on its own row with its outcome and the `completedSequence` that outcome was recorded under: the sequence its
> reply used up, kept or not, or one an ending takes. The reply carries `latestSequence`, the cursor to pass next,
> rather than leaving the poller to take the highest row seen, which for a request answered since is older than its
> answer and would return it again on every poll. The cursor is the pump's watermark at the drain, and the listing
> is read as it stood there: the snapshot reads one ring at a time while the pump carries on, so a cursor taken from
> what was read skipped rows another connection recorded meanwhile, hundreds in two hundred thousand. A poll cut
> short by `limit` keeps the earliest changes, and its cursor continues from them, so following it pages through
> without loss; a listing that is not a poll still keeps the newest rows. A cursor beyond anything recorded, from an
> earlier run of the IDE, lists this run from its start and says why. `get_lsp_message` on a sequence a request was
> answered or ended under says so rather than calling it missing. Both descriptions and the walkthrough in
> `mcp-first-contact.md` say to poll from `latestSequence`. Guarded by `CaptureQueriesTests`, including a
> three-connection stress poll that the old cursor fails. That an outcome recorded after the watermark is held back
> to the next poll, rather than shown now and again then, is by reading.
