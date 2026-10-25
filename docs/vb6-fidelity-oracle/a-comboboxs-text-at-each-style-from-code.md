## A ComboBox's Text, at each Style, from code (2026-10-22, #926)

**The probe.** A form with four ComboBoxes, built with `/make` and run, native and p-code giving byte-identical logs:
- `Combo1`, Style 0 (Dropdown Combo), saved with `Text = "Combo1"`;
- `Combo2`, Style 1 (Simple Combo), saved with `Text = "Combo2"`;
- `Combo3`, Style 2 (Dropdown List), with no Text line;
- `Combo4`, Style 2, saved with `Text = "x"`.

Each of the first three was filled with `apple`, `Banana` and `cherry`, then given each Text in turn. Every handler for
Click and Change logged its name, and each row was read under `On Error Resume Next`.

| Read at load | VB6 |
|---|---|
| `TypeName(Combo1.Text)` | **String** |
| `Combo1.Text`, `Combo2.Text` | `Combo1`, `Combo2`, as saved |
| `Combo3.Text`, and **`Combo4.Text`, saved as `x`** | `""` each: **a Dropdown List ignores a saved Text** |
| `ListIndex` of each | -1 |
| `Style` of each | `0`, `1`, `2`, an **Integer** |

| Statement | Style 0 and Style 1, alike | Style 2 |
|---|---|---|
| `Text = "Banana"`, an item's text | Text `Banana`, **`ListIndex` -1**, **Change** | selects it: `ListIndex` 1, **Click** |
| `Text = "banana"`, its text in other case | Text `banana`, `ListIndex` -1, Change | `Banana`, already selected, stays so; Text `Banana`; **Click all the same** |
| `Text = "Ban"`, `"zz"` or `""` | that Text, `ListIndex` -1, Change | Err **383**, nothing changed, no event |
| `Text = "cherry"` | Text `cherry`, Change | selects it, `ListIndex` 2, Click |
| `ListIndex = 0` | Text `apple`, `ListIndex` 0, **Click, and no Change** | Text `apple`, Click |
| `ListIndex = -1` | Text `""`, Click, no Change | Text `""`, Click |
| `Text = Null` | Err **13**, not 94; nothing changed | Err 13 |
| `Text = 5` | Text `5`, Change | Err 383 |

What these show:
- **A Style 0 or 1 combo's Text is its edit box's,** free of the list. Setting it to an item's text selects nothing, and
  every assignment here raised Change. The second probe, below, asks what this one did not.
- **A Style 2 combo's Text is its selected item.** Setting it selects the first item whose whole text matches without
  regard to case, and raises Click every time. With no match it is 383, "'Text' property is read-only".
- **Moving `ListIndex` changes a Style 0 combo's Text** without raising Change.

**A second probe,** the same way, on a Style 0 and a Style 1 combo filled with `apple`, `Banana` and `cherry`. The two
styles gave the same rows, and native and p-code the same logs:

| Statement | VB6 |
|---|---|
| `Text = Text`, unchanged | nothing: **no Change**, `ListIndex` -1 still |
| `ListIndex = 1` | Text `Banana`, Click |
| `Text = "zz"`, with item 1 selected | Text `zz`, **`ListIndex` -1**, Change |
| `Text = "cherry"`, another item's text, with item 1 selected | Text `cherry`, **`ListIndex` -1**: it selects nothing; Change |
| `Text = "Banana"`, the selected item's own text | **nothing**: `ListIndex` 1 still, no Change |
| `Text = "banana"`, that text in other case | Text `banana`, **`ListIndex` -1**, Change |
| `ListIndex = 2`, then `ListIndex = 2` again | Click the first time, **none the second** |
| `Text = "cherry"`, with item 2, `cherry`, selected | nothing |
| `Clear` | Text `""`, `ListIndex` -1 |

So **a Style 0 or 1 combo's `Text = s` does nothing when `s` is its Text already,** compared exactly. Otherwise it puts `s`
in the edit box, clears the selection and raises Change.

What resisted explanation:
- **`Text = Null` is 13, Type mismatch,** where a TextBox's `SelText = Null` and the list's `List(Null)` are 94.

### Drawn in the VB6 IDE's designer

