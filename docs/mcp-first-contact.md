# First contact with the automation surface

What a caller who has never seen HexIDE's MCP tools does first, what comes back, and what they should
do next. This is the path the tool descriptions have to support on their own: no repository, no design
record, no author standing by.

It exists because reading a description is not enough to evaluate it. The empty reply this walk opens with
used to say nothing but `{"messages":[],"matched":0}`, and the author of the tool missed it by re-reading.
It was caught by being the caller ([#396](https://github.com/hexide-io/HexIDE/issues/396)). So **diff this
file when a tool on its path changes shape.** If the obvious next step is no longer the one shown, that
is what to fix, in the tool or its description.

## How it is kept honest

Every reply below was taken from a real session and not written from the record types. A reply
composed from the author's picture of a tool is exactly what this document exists to test. The session
was a Debug build started with `--server-port 5123 --newproject` and a fresh `--user-data-dir`. Nothing
was armed. It was re-recorded whole on 2026-10-13, when `open_file` began to name what it opened
([#273](https://github.com/hexide-io/HexIDE/issues/273)), rather than patched, so every reply below comes
from one session.

`FirstContactTranscriptTests` fails the build when a call shown here could no longer be made as written
(a tool renamed, a parameter renamed, a required parameter added), or when a reply shows a field no reply
carries any longer. It cannot tell whether a reply is still *true*. When a tool on this path changes,
**re-record the step. Do not edit the reply by hand**, because a hand-edited reply is the author's model
again.

One liberty is taken, and it is marked where it occurs: a project path under the user's profile is
shortened.

## 1. Ask what the protocol record holds

A caller who wants to know what crossed the wire to a language server reaches for the tool that says so.
Every argument is optional, so the first call passes none.

```mcp
list_lsp_messages
```

```json
{
  "messages": [],
  "matched": 0,
  "truncated": false,
  "framesDropped": 0,
  "note": "No language server has connected yet, so there is nothing recorded. Servers start on the first document of a language they claim — open a file and ask again. Nothing needs arming for envelopes to be recorded."
}
```

An empty list alone could mean nothing happened, nothing is configured, or the tool is broken. The
`note` says which, and it names the next step. It also answers the question a careful caller asks next,
which is whether they need to arm something first. They do not.

## 2. Find a file to open

```mcp
get_project_info
```

```json
{
  "projectName": "Project1",
  "projectPath": "%TEMP%\\hexide_Project1_4f4566a0701e4f48b83e5793650e6563\\Project1.vbp",
  "forms": ["Form1"],
  "modules": [],
  "relatedDocuments": [],
  "projects": [
    {"name": "Project1", "path": "%TEMP%\\hexide_Project1_4f4566a0701e4f48b83e5793650e6563\\Project1.vbp", "isStartup": true, "forms": ["Form1"], "modules": [], "relatedDocuments": []}
  ]
}
```

The paths were shortened here, and the rest of the reply is verbatim. `--newproject` makes a scratch
project in the temporary directory, which is why the path looks like that. With one project loaded,
`projects` repeats the top-level fields; with a group open it lists every project, and `note` says the
top-level fields describe only the startup one.

## 3. Open it

```mcp
open_file {"name": "Form1"}
```

```json
{
  "success": true,
  "note": "Active tab: 'Project1 - Form1 (Code)' (code); 2 open. Keyboard focus is in it.",
  "project": "Project1",
  "document": "Form1"
}
```

`project` and `document` name what was opened, which matters once a group holds two documents of one
name. Two tabs are open, not one, because `--newproject` opens the new form's designer first.

## 4. Ask again

```mcp
list_lsp_messages
```

Asked immediately, the handshake was still in flight:

```json
{
  "messages": [
    {"sequence": 1, "connectionId": "hexide.vb6", "at": "2026-10-13T13:42:41.8399375+00:00", "direction": "Local", "kind": "Lifecycle", "sizeBytes": 0, "detail": "connecting", "hasBody": false},
    {"sequence": 2, "connectionId": "hexide.vb6", "at": "2026-10-13T13:42:41.8985645+00:00", "direction": "Local", "kind": "Lifecycle", "sizeBytes": 0, "detail": "connected", "hasBody": false},
    {"sequence": 3, "connectionId": "hexide.vb6", "at": "2026-10-13T13:42:42.0692472+00:00", "direction": "Local", "kind": "Lifecycle", "sizeBytes": 0, "detail": "began waiting for the server's first bytes", "hasBody": false},
    {"sequence": 4, "connectionId": "hexide.vb6", "at": "2026-10-13T13:42:42.1636280+00:00", "direction": "Sent", "kind": "Request", "method": "initialize", "id": "2", "sizeBytes": 588, "hasBody": true},
    {"sequence": 5, "connectionId": "hexide.vb6", "at": "2026-10-13T13:42:42.1740952+00:00", "direction": "Local", "kind": "Lifecycle", "sizeBytes": 0, "detail": "began writing 611 bytes to the server", "hasBody": false},
    {"sequence": 6, "connectionId": "hexide.vb6", "at": "2026-10-13T13:42:42.1789937+00:00", "direction": "Local", "kind": "Lifecycle", "sizeBytes": 0, "detail": "the first 611 bytes reached the server's channel", "hasBody": false}
  ],
  "matched": 6,
  "truncated": false,
  "framesDropped": 0
}
```

`initialize` has no `outcome` because it has not been answered yet. The description says an outcome is
absent while a request is still waiting, so this is not a lost reply. The `Local` rows around it are the
channel's own account, given once each: when the client began waiting to read, began writing, and saw its
first bytes reach the server. A message serialized and then held somewhere on its way out would otherwise
look exactly like a server that received it and said nothing
([#390](https://github.com/hexide-io/HexIDE/issues/390)).

To see what has changed since, pass the last reply's `latestSequence` as `afterSequence`. The replies here were
recorded before that field existed (#961), so they lack it, and so does each completed row's `completedSequence`.
What they show instead is the workaround of the time: a sequence below `initialize`'s own row.

```mcp
list_lsp_messages {"afterSequence": 3}
```

Asked a moment later:

```json
{
  "messages": [
    {"sequence": 4, "connectionId": "hexide.vb6", "at": "2026-10-13T13:42:42.1636280+00:00", "direction": "Sent", "kind": "Request", "method": "initialize", "id": "2", "sizeBytes": 588, "outcome": "Answered", "elapsedMs": 1603.9062, "hasBody": true, "answerSizeBytes": 508, "answerSequence": 8},
    {"sequence": 5, "connectionId": "hexide.vb6", "at": "2026-10-13T13:42:42.1740952+00:00", "direction": "Local", "kind": "Lifecycle", "sizeBytes": 0, "detail": "began writing 611 bytes to the server", "hasBody": false},
    {"sequence": 6, "connectionId": "hexide.vb6", "at": "2026-10-13T13:42:42.1789937+00:00", "direction": "Local", "kind": "Lifecycle", "sizeBytes": 0, "detail": "the first 611 bytes reached the server's channel", "hasBody": false},
    {"sequence": 7, "connectionId": "hexide.vb6", "at": "2026-10-13T13:42:43.7186577+00:00", "direction": "Local", "kind": "Lifecycle", "sizeBytes": 0, "detail": "the first 531 bytes arrived from the server", "hasBody": false},
    {"sequence": 9, "connectionId": "hexide.vb6", "at": "2026-10-13T13:42:43.8166847+00:00", "direction": "Sent", "kind": "Notification", "method": "initialized", "sizeBytes": 52, "hasBody": true},
    {"sequence": 10, "connectionId": "hexide.vb6", "at": "2026-10-13T13:42:43.8455029+00:00", "direction": "Local", "kind": "Lifecycle", "sizeBytes": 0, "detail": "initialized: HexIDE VB6 Language Server 1.0.0", "hasBody": false},
    {"sequence": 11, "connectionId": "hexide.vb6", "at": "2026-10-13T13:42:43.9194682+00:00", "direction": "Sent", "kind": "Notification", "method": "textDocument/didOpen", "sizeBytes": 481, "hasBody": true},
    {"sequence": 12, "connectionId": "hexide.vb6", "at": "2026-10-13T13:42:44.4564030+00:00", "direction": "Sent", "kind": "Request", "method": "textDocument/foldingRange", "id": "3", "sizeBytes": 203, "outcome": "Answered", "elapsedMs": 834.5468, "hasBody": true, "answerSizeBytes": 64, "answerSequence": 14},
    {"sequence": 13, "connectionId": "hexide.vb6", "at": "2026-10-13T13:42:45.1392468+00:00", "direction": "Received", "kind": "Notification", "method": "textDocument/publishDiagnostics", "sizeBytes": 202, "hasBody": true},
    {"sequence": 15, "connectionId": "hexide.vb6", "at": "2026-10-13T13:42:45.6206673+00:00", "direction": "Sent", "kind": "Request", "method": "textDocument/documentSymbol", "id": "4", "sizeBytes": 205, "outcome": "Answered", "elapsedMs": 89.6947, "hasBody": true, "answerSizeBytes": 223}
  ],
  "matched": 10,
  "truncated": false,
  "framesDropped": 0
}
```

`initialize`'s row came back completed by its reply, with `outcome` and `answerSequence` filled in. It came
back because 4 is above the 3 passed. Until #961 that was the only way it could: polled with the highest
sequence seen, 6, it never came back answered, because a reply completes its request's row rather than adding
one. Now a request answered or ended after `afterSequence` comes back on its own row, carrying the
`completedSequence` its outcome was recorded under, and each reply's `latestSequence` is the cursor to pass next.

There is no `Unconsumed` row, because this client uses everything the bundled server advertises. A
server that offers something the client does not use gets one here, between `initialized` and the first
document.

## 5. Read what the server advertised

The `initialize` row carries `answerSequence` 8. That number is how the reply is read, because a reply
has no row of its own:

```mcp
get_lsp_message {"connectionId": "hexide.vb6", "sequence": 8}
```

```json
{
  "sequence": 8,
  "connectionId": "hexide.vb6",
  "method": "initialize",
  "trueLength": 508,
  "head": "{\"id\":2,\"result\":{\"capabilities\":{\"textDocumentSync\":{\"openClose\":true,\"change\":1},\"completionProvider\":{\"resolveProvider\":false},\"hoverProvider\":true,\"signatureHelpProvider\":{\"triggerCharacters\":[\"(\",\",\"]},\"definitionProvider\":true,\"documentHighlightProvider\":true,\"documentSymbolProvider\":true,\"documentFormattingProvider\":true,\"renameProvider\":true,\"foldingRangeProvider\":true,\"experimental\":{\"vbBuiltinSymbols\":true}},\"serverInfo\":{\"name\":\"HexIDE VB6 Language Server\",\"version\":\"1.0.0\"}},\"jsonrpc\":\"2.0\"}",
  "truncated": false,
  "isAnswer": true,
  "answerTo": 4
}
```

That is the whole answer, and the handshake was readable without arming anything, because a connection's
opening is always kept.

## What looks like a defect along this path, and whether it is

| Looks like | Is |
|---|---|
| Sequence numbers skip 8 and 14 | Not a loss. A reply consumes a sequence number, as does a request's cancellation or abandonment, and completes its request's row instead of adding one. `framesDropped` is the only report of real loss. |
| The documentSymbol request is `Answered` with `answerSizeBytes` but no `answerSequence` | Not a loss. Its reply's body was not retained, since nothing was armed and the opening allowance was spent. A row with `answerSequence` can always be read; one without it cannot. |
| `get_lsp_message` returns the complete body in a field called `head` | Not truncation. `truncated` is false and `trueLength` matches. A body over the per-frame limit comes back as `head` plus `tail`. |
| `initialize` without an `outcome` on the first ask | Not a lost reply. It was still waiting, and asking again shows it answered. |
| `initialize` missing from a poll with `afterSequence` 6, though it was answered | Fixed by [#961](https://github.com/hexide-io/HexIDE/issues/961): a request answered or ended after `afterSequence` comes back on its own row. Poll from the reply's `latestSequence`. |
