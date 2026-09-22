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
was armed.

`FirstContactTranscriptTests` fails the build when a call shown here could no longer be made as written
(a tool renamed, a parameter renamed, a required parameter added), or when a reply shows a field no reply
carries any longer. It cannot tell whether a reply is still *true*. When a tool on this path changes,
**re-record the step. Do not edit the reply by hand**, because a hand-edited reply is the author's model
again.

Two liberties are taken, and both are marked where they occur. A project path under the user's profile is
shortened. A long list reply loses some rows, and the rows that remain are verbatim.

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
  "projectPath": "%TEMP%\\hexide_Project1_56ba212511664189866828561e6d40a3\\Project1.vbp",
  "forms": ["Form1"],
  "modules": [],
  "relatedDocuments": []
}
```

The path was shortened here, and the rest of the reply is verbatim. `--newproject` makes a scratch
project in the temporary directory, which is why the path looks like that.

## 3. Open it

```mcp
open_file {"name": "Form1"}
```

```json
{"success": true}
```

## 4. Ask again

```mcp
list_lsp_messages
```

Asked immediately, the handshake was still in flight:

```json
{
  "messages": [
    {"sequence": 1, "connectionId": "hexide.vb6", "at": "2026-09-22T01:51:30.8108068+00:00", "direction": "Local", "kind": "Lifecycle", "sizeBytes": 0, "detail": "connecting", "hasBody": false},
    {"sequence": 2, "connectionId": "hexide.vb6", "at": "2026-09-22T01:51:31.4937872+00:00", "direction": "Local", "kind": "Lifecycle", "sizeBytes": 0, "detail": "connected", "hasBody": false},
    {"sequence": 3, "connectionId": "hexide.vb6", "at": "2026-09-22T01:51:31.6161638+00:00", "direction": "Sent", "kind": "Request", "method": "initialize", "id": "2", "sizeBytes": 589, "hasBody": true}
  ],
  "matched": 3,
  "truncated": false,
  "framesDropped": 0
}
```

`initialize` has no `outcome` because it has not been answered yet. The description says an outcome is
absent while a request is still waiting, so this is not a lost reply. To see only what arrived since,
pass the last sequence already seen:

```mcp
list_lsp_messages {"afterSequence": 2}
```

This reply has two rows elided (`initialized` at sequence 5 and `textDocument/didOpen` at 8):

```json
{
  "messages": [
    {"sequence": 3, "connectionId": "hexide.vb6", "at": "2026-09-22T01:51:31.6161638+00:00", "direction": "Sent", "kind": "Request", "method": "initialize", "id": "2", "sizeBytes": 589, "outcome": "Answered", "elapsedMs": 1383.7921, "hasBody": true, "answerSizeBytes": 508, "answerSequence": 4},
    {"sequence": 6, "connectionId": "hexide.vb6", "at": "2026-09-22T01:51:33.0154921+00:00", "direction": "Local", "kind": "Lifecycle", "sizeBytes": 0, "detail": "initialized: HexIDE VB6 Language Server 1.0.0", "hasBody": false},
    {"sequence": 7, "connectionId": "hexide.vb6", "at": "2026-09-22T01:51:33.0156913+00:00", "direction": "Local", "kind": "Unconsumed", "sizeBytes": 0, "detail": "advertised and unused: 'textDocumentSync'", "hasBody": false},
    {"sequence": 9, "connectionId": "hexide.vb6", "at": "2026-09-22T01:51:33.2274476+00:00", "direction": "Received", "kind": "Notification", "method": "textDocument/publishDiagnostics", "sizeBytes": 113, "hasBody": true},
    {"sequence": 10, "connectionId": "hexide.vb6", "at": "2026-09-22T01:51:33.2625694+00:00", "direction": "Sent", "kind": "Request", "method": "textDocument/documentSymbol", "id": "3", "sizeBytes": 116, "outcome": "Answered", "elapsedMs": 1.7871, "hasBody": true, "answerSizeBytes": 222, "answerSequence": 11},
    {"sequence": 12, "connectionId": "hexide.vb6", "at": "2026-09-22T01:51:33.5337048+00:00", "direction": "Sent", "kind": "Request", "method": "textDocument/foldingRange", "id": "4", "sizeBytes": 114, "outcome": "Answered", "elapsedMs": 1.6766, "hasBody": true, "answerSizeBytes": 63}
  ],
  "matched": 8,
  "truncated": false,
  "framesDropped": 0
}
```

`afterSequence` returned sequence 3 again. The request's row was completed by its reply, so it came
back with `outcome` and `answerSequence` filled in, which is the change a poller needs to see.

## 5. Read what the server advertised

The `initialize` row carries `answerSequence` 4. That number is how the reply is read, because a reply
has no row of its own:

```mcp
get_lsp_message {"connectionId": "hexide.vb6", "sequence": 4}
```

```json
{
  "sequence": 4,
  "connectionId": "hexide.vb6",
  "method": "initialize",
  "trueLength": 508,
  "head": "{\"id\":2,\"result\":{\"capabilities\":{\"textDocumentSync\":{\"openClose\":true,\"change\":1},\"completionProvider\":{\"resolveProvider\":false},\"hoverProvider\":true,\"signatureHelpProvider\":{\"triggerCharacters\":[\"(\",\",\"]},\"definitionProvider\":true,\"documentHighlightProvider\":true,\"documentSymbolProvider\":true,\"documentFormattingProvider\":true,\"renameProvider\":true,\"foldingRangeProvider\":true,\"experimental\":{\"vbBuiltinSymbols\":true}},\"serverInfo\":{\"name\":\"HexIDE VB6 Language Server\",\"version\":\"1.0.0\"}},\"jsonrpc\":\"2.0\"}",
  "truncated": false,
  "isAnswer": true,
  "answerTo": 3
}
```

That is the whole answer, and the handshake was readable without arming anything, because a connection's
opening is always kept.

## What looks like a defect along this path, and whether it is

| Looks like | Is |
|---|---|
| Sequence numbers skip 4 and 11 | Not a loss. A reply consumes a sequence number and completes its request's row instead of adding one. `framesDropped` is the only report of real loss. |
| The foldingRange request is `Answered` with `answerSizeBytes` but no `answerSequence` | Not a loss. Its reply's body was not retained, since nothing was armed and the opening allowance was spent. A row with `answerSequence` can always be read; one without it cannot. |
| `get_lsp_message` returns the complete body in a field called `head` | Not truncation. `truncated` is false and `trueLength` matches. A body over the per-frame limit comes back as `head` plus `tail`. |
| `initialize` without an `outcome` on the first ask | Not a lost reply. It was still waiting, and asking again shows it answered. |
| `advertised and unused: 'textDocumentSync'` at sequence 7, one row before the `didOpen` that uses it | **A defect.** The client reads that capability and gates every document notification on it. Tracked in [#394](https://github.com/hexide-io/HexIDE/issues/394) and [#430](https://github.com/hexide-io/HexIDE/issues/430). |
