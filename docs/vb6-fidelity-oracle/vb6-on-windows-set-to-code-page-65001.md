## VB6 on Windows set to code page 65001, UTF-8 (2026-11-21, #845, SP6)

**The question.** Windows can make UTF-8 the ANSI code page: Region settings, "Beta: Use Unicode UTF-8 for worldwide language support". #845 asks what VB6 does on such a machine, before HexIDE settles what "the host's ANSI code page" means there:
- whether `/make` and the IDE work at all;
- what a program sees for a source holding Windows-1252 bytes (`E9` for é) and UTF-8 bytes (`C3 A9`);
- which bytes the IDE saves for a literal typed with é, and with a CJK character;
- what `MsgBox` shows.

**The probe:**
- **The machine:** a clone of the oracle VM, Windows 10 21H2, with the en-US system locale and the en-GB user locale. The registry's `ACP`, `OEMCP` and `MACCP` code pages were set to 65001 and the guest restarted. Those are the values that option sets; its checkbox itself wasn't used. `GetACP` then returned 65001. Afterwards the clone was restored to its checkpoint, at 1252.
- **The project:** written byte for byte by a script.
  - A `.bas` held string literals and a comment with the bytes `E9`, `C3 A9` and `E4 B8 AD`, which is 中 in UTF-8.
  - A form held:
    - a Caption of `F` and `E9`;
    - a Label's Caption of `L` and `C3 A9`;
    - a TextBox's Text of `t` and `C3 A9`;
    - a ListBox whose List record in the `.frx` held `a` and `E9`, `b` and `C3 A9`, and `E4 B8 AD`.
  - A second form held a Label's Caption and a multi-line TextBox's Text as `$"Form1.frx"` string records: `m E9 n C3 A9`, and `x E9 CR LF y C3 A9`.
- **The runs,** each at 1252 first and then at 65001:
  - **`/make`,** as native code and as p-code, run in session 0. The program logged each string's characters as code points. It read `MsgBox`'s text back from the box's own window with `GetWindowTextW`.
  - **The IDE, on the guest's console, driven by keys.**
    1. Open the project.
    2. Type `Private Const T1 = "é"` and `Private Const T2 = "中"` at the end of Module1, as Unicode keystrokes through `SendInput`, and press Ctrl+S.
    3. Do the same with é in each form's code.
    4. Compare the saved files byte for byte with the originals.
- **Native code and p-code agreed on every row except one,** which is marked.

### What the program sees

**`/make` and the programs worked at 65001.** The build logged no message, the programs ran, and the IDE opened, edited and saved without a dialog.

Characters are code points in hex:

| Source bytes | At 1252 | At 65001 |
|---|---|---|
| a `.bas` literal, `caf` and `E9` | `caf`, E9 | `caf`, **FFFD** |
| `caf` and `C3 A9` | `caf`, C3, A9: five characters | `caf`, **E9**: four |
| `E4 B8 AD` | E4, B8, AD | **4E2D**, one character |
| `x E9 y` | x, E9, y | x, FFFD, y |
| `E9 A B` | E9, A, B | FFFD, A, B |
| the form's Caption, `F E9` | F, E9 | F, FFFD |
| the Label's Caption and the TextBox's Text, ending `C3 A9` | C3, A9 | E9 |
| the `.frx` List items | a E9; b C3 A9; E4 B8 AD | a FFFD; b E9; 4E2D |
| the `$` record Caption, `m E9 n C3 A9` | m, E9, n, C3, A9 | m, FFFD, n, E9 |
| the `$` record multi-line Text, `x E9 CR LF y C3 A9` | all seven | **x, FFFD, CR, LF**: the `y` and é are lost |

**At 65001, every string in the source is decoded as UTF-8.** A valid sequence becomes its character, and each byte that isn't part of one becomes U+FFFD, with the ASCII after it kept. The multi-line TextBox is the exception: it lost what followed its line break, and no rule here explains why.

| Expression | At 1252 | At 65001 |
|---|---|---|
| `Chr$(233)`, `Chr$(195)` | E9, C3 | **FFFD**, FFFD, no error |
| `Asc(ChrW$(233))` | 233 | **-15447**: &HC3A9, both bytes of é in UTF-8 as one Integer |
| `Asc(ChrW$(&H4E2D))` | 63, a `?` | **91 native, 74 p-code** |
| `StrConv(ChrW$(233), vbFromUnicode)` | `E9` | **`E9`**, not `C3 A9` |
| `StrConv(ChrW$(&H4E2D), vbFromUnicode)` | `3F` | `3F` |
| `StrConv` of the bytes `C3 A9`, `vbUnicode` | C3, A9 | C3, A9 |
| `Print #` of é and 中 | `E9 3F` | **`C3 A9 E4 B8 AD`** |
| `Line Input #` of the bytes `C3 A9 E9` | C3, A9, E9 | **E9, FFFD** |
| `MsgBox` of the literal `m E9`, then é and 中: the box's text | m, E9, E9, 3F | m, FFFD, E9: **中 is dropped** |

