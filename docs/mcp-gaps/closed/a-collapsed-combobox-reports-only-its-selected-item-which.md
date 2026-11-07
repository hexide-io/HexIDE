# A collapsed ComboBox reports only its selected item, which reads as "the item is missing" (#1263)

**Symptom.** `inspect_element` on a closed dropdown returns a `selectionItems` array containing just the
current selection:

```
"selectionItems": ["Form1"],
"value": "Form1"
```

The control had two items. `expand` then `dump_visual_tree` shows both:

```
ComboBoxItem[Sub Main]
ComboBoxItem[Form1]
```

**How it bit.** Verifying that `Sub Main` had been added to the Project Properties → Startup Object list
(#210). The inspection said the list held only `Form1`, which is exactly what a change that had failed to
take effect would look like — and the build had just been rebuilt and relaunched, so "the binary is stale"
was the obvious next hypothesis. The feature was working the whole time.

`interact(action: "select")` is honest about this — it fails with *"has no selectable items realized — if
it's a dropdown, 'expand' it first"* — and `dump_visual_tree`'s own description warns that virtualized
dropdown items are not addressable until realized. `inspect_element` carries no such warning and reports a
plausible-looking array instead of an empty one, which is the part that misleads.

**Workaround.** Never read `selectionItems` from a collapsed dropdown as the item list. `expand` first,
then `dump_visual_tree(root=<combo>)` and read the `ComboBoxItem` children. Expand again immediately before
`select` if the popup has closed. Since #907, `select` closes the dropdown as a click does, and its reply says so.

**Corrected 2026-10-20: `selectionItems` is the selection, not a partial list of items.** Read in code at 4789f9a,
not run. `inspect_element` fills it from the selection provider's `GetSelection()` (`UiAutomationDriver.Inspect`), as
it has since the first commit, so it holds what is selected whether the dropdown is open or closed: Avalonia
12.0.4's ComboBox peer reports a closed box's selected item through a stand-in peer (`UnrealizedSelectionPeer`, read
by reflection). `["Form1"]` was the right answer to another question. What misled is that the name reads as the
list's items and nothing in the reply says how many items there are, so the fix first suggested below aims at a
mechanism that is not there.

**Suggested fix, as first written.** Either omit `selectionItems` when the items are unrealized, or mark it — an
`itemsRealized: false` alongside it would be enough. A partial list that looks complete is worse than no
list, because it supports a confident wrong conclusion; an empty array with a flag supports none.

**Suggested fix.** Say in `inspect_element`'s description that `selectionItems` is what is selected, not the list,
and report an `itemCount` for a list or dropdown, which counts its items whether or not they are realized.
[#1263](https://github.com/hexide-io/HexIDE/issues/1263).

> **Fixed.** `inspect_element` now reports `itemCount` for a list, dropdown or tab control, from its `ItemCount`, which
> counts its items whether or not their containers exist, hidden ones too. It is absent for anything else: a menu or the
> dock's lists count separators and splitters that a dump does not show. Its description says `selectionItems` is what is
> selected, not the list's items, and that a dropdown's items are read by expanding it and dumping it. A ListBox's and a
> TabControl's peers offer no selection provider, so `selectionItems` was `[]` on every one of them, which "what is
> selected" would have read as nothing selected. It is now read from the list itself, by the names the dump gives its
> rows. Guarded by `InspectItemCountTests` (a ComboBox closed and open, a ListBox, a TabControl, a menu and a button) and
> `InspectElementDescriptionTests`.

> **Live, through the hexide tools (2026-10-27, Windows at 150%, a Debug build of main at f9b5649c, headless
> `claude -p`, #1330).** The review of #1287 said it was checked live without saying how, so it was checked again. A project
> with Form1 and a module holding `Sub Main`, in Project ▸ Properties: `inspect_element` on the Startup Object
> dropdown, `Window/Custom/Tab/ComboBox[#1]`, closed, gave `"selectionItems":["Form1"],"value":"Form1","itemCount":2`.
> On the dialog's tab control, `Window/Custom/Tab`, it gave `"selectionItems":["General"]` and `"itemCount":5`.
