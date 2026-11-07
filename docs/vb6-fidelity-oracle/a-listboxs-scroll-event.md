## A ListBox's Scroll event: what raises it, and what doesn't (2026-11-06, #3275)

**The question.** HexIDE's ListBox has no Scroll event (#3275). When does vb6.exe raise it, how many times, and in what
order against Click and the key events?

**The probe.** Two forms, each built with `/make` as native code and as p-code on the Win10 guest, VB6 SP6, and run in
the guest's console session. All four builds logged the same lines.
- Form1 holds List1, 2400 by 2205 twips, which shows 11 rows. It gets 60 items, `item 00` to `item 59`, in Form_Load.
- The list has the focus, and the form has the foreground.
- Each handler logs its event with `TopIndex` and `ListIndex`: Scroll, Click, KeyDown, KeyUp, MouseDown and MouseUp.
- A Timer takes one step every 500 ms:
  - the mouse with `SetCursorPos` and `mouse_event`, with the arrows, the track and the thumb found by
    `GetScrollBarInfo`;
  - the wheel with `mouse_event`, a notch being 120;
  - the keys with `keybd_event`;
  - messages with `SendMessage`.

The first form was also run in session 0, where no real input arrives. It raised the same events for every code and
message row.

**From code:**

| Step | vb6.exe |
|---|---|
| `TopIndex = 5`, from 0 | **Scroll** once, with `TopIndex` already 5 inside it |
| `TopIndex = 5` again | nothing |
| `TopIndex = 0`, from 5 | Scroll once, 0 inside it |
| `TopIndex = 59`: the last page starts at 49 | Scroll once, with `TopIndex` **49** inside it |
| `TopIndex = 49`, and then `59`, with `TopIndex` already 49 | nothing, both times |
| `TopIndex = 60`, which is `ListCount`, and `TopIndex = -1` | error 380, "Invalid property value"; nothing raised, and `TopIndex` unchanged |
| `ListIndex = 27`, the bottom row shown | Click only |
| `ListIndex = 50`, below the rows shown, with `TopIndex` 0 | **Click only**, though the list scrolls to `TopIndex` 40 |
| 60 `AddItem` calls in Form_Load | nothing |

**The mouse**, real input:

| Input | vb6.exe |
|---|---|
| one wheel notch down, `TopIndex` 0 | **nothing**, though `TopIndex` goes to 3 |
| one notch up | nothing; back to 0 |
| two notches down, sent in the same Timer tick | nothing; `TopIndex` 6 |
| one notch up, from 49 | nothing; 46 |
| click the down arrow, `TopIndex` 6 | Scroll once, 7 inside it |
| click the track below the thumb, `TopIndex` 7 | Scroll once, 17 inside it |
| click the up arrow, `TopIndex` 0 | nothing |
| press on the thumb, move 20 px down, move 20 px more, release, one Timer tick apart | Scroll at each move, with `TopIndex` 11 and then 22; nothing at the release |

No press on the scroll bar raised MouseDown or MouseUp. The Timer went on ticking during the drag, inside the scroll
bar's own message loop, so the moves and the release came from it.

**Keys**, the list focused:

| Key | vb6.exe |
|---|---|
| Down, on the bottom row shown: `ListIndex` 27, `TopIndex` 17 | KeyDown, Click (`ListIndex` 28, `TopIndex` already 18), **Scroll** (18), KeyUp |
| Up, to a row still shown | KeyDown, Click, KeyUp: no Scroll |
| Page Down, `ListIndex` 27, `TopIndex` 18 | KeyDown, Click (37), Scroll (27), KeyUp |
| Home | KeyDown, Click (0), Scroll (0), KeyUp |
| End, `TopIndex` 0 | KeyDown, Click (59), Scroll (49), KeyUp |
| Down, on the last item | KeyDown, KeyUp |

**Messages**, sent to List1 with `SendMessage`:

| Message | vb6.exe |
|---|---|
| `WM_VSCROLL` `SB_LINEDOWN`, `TopIndex` 40 | Scroll once, 41 inside it |
| `SB_ENDSCROLL` | nothing |
| `SB_THUMBPOSITION` 20, `TopIndex` 41 | Scroll once, 20 inside it |
| `SB_LINEUP`, `TopIndex` 0 | nothing |
| `SB_LINEDOWN`, `TopIndex` 49, the last page | nothing |

What these add:
- **Scroll comes once for each change of the top row** made through the scroll bar, the keyboard, a `WM_VSCROLL`
  message or an assignment to `TopIndex`. Whatever the input, it never comes when the top row stays where it was.
- **The wheel scrolls the list and raises no Scroll.** Neither does a `ListIndex` assignment that brings its item into
  view. In both cases `TopIndex` has moved by the time the program next reads it.
- **During a thumb drag, Scroll comes with each move**, so a handler can follow the thumb. The release raises nothing
  more.
- **A key that scrolls raises Click and then Scroll**, both between KeyDown and KeyUp. Inside Click, `TopIndex` already
  has its new value.
- **`TopIndex` accepts 0 to `ListCount - 1`**, and moves only as far as the last page. Outside that range it is error
  380.

**Overturned.** The obvious reading is that anything that scrolls the list raises Scroll, the wheel included. vb6.exe
raises nothing for the wheel, nor for a `ListIndex` assignment that scrolls. #3275's own report scrolled with the
wheel, and there vb6.exe is as silent as HexIDE.

**Not explained.** These rows don't show why the wheel and `ListIndex` are left out, when the keyboard and `TopIndex`
are not.

**Not measured:**
- a horizontal scroll bar, with `Columns` above 0;
- a MultiSelect list, or Style 1, with check boxes;
- a scroll arrow held down so that it repeats;
- `Selected(i) = True` or `Text`, where the item is out of view;
- a run under the VB6 IDE: only compiled exes ran;
- the wheel at a setting other than this guest's, which scrolled 3 rows a notch.

**In HexIDE**, at main 0ee530a5d, the ListBox has no Scroll event, so no row above raises one (#3275).
