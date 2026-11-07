## A Simple Combo's list, drawn and clicked (2026-10-23, #1603)

**The question.** HexIDE drew a Simple Combo (`Style = 1`) as a Dropdown Combo, with a button that drops its list. VB6
shows the list all the time, under the edit box. #1429 measured a Simple Combo's typing and keys, but the pointer on its
list couldn't be measured against HexIDE, which had no such list. How is the list drawn, what does a click on it raise,
and what does a Height too small for an item do?

**The probes.** VB6 as first installed on the oracle guest, the release build, before SP6. One form, compiled native and
p-code. Three Simple Combos, 2000 twips wide, each saved with its own name as its Text and filled in `Form_Load`:
- `cboS`, saved `Height = 1500`, with eight items, apple to honey;
- `cboT`, saved `Height = 400`, with three;
- `cboU`, saved `Height = 900`, with two.

Each combo's Click, DblClick, Change, GotFocus, Scroll and DropDown logged its name, ListIndex and Text, and Activate logged
the three Heights. On the guest's console a script read each combo's own windows: its `Edit`, and its `ComboLBox` with
`LB_GETITEMHEIGHT` and its style. It then pressed and released the real pointer, by `mouse_event`, 900 ms apart, a double
click 80 ms apart, and turned the wheel once over cboS. A second run clicked in a different order. The same form was
opened in the VB6 IDE's designer, captured, and saved.

Native and p-code logged the same lines, line for line, in both runs.

**How it is drawn.** At run time each combo is its edit box, sunken, 21 pixels high, with its list directly under it,
sunken too, its rows 13 pixels high. There is no button. cboS's eight items didn't fit its five rows, so its list had a
vertical scroll bar. cboU's two fitted, and had none. cboT showed its edit box and no list. The designer drew each the
same way, the edit box holding the combo's Text and the list empty.

**The Height snaps to whole rows**, at run time and in the designer, and the designer's next save writes it:

| Saved | Read at run time | Written by the designer's save | Rows shown |
|---|---|---|---|
| 1500 | 1350 | 1350 | 5 |
| 400 | 315 | 315 | 0, the edit box alone |
| 900 | 765 | 765 | 2 |

All three fit **21 + 4 + 13n pixels, n = floor((Height − 25) / 13)**: the edit box, the list's border and whole rows. With
n = 0 there is no list, and the Height is the edit box's. The 21 and the 13 are MS Sans Serif 8's, VB6's default font. Only
that font was measured, so this is a rule for it, not a constant.

**The pointer.** The first run, with the focus in cboS as the form opened:

| Input | Logged |
|---|---|
| Click cboS's second item | `cboS_Click ListIndex 1 Text banana` |
| Double-click its third | `cboS_Click ListIndex 2 Text cherry`, `cboS_DblClick ListIndex 2 Text cherry` |
| Click cboU below its items | `cboU_GotFocus ListIndex 1 Text cboU`, `cboU_Click ListIndex 1 Text banana` |
| Click cboU's first item | `cboU_Click ListIndex 0 Text apple` |
| The wheel down once over cboS | nothing; its top row went from 0 to 3 |

The second run, from the same start:

| Input | Logged |
|---|---|
| Click cboU's second item | `cboU_GotFocus ListIndex 1 Text cboU`, `cboU_Click ListIndex 1 Text banana` |
| Click cboU below its items, the second selected | `cboU_Click ListIndex 1 Text banana` |
| Click cboU's first item | `cboU_Click ListIndex 0 Text apple` |
| Click cboU below its items again | `cboU_Click ListIndex 1 Text banana` |
| Click cboS's fourth item | `cboS_GotFocus ListIndex 3 Text cboS`, `cboS_Click ListIndex 3 Text date` |
| Click it again | `cboS_Click ListIndex 3 Text date` |
| Double-click cboU's second item | `cboU_GotFocus ListIndex 1 Text banana`, `cboU_Click ListIndex 1 Text banana`, `cboU_DblClick ListIndex 1 Text banana` |

A third run, with the focus in cboS, pressed F4 and then Alt+Down, which drop a Dropdown Combo's list. Neither raised
anything.

**What it shows:**
- **A click on an item picks it,** with Click, and the edit box takes its text, selected. **The item already selected is
  picked again,** with another Click.
- **A click below the items picks the last of them.**
- **A double click is one Click and then DblClick**, with no second Click.
- **None of it raises Change**, though the edit box's text changes. Nor DropDown: the list never drops, by the
  pointer, F4 or Alt+Down.
- **A click on a combo without the focus moves the list's selection before GotFocus runs**, so GotFocus reads the new
  ListIndex. The edit box hasn't taken the item's text yet, so it reads the Text from before. Click follows with the new
  Text.
- **The wheel scrolled three rows and raised no Scroll.**
- After the pointer, the focus was in the combo's edit box, its text selected, and the item highlighted in the list.

**Not measured:**
- a font other than MS Sans Serif 8;
- `Height = n` from code;
- the Scroll event by the scroll bar's own arrows and thumb;
- the keys in the list. #1429 measured Up and Down in a Simple Combo from its edit box.

### In HexIDE (#1603)

- **A Simple Combo is drawn as its edit box over its list,** always shown, with no button, in the designer and at run
  time. The edit box is 21 pixels high, as measured at the default font, and the list takes the rest of the Height and
  scrolls when its items don't fit. Its rows are a line of text high.
- **The pointer on the list is vb6.exe's:**
  - a press picks the item under it, or the last below them, the one already selected too, with Click;
  - a double click's second press is DblClick;
  - none of it raises Change or DropDown.

  The list's selection moves before GotFocus runs, and code reading the Text inside that GotFocus gets the Text from
  before the pick.
- **Not followed: the Height's snap to whole rows** ([#1655](https://github.com/hexide-io/HexIDE/issues/1655)). HexIDE
  keeps the file's Height and draws a partial row at the foot. Snapping with Avalonia's text metrics would rewrite the
  Height of every Simple Combo VB6 saved.
- **The wheel scrolls by Avalonia's step**, not by three rows.

