## The design-time Font dialog: its Script list, its sizes, and the Charset it writes (2026-11-21, #4338, design-time, IDE, SP6)

**The probe.** VB6.EXE 6.00.9782 (SP6) in a clone of the oracle VM, on its console at 96 dpi.
- **The dialog:** a form's Font row in the Properties window, its `...` button. That opens a font dialog with Font,
  Font style and Size lists, Effects, a Sample and a Script box.
- **The fonts:** each font name was typed into the Font box, then Tab.
- **The Script list:** stepped through with Down.
- **What it writes:** OK, then Ctrl+S, then the `.frm`'s `BeginProperty Font` block was read.

**The Script list for each font:**

| Font | Script entries, in order |
|---|---|
| MS Sans Serif | Western |
| Wingdings | Symbol |
| Arial | Western, Hebrew, Arabic, Greek, Turkish, Baltic, Central European, Cyrillic, Vietnamese |

**The Charset each entry writes:**

| Script | Charset |
|---|---|
| Western | 0 |
| Hebrew | 177 |
| Arabic | 178 |
| Greek | 161 |
| Turkish | 162 |
| Baltic | 186 |
| Central European | 238 |
| Cyrillic | 204 |
| Vietnamese | 163 |
| Symbol (Wingdings) | 2 |

All the rows were written with Arial except the last.

**The size lists:**
- **MS Sans Serif:** 8, 10, 12, 14, 15, 17, 18, 23, 24, 30.
- **Arial and Wingdings:** 8, 9, 10, 11, 12, 14, 16, 18, 20, 22, 24, 26, 28, 36, 48, 72.

So the bitmap font offers its own sizes.

**Opening on a Charset, and changing the font:**

| Before | Done in the dialog | Script shown, then written |
|---|---|---|
| Arial, Charset 161 | opened it | Greek |
| Arial, Greek | typed Times New Roman | Greek; Charset 161 |
| Times New Roman, Greek | typed MS Sans Serif | Western; Charset 0 |
| Arial, Charset 204 | opened it | Cyrillic |

- **The dialog opens on the font's saved script.**
- **Changing the font keeps the script where the new font has it,** and falls back to the new font's first script
  where it doesn't.

**A file whose Charset the font lacks.** The form's block was edited to `Name = "MS Sans Serif"`, `Charset = 161`, and
the project reopened.
- **The dialog opened on Arial, Greek.**
- **After Cancel, the Properties window's Font row read `Arial`.**
- **Ctrl+S wrote `Name = "Arial"`,** with Size 8.25 and Charset 161.

So the font VB6 loaded wasn't the one the file named. The name in the file changed on the next save, with no edit to
the font. [A control's Font object](a-controls-font-object.md) records the same substitution at run time, where MS Sans
Serif given a size it lacks becomes Arial.

### Not measured

- **The rule at 120 or 144 dpi.**
- **Every font on the guest.** Only the four above were read.
- **The Font style list,** and Strikeout and Underline.
