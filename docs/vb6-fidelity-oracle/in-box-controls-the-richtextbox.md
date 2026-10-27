## In-box controls: the RichTextBox (2026-10-27, #1100)

**The probes.**
- **The software:** VB6 SP6 (6.00.9782), with RICHTX32.OCX 6.01.9782, on Windows 10.
  - The form line is `Object = "{3B7C8863-D78F-101B-B9B5-04021C009402}#1.2#0"; "RICHTX32.OCX"`.
  - The block is `Begin RichTextLib.RichTextBox`, `_Version` 393217.
- **The form:** written by hand, holding RichTextBox1 5000 × 3000 with only its position, extents and `_Version`.
- **The programs:** each ran in a Sub Main and logged each value with its `TypeName`, control characters shown as
  `<CR>`, `<LF>`, `<TAB>` and `<n>`.
- **The builds:** `/make` as native code and as p-code, both run on the guest's console. They logged the same lines,
  the EXE's folder apart.
- **The RTF is Windows 10's rich edit control's.** The control hands the text to it, and every RTF string here is what
  that control wrote. An older Windows may write it differently.

### The object model

- **Its members,** from the type library:
  - **Text:** Text, TextRTF, SelStart, SelLength, SelText, SelRTF.
  - **Character formatting:** SelBold, SelItalic, SelUnderline, SelStrikeThru, SelColor, SelFontName, SelFontSize,
    SelCharOffset, SelProtected.
  - **Paragraph formatting:** SelAlignment, SelIndent, SelHangingIndent, SelRightIndent, SelBullet, BulletIndent,
    SelTabCount, SelTabs(n).
  - **The box:** MultiLine, ScrollBars, Locked, MaxLength, HideSelection, DisableNoScroll, RightMargin, FileName,
    BackColor, Appearance, BorderStyle, Font, Enabled, Hwnd, MousePointer, MouseIcon, AutoVerbMenu, OLEObjects,
    OLEDragMode, OLEDropMode.
  - **Methods:** Find, Span, UpTo, GetLineFromChar, LoadFile, SaveFile, SelPrint, Refresh, OLEDrag.
- **The Sel properties are Variants**, so a selection whose characters differ can answer Null.
- **Its events:** Change, SelChange, Click, DblClick, KeyDown, KeyPress, KeyUp, MouseDown, MouseMove, MouseUp, and the
  OLE drag events.
- **The constants:**
  - rtfWholeWord 2, rtfMatchCase 4, rtfNoHighlight 8;
  - rtfRTF 0, rtfText 1;
  - rtfLeft 0, rtfRight 1, rtfCenter 2;
  - rtfInvalidCharPosition 32000, rtfCannotLoadFile 32002, rtfProtected 32011.
- **`TypeName`** is `RichTextBox`.
- **The default member is TextRTF, not Text.** `v = RichTextBox1` read the whole RTF. `RichTextBox1 = "via default"`
  assigned TextRTF, and that string, not being RTF, became the text.

### Defaults

| Read | Logged |
|---|---|
| Text, SelText, FileName | empty Strings |
| TextRTF | `{\rtf1\ansi\ansicpg1252\deff0\deflang1033{\fonttbl{\f0\fnil\fcharset0 MS Sans Serif;}}<CR><LF>\viewkind4\uc1\pard\f0\fs17 <CR><LF>\par }<CR><LF>` |
| SelRTF, nothing selected | `{\rtf1\ansi\ansicpg1252\deff0\deflang1033\uc1 }<CR><LF>` |
| SelStart, SelLength | 0, Longs |
| SelBold, SelItalic, SelUnderline, SelStrikeThru, SelBullet, SelProtected | False |
| SelColor | 0, a Long |
| SelFontName, SelFontSize | MS Sans Serif, 8.25 (a Single) |
| SelAlignment, SelTabCount | 0, Integers |
| SelIndent, SelHangingIndent, SelRightIndent, BulletIndent, RightMargin | 0, Singles |
| SelCharOffset | 0, a Long |
| MultiLine, HideSelection | **True**, True |
| ScrollBars, MaxLength | 0, 0 |
| Locked, DisableNoScroll, AutoVerbMenu | False |
| BackColor | `&H80000005` |
| Appearance, BorderStyle | 1, 1 |
| OLEDragMode, OLEDropMode | 1, **2** |
| Font | MS Sans Serif 8.25 |

