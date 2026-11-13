# AI Chat text audiences

Audit for [#3861](https://github.com/hexide-io/HexIDE/issues/3861), following #3687's decision that
program-facing replies stay English. The add-in reads the shell's active language resources.

| Original literal in the view/view models | Audience and treatment |
|---|---|
| `✔ Approved`, `✗ Rejected` | Person; `Str.AIChat.Approved` / `Rejected` |
| `Allow the AI to replace the entire contents of …?` | Person; `AllowReplace` / `AllowReplaceUnnamed` (pack owns filename quotation marks) |
| `Allow the AI to run the project in the IDE runtime?` | Person; `AllowRunProject` |
| `Allow the AI to stop the running project?` | Person; `AllowStopProject` |
| `Allow the AI to run “…”?` | Person; `AllowTool` (tool name preserved) |
| `Error: …` | Person; `Error` (exception text preserved) |
| `Not applied: no code window is active. Open the file to apply the code to, then click Apply again.` | Person; `NoActiveCodeWindow` |
| `Applied to {file}.` | Person; `Applied` / `AppliedUnnamed` |
| `Not applied to …: …` | Person; `NotAppliedTo`, `NotAppliedToUnnamed`, and their `NoReason` variants |
| `Not applied: …` | Person; `NotApplied` |
| ``Using tool: `…` `` | Person; `UsingTool` (tool name preserved) |
| `` `…` failed: … `` | Person; `ToolFailed` / `ToolFailedNoReason` |
| `no reason was given.` | Person; included in complete failure sentences when no detail was supplied |
| `Approve`, `Reject`, `Apply`, `Send`, `New` | Person; matching keys, AXAML dynamic resources |
| `Ask about your VB6 code…` | Person; `InputPlaceholder`, now language-neutral |
| `New chat (history is preserved)` | Person; `NewTip` |
| `Cancelled` tool-result error | Model; stays English |
| `The user declined this action.` tool-result error | Model; stays English |
| `[Note from the IDE]`, `The developer applied a code block from this chat to …`, accepted/refused/failed descriptions, header-retention and nothing-written clauses, `(project …)` | Model; stays English, including when saved history displays the note |
| Tool executor JSON results, including their `error` values | Model; stay English; the visible failure wrapper is localized, the supplied detail is preserved |
| System context, `Active file:`, `Content:`, `Diagnostics for …:`, `No active editor.`, transcript role/tool labels, summarizer instruction | Model; stay English |

All key names in the table have the `Str.AIChat.` prefix. File names, project names, tool names, source
code, user/model messages and supplied diagnostic details are preserved. No British-English overrides
are needed for these new entries.

Each sentence is translated as a whole. Missing file names and missing failure details select complete
sentence keys; translated articles, nouns and fallback reasons are never format arguments to another
translation. Only supplied file/tool names and diagnostic details are interpolated.
