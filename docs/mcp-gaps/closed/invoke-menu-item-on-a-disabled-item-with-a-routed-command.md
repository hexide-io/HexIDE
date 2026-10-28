# `invoke_menu_item` on a disabled item with a routed command blames keyboard focus — **CLOSED**

> **Fixed (#1534, PR #1551, 2026-10-23).** A materialised menu item that is not effectively enabled is now
> reported as disabled in the menu before routed-command focus is checked. The focus explanation remains for
> enabled items. Guarded by `MenuCommandRefusalTests`.

**Symptom (2026-10-23, #746, Windows).** On `--newproject`, with one project and no group,
`dump_visual_tree {"window":"ide","match":"Make Project Group","interactiveOnly":false}` showed
`MenuItem[Make Project Group...]` with `isEnabled: false`, as VB6 greys it. `invoke_menu_item {"path":"File/Make Project
Group..."}` answered `cannot execute where keyboard focus is now (a MenuItem named 'PART_ItemsPresenter' showing
MainViewViewModel)`, went on about giving a document focus, and only in its last sentence said that a disabled item is
refused the same way. The item belongs to no document, and no focus would enable it.

**Workaround.** Open the menu and read the item's `isEnabled` with `dump_visual_tree` before invoking it.

**Suggested fix.** When the `MenuItem` is materialised and not effectively enabled, say it is disabled in the menu, as
the non-routed branch and the checkable-item branch already do. Keep the focus explanation for an item the menu shows
enabled. [#1534](https://github.com/hexide-io/HexIDE/issues/1534).