**Colours read late-bound come back as unsigned Longs,** VarType 19.

### Text and TextRTF

- **`Text = "Hello"` gives this TextRTF:**

  ```
  {\rtf1\ansi\deff0{\fonttbl{\f0\fnil\fcharset0 MS Sans Serif;}}
  \viewkind4\uc1\pard\lang1033\f0\fs17 Hello
  \par }
  ```

  The lines end CR LF. It is not the default's shape: `\ansicpg1252` and `\deflang1033` are gone from the header, and
  `\lang1033` comes after `\pard`.
- **Text keeps what it was given.** `a<CR><LF>b` read back as `a<CR><LF>b`, Len 4. `a<LF>b` and `a<CR>b` read back
  unchanged too.
- **The control is ANSI.**
  - A character outside the code page is lost: alef (U+05D0) read back as `?`, in Text and in TextRTF.
  - `é` and `€` are written as `\'e9` and `\'80`, code page 1252's bytes.
  - A tab is written as `\tab`.
- **TextRTF takes RTF and writes it back normalised.**
  - `{\rtf1\ansi{\fonttbl{\f0 Arial;}}\f0\fs20 plain \b bold\b0  \i ital\i0 \par second}` read back as Text
    `plain bold ital<CR><LF>second`.
  - Its TextRTF came back with `\viewkind4\uc1\pard\lang1033`, `\fnil` in the font table, and a `\par` added at the end.
- **A TextRTF that isn't RTF becomes the text, raising nothing.** `just text` and `{\rtf1 unclosed` each read back as
  Text, braces included.
- **Setting Text keeps the formatting at the insertion point.** After the Arial 10 RTF above, `Text = "The quick brown
  fox"` was Arial 10.

**With "slow" made bold, italic, underlined, red, Courier New and 14 point inside `The slow brown fox`:**

```
{\rtf1\ansi\deff0{\fonttbl{\f0\fnil Arial;}{\f1\fnil\fcharset0 Courier New;}}
{\colortbl ;\red255\green0\blue0;}
\viewkind4\uc1\pard\lang1033\f0\fs20 The \cf1\ul\b\i\f1\fs28 slow\cf0\ulnone\b0\i0\f0\fs20  brown fox
\par }
```

**SelRTF of the selection is a whole document.** It carries only the fonts and colours it uses:

```
{\rtf1\ansi\deff0{\fonttbl{\f0\fnil\fcharset0 Courier New;}}
{\colortbl ;\red255\green0\blue0;}
\uc1\pard\cf1\lang1033\ul\b\i\f0\fs28 slow}
```

**Setting SelRTF replaces the selection** with the RTF's text and formatting. `{\rtf1{\colortbl;\red0\green0\blue255;}\cf1 BLUE}`
over `The` gave a blue `BLUE`: SelColor 16711680.

### The selection

| Assignment, on `The quick brown fox` | Logged |
|---|---|
| `SelStart = 100` | **19**, the end. No error |
| `SelStart = -1` | **380** |
| `SelLength = 100`, from 4 | **17**, though 15 characters are left. The two more are presumably the closing paragraph mark, counted as CR LF, but SelStart = 100 stopped at 19 |
| `SelLength = -1` | **380** |
| `SelText = "slow"` over `quick` | replaced, and the caret after it: SelStart 8, SelLength 0 |

- **Over a selection whose characters differ, the Sel character properties read Null.** Over `The slow b`, SelBold,
  SelItalic, SelUnderline, SelColor, SelFontName and SelFontSize read Null. SelStrikeThru, the same throughout, read
  False.
