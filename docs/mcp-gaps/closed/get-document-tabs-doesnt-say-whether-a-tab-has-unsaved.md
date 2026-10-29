# `get_document_tabs` doesn't say whether a tab has unsaved changes — **CLOSED** (#1945, 2026-10-25)

**Symptom (2026-10-25, Debug build with #1797).** With #1797's unsaved-changes marker live, a designer tab draws
`Project1 - Form1 (Form) *`, but the reply gives the title without the `*` and has no other field for the state.
After `add_control` (Form1, CommandButton), `interact click` on `ListItem[Command1]` and `press_key Delete`:

```
get_document_tabs {}
→ {"tabs":[{"title":"Project1 - Form1 (Form)","type":"designer","isActive":true,...}]}
```

`take_snapshot` showed the `*`. A client couldn't tell a modified document from a saved one without a screenshot,
and a script that edits and then saves had nothing to assert against.

**Workaround (before the fix).** `take_snapshot`, and read the tab.

**Fixed (#1945).** Each tab that can be saved, in `get_document_tabs` and in `get_open_editors`' `editors` and
`activeEditor`, carries `isModified`, read from the tab's own `IsModified`, which is what the `*` is drawn from. A
carried file has it too, without `project` or `document`. A tool tab, and the read-only project and archive views, have
no `isModified`: they hold nothing to save, and a `false` would read as "saved", which for the project view of a
project never saved would be untrue. The field is per tab, and a form's designer and code tabs can differ, as their
markers do; the description says so. The title is unchanged, so it is still what `activate_document_tab` and
`close_document_tab` match. Neither tool takes an argument, so neither schema has a `required` array.
`DocumentTabsModifiedTests` fail before the fix. The archive view is not among them, since its entries are built only
by reading a real archive file.

**Live (Windows, a Debug build of the fix, the hexide tools from a headless Claude Code session).** On a new
project:

```
get_document_tabs {}                         → Form1's designer, "isModified":false
add_control {"formName":"Form1","type":"CommandButton","x":40,"y":40,"width":81,"height":33}
get_document_tabs {}                         → "isModified":false (add_control saves the form)
interact {"target":"…/ListItem[Command1]","action":"click"}
press_key {"target":"…/ListItem[Command1]","key":"Delete"}
get_document_tabs {}                         → "isModified":true
take_snapshot {"window":"ide"}               → the tab reads "Project1 - Form1 (Form) *"
get_open_editors {}                          → "isModified":true in editors[0] and activeEditor
invoke_menu_item {"path":"View/Object Browser"}
get_document_tabs {}                         → {"title":"Object Browser","type":"tool","isActive":true}
open_file {"name":"Form1"}
type_text {"target":"…/None[TextEditor]","text":"' typed"}
get_document_tabs {}                         → designer true, code true
invoke_menu_item {"path":"File/Save Form1"}
get_document_tabs {}                         → designer false, code false
```

On a second new project, with the project view opened by `interact {"action":"double_click"}` on
`TreeItem[Project1 (Project1.vbp)]`, `get_document_tabs` listed `{"title":"Project1.vbp","type":"code","isActive":true}`,
with no `isModified`, beside Form1's designer with one. It still had none after `add_file` added a module.
