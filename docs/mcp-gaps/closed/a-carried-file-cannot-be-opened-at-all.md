# A carried file cannot be opened at all — FIXED

**Symptom.** A project's related documents (a `RelatedDoc=` in the `.vbp` — a README, a changelog, a
`.sql`) appear in the Project Explorer as `RelatedDocViewModel` nodes, and there is no MCP route to open
one. Every door is shut:

- The tree opens an item on **double-tap** (`ProjectToolView.axaml` → `TreeView_OnDoubleTapped` →
  `ProjectToolViewModel.OpenSelected()`), and `interact` has no double-click action.
- `interact select` on the `TreeViewItem` fails with *element does not support 'select'* — it reports only
  a `scroll` provider, so the row cannot even be **selected**, which `OpenSelected` reads.
- `OpenSelected()` is a plain method, not an `ICommand`, so `interact invoke_command` cannot reach it. The
  context menu's `ViewCodeCommand` handles forms and modules only.
- `open_file` and `add_file` are documented as forms and modules only, and reject a related document.
- `Project > Add File...` on an already-carried file does open it, but goes through
  `IStorageProvider.OpenFilePickerAsync` — a native Win32 dialog no MCP tool can drive.

**Consequence.** A whole editor type is unverifiable. This blocked the live check for #255: the point of
that change is attaching a language server for a file type HexIDE has no other support for, and a carried
`.md` is exactly that file. The configuration, the registry and the bundled server were all confirmable;
the foreign server's diagnostics rendering in an editor were not, and needed a human to double-click.

**Fixed** in the change that needed it. `open_file` now accepts a carried file by name, and
`get_project_info` lists them, so the whole editor type is drivable. That route was chosen over a
`double_click` action on `interact` for a reason worth recording: **it changes no tool's parameters**, so
it needed no schema change and therefore no session restart — the fix was usable in the session that
found the gap. `open_file` was already the "open this project item" tool; its refusing one kind of item
was the surprising part.

**Still open, and independent:** the `TreeViewItem` in the Project Explorer exposes no `selectionItem`
provider, so a row cannot be selected through `interact` at all. That blocks every context-menu path in
that tree, not just this one, and `open_file` does not help there. `interact` also still has no
double-click action, which is the general form of the problem.