- **With nothing selected, the Sel properties read the formatting at the caret:** inside `slow`, bold, 14, Courier New.
- **Over several paragraphs whose formatting differs,** SelAlignment, SelIndent and SelBullet read Null.

Each assignment below was logged with Err read straight after it. A first pass read the value back through a helper
holding its own `On Error`, which clears Err, and logged SelAlignment 3 and -1 as ignored without an error. They are
380.

| Assignment | Logged |
|---|---|
| SelAlignment 0, 1, 2 | taken |
| SelAlignment 3, 4, 100, -1, Null | **380**, the alignment kept |
| `SelIndent = 500` | **494.9292**: rounded to whole pixels, read back through HIMETRIC |
| `SelHangingIndent = 200` | 195.0236 |
| `SelRightIndent = 300` | 299.9055 |
| `SelIndent = -100`, `SelHangingIndent = -100` | 0, no error |
| `SelRightIndent = -100` | **-74.26772**: a negative right indent is kept |
| `SelFontSize = 0`, `SelFontSize = -5` | **ignored**, no error |
| `SelFontSize = 2000` | **1638.35**, no error |
| `SelColor = -1`, `SelColor = &H80000008` | **0**, black, no error. A system colour isn't kept |
| `SelColor = &H1000000` | 16777216, kept, no error |
| `SelFontName = "No Such Font"` | **kept** as given, no error |
| `SelFontName = ""` | no error, and read back **Arial**, the face the text had before "No Such Font". No rule offered |
| `SelBold = Null`, `SelBold = 5` | no error, and SelBold read **True** after each, though the text had not been bold |
| `SelCharOffset = 100` | 100 |
| `SelTabCount = 2`, `SelTabs(0) = 720`, `SelTabs(1) = 1440` | taken. SelTabs(0) read back 720.0001. Reading or writing SelTabs(5) is **381** |
| `SelTabCount = -1`, `SelTabCount = 100` | **380** |
| `SelProtected = True` | taken, but `SelText` from code still replaced the protected text |
| MultiLine, ScrollBars at run time | **383**, read-only |

- **The indents in the RTF:** SelIndent 495 with SelHangingIndent 195 is written `\fi-195\li690`. `\li` is where the
  later lines start, SelIndent plus the hanging indent, and `\fi` takes the first line back.
- **SelBullet True sets SelHangingIndent to BulletIndent.** At BulletIndent 0 it cleared a hanging indent of 195; at 300
  it set it to 299.9055.
- **A bullet is written** `{\pntext\f1\'B7\tab}{\*\pn\pnlvlblt\pnf1\pnindent0{\pntxtb\'B7}}`, with Symbol added to the
  font table as `{\f1\fnil\fcharset2 Symbol;}`.
- **Centred is `\qc`,** and SelRightIndent 300 is `\ri300`.

### Find, Span, UpTo and GetLineFromChar

On `Cat cat concat CAT`, CR LF, `second cat line`:

| Call | Returned |
|---|---|
| `Find("cat")` | 0, and the selection 0,3: **case is ignored unless asked** |
| `Find("cat", 1)` | 4 |
| `Find("cat", 1, , rtfMatchCase)` | 4 |
| `Find("cat", 5, , rtfWholeWord)` | 15, `CAT`, past `concat` |
| `Find("cat", 5, , rtfWholeWord + rtfMatchCase)` | 27 |
| `Find("dog")` | **-1**, the selection unchanged |
| `Find("cat", 0, , rtfNoHighlight)` | 0, the selection unchanged |
| `Find("cat", 0, 2)` | -1: the match must end by the end |
| `Find("line", 25, 10)` | -1, with no error |
| `Find("")`, `Find("cat", 999)` | -1 |

- **Span selects forward from the caret over the characters in its set, taken literally.** `"a-z"` is the three
  characters `a`, `-` and `z`, not a range: at `cat`, it selected nothing, and `"abct"` selected `cat`.