**What it shows:**
- **The compiler, form loading and file I/O use the ANSI code page,** so at 65001 they read and write UTF-8.
- **`StrConv` doesn't.** It converted with Windows-1252, the user locale's code page, at 65001 as at 1252. That's the rule #1509's measurement found under code page 932.
- **`Chr`, `Asc` and `MsgBox` treat a character as at most two bytes:**
  - `Chr` of a number above 127 gives U+FFFD, since no single byte above 127 is a UTF-8 character;
  - `Asc` of é gives its two UTF-8 bytes as one Integer, as `Asc` gives a double-byte character's two bytes under code page 932 (#1509);
  - `Asc` of 中, three bytes in UTF-8, gave 91 as native code and 74 as p-code. This is the one row where they differ, and no rule here explains either value;
  - `MsgBox` lost the 中 at the end of its text. Its four characters take 9 bytes in UTF-8, one more than two bytes a character allows. A conversion buffer of two bytes a character, cut short, fits that. That's reasoned, not checked.

### What the IDE saves

Each file saved by Ctrl+S, against the original:

| | At 1252 | At 65001 |
|---|---|---|
| Module1's bytes, `E9`, `C3 A9` and `E4 B8 AD` in literals and a comment | kept | kept |
| `Private Const T1 = "é"`, typed | `E9` | **`E9`**, which `/make` then reads as U+FFFD |
| `Private Const T2 = "中"`, typed | `?` | `?` |
| é typed into each form's code | `E9` | `E9` |
| the form's Caption, `F E9` | kept | **`F EF BF BD`**: U+FFFD in UTF-8 |
| the Label's Caption and the TextBox's Text, ending `C3 A9` | kept | kept |
| the `.frx` List items `a E9`, `b C3 A9`, `E4 B8 AD` | kept | **`a E9` becomes `a EF BF BD`.** The others are kept |
| the List record's second field, written as 9 | rewritten as **3** | rewritten as **4** |
| the `$` record Caption, `m E9 n C3 A9` | moved inline, bytes kept | moved inline, **bytes kept** |
| the `$` record multi-line Text | rewritten as a `"Form1.frx"` record without the `$`: a 1-byte length, 07, and the seven bytes | rewritten the same way, as 07 and **`x EF BF BD CR LF y`**: the é is lost |

**What it shows:**
- **The code window holds bytes, not text.** It kept the file's bytes, and at 65001 it still drew each as a Windows-1252 character: the comment's `E9` showed as é and its `C3 A9` as `Ã©`. That's the reverse of what the compiled program sees.
- **Typing goes through 1252, not UTF-8.** A typed é is saved as `E9`, which `/make` reads back as U+FFFD at 65001. A CJK character becomes `?` at either code page.
- **A property held by a Windows control goes through UTF-8 and comes back re-encoded.** That covers the form's caption, the ListBox's items and the multi-line TextBox's text:
  - a byte that isn't UTF-8 comes back as `EF BF BD`;
  - the TextBox's record kept its old length, 7 bytes, so its text was cut short.
- **A property VB6 keeps itself keeps its bytes:** the windowless Label's Caption, moved inline, and the code.
- **The List record's second field is the longest item's length in bytes:** 3 for items of 2, 3 and 3 bytes, and 4 for 4, 3 and 3. VB98's `group.frx` has 9 for its one 9-byte item. [A list's saved records](a-lists-newindex-itemdata-and-saved-records.md) left this open.

**How the IDE draws these at 65001,** from the console screenshots:
- the form designer's title bar shows `F�`;
- the Properties window's Caption shows `Fï¿½`;
- the Label and the TextBox show `LÃ©` and `tÃ©`;
- the ListBox shows `a�`, `bé` and `中`.

**So VB6 at 65001 doesn't agree with itself.** The compiler and the running program read UTF-8. The code window shows and writes Windows-1252. The designer re-encodes what its controls hold, and loses bytes doing it.

**Not evidence here:**
- **The UTF-8 option's checkbox wasn't used;** the registry values it sets were.
- **Typing was Unicode keystrokes through `SendInput`.** A keyboard layout or an IME for a CJK language wasn't tried.
- **`MsgBox`'s text was read back in session 0,** not seen on the screen.