**The probe.** In the guest's console session, the VB6 IDE opened a project holding an empty Form1. Each toolbox icon was
double-clicked, which adds that control to the form at its default size, centred: a TextBox, then three ComboBoxes. Style
was set in the Properties window by Ctrl+Shift+S, the value's number and Enter, and the form saved with Ctrl+S. Every
step was checked by a screenshot. The saved blocks:

| Drawn | What the designer wrote |
|---|---|
| TextBox | `Text = "Text1"`, Height 495, Width 1215 |
| ComboBox, Style left at 0 | `Text = "Combo1"`, Height 315, Width 1215 |
| ComboBox, Style set to 1 | `Style = 1 'Simple Combo`, `Text = "Combo2"`, Height 315 |
| ComboBox, Style set to 2 | `Style = 2 'Dropdown List`, **no Text line**. The designer still showed `Combo3` in the box |
| That combo, Style set back to 0 | `Text = "Combo3"` |
| Combo1 given `Text = zz` in the Properties window | `Text = "zz"` |
| Then Style 2 | the Properties window's **Text read `Combo1`** |
| Then Style 0 again | `Text = "Combo1"`: the `zz` is gone |

What these show:
- **A drawn TextBox or ComboBox starts with its own name as its Text,** as its Caption would be for a control that has
  one. The names count from 1 (#540).
- **Setting Style to 2 resets the Text to the control's name,** and a Dropdown List's Text is never written. Going back
  to 0 or 1 writes that name.

Not measured: what Style 2 does in the designer to a combo whose Name has been changed, which would show whether it is
the name now or the name the control was drawn with.

A Dropdown List's box shows the name it has now: "What VB6's designer draws in a Dropdown List's box" (#1604).

### In HexIDE (#926)

- **A drawn TextBox or ComboBox** starts with its name as its Text, and a control with no Caption is no longer given one.
- **A Dropdown Combo's and a Simple Combo's Text** reads and writes at run time as both probes measured. That includes
  `Null` as 13, and the two #821 rows in which the selected item's text follows it. Avalonia's editable combo would select
  the item a Text matches, without regard to case, so while code sets the Text every item reads as a text no program can
  set, and none matches.
- **A Dropdown List's Text** is never written to the .frm, and is ignored at load. At run time it was refused as not
  implemented until #1428 modelled the Style (below).
- **Click and Change** come as measured since #1429, which records them with real input.

### What VB6's designer draws in a Dropdown List's box (2026-10-23, #1604)

**The question.** "Drawn in the VB6 IDE's designer" saw a drawn combo set to Style 2 still showing its name, `Combo3`, in
its box. It left two cases open:
- a Dropdown List read from a file, which has no Text line to show;
- a Dropdown List renamed in the Properties window: does the box show the new name, or the name it was drawn with?

**The probe.** In the guest's console session, the VB6 IDE opened a project holding one form, through the agent. The form
held, from its file:
- `Combo1` and `cboList`, two Dropdown Lists with no Text line;
- `Combo3`, a Dropdown List whose file saved `Text = "x"`;
- `Combo4`, a Dropdown Combo with `Text = "Combo4"`.

The designer was opened from the Project window, as in "What VB6's Properties window shows a position in". Combo1 was
selected in the Properties window's object box. Then F4, Home and the keys `cboNew`, and Enter. Home put the caret at
the start of the (Name) row's value, so the name became `cboNewCombo1`. The form was saved with Ctrl+S, and every step
was captured by a screenshot.

| Combo | Its box in the designer |
|---|---|
| Combo1, from the file | `Combo1` |
| cboList, from the file | `cboList` |
| Combo3, whose file saved `Text = "x"` | `Combo3`, not `x` |
| Combo4, a Dropdown Combo | `Combo4`, its Text |
| Combo1 renamed `cboNewCombo1` | `cboNewCombo1`, at once |

The save wrote `Begin VB.ComboBox cboNewCombo1` with no Text line. It also wrote **no Text line for Combo3**: the `x` the
file had held was dropped.

**What it shows:**
- **The designer draws a Dropdown List's box with the control's name**, whether it was drawn or read from a file.
- **It is the name the control has now.** A rename shows at once.
- **A Text saved in the file is not drawn,** and the next save drops it, as "Drawn in the VB6 IDE's designer" found
  that a Dropdown List's Text is never written.

**What Text a renamed Dropdown List takes when its Style goes back to 0** was not measured on the release build. A second
run meant to set the Style lost its keys to the (Name) row: it renamed the control `S` and then refused `0` as a name,
with "Not a legal object name: '0'". Its box showed `S` at once, which agrees with the rename row above.

**SP6 answered it** (2026-10-24, VB6.EXE 6.00.9782, the same rig with the Project window clicked before its keys). The
rename save was byte-identical to the release build's. Then Ctrl+Shift+S twice reached Style, and `0` and Enter set it.
The save wrote `Begin VB.ComboBox cboNewCombo1` with `Text = "cboNewCombo1"`, and the box showed `cboNewCombo1`. So the
Text a Dropdown List goes back with is the name it has then, not the one it was drawn with.

### In HexIDE (#1604)

- **The designer draws a Dropdown List's box with the control's current name,** in its ForeColor, as an item would be
  drawn. A Text saved in the file isn't drawn. The running control still shows nothing there until an item is selected.
- **A Dropdown List renamed takes its new name as its Text,** so Style 0 afterwards writes that name, as SP6 did.
- **This is the designer's first control that draws differently from the running one.** `ComponentBaseClass` has
  `InstantiateForDesigner` for it, which the designer's renderer calls. Every other class returns its running control.

### Style, and a Dropdown List's Text from code (2026-10-23, #1428)

**The probe.** A form with a Dropdown List, `Combo1`, filled with `5`, `apple`, `10`, `2.5` and `True`, and a Dropdown
Combo, `Combo2`, built with `/make` native and p-code and run. Each row was logged under `On Error Resume Next`, with
Click and Change logged by their handlers. A second project, its `Sub Main` holding `Form1.Combo1.Style = 1`, was only
built. Native and p-code gave the same logs and the same build error.

| Statement | VB6 |
|---|---|
| `Combo1.Style`, `Combo2.Style` | `2` and `0`, `TypeName` **Integer** |
| `Text = 5` | Click, ListIndex 0, Text `5` |
| `Text = 10#` | Click, ListIndex 2, Text `10` |
| `Text = 2.5` | Click, ListIndex 3, Text `2.5` (the guest's locale is en-US) |
| `Text = True` | Click, ListIndex 4, Text `True` |
| `Text = "APPLE"` | Click, ListIndex 1, Text `apple`: the item's own text |
| `Text = "5 "` | **383** "'Text' property is read-only", nothing changed |
| `Text = Empty` | **383**, nothing changed |
| `Combo1.Style = 1`, built | **Compile error: "Can't assign to read-only property"** |

What these show:
- **A Dropdown List's Text is made a String as `CStr` makes it, then matched.** A number or a Boolean selects the item
  whose text is its `CStr`. `Empty` is `""`, which no item is, so it is 383 as `""` was in the first probe.
- **Style is read-only to a program**: assigning it does not compile, as MultiSelect's doesn't (#1413).

**And from #926's saved form,** re-read from the guest for #1428: both combos whose Style was set to 2 and back to 0 in
the designer were saved with no Style line. So **VB6's designer never writes a Dropdown Combo's Style**, even after a
change and back.

### In HexIDE (#1428)

- **Style is a modelled property**, offered by the Properties window with VB6's three names. It is written as VB6's
  designer writes it: `1  'Simple Combo` and `2  'Dropdown List` between Left and TabIndex, and never 0. At run time it
  reads as an Integer, and assigning it is the compile error, whatever the value.
- **A Dropdown List's Text** reads as its selected item's text, or `""`. Written, it is made a String and selects the
  first item it matches without regard to case, raising Click every time. With no match, `""` and `Empty` included, it
  is 383 with VB6's own description, and the selection stays. `Null` is 13, as at the other two Styles.
- **The designer resets the Text to the control's name when Style becomes 2**, as one change with the Style, so Undo
  puts both back. The Properties window and `set_control_property` do it alike, and the tool's reply says so.
- **Not done:** a Simple Combo is still drawn as a Dropdown Combo, its list not always shown
  ([#1603](https://github.com/hexide-io/HexIDE/issues/1603)). The designer draws a Dropdown List's box empty, where VB6's
  shows the name ([#1604](https://github.com/hexide-io/HexIDE/issues/1604)).

