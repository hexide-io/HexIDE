# `select` on a tree item reports success and leaves the tree's selection where it was — **CLOSED** (#714, 2026-10-20)

> **Fixed.** The cause was not the one first recorded below. Avalonia 12's `TreeViewItem` peer has no
> `ISelectionItemProvider`, so `select` always went through `TrySelectThroughOwningTree`, which set the item's
> `IsSelected` and read that back. Avalonia turns that into `SelectedItems.Add`, and moves `SelectedItem` only
> when nothing was selected. With a selection already there, both items were highlighted, the bound property
> stayed put, and the read-back said yes. Any tree with a selection was affected, and `double_click` pre-selects
> the same way, so on the Project Explorer it would have opened the node selected beforehand.
>
> The driver now sets the owning tree's `SelectedItem` and reads that back, as it does for a grid row. `select`
> with a `value` now considers tree items, which it had dropped for lacking the provider, and an empty tree's
> refusal no longer talks about dropdowns. The Options, Locals and Watches trees name their items after their
> text, as the Project Explorer's already did, so a reply names what it selected and a path can say
> `TreeItem[Theme]`. Guarded by `UiAutomationDriverTests`, which start from a selection already made.
>
> Review found that once tree items could be selected by text, an item under a collapsed node had no way to be shown:
> the node's peer offers no ExpandCollapse provider either, so `expand` refused. `expand` and `collapse` now set a
> node's `IsExpanded` and read it back, and the dump advertises `expandCollapse` on a node with children. A text that
> matches only an item under a collapsed node names that node to expand. One under a node never expanded matches
> nothing, since its item is not created until then, so the refusal lists the collapsed nodes instead of telling the
> caller to check the spelling. The read-back confirms the tree took the item, not that a bound view model accepted
> it. Avalonia cannot tell those apart, and the one tree whose view model refuses, Watches for its child nodes, is
> filed as #1229.

**Symptom (2026-10-02, during #273 task 3.15's live check).** In Tools → Options, the page tree's items each
advertise `selectionItem`. `interact {"target":"Window/Custom/Tree/Pane/TreeItem[#0]/TreeItem[#1]","action":"select"}`
(the Theme item) replies `{"success":true,"mechanism":"peer","detail":"selected 'TreeItem'"}`, and nothing has
changed: `inspect_element {"target":"Window"}` still has the same `SelectedNode`, and the page beside the tree
is still `EnvironmentGeneralPageView`. Seen on three launches. Selecting by text fails differently:
`interact {"target":"Window/Custom/Tree","action":"select","value":"Theme"}` replies `'Tree' has no selectable
items realized`, although `dump_visual_tree {"root":"Window/Custom/Tree/Pane/TreeItem[#0]","interactiveOnly":false,"maxDepth":4}`
shows every item realized, with its text in a child `Text[Theme]`. The items have no automation name of their
own, which is also why the success reply calls the item `'TreeItem'`.

**Workaround.** Keyboard: `press_key {"target":"Window/Custom/Tree/Pane/TreeItem[#0]/TreeItem[#0]","key":"Down"}`
moves from General to Theme and the page follows. A theme is then switched by `interact expand` on
`Window/Custom/Custom/ComboBox`, `interact select` with the theme's name as `value`, and `interact invoke` on
`Window/Custom/Button[OK]`.

**Suggested fix.** `DoSelect` takes the item peer's `Select()` as done. It should read the selection back, from
the item and from its owning tree, fall back to `TrySelectThroughOwningTree` when it did not take, and say so
when neither did, rather than reporting a selection it never checked. Naming a tree item after its header text
would let `select` with a `value` find it, and would let the reply name what it selected. Filed as
[#714](https://github.com/hexide-io/HexIDE/issues/714).
