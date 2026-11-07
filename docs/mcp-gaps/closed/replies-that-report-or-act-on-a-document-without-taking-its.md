# Replies that report or act on a document without taking its name do not say which project's it is — **CLOSED** (#886, 2026-10-24)

**Symptom.** Read in code, not run, at ec9bfc4. #273 made every tool that takes a document's name find it in any
loaded project and name it back as `project` and `document` (archived under the #575 and #467 entries). The tools
that report or act on a document without taking its name were left as they were. The shapes, from the code, with a
group holding a `Form1` in each of two projects:

```
step_over {}               → "note": "Paused at Form1 line 9 (Step)."
                             as run_project, break_program, continue_program, step_into, step_out, run_to_cursor
get_locals {}              → "context": "Form1.Form_Load", the Module.Procedure header, as the watch tools' context
invoke_designer_undo {}    → "note": "Undid '…'. Next undo: …", on the active designer, its form not named,
                             as invoke_designer_redo, get_undo_state and invoke_format_command
get_document_tabs {}       → each tab's title, which activate_document_tab and close_document_tab take
get_open_editors {}        → each editor's title, and 'activeWindow' as a title
```

None of these says which project's `Form1` it means. `get_debug_state` and `get_call_stack` now carry the running
`project`; the run-control notes beside them do not. A tab's title starts with its project's name
(`Second - Module1 (Code)`, measured in the #575 entry), but it is the IDE's display string, whose suffix follows
the IDE's language, and not the `project` and `document` every naming tool takes and returns.

**Workaround.** For a run, `get_debug_state {}` gives `project` beside `module`, and every frame `get_call_stack {}`
lists is in that project. For a designer, `view_designer` with `project` first makes the intended form the active
designer, and its reply names it. Address a tab by the title `get_document_tabs` returns, never by one composed from
a name.

**Suggested fix.** #886's: the running project beside a bare module name in the run-control notes and in the
locals and watch `context`; `project` and `document` for the document behind the active designer in the designer
tools' replies; and the tab tools reporting `project` and `document`, and accepting them in place of a title.
Until then the hexide-mcp-server requirement #273 added, that "a tool that accepts or reports a form, module or class
SHALL identify it by its project and its name", is only partly met.
[#886](https://github.com/hexide-io/HexIDE/issues/886).


**Fixed (#886).** Run-control pause notes include the running project; locals and watches replies carry
`project` (the project in which watch expressions are evaluated). Designer undo state and undo/redo/format
replies carry `project` and `document`, including refusals after finding a designer. Tab listings carry the
same identity; `get_open_editors` adds structured `editors` and `activeEditor` alongside its existing titles.
Activate and close accept `name` with optional `project` through `NamedDocuments`; `type` chooses code or
designer when both are open. Title addressing remains available for tool tabs, and duplicate matches are
refused. Headless guards cover the reply shapes and lookup wiring; direct tests cover tab targeting.
Live verification of focus, close prompts and grouped-project replies remains the supervisor's check.