- **`Span set, True, True` selects up to the first character in the set:** `" "` with Negate selected `cat`.
- **UpTo moves the caret, selecting nothing.** `UpTo " "` from 4 left SelStart 7, SelLength 0.
- **GetLineFromChar:**
  - 0 for characters 0 and 19 (the CR), and 1 for character 20;
  - 999 gave the last line, 1;
  - **-1 is 32000** "Invalid character position".

### MaxLength, Locked and the files

- **MaxLength cuts what code assigns.** With MaxLength 5, `Text = "1234567890"` read `12345`. Then `SelText = "abc"` at
  the end was dropped, with no error.
- **Locked stops the user, not code.** `Text =` still set the text.
- **SaveFile path writes TextRTF, followed by a NUL byte.** `SaveFile path, rtfText` writes the text, CR LF kept, with
  no NUL. The file type 2 is 380.
- **SaveFile doesn't set FileName. LoadFile sets it to the full path. Setting FileName loads the file.**
- **LoadFile:**
  - a plain text file loaded as RTF becomes the text;
  - a broken RTF file becomes its own source text;
  - an ANSI text file loaded as rtfText keeps `é`;
  - a file that doesn't exist, or a SaveFile into a folder that doesn't exist, is **75**.

### Change and SelChange from code

| Statement | Raised |
|---|---|
| `Text = "abc"` | SelChange (0,0), then Change. **Assigning the same text again raised Change again** |
| `TextRTF =` | Change |
| `SelStart = 1` | SelChange. The same value again, nothing |
| `SelLength = 1` | SelChange |
| **`SelBold = True`** | **Change**: formatting raises Change |
| `SelText = "Q"`, `SelRTF =` | SelChange, then Change |
| `Find "R"` | SelChange |

### Events, with real input

**The probe:** `alpha beta gamma` in the box, run on the guest's console. A driver clicked and typed for real and marked
each action in the log, which recorded every event. Native and p-code logged the same lines, mouse coordinates apart.

| Action | Events, in order |
|---|---|
| Click in the text | MouseDown, MouseUp, Click, **then SelChange** |
| Type `x` | KeyDown, KeyPress, **SelChange, Change**, KeyUp |
| Enter | KeyDown, KeyPress 13, SelChange, Change, KeyUp. A CR LF is inserted, and the caret moves 2 |
| Backspace | KeyDown, SelChange, Change, **then KeyPress 8**, KeyUp. Backspace after a CR LF removes both |
| Tab | KeyDown 9, then the focus moves to the next control. **No tab is typed** |
| Double-click `beta` | MouseDown, MouseUp, Click, SelChange (9,0), DblClick, **SelChange (6,5)**: the word with its trailing space, then MouseUp |
| Right-click | MouseDown and MouseUp with Button 2, then **Click**. There is no menu, AutoVerbMenu being False |
| Ctrl+A | selects all, SelChange 0,16, and KeyPress 1 |
| Ctrl+B, Ctrl+U | **nothing** but KeyPress 2 and 21 |
| **Ctrl+I** | **types a tab** over the selection, KeyPress 9 |
| Ctrl+E, Ctrl+R, Ctrl+L | centre, right and left the paragraph, each raising Change |
| Ctrl+Z | undoes the last change: Ctrl+L's, putting `\qr` back. SelChange, Change |
| Ctrl+C, then Ctrl+V at the end | pasted `beta`. SelChange, Change |
| With Locked True: type, Backspace | KeyDown, KeyPress, KeyUp. Nothing changes, and no Change |
| With MaxLength 20, type 8 characters after 16 | the first 4 typed, the other 4 dropped. Each raised KeyDown, KeyPress and KeyUp, but only the first 4 raised Change |
| Type or Delete inside protected text | KeyDown, KeyPress, KeyUp. Nothing changes |

- **Assigning Text at load raised Change** before the form's code ran on.
- **Mouse X and Y are in twips.**

### Drawing

Screenshots on Windows 10 at 96 dpi, one fresh form per state, native code:
- **The selection is drawn in inverse video:** black, with white text. That is not the system highlight colour.
  **With HideSelection True and the focus elsewhere, no selection is drawn.**
