# A Project Explorer node that is not a form or module cannot be selected or opened — **CLOSED** (#362, 2026-09-22)

> **Closed (#362).** A Project Explorer node is now reported as `TreeItem` with `selectionItem`, and
> `interact(target: ".../Custom/Tree/Pane/TreeItem/TreeItem", action: "double_click", window: "ide")` on
> Form1's node answered `double-clicked 'TreeItem' (selected it first, as a real double-click does)` and
> activated its designer. `open_file` also resolves carried files by name or filename
> (`HexIdeTools.OpenFileAsync`; the filename half only from #548, which found it matched the name alone). **Not driven in this pass:** a carried-file node specifically. The project
> had none, and `add_file` cannot create one. The route is the same one that worked on the form node.

**Symptom.** There is no way to drive "select this tree node, then open it" for any node kind beyond forms
and modules. Verified against a related-document node (a file the project carries but does not compile):

- `interact select` on its `TreeViewItem` → `element does not support 'select'`; the item exposes only a
  `scroll` provider, no `selectionItem`. (Since #361 a tree node reports no `scroll` either: nothing
  behind its peer scrolls, so the token was a lead that went nowhere.)
- `press_key` on the `TreeView` does nothing useful — `inspect_element` reports the tree as
  `isKeyboardFocusable: false`, so arrow keys never reach a selection model.
- `interact set_property` cannot help either: `ProjectToolViewModel.SelectedItem` is typed `Object`, and the
  reflection fallback coerces a **string** to the property type. There is no way to name an existing
  view-model instance as a value.
- `open_file(name)` opens a form or module by name only, so it cannot reach anything else.

**Consequence.** The double-click-to-open gesture is unverifiable through MCP for any new node kind. That
matters because CLAUDE.md requires a UI feature be confirmed against the running IDE, and here the *tree*
can be confirmed by snapshot while the *gesture* cannot be driven at all.

**Workaround.** Split the verification and say which half was driven. The node's presence, icon and caption
are visible in `take_snapshot` and assertable via `dump_visual_tree` (the node reports its
`dataContextType`, so its type is checkable). The routing — selection to the right editor — and the editor's
own behaviour are covered by tests instead. State plainly that the gesture itself was not driven.

**Suggested fix.** Either surface a `selectionItem` provider on tree items so `interact select` works, or
add a selection-by-path action (`interact select_node` taking the same `path` the tree dump already
returns). The second is probably better: paths are already the addressing scheme everywhere else in this
server, and it would work for every current and future node kind rather than needing a provider per
control. A narrower `open_file` that accepts any project member would help too, but would not fix
selection, which is what several context-menu commands key on.
