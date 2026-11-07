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

### Keyboard selection: where the endpoints land (2026-10-27)

**The probe:**
- **The form:** RichTextBox1, a hand-written minimal block 8700 twips wide so that no line wraps, and Text1, a
  MultiLine TextBox the same size, for comparison.
- **The text,** with SelStart's positions:

  | Positions | Text |
  |---|---|
  | 0 to 39 | `Hello, world. Foo-bar baz! it's 3.14 (x)` |
  | 40, 41 | a line break, vbCrLf |
  | 42, 43 | an empty line's break |
  | 44 to 61 | `Second line, here.` |
  | 62, 63 | a break |
  | 64 to 66 | `End` |

  Both controls read Len(Text) 67, with CR and LF at 40 to 43 and 62 to 63.
- **The input.** Each case placed the caret, or a selection, from code, then pressed real keys with keybd_event. SelStart
  and SelLength were read after each key.
- **The builds.** Native and p-code logged the same lines.

**A paragraph break is one step, two positions.** Shift+Right from 40 gave 40+2, 40+4, then 40+5, in both controls.
Shift+Left from 44 came back the same way.

| From | Keys | RichTextBox | TextBox |
|---|---|---|---|
| 0 | Shift+Right; Shift+Left | 0+1; 0+0 | the same |
| 5 | Shift+Left twice | 4+1, 3+2 | the same |
| 9 | Shift+Home | 0+9 | the same |
| 9 | **Shift+End**, then again | **9+33, to 42: past the line's break** | **9+31, to 40: before it** |
| 9 | Shift+Home then Shift+End | 0+9, then 9+33 | 0+9, then 9+31 |
| 3 | **Shift+Down** three times | **3+41, 3+61, 3+66** | **3+39, 3+41, 3+61** |
| 3 | Shift+Down then Shift+Up | 3+41, then 0+3 | 3+39, then 0+3 |
| 3 | **Shift+Up, on the first line** | **0+3, to the start** | **3+0, no change** |
| 66 | **Shift+Down, on the last line** | **66+3, to 69** | **66+0, no change** |
| 67, the end | **Shift+Right** | **67+2, to 69** | **67+0** |
| 67 | Shift+Home | 64+3 | the same |

- **A RichTextBox extending to the end of a line takes in the line's break.**
  - Shift+End stopped at 42, not 40.
  - Shift+Down onto the empty line went to 44.
  - At the end of the text, a selection went two positions past Len(Text), to 69. **SelStart + SelLength can exceed
    Len(Text) by 2.** That is the hidden break after the last line.
- **Up and Down, after an empty line:** in both controls, the next Down went to the start of the next line, not back to
  the column the caret started in.
- **Shift+Up on the first line, and Shift+Down on the last,** extend to the start and to the end in the RichTextBox. The
  TextBox does nothing.

**Which end moves:**

| Selection | Keys | RichTextBox | TextBox |
|---|---|---|---|
| **7+5 set by code** | Shift+Left; Shift+Right | 7+4; 7+6: **the end at 12 moves** | the same |
| 7+5 made by Shift+Left from 12 | Shift+Right; Shift+Left | 8+4; 6+6: the end at 7 moves | the same |
| 7+5 by code | **Left** | **7+0: collapses to its start** | **11+0: one left of the end at 12** |
| 7+5 by code | **Right** | **12+0: collapses to its end** | **13+0: one right of 12** |
| 7+5 made backwards | **Left** | **7+0** | **6+0: one left of the end at 7** |
| 7+5 by code | **Ctrl+Right** | **12+0: collapses, and goes no further** | **14+0: a word on from 12** |
| 7+5 by code | Ctrl+Shift+Right | 7+7 | the same |

**So a selection set by code has its moving end at SelStart + SelLength.** Plain Left and Right collapse a
RichTextBox's selection to the side pressed. A TextBox moves its caret one position from the moving end.

**Ctrl+Right and Ctrl+Left: where the caret stops,** walked from each end until it stopped moving:

| Walk | RichTextBox | TextBox |
|---|---|---|
| Ctrl+Right from 0 | 5, 7, 12, 14, 17, 18, 22, 25, 27, 32, 33, 34, 37, 38, 39, 40, 42, 44, 51, 55, 57, 61, 62, 64, 67 | 7, 14, 18, 22, 27, 32, 37, 40, 42, 44, 51, 57, 62, 64, 67 |
| Ctrl+Left from 67 | 64, 62, 61, 57, 55, 51, 44, 42, 40, 39, 38, 37, 34, 33, 32, 27, 25, 22, 18, 17, 14, 12, 7, 5, 0 | 64, 62, 57, 51, 44, 42, 40, 37, 32, 27, 22, 18, 14, 7, 0 |
| Ctrl+Right from 16, inside `Foo` | 17, then as above | 18, then as above |

- **The RichTextBox stops at each run of punctuation as a word of its own:**
  - `Hello` stops at 5, before the comma, then at 7;
  - `world.` at 12, before the full stop;
  - `Foo-bar` at 17, before the hyphen, then at 18;
  - `baz!` at 25;
  - `3.14` at 32, 33 and 34;
  - `(x)` at 37, 38 and 39.

  **The apostrophe in `it's` is not a break.**
