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
