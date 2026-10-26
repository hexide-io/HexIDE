## When a menu's Click is raised (2026-10-22, #1344)

**The probe.** Found by the Notepad demo, whose `mnuEdit_Click` enables Cut, Copy and Delete when there is a selection,
and which never ran. The probe's menus:
- **Top**, which holds three items:
  - Alpha, shortcut Ctrl+Q;
  - Sub, which holds Leaf, shortcut Ctrl+W;
  - Dis, shortcut Ctrl+E, disabled at design time.
- **Solo**, a top-level menu with no items.

Each Click logs its name, and Top's logs whether Dis is enabled. A Timer sends one set of keys each tick with
`SendKeys`, and changes Enabled or Visible first where the step says so. Built native and p-code, and run **in the
guest's console session**, each build at least 200 s after the last input, since the guest gives a newly started window
no foreground within 200 s of any input (`ForegroundLockTimeout`), and its keys then reach nothing. Every row agreed.

| Keys | Clicks raised, in order |
|---|---|
| Alt+T, opening Top | Top |
| Alt+T, then S, opening Sub | Top, Sub |
| Alt+T, then Enter, choosing Alpha | Top, Alpha |
| Alt+O, choosing Solo | Solo |
| Ctrl+Q | Top, Alpha |
| Ctrl+W | Top, Sub, Leaf |
| Ctrl+E, with Dis disabled | Top |
| Ctrl+E, with Top's Click setting `mnuDis.Enabled = True` | Top, Dis |
| Ctrl+Q, with Top disabled | none |
| Ctrl+Q, with Top hidden | none |
| Ctrl+Q, with Alpha hidden | Top |
| Ctrl+W, with Sub disabled | Top |
| Ctrl+W, with Sub hidden | Top |
| Ctrl+W, with Leaf disabled | Top, Sub |

So:
- A menu that holds items raises Click as it opens, a submenu as much as a top-level one.
- A shortcut raises the Click of each menu from the top-level one down to its item.
- It stops at the first menu that is disabled or hidden, without raising that one's Click.
- The item's Enabled is read only after the Clicks above it have run. That is what lets a parent's Click enable its items
  and have their shortcuts work, the pattern every Edit menu uses.

The comment that stood in `VBLoader`, that VB6 raises Click "on nothing above" the chosen item, was a guess, and this
table overturns it.

**In HexIDE (#1344):** as the table says. Opening a menu that holds items raises its Click, Avalonia's `SubmenuOpened`
being the moment, so `interact` with `expand` raises it too. A shortcut queues each Click in order, and each is asked at
its turn whether it, and every menu above it, is still enabled and visible. Not measured, and assumed to match: whether
a menu between the top and the item is also asked late, and opening a menu with the mouse.