- **The TextBox stops only at the start of each word.** A comma, full stop or bracket goes with the word before it, and
  so does a hyphen: `Foo-` then `bar`.
- **In both, a word takes the spaces after it.** The stop is at the next word's first character.
- **In both, the end of a line is a stop:** 40, then the empty line at 42, then 44. Ctrl+Right from 40 doesn't go
  straight to 44.
- **Ctrl+Shift with the arrows stops at the same places,** extending from the anchor. From 0: 0+5, 0+7, 0+12. From 40
  leftwards: 39+1, 38+2, 37+3.

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
  `corpus/inbox-controls/saved/richtextbox/`.
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

HexIDE runs the code-driven text, selection and character-formatting surface, plus paragraph alignment.
The measured saved `.frm` and `.frx` now round-trip byte for byte. Saved TextRTF loads through the model;
MultiLine False keeps its first paragraph, including on subsequent Text assignments. Other saved own
properties are carried without designer editors; runtime access still refuses except Enabled, Appearance, BorderStyle, Locked, MaxLength and read-only MultiLine/ScrollBars. RichTextLib is
available in Components/the toolbox, and placement seeds the name as RTF. A first input/drawing slice now
accepts typing, Enter and Backspace with the recorded event order, respects saved ReadOnly/MaxLength and
protected text, and draws character formatting and inverse selection. Click/double-click selection, the recorded shortcuts, plain-text clipboard/Undo and caret scrolling now work.
Undo retains the RTF model, including paragraph formatting. Shift-arrow, Shift+Home/End and command-arrow selection implement the endpoints above, including the hidden final break and punctuation stops. Modified clicks, drag selection and word
selection outside the recorded ASCII word with its trailing space explicitly refuse. Formatted clipboard
transfer also refuses. Saved BackColor, disabled text/background and flat/inset borders now draw. Enabled supports Boolean runtime access; Appearance and BorderStyle read saved/default values and refuse assignments with 383. Runtime BackColor access, paragraph drawing and character offsets remain deferred. Undo after a code edit explicitly refuses: that interaction is unmeasured. Non-ASCII KeyPress conversion is unmeasured
and refuses before editing; code-driven ANSI text still works.

Locked and SelProtected now expose Boolean runtime access through the existing input guards; code can
still replace protected text. MultiLine/ScrollBars read saved/default values and reject writes with 383.
MaxLength accepts the recorded 0, 5 and 20 values, truncates Text and drops SelText appended at the full
limit. Other limit assignments, reducing below the existing length, other code selection replacements
under a limit and RTF assignments under a limit refuse. No new conversion or RTF truncation rule is inferred.

The checked-in save's first `.frx` record (the toolbox-created RichTextBox3) has the `\ansi\deff0`
header, without `\ansicpg1252`, as the plain Text shape above does. That artifact is what the persistence
and toolbox tests pin; the earlier third-shape description above is not used to change its bytes.
Provenance: group 3's VB6-saved `Form1.frx` at offset 0, issuecomment-6433151637 (oracle PR #2250).
No new VB6 measurements were made in this implementation run.

The A0 foundation supplies the internal RTF reader/writer connected to the control. It reproduces the empty,
plain-text and formatted Arial/Courier New documents above, including character runs and font/colour tables.
It also reads the measured Arial input with a paragraph break and writes CR LF in plain Text as paragraph
breaks. Paragraph runs retain alignment, left/right/first-line indents, tab stops and the recorded Symbol
bullet groups. Accepted paragraph words retain their order; no additional rich-edit normalisation rule is
claimed. The recorded empty SelRTF and the `slow` selection above are emitted byte for byte, with the latter
carrying only its used font and colour. Mixed-format or paragraph selection output and selection font
charset normalisation remain refused.
Strikethrough, protection, paragraph resets, tab stops, group restoration and combined paragraph fragments
have parser-invariant tests; these are not new VB6 measurements. The composed bullet test pins the recorded
groups and Symbol table entry, not a newly measured whole document. Lone CR/LF output from plain Text, multiple bullet paragraphs, other
bullet/table syntax and unknown words remain refused. The remaining control members and the rest of input/drawing remain separate parts. No additional VB6 measurements were made for this slice.

Keyboard selection follows "Keyboard selection: where the endpoints land" above. Wrapped-line vertical endpoints, lone-break navigation and non-ASCII word endpoints remain refused. Collapsing a hidden-break selection and command-left collapse also remain refused as unmeasured.

Plain Left/Right/Home/End/Up/Down without a selection now use the corresponding recorded Shift
endpoints, as directed by the supervisor's 2026-10-28 comment on #1100; this is an implementation
mapping, not a new VB6 measurement. Lone-break navigation and wrapped Home/End/Up/Down refuse,
as do plain Home/End/Up/Down with a selection (their collapse endpoints are unmeasured).
The designer uses the existing labelled, view-only placeholder even for unsupported RTF.
Saved BackColor accepts hexadecimal and decimal OLE_COLOR spellings; the checked-in group-3
save's RichTextBox1 uses decimal 12648447 (RGB 255, 255, 192). The runtime regression loads
that saved form and its companion rather than substituting a handwritten hexadecimal colour.
