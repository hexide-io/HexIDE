## A property named twice, and a measurement VB6 can't set, in a .frm (2026-11-16, #4096, design-time, IDE, SP6)

**The question.** The `.frm` fuzzer (#4096) found forms HexIDE could load but not save stably: a property named twice in
one block, and a measurement too large to convert back. Which line VB6 takes, what it reports, and whether it will save
such a form, decide whether HexIDE may save it or must leave it read-only.

**Method:**
- **`/make`:** a form with a CommandButton, built headless with `/out`. Each form ran as its own project, and its `.log` was read afterwards. Where it built, the EXE read the values back.
- **The IDE:** three of the forms were opened in vb6.exe's IDE in the console session.
  1. Opening the designer (Shift+F7), as that's when the IDE reads the form's designer block.
  2. F5, with `Form_Load` writing the values to a file.
  3. Ctrl+S.

### Item 3: a property named twice in one block

| Form | `/make` | Values read back |
|---|---|---|
| `ClientHeight = 3000`, then `ClientHeight = 4000` | builds, no log | `ScaleHeight` 4005: **the last wins** |
| `ClientHeigHt = 3000`, then `ClientHeight = 4000` | builds, no log | 4005: the names match without regard to case |
| Command1 `Height = 375`, then `Height = 600` | builds, no log | 600 |
| Command1 `Caption = "First"`, then `Caption = "Second"` | builds, no log | `Second` |
| `caption = "First"`, then `Caption = "Second"` | builds, no log | `Second` |

**A duplicate on its own:**
- **Each line is applied in turn, so the last one wins.** Nothing warns.
- **In the IDE, Ctrl+S saves the file in place, with no dialog.** It writes one line, the value the control ends with: `ClientHeight = 4005`, snapped to whole pixels at 15 twips each.

**A duplicate whose value can't be read.** This is the fuzzer's shape.

| Form | `/make` | The IDE |
|---|---|---|
| `ClientHeight = 2000`, then `ClientHeight = abc` | **fails**: "Errors during load", "'Form1.frm' could not be loaded", build failed. The log reads `Line 5: Property ClientHeight in Form1 had an invalid value.` | The same log entry, and a message box, "Errors during load. Refer to '…\Bad2.log' for details". After OK the form opens, `ScaleHeight` is 1995, so **the readable line stands**, and the bad line is skipped. |
| `ClientHeigHt = 0`, then `ClientHeight = abc` | fails the same way, `Line 5` | not opened; going by the row above, it would keep 0 |
| `ClientHeight = abc`, then `ClientHeight = 2000` | fails, `Line 4` | not opened |

So a line VB6 can't read doesn't undo an earlier one. **It costs the whole form in a build, and only that one line in the IDE.**

### Item 5: a measurement too large

| Value | `/make` log |
|---|---|
| Command1 `Height = 40000` | builds; reads back 40000 |
| `Height = 1E+9` | builds; reads back 1E+09 |
| `Height = 2147483520`, the largest Single below 2^31 | builds; reads back 2.147484E+09 |
| `Height = 2147483584`, which is 2^31 as a Single | **fails**: `Line 11: Property Height in Command1 could not be set.` |
| `Height = 2147483647` | fails the same way: 2^31 as a Single |
| `Height = 3.7E+18`, and `3.755555555555555E+18` | fails, "could not be set" |
| `Height = 1E+30` | fails, "could not be set" |
| `Height = 1E+39`, beyond Single | fails, `Line 13: Property Height in Command1 had an invalid value.` |
| `Height = -1`, `-2147483648`, `-2147483649` | fails, "could not be set" |
| Command1 `Left = 3.7E+18`, `Left = 2147483648` | fails, "could not be set" |
| the form's `ClientHeight = 3.7E+18` | fails, `Line 2: Property ClientHeight in Form1 could not be set.` |

**The rules:**
- **The value is read as a Single.** A measurement is set when that Single is below 2^31. 2147483647 rounds up to 2^31 as a Single, so it fails, and 2147483520 is the largest that loads.
- **Height also refuses a negative.**
- **The two messages report different lines:**
  - "had an invalid value": the text isn't a number of the property's type. It names the **property's own line** (13).
  - "could not be set": it parses, but the object refuses it. It names the line of the **object's `Begin`** (11 for Command1, 2 for the form).
- **In the IDE, the refused property is left as if its line were absent.** Command1 with `Height = 3.7E+18` opened with `Height` 0 (F5 read 0), drawn as no button at all, and still in the Properties window's object list.

### What VB6 does when you save a form that loaded with errors

**Ctrl+S on such a form opens Save File As instead of saving.** It was measured on both damaged forms above.
- The dialog starts in the folder above the form's own, under the form's own name.
- The original `.frm` was left untouched.
- Saving to a new name wrote the values the form held: `Height = 0` for the refused button, and `ClientHeight = 1995` for the duplicate with the unreadable line.

The control is the duplicate-only form, which loaded clean: there, Ctrl+S saved in place. So **VB6 won't overwrite a form that loaded with errors unless you choose to.** It asks where to save, which keeps the damaged original unless you pick its name.

**What this overturned.** It was reasonable to guess VB6 refuses a duplicated property, or keeps the first. It takes
each line in turn, silently. And the refusal people remember, "Errors during load", turned out to cost the whole form
only in a `/make` build. The IDE opens the form, leaves the failed lines unset, and then declines to save it in place.

### Which values a .frm line can hold, by the property's type (2026-11-19, #4317, `/make`, SP6)

**The question.** "Had an invalid value" is the log entry for a line VB6 can't read, and the rows above measure one
case of it, `ClientHeight = abc`. Which other texts does VB6 refuse, and does it depend on the property's type?

**The probe.**
- **The software:** VB6 SP6 6.00.9782.
- **The form:** one form holding 28 CommandButtons. Each block names one property twice: a readable value, then the
  text under test on the next line.
- **The types:**
  - Height, a Single, with 15 texts;
  - TabIndex, an Integer, with 4;
  - BackColor, a colour, with 4;
  - Style, an enum, with 3;
  - Caption, a string, with 2.
- **The run:** one `/make` in session 0. It fails, because some lines are refused, and its log lists every refused line
  by number. A line with no log entry was accepted.

| Property | Refused: "had an invalid value" | Accepted: no log entry |
|---|---|---|
| Height (Single) | `abc`, `x`, `Fixed`, `5abc`, `1D2`, `1,5`, `"500"`, `&H10`, `&H10&`, `&O17`, `&H8000000F&` | `1e3`, `--5`, `1.5.2` |
| TabIndex (Integer) | `abc`, `1.5`, `"3"` | `&H3` |
| BackColor | `abc`, `"255"` | `255`, `&H02C0C0C0&` |
| Style (enum) | `abc`, `1.5`, `Graphical` | |
| Caption (string) | `abc` and `5`, logged as "must be a quoted string" | |

- **An empty value,** `Height =` with nothing after it, was logged as "could not be loaded", not "had an invalid value",
  and against the line after it (78 for a value on line 77).
- **What an accepted text sets isn't measured.** The build failed, so nothing read `--5` or `1.5.2` back.

**What it says.**
- **A word is refused for every type,** even the enum's own name: `Graphical` for Style.
- **A quoted value is refused on a property that isn't a string,** even a quoted number.
- **A number isn't always accepted.** An Integer or an enum refuses `1.5`. A Single refuses `1,5`, `1D2`, and `&H` and
  `&O` forms, where an Integer accepts `&H3`.
- **VB6 accepts some texts that don't look like numbers,** `--5` and `1.5.2` on a Single. I have no rule for which.

**Where HexIDE's reader and this disagree,** read from `FormDeserializer.cs` and `VbFrmFormatDeserializer.cs` on main
`d5c6dc78d`, not run:
- **HexIDE can't read these, though VB6 does:** `--5` and `1.5.2` on a Single, `&H3` on an Integer, and `255` on a
  colour.
- **HexIDE reads these, though VB6 refuses them:** `1,5` on a Single, read as a number with a thousands separator, and
  `1.5` on an Integer or an enum.

### What an accepted text reads as, and more texts by type (2026-11-20, #4988, `/make`, SP6)

**The question.** The section above found texts VB6 accepts that don't look like numbers, but couldn't read their
values, because the build failed. What does each accepted text set? And which other spellings that .NET reads as
numbers does VB6 refuse?

**The probe:**
- **The software:** VB6 SP6 6.00.9782, in the Win10 oracle guest.
- **The form:** 34 CommandButtons, each naming one text once, with no duplicates:
  - 20 on `Left`, a Single;
  - 8 on `MousePointer`, an Integer;
  - 6 on `BackColor`.

  `MousePointer` stands in for an Integer, because duplicate `TabIndex` values would renumber each other.
- **Phase 1:** one `/make` of every text. It fails, and the log lists the refused lines.
- **Phase 2:** a `/make` of only the accepted texts, built native and p-code. Each EXE wrote every control's value from
  `Form_Load`. The two builds wrote the same lines.

| Property | Text | VB6 |
|---|---|---|
| Left (Single) | `--5`, `---5`, `--5.5` | 0 |
| | `1.5.2`, `1.5.2.` | 2 |
| | `1.2.3.4` | 1 |
| | `1..5` | 1 |
| | `5-`, `+5`, `5e`, `5.` | 5 |
| | `1e` | 1 |
| | `.5` | 0 |
| | `1e3` | 1000 |
| | `-5` | −5 |
| | `(5)`, `NaN`, `Infinity` | refused: "had an invalid value" |
| | `- 5`, `5 5` | refused differently: `Syntax error: property 5 in C15 was missing an '='` |
| MousePointer (Integer) | `&H3`, `&H3&` | 3 |
| | `&O7` | 7 |
| | `3.`, `1.0`, `(3)`, `3-`, `--3` | refused: "had an invalid value" |
| BackColor | `255`, `&HFF`, `&HFF&` | 255 |
| | `16777215` | 16777215 (`&HFFFFFF`) |
| | `--255`, `255.0` | refused: "had an invalid value" |

**What it says:**
- **On a Single, a refusal depends on the characters, and the value on the text's start.**
  - Every accepted text uses only digits, `.`, `+`, `-` and `e`.
  - Every text refused as invalid holds some other character: a quote, `&`, `(`, a comma, or a letter other than `e`.
    That includes the rows above, such as `5abc`, `1D2`, `1,5`, `"500"` and `&H10`.
  - The value is the longest number at the start of the text, or 0 when there is none. That's how `--5` gives 0 and
    `1.5.2` gives 1.5.
  - No row contradicts this, but it's a rule fitted to these rows, not one I'd defend beyond them. Other characters,
    and an upper-case `E`, weren't tried.
- **A space ends the value.** `Left = - 5` makes VB6 read `5` as the next property's name, missing its `=`. The
  error is a syntax error, not an invalid value.
- **`Left` keeps whole twips.** `1.5.2` reads back 2 and `.5` reads back 0: 1.5 and 0.5 rounded half to even.
  - These rows don't show whether the rounding is the reader's or `Left`'s.
  - A font's `Size = 8.25` keeps its fraction, so not every Single is rounded.
- **An Integer is stricter than a Single.**
  - It refuses a decimal point, even in `1.0` and `3.`.
  - It refuses a trailing `-` and a doubled `-`, which a Single accepts.
  - Like a Single, it refuses parentheses.
  - It accepts `&H` and `&O`, which a Single refuses.
- **A colour takes a plain decimal and a short `&H`,** as well as the eight-digit `&H…&` VB6 writes. It refuses a
  decimal point.

**Where HexIDE's reader disagrees.** From main `1bec9d018`:
- *Run* in PowerShell: `ParseValue`'s parse of each text, `int.TryParse` and then `double.TryParse` with
  `NumberStyles.Any` less the thousands separator, invariant culture.
- *Read from the code, not run:* what `FormDeserializer` then does with it.

| Text | VB6 | HexIDE's parse | So HexIDE |
|---|---|---|---|
| Single `5-` | 5 | −5 | reads −5 |
| Single `(5)` | refused | −5 | reads −5 |
| Single `NaN`, `Infinity` | refused | NaN, ∞ | reads NaN, ∞ |
| Integer `3.`, `1.0` | refused | 3, 1 | reads 3, 1 |
| Integer `(3)`, `3-` | refused | −3 | reads −3 |
| Single `--5`, `1.5.2`, `5e`, `1e`, `1..5` | 0, 2, 5, 1, 1 | not a number | refuses |
| Integer `&H3`, `&H3&`, `&O7` | 3, 3, 7 | not a number | refuses |
| colour `255`, `16777215`, `&HFF`, `&HFF&` | 255, 16777215, 255, 255 | not a colour | refuses |

The first five rows are values VB6 never sets. The rest are refusals of values VB6 reads.

### A fractional position in twips, and in a container that isn't twips (2026-11-21, #4988, `/make`, SP6)

**The question.** The section above found `Left` keeping whole twips: `.5` read 0, and `1.5.2` read 2. Two things were
left open: whether the rounding is the reader's or the property's, and whether it's always half to even. It also left
open whether a position on a form that isn't in twips reads the same way.

**The obvious guess, and what overturned it.** #4988's slice 3 (#5090) left `Left = .5` open, saying that "HexIDE reads
0.5 twips". That was read from the code and not run, and it was repeated on #4988 as a wrong value on main. Run on main
`084ab0365`, a CommandButton with `Left = .5` reads 0, as VB6's does. Main's model keeps the 0.5 twips. Then reading
`Left` rounds the pixels, times 15, half to even (`ControlPositions.Read`), which I read from the code. The rows below
are where main and VB6 really differ.

**The probes:**
- **The software:** VB6 SP6 6.00.9782, in the Win10 oracle guest.
- **The builds:** each project built with `/make` native and p-code. Each EXE wrote every control's value from
  `Form_Load`, then assigned values to a few controls and wrote what each read back.
- **Native and p-code wrote the same lines in all four probes.**
- **Probe A, a form in twips:**
  - 43 CommandButtons, 7 Labels and 5 Shapes, each with one fractional `Left`, `Top`, `Width` or `Height`, and one
    more CommandButton and Label, with whole values, for the assignments;
  - a Line with fractional ends;
  - assignments of Doubles to a CommandButton's `Left` and `Width`, a Label's `Left` and the Line's `X1`.
- **Probes B and C, a form whose scale isn't twips:** each form ran twice, once as `ScaleMode = 3` (pixels) and once as
  `ScaleMode = 0`.
  - The user scale was `ScaleWidth` 100.5, `ScaleHeight` 50.25, `ScaleLeft` 0.5 and `ScaleTop` −0.25, over a
    `ClientWidth` of 9000.
  - Each value read back was converted to twips with the form's own `ScaleX` and `ScaleY`, after taking off `ScaleLeft`
    and `ScaleTop`.
  - Probe B's user-scale run printed `ScaleX(Left)` without taking off `ScaleLeft`. Its twips there were worked out
    from the printed `Left` in the same way.
- **Probe D, a PictureBox whose scale differs from its form's:** button X sits in the PictureBox, and button Y on the
  form.
  - A pixel form holding a twips PictureBox.
  - A twips form holding a pixel PictureBox.

**In a twips container, at load,** read back with every `TypeName` Single:

| Control and property | .frm text, then what VB6 reads |
|---|---|
| CommandButton `Left` | `.5` 0, `0.5` 0, `1.5` 2, `2.5` 2, `3.5` 4, `4.5` 4, `0.4` 0, `0.6` 1, `0.49` 0, `0.51` 1, `1.49` 1, `1.51` 2, `2.5001` 3, `0.25` 0, `0.75` 1, `100.5` 100, `101.5` 102 |
| | `-0.5` 0, `-1.5` −2, `-2.5` −2, `-0.4` 0, `-0.6` −1, `-0.51` −1 |
| | `3.4999999` 4, `0.50000001` 0, `1.25e1` 12, `1.5e0` 2 |
| CommandButton `Top` | `.5` 0, `1.5` 2, `2.5` 2, `-0.5` 0, `0.6` 1, `3.4999999` 4 |
| CommandButton `Width` | `600.5` 600, `601.5` 602, `600.4` 600, `600.6` 601, `0.5` 0, `1.5` 2 |
| CommandButton `Height` | `300.5` 300, `301.5` 302, `300.6` 301, `0.5` 0 |
| Label `Left` | `.5` 0, `1.5` 2, `2.5` 2, `-0.5` 0, `0.6` 1 |
| Label `Width`, `Height` | `600.5` 600, `300.5` 300 |
| Shape `Left` | `.5` 0, `1.5` 2, `2.5` 2, `-0.5` 0 |
| Shape `Width` | `600.5` 600 |
| Line `X1`, `X2`, `Y1`, `Y2` | `100.5` 100, `101.5` 102, `200.5` 200, `2.5` 2 |

**In a twips container, on assignment** of a Double, with no error on any row:

| Target | Value assigned, then what VB6 reads back |
|---|---|
| CommandButton `Left` | 0.5 0, 1.5 2, 2.5 2, 3.5 4, −0.5 0, −1.5 −2, 0.4 0, 0.6 1, 3.4999999 4, 100.5 100, 0.50000001 0 |
| CommandButton `Width` | 600.5 600, 601.5 602, 600.4 600, 600.6 601 |
| Label `Left` | 0.5 0, 1.5 2, 2.5 2, 3.5 4, −0.5 0, −1.5 −2, 0.4 0, 0.6 1, 3.4999999 4, 100.5 100 |
| Line `X1` | 0.5 0, 1.5 2, 2.5 2, −0.5 0, 0.6 1 |

**In a container that isn't twips, at load.** The .frm value is twips here too, whatever the container's scale. The
pixel and user forms gave the same twips on every row:

| Control and property | .frm text, then the twips VB6 holds |
|---|---|
| CommandButton `Left` (probe B) | `8.5` 8, `9.5` 9, `10.4` 10, `10.6` 10, `.5` 0, `-0.5` 0, `1.5` 1 |
| CommandButton `Left` (probe C) | `10.9` 10, `11.5` 11, `12.5` 12, `0.9` 0, `-1.6` −1, `-1.5` −1, `-2.5` −2, `-0.9` 0, `-30.5` −30, `-31.5` −31 |
| CommandButton `Top`, `Width`, `Height` (probe C) | `-8.9` −8, `41.9` 41, `26.5` 26 |
| Label `Left`, `Top`, `Width` (probe B) | `2.5` 2, `8.5` 8, `41.5` 41 |
| Label `Left`, `Top`, `Width`, `Height` (probe C) | `3.9` 3, `8.9` 8, `42.5` 42, `25.9` 25 |

**In a container that isn't twips, on assignment** to a CommandButton's `Left`, in the container's units:
- **On the pixel form:** 0.5 read 0.5333334 (8 twips), 1.5 read 1.466667 (22), 2.5 read 2.533333 (38), 8.5 read
  8.533334 (128), 0.6 read 0.6 (9), and 0.25 read 0.2666667 (4).
- **On the user-scale form:** 0.5 read 0.5, 1.5 read 1.505, 2.5 read 2.498833, 8.5 read 8.495334, 0.6 read 0.6005, and
  0.25 read 0.2543333.

**Which container decides,** from probe D:

| Form | Control | .frm `Left`, `Top`, `Width`, `Height` | Twips VB6 holds |
|---|---|---|---|
| pixels | X, in a twips PictureBox | `9.5`, `10.6`, `41.5`, `26.5` | 10, 11, 42, 26 |
| pixels | Y, on the form | `9.5`, `2010.6`, `41.5`, `26.5` | 9, 2010, 41, 26 |
| twips | X, in a pixel PictureBox | `9.5`, `10.6`, `41.5`, `26.5` | 9, 10, 41, 26 |
| twips | Y, on the form | `9.5`, `2010.6`, `41.5`, `26.5` | 10, 2011, 42, 26 |

**What it says:**
- **In a twips container, a position is whole twips, rounded half to even after the value becomes a Single.**
  - This holds at load and on assignment alike. So it's the property's rounding, not the reader's, which answers the
    section above.
  - It's the same for windowed CommandButtons, windowless Labels and Shapes, and a Line's ends.
  - **Half to even:** `2.5`, `4.5`, `100.5` and `-2.5` give 2, 4, 100 and −2. Half away from zero would give 3, 5, 101
    and −3.
  - **A Single first:** `3.4999999` gives 4 and `0.50000001` gives 0, because each becomes the Single 3.5 or 0.5
    before rounding. Rounding the Double would give 3 and 1. This is shown for `Left` and `Top` at load, and for `Left`
    assigned on a CommandButton and a Label. The other properties and controls weren't given such a value.
- **In a container whose scale isn't twips, a position read from the .frm truncates toward zero to whole twips.**
  - `-1.6` and `-0.9` give −1 and 0, which rules out rounding down. `11.5` and `-1.5` give 11 and −1, which rules out
    rounding half to even.
  - Pixels and a user scale are measured. Scale modes 2 and 4 to 7 weren't tried.
- **An assignment there still rounds to the nearest whole twip.** Four of the six pixel rows land on half twips, and go
  to even: 7.5 to 8, 22.5 to 22, 37.5 to 38, 127.5 to 128.
- **The control's own container decides, not the form.** A twips PictureBox on a pixel form rounds, and a pixel
  PictureBox on a twips form truncates.

**What resisted explanation.** Why a position loaded into a container that isn't twips truncates, where an assignment
to the same control rounds. I have no rule for it, only the rows.

**Not measured:**
- a value within a Single's precision of a whole twip in a container that isn't twips, such as `10.99999999`;
- scale modes 2 and 4 to 7;
- what the VB6 IDE shows in the Properties window for such a form, and what it saves.

**Where main differs.** Main `084ab0365` ran the same `.frm` files on Windows through a Debug `HexIDE.Standalone`.
Probes A, C and D were run, and B wasn't.
- **Twips container:** every row matches, except the Single rows:
  - at load, `Left` `3.4999999` and `0.50000001` read 3 and 1, and `Top` `3.4999999` reads 3;
  - on assignment, `Left = 3.4999999` reads 3 on the CommandButton and the Label, and `Left = 0.50000001` reads 1 on
    the CommandButton.
- **Container that isn't twips, at load:** main rounds half to even where VB6 truncates.
  - `10.9`, `11.5`, `0.9`, `-1.6`, `-1.5`, `-0.9` and `-31.5` read 11, 12, 1, −2, −2, −1 and −32. VB6 reads 10, 11, 0,
    −1, −1, 0 and −31.
  - The Label's `3.9`, `8.9` and `25.9` read 4, 9 and 26, against VB6's 3, 8 and 25.
  - The CommandButton's `-8.9` and `41.9` read −9 and 42, against VB6's −8 and 41.
  - In probe D, the controls that truncate on VB6, Y on the pixel form and X in the pixel PictureBox, read 10, 11 or
    2011, 42 and 26 on main.
- **Container that isn't twips, on assignment:** all twelve rows match.

**Seen in passing.** Probe D's PictureBox has `Width = 3100` and the default border. Its `ScaleWidth` read 3045 in
twips and 203 in pixels on VB6, and 3040 and 202.6667 on main. The probe didn't read the PictureBox's own `Width`, so
these rows don't say where the 5 twips come from.

**Also seen in passing.** In probe C, `For Each c In Me.Controls` listed the CommandButton W00 before the Label B00 on
VB6, though the .frm has B00 first. Main listed them in the file's order. B00 had no `TabIndex` line. One pair isn't
enough for a rule about the collection's order.
