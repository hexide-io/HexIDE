# `inspect_element` reported `selectionItems: []` for a tree whatever was selected — **CLOSED** (#2288, 2026-10-27)

**Symptom (2026-10-27, Windows, #2287's live check).** After `interact select` on the Watches row `TreeItem[x]`
answered `selected 'x'`, `inspect_element` on its `Tree` answered `"selectionItems":[]`. It gave the same after
`remove_from_selection`, so a check that expected `[]` after clearing the row was checking nothing.

**Cause.** `Inspect` filled `selectionItems` from the peer's `ISelectionProvider`, or else from a
`SelectingItemsControl`. Avalonia's `TreeView` is neither: it derives from `ItemsControl`, and its peer offers no
selection provider. So neither branch ran, and the default empty array went out as the answer. All four of the IDE's
trees were affected: Watches, Locals, Project Explorer and the Options page tree.

**Workaround.** None in the tree itself. #2287's check used a consequence instead: Debug → Edit Watch opens its dialog
only while a row is selected.

**Fixed (#2288).** `Inspect` reads a tree's `SelectedItems` and names each row as `remove_from_selection` does: by
its container's label, falling back to `NameWithoutRow` for a row not realised. `InspectTreeSelectionTests` covers a
tree of rows and a bound tree named the way the IDE's trees are. Both fail with the new branch disabled.

**Live (2026-10-27, a Debug build of the fix on Windows, headless `claude -p` through the hexide tools).**

- **Project Explorer:** after selecting Form1's row, `selectionItems` was `["Form1 (Form1.frm)"]`.
- **Watches:** with a watch on `x`, `selectionItems` was `[]` before selecting, `["x"]` after `select`, and `[]` after
  `remove_from_selection`.
- **Options:** the page tree read `["General"]` on opening and `["Formatting"]` after selecting Editor → Formatting.
