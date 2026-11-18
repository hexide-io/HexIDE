## What the Properties window's object box lists, and in what order (2026-11-18, #1520, design-time, IDE, SP6)

**The question.** VB6's Properties window has an object box, a drop-down naming the form and each of its controls. #1520
measured one control array on 2026-10-23, during #1381's live check. A form holding `optSize(1)` and then
`optSize(0)` listed them as `optSize(0)` and `optSize(1)`, each with its index and in index order, though the file held
1 first. That showed the box sorts, but not by what. Does it sort by name? Is it case-sensitive? Does the form come
first? Is an array's index compared as a number or as text? Or does it follow file order or TabIndex?

**The probe.** SP6's IDE (`VB6.EXE` 6.00.9782) on the oracle guest's console. The guest's user culture is en-GB and its
system locale en-US. A Standard EXE form, opened from its `.vbp`, with Form1 selected in the Project window, then
Shift+F7. The object box was dropped down and read from a screenshot.

The first form's controls, in file order with their TabIndex:

| Block | Class | TabIndex |
|---|---|---|
| zLabel | Label | 3 |
| optSize, Index 10 | OptionButton | 5 |
| Command1 | CommandButton | 0 |
| optSize, Index 2 | OptionButton | 4 |
| aButton | CommandButton | 2 |
| Bravo | TextBox | 1 |

The second form added three CommandButtons after them: `aZ`, `a_x` and `A1`, with TabIndex 6, 7 and 8.

| Form | The object box, top to bottom |
|---|---|
| First | aButton, Bravo, Command1, **Form1**, optSize(10), optSize(2), zLabel |
| Second | **a_x**, A1, aButton, aZ, Bravo, Command1, Form1, optSize(10), optSize(2), zLabel |

Each row read as the name in bold, then the class: `aButton CommandButton`, `Form1 Form`, `optSize(10) OptionButton`.

**What it shows:**
- **The list is sorted by name, without regard to case:** aButton before Bravo, zLabel last. It isn't file order, and
  it isn't TabIndex, which ran Command1, Bravo, aButton, zLabel.
- **The form isn't first.** It sorts among its controls by its own name.
- **An array's element sorts by its name with the index as text:** `optSize(10)` comes before `optSize(2)`.
- **The comparison isn't ordinal.** `a_x` comes before `A1`, where an ordinal comparison, ignoring case, puts `1` (0x31)
  before `_` (0x5F). Punctuation sorting before digits, and digits before letters, is a locale-aware comparison.
  - Sorting the second form's ten names in .NET 10, `StringComparer.OrdinalIgnoreCase` gives `A1, aButton, aZ, a_x, ...`
    and differs.
  - `InvariantCultureIgnoreCase`, and en-GB or en-US ignoring case, each give VB6's order exactly.

**Not measured:**
- **A locale where culture orders differ,** such as Turkish's dotted and dotless I, or Swedish's letters after Z. These
  rows don't separate the invariant culture from the user's.
- **Whether the class name takes part in the sort.** Every name here differs, so the name alone decides.
- **A menu,** which the object box also lists.
