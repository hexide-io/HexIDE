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

So Page Up first moves the selection to the view's top row. Only when it's there already does it page up, by the rows shown less one, scrolling with it. Page Down, measured for #3275, is the mirror image.

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
| Page Up, from `ListIndex` 36 | KeyDown, Click (28), KeyUp: no Scroll, moving to the top row |
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
