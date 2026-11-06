# `invoke_menu_item` couldn't reach an item with a control header, or a submenu filled when it opens — **CLOSED** (#2321, 2026-10-27)

**Symptom (2026-10-27, Windows, #2140's live check).** #2140's Window → Panel Layouts submenu has both kinds of item.

- **A control header.** Each saved layout has a TextBlock header and an automation name. `invoke_menu_item
  {"path":"Window/Panel Layouts/Beta/Switch"}` answered `No item 'Beta' in menu 'Panel Layouts'. It holds: Save Layout
  As…, Full Editor, Avalonia.Controls.TextBlock, Avalonia.Controls.TextBlock`.
- **A submenu filled when it opens.** The submenu holds a placeholder until its SubmenuOpened handler fills it. In a
  fresh session, `"Window/Panel Layouts/Full Editor"` answered `It has no items.`, and the same call worked once
  something had opened the submenu.

**Workaround.** `interact` `expand` along the path, then `invoke` on the item by its tree path.

**Cause.** `MenuPath` read a header by its `ToString`, which for a control is its type's name. It also walked a
submenu's logical items without opening it, so a handler that fills the submenu when it opens had not run.

**Fixed (#2321).** A header that is a control is read by the item's automation name, which the dump shows it by, or
else its text. Each closed submenu on the path gets its SubmenuOpened before its items are read, as opening it does,
without a popup. `MenuPathTests` covers both, and each test fails with its half of the fix removed.

**Live (2026-10-27, a Debug build of the fix cherry-picked onto #2140's branch on Windows, headless `claude -p` through
the hexide tools).**

- In a fresh session, with no menu opened by hand, `Window/Panel Layouts/Full Editor` answered `Invoked 'Full Editor'.`
- After saving a layout named Beta, a miss in Panel Layouts listed `Save Layout As…, Full Editor, Beta`.
- `Window/Panel Layouts/Beta/Switch` answered `Invoked 'Switch'.`
