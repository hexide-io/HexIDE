# `invoke_menu_item` misses items with `_` in their text, and describes menus it cannot read (#544) — **CLOSED** (#560, 2026-09-22)

> **Closed (#560).** `MenuPath.Resolve` matches a segment as typed first and with its access key
> stripped second, so `File/Remove My_App` reaches the item showing that text. An `ItemsSource`-backed
> submenu such as Recent Projects is resolved through each entry's view model (`Header`, `Command`,
> `CommandParameter`), so its entries are listed and run without the submenu being opened first. A hidden
> item is neither listed nor run, at any level of the path, and the reply names it as hidden instead.
> Pinned by three `MenuPathTests`, each failing with its fix undone. Still open: a trailing `...` has to be
> typed (#564), and an entry built from a view model is always treated as visible.

**Symptom.** Three, all from #519's resolver (`MenuPath`). (1) An item whose displayed text contains an
underscore cannot be invoked: with a project named `My_App`, `invoke_menu_item(path: "File/Remove My_App")`
misses, because the caller's segment is stripped of an access key as if it were a header and becomes
`Remove MyApp`. The miss then lists `Remove My_App` among the items the menu holds. This worked before #519.
(2) `invoke_menu_item(path: "File/Recent Projects/anything")` answers `No item 'anything' in menu 'Recent
Projects'. It has no items.` while Recent Projects is visible, which it only is when it has entries: its
items come from `ItemsSource` and are not `MenuItem`s. (3) `invoke_menu_item(path: "Tolls/Options")` lists
`Go to github repo`, which is hidden on desktop, and invoking that path opens the browser.

**Workaround.** For (1) and (2), open the menu with `interact` `expand`, then `dump_visual_tree` scoped to
it, then `interact` `invoke` on the item: the realised containers carry the displayed text. For (3), check
`isHidden` in a dump before trusting a listing.

**Fix.** In the issue: match the segment as given, resolve item containers as well as `MenuItem` children,
and skip hidden items.