- **Character formatting draws as set:** bold, italic, underline, strikethrough, colour, size and face.
  **SelCharOffset ±60 raises or lowers text** for a superscript or subscript.
- **Alignment:** left, centred and right paragraphs.
- **Bullets:** a `•` at the left with the text at the hanging indent.
- **MultiLine wraps at the box's edge. RightMargin 2000 wraps at 2000 twips**, however wide the box.
- **Tab stops:** SelTabs 1500 and 3000 set the first paragraph's stops. The next paragraph kept the default stops, each
  about half an inch.
- **Enabled False greys the text and the background,** which turns the button face colour.
- **BackColor** fills the box.
- **Appearance and BorderStyle are read-only at run time:** setting Appearance was 383.

### The saved files

**The programs:**
- **The save:** a form written by hand, opened in VB6's IDE, dirtied with a comment and saved. VB6's save is in
  `corpus/inbox-controls/pending/richtextbox/`.
  - RichTextBox1 had every property off its default, its lines in reverse order, and an inline
    `TextRTF = "{\rtf1\ansi{\fonttbl{\f0 Arial;}}\f0\fs20 Hello \b bold\b0\par second line}"`.
  - RichTextBox2 had only its position, extents and `_Version`.
  - A third box was double-clicked from the toolbox first.
  - A second pass also set RichTextBox1's Locked to True in the Properties window before saving.
- **The read:** the hand-written form and both saves were read back at run time, natively and as p-code. Each pair
  logged the same lines.

**Locked is saved as `ReadOnly`.** The `Locked = -1` written by hand was ignored: the Properties window showed False,
the save left it out, and the program read False. Set in the Properties window, it was saved as `ReadOnly = -1 'True`,
and the program read Locked True.

**The order VB6 writes the lines in,** whatever order it reads them in: the extender's, the extents and `_Version`,
then

```
BackColor, BorderStyle, Enabled, HideSelection, MultiLine, ReadOnly, ScrollBars, MousePointer,
DisableNoScroll, MaxLength, BulletIndent, Appearance, RightMargin, AutoVerbMenu, OLEDragMode,
OLEDropMode, TextRTF, the Font block
```

- **Enabled and TextRTF are always written,** Enabled even at its default, True. The rest are written only when off
  their defaults. There is no `Text` line.
- **TextRTF goes to the `.frx`,** as `TextRTF = $"Form1.frx":0101`. The `$` marks a string held there. Each entry is a
  4-byte little-endian length and then the RTF's bytes, with no terminator.
- **A TextRTF written inline,** as a quoted string, was read. VB6 moved it to the `.frx` when it saved.
- **The minimal block was given the empty control's RTF** in the `.frx`, `\ansicpg1252\deff0\deflang1033` shape.
- **A double-clicked box holds its own name as its text,** as a TextBox does:

  ```
     Begin RichTextLib.RichTextBox RichTextBox3 
        Height          =   750
        Left            =   4050
        TabIndex        =   2
        Top             =   3225
        Width           =   1500
        _ExtentX        =   2646
        _ExtentY        =   1323
        _Version        =   393217
        Enabled         =   -1  'True
        TextRTF         =   $"Form1.frx":0000
     End
  ```

  Its RTF is a third shape, `{\rtf1\ansi\ansicpg1252\deff0{\fonttbl...}` with `\lang1033` after `\pard`.
- **With MultiLine False only the first paragraph is kept.**
  - The `second line` of the RTF written by hand was lost at load, in the IDE and in the program, and VB6's `.frx`
    no longer has it.
  - At run time, `Text = "set" & vbCrLf & "by code"` read back `set`.
- **The extents are recomputed** from Width and Height: `_ExtentX` 7761, written by hand, was saved as 7752.

### In HexIDE

HexIDE doesn't model the RichTextBox (#1100). The designer draws it as an empty box (#157), and running a form that
holds one is refused, as for any control HexIDE can't create (#857).
