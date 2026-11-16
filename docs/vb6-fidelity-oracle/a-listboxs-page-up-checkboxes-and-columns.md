## A ListBox's Page Up, Selected and Text, checkboxes and columns (2026-11-16, #3986)

**The question.** #3275 measured when a ListBox raises Scroll for a plain, single-column list
(`a-listboxs-scroll-event.md`). #3986 left Page Up, `Selected` and `Text` bringing an item into view, Style 1, and
`Columns` above 0.

**The probe.** vb6.exe SP6, `/make`, run in the oracle guest's console session. A made EXE ran in the oracle VM's console session with three 60-item ListBoxes, each 2400 by 2205 twips:
- **List1**, plain, showing 11 rows;
- **List2**, Style 1 (checkboxes), showing 9;
- **List3**, `Columns = 2`, showing two columns of 9.

Each handler logged its event with `TopIndex` and `ListIndex`. A Timer took commands from a file to set the focus, `ListIndex`, `TopIndex`, `Selected` and `Text`, and real keys came through `SendKeys` with the form in the foreground.

### Page Up, on the plain list

| From | Events |
|---|---|
| `ListIndex` 37, `TopIndex` 27: the selection below the top row | KeyDown, Click (`ListIndex` **27**, `TopIndex` 27), KeyUp: **no Scroll** |
| `ListIndex` 30, `TopIndex` 30: the selection on the top row | KeyDown, Click (20, `TopIndex` already 20), **Scroll** (20), KeyUp |
| `ListIndex` 5, `TopIndex` 0 | KeyDown, Click (0), KeyUp |
| `ListIndex` 0, `TopIndex` 0 | KeyDown, KeyUp |

**Overturned the same day; see the next section.** These four rows first read as "Page Up moves the selection to the view's top row, and pages up only from there". But in each of them the selection was on the view's bottom row, or within a page of the top of the list, so they can't tell that rule from "a page less one back from the focus". Rows where the two disagree chose the second.

### Page Up and Page Down a page from the focus, wherever it is (2026-11-16, #3986's review)

**Why these rows.** Reviewing the first implementation showed that the Page Up rows above couldn't discriminate.
"Go to the top row first" and "a page less one back from the focus" give the same answer for each of them. The rows
below put the focus where the two rules disagree: mid-view, one below the top row, above the view and below it. They
use the same EXE, lists and method. The plain list shows 11 rows, so a page is 10, and the Style 1 list shows 9, so a
page is 8.

| List | From `ListIndex`, `TopIndex` | Key | Events | Top row first would give |
|---|---|---|---|---|
| plain | 32, 27: mid-view | Page Up | KeyDown, Click (**22**, `TopIndex` 22), Scroll (22), KeyUp | 27, no Scroll |
| plain | 28, 27: one below the top row | Page Up | KeyDown, Click (**18**, 18), Scroll (18), KeyUp | 27, no Scroll |
| plain | 5, 20: above the view | Page Up | KeyDown, Click (0, 0), Scroll (0), KeyUp | 20 |
| plain | 50, 20: below the view | Page Up | KeyDown, Click (**40**, `TopIndex` 30), Scroll (30), KeyUp | 20 |
| plain | 4, 0: mid-view | Page Down | KeyDown, Click (**14**, `TopIndex` 4), Scroll (4), KeyUp | 10, no Scroll |
| plain | 10, 0: the bottom row | Page Down | KeyDown, Click (20, 10), Scroll (10), KeyUp | 20 |
| plain | 40, 0: below the view | Page Down | KeyDown, Click (50, 40), Scroll (40), KeyUp | |
| plain | 2, 20: above the view | Page Down | KeyDown, Click (**12**, `TopIndex` 12), Scroll (12), KeyUp | |
| Style 1 | 31, 28: mid-view | Page Up | KeyDown, Click (**23**, 23), Scroll (23), KeyUp | 28, no Scroll |
| Style 1 | 2, 0: mid-view | Page Down | KeyDown, Click (**10**, `TopIndex` 2), Scroll (2), KeyUp | 8, no Scroll |

**The rule, for both keys and both styles:**
- The focus moves by a page, the rows shown less one, clamped to the list.
- The view then scrolls the least it can to show the new focus: the item lands on the top row going up, and on the
  bottom row going down, unless it was already in view.
- Click comes when the selection moves, then Scroll when the top row changed, as the arrows do.
- **The view's top row plays no part.** The focus counts from wherever it is, even out of view.

