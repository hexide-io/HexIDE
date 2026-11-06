# `list_lsp_messages` `failuresOnly` omits a message that could not be serialized

**Symptom.** With a serialization failure at seq 183 in the record (#944) (Linux, openSUSE Leap 16 under XWayland,
main at b15c7cf, 2026-10-13):

```
list_lsp_messages {"failuresOnly":true,"limit":20}
→ {"messages":[],"matched":0,"note":"118 envelope(s) are recorded, and none match this filter. Drop the filters to see what is there."}
```

The record held:

```
{"sequence":183,"direction":"Local","kind":"NeverSent","method":"$/cancelRequest","detail":"could not be serialized: ..."}
```

The Protocol Inspector's Failures only filter and its failure count miss it the same
way: both count only error responses and the Failed, Cancelled and Abandoned outcomes. So a filter for finding
failures hides the client-side defect the capture was built to expose. Not every NeverSent row is a failure: a
request withheld because the server did not advertise the capability is recorded as a Sent NeverSent row,
deliberately. Cross-platform by code reading.

**Workaround.** Call `list_lsp_messages {}` with no filter and look for `kind:"NeverSent"` with
`direction:"Local"`.

**Suggested fix.** Count a Local NeverSent row as a failure in `CaptureQueries.IsFailure` and
`ProtocolInspectorRowViewModel.IsFailure`, and say so in `failuresOnly`'s description.
[#945](https://github.com/hexide-io/HexIDE/issues/945).

> **Fixed.** A failure is now one rule, `ConversationFailures.IsFailure`, read by both `list_lsp_messages`
> `failuresOnly` and the Protocol Inspector's Failures only filter and count, so they cannot disagree again. Besides
> error responses and the Failed, Cancelled and Abandoned outcomes, it counts an envelope its recorder marked
> `Fault`. The marked cases are:
>
> - a message this client could not serialize;
> - a frame it could not decode, though not the note saying a frame was repaired;
> - a connection it could not make;
> - a server that disconnected while the client was not stopping it;
> - a process that exited non-zero, unless this client was stopping it;
> - a debug adapter that could not start, or sent a frame this client could not read.
>
> The review found that keying the rule on direction was fragile: a withheld-capability row is filed as Local in two
> test fixtures and as Sent in production. It also found the same blind spot for undecodable frames and crashes. So
> the flag is set where each fault is known, and the row's direction and wording decide nothing.
>
> A row now carries `fault: true`, and so does an export's envelope. The inspector draws a failed local row in full
> rather than faint. `failuresOnly`'s description and the protocol-inspector spec list what counts. A debug adapter's
> connection that closed on an error is not marked, because a deliberate stop can close it the same way. Guarded by `ConversationFailuresTests`,
> `TheRecordSeesTheProcessTests` (each fault site through a real client, including stopping), `CaptureQueriesTests`,
> `ProtocolInspectorToolViewModelTests`, `ConversationExportTests`, `DapCaptureTests` and the process-notice tests.
>
> **Live (#1330, 2026-10-25).** Windows, a Debug build of main at beada72, from a Claude Code session's hexide tools.
> The setup: a session profile whose `lsp-servers.json` adds a `.txt` server whose `command` does not exist, and a
> project carrying `notes.txt`.
>
> ```
> open_file {"name":"notes.txt"}
> list_lsp_messages {"failuresOnly":true}
> → {"messages":[{"sequence":2,"connectionId":"broken-txt",…,"direction":"Local","kind":"Lifecycle",
>    "detail":"could not connect: could not start 'C:/nonexistent/no-such-server.exe' … The system cannot find the
>    file specified.","hasBody":false,"fault":true}],"matched":1,…}
> ```
>
> - **Without the filter**, the list adds sequence 1, `connecting`, with no `fault`.
> - **In Tools ▸ Protocol Inspector**, `interact` `toggle` on `CheckBox[Failures only]` replied "it is now on". The
>   counts read "1 shown" and "1 failed".
> - **`take_snapshot`** drew the row in full: its time, `•` and `broken-txt` in normal text, and the failure in bold
>   red in the Method column.
> - **The filtered dump** also lists a recycled grid row holding the same failure, marked `isHidden`:
>   [#1977](https://github.com/hexide-io/HexIDE/issues/1977). Fixed by excluding recycled rows in the
>   DataGrid rows presenter, including their stale cells. A headless regression filters two rows to one
>   and checks that the remaining row appears once and its path resolves. Live recheck: repeat the
>   filter and dump above; the one shown failure should have one DataItem, with no recycled duplicate.
>
> **Corrected later.** Two exits stayed out of the rule ([#1314](https://github.com/hexide-io/HexIDE/issues/1314)).
> A language server's non-zero exit while this client stopped it was excused as a kill, but no transport reports a kill
> it makes, so the excuse hid only the server's own exit code, which LSP makes its account of the stop. And a debug
> adapter's exit was never marked, though the IDE tells the user of a non-zero code, a kill and a failed cleanup.
> Both now count, by the rule the IDE tells the user by.
