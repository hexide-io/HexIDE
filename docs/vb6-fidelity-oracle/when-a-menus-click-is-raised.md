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


## A menu shortcut's key events (2026-11-23, #5011, `/make`, SP6)

**The question.** Does a form's or a control's KeyDown, KeyPress or KeyUp see a key that a menu's Shortcut takes, and in
what order against the menu Clicks? #5011 needed it: on a Mac, a native menu that claims a shortcut takes the key
before the window sees its KeyDown.

**The probe.**
- **The form:** KeyPreview True, holding Text1 (focused) and a Timer. It has one menu, Top, with two items:
  - Alpha, shortcut Ctrl+Q;
  - Dis, shortcut Ctrl+E, disabled at design time.
- **The log:** every Form and Text1 KeyDown, KeyPress and KeyUp, with KeyCode, Shift or KeyAscii, and every Click.
- **The keys:** the Timer sends one set of keys per tick with `SendKeys`.
- **The runs:** built native and p-code with `/make`, and run in the console session of the Win10-D oracle clone. The
  form was the foreground window with Text1 focused. Both builds logged the same lines, so one column is given.

| Keys | Events, in order |
|---|---|
| Ctrl+Q (Alpha's shortcut) | Form KeyDown 17/2, Text1 KeyDown 17/2, **Form KeyDown 81/2, Text1 KeyDown 81/2**, Top Click, Alpha Click, Form KeyUp 81/2, Text1 KeyUp 81/2, Form KeyUp 17/0, Text1 KeyUp 17/0 |
| Ctrl+E (Dis's shortcut, Dis disabled) | Form KeyDown 17/2, Text1 KeyDown 17/2, **Form KeyDown 69/2, Text1 KeyDown 69/2**, Top Click, **Form KeyPress 5, Text1 KeyPress 5**, Form KeyUp 69/2, Text1 KeyUp 69/2, Form KeyUp 17/0, Text1 KeyUp 17/0 |
| Ctrl+K (no shortcut) | Form KeyDown 17/2, Text1 KeyDown 17/2, Form KeyDown 75/2, Text1 KeyDown 75/2, Form KeyPress 11, Text1 KeyPress 11, Form KeyUp 75/2, Text1 KeyUp 75/2, Form KeyUp 17/0, Text1 KeyUp 17/0 |
| Ctrl+Q, KeyPreview set False first | Text1 KeyDown 17/2, Text1 KeyDown 81/2, Top Click, Alpha Click, Text1 KeyUp 81/2, Text1 KeyUp 17/0 |
| Ctrl+K, KeyPreview False | Text1 KeyDown 17/2, Text1 KeyDown 75/2, Text1 KeyPress 11, Text1 KeyUp 75/2, Text1 KeyUp 17/0 |

Text1 was still empty at the end.

**So:**
- **A shortcut's key still raises KeyDown, before the Clicks,** on the form when KeyPreview is on, and on the focused
  control either way. It raises KeyUp after them. The shortcut takes only the KeyPress.
- **When the shortcut's item doesn't run, the key goes on as a character.** With Dis disabled, Top's Click came, and then
  KeyPress 5 (Ctrl+E's character) on the form and Text1, as for a key with no shortcut. The text box ignored the
  character.

**The obvious guess was that a shortcut hides its key from the program entirely,** since the menu takes it. It is wrong
for KeyDown and KeyUp.

**Not measured:**
- whether a KeyDown handler that sets KeyCode to 0 stops the shortcut;
- an item made unreachable another way: Visible False, or Top disabled or hidden. Those raise fewer Clicks (#1344's
  table), and whether they then raise KeyPress is unknown.

**In HexIDE** (a Windows Debug build of `ef65c5d96`, 2026-11-23): the same form, with the user32 call the probe used
for its foreground check taken out and the keys pressed with `press_key`, since HexIDE has no SendKeys:
- **Ctrl+Q:** gave both Clicks and every KeyUp, but no KeyDown 81 on the form or Text1.
- **Ctrl+E:** gave Top's Click, but no KeyDown 69 and no KeyPress 5.
- **Ctrl+K:** gave its KeyDowns and KeyUps, and no KeyPress 11, which #2325's measured table lists and HexIDE raises
  only for Ctrl+A.

Reading the code: the runtime matches shortcuts in the tunnel phase on the form's content and marks the key handled
(`IDE/HexIDE.Runtime/VBLoader.cs:365-385`). The handlers that raise a control's KeyDown skip a handled key
(`IDE/HexIDE.Runtime/BuiltinControls/TextBoxKeyEvents.cs:22-25`).