**`ListIndex = -1` keeps the focus.** It clears the selection, but the key still counts from the item that last had
the focus:

| Before | Then | Key | Events |
|---|---|---|---|
| focus 40, then `ListIndex = -1`, `TopIndex = 20` | | Page Up | KeyDown, Click (`ListIndex` **30**, `TopIndex` 20), KeyUp: no Scroll, 30 being in view |
| focus 12, then `ListIndex = -1`, `TopIndex = 20` | | Page Down | KeyDown, Click (`ListIndex` **22**, `TopIndex` 20), KeyUp |

**Side readings from the setup steps.** These were measured in passing, not designed:
- `ListIndex = 4` from `TopIndex` 22 gave `TopIndex` 4, putting the item on the top row.
- `ListIndex = 50` from `TopIndex` 0 gave 40, putting it on the bottom row.

So an assignment scrolls the least it can, as a key does, with Click only.

### Selected and Text bringing an item into view

| Step, from `TopIndex` 0 | Events |
|---|---|
| plain list, `Selected(50) = True` | Click (`ListIndex` 50, `TopIndex` already 40): **no Scroll** |
| plain list, `Text = "item 45"` | Click (45, `TopIndex` 35): no Scroll |
| plain list, `Text = "item 05"`, already in view | Click (5, `TopIndex` 0) |
| Style 1, `Selected(50) = True` | **ItemCheck 50**, then Click (`ListIndex` 50, `TopIndex` 42): no Scroll |

These behave as #3275's `ListIndex = 50` row does: the list scrolls to the item, and only Click is raised. On a checkbox list `Selected` checks the item and selects it too, with ItemCheck first.

### Style 1 (checkboxes)

| Step | Events |
|---|---|
| Down, on the bottom row (`ListIndex` 27, `TopIndex` 17) | KeyDown, Click (28, `TopIndex` already 20), Scroll (20), KeyUp |
| Page Down | KeyDown, Click (36, 28), Scroll (28), KeyUp |
| Page Up, from `ListIndex` 36 | KeyDown, Click (28), KeyUp: no Scroll |
| End | KeyDown, Click (59, 51), Scroll (51), KeyUp |
| Home | KeyDown, Click (0, 0), Scroll (0), KeyUp |
| Space | KeyDown, **Click, then ItemCheck 0**, KeyUp |
| `TopIndex = 59` | Scroll (51): the last page |

A checkbox list scrolls exactly as a plain one does, with 9 rows to a page here.

### Columns = 2

| Step | Events |
|---|---|
| `TopIndex = 25` | Scroll (**18**): the start of the column holding item 25, 9 rows to a column |
| `TopIndex = 59` | Scroll (**45**): the last page, columns 45 and 54 |
| `ListIndex = 21`, from `TopIndex` 0 | Click (`TopIndex` 9): it scrolls a column, with no Scroll |
| Down, `ListIndex` 21 to 22, in view | KeyDown, Click, KeyUp |
| Right, `ListIndex` 3 to 12, in view | KeyDown, Click, KeyUp |
| **Right, `ListIndex` 14 to 23, a column out of view** | KeyDown, Click (23, `TopIndex` already 9), **Scroll** (9), KeyUp |
| Page Down from 0, `TopIndex` 0 | KeyDown, Click (**17**, the last item shown), KeyUp: no Scroll |
| Page Down again | KeyDown, Click (34, 18), Scroll (18), KeyUp |
| Page Up | KeyDown, Click (17, 9), Scroll (9), KeyUp |
| End | KeyDown, Click (59, 45), Scroll (45), KeyUp |
| Home | KeyDown, Click (0, 0), Scroll (0), KeyUp |
| `ListIndex = 50`, from `TopIndex` 0 | Click (`TopIndex` 36): no Scroll |

- **In a multi-column list, `TopIndex` moves by whole columns,** and a horizontal scroll raises Scroll as a vertical one does.
- **The same rule holds:** Scroll comes once for each change of the top item through the keys or `TopIndex`, and never for a `ListIndex`, `Selected` or `Text` assignment.

### Not measured

- **A held arrow.** `keybd_event` sends one key-down and Windows doesn't auto-repeat it, so holding Down for 1.5 s gave one KeyDown. Measuring the repeat needs a real keyboard, or repeated key-downs with the repeat flag set.
- **`WM_VSCROLL`** is already on #3275's page.
