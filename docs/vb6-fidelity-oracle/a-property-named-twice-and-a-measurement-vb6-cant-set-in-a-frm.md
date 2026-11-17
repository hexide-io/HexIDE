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
