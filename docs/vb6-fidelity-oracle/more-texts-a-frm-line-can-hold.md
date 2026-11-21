## More texts a .frm line can hold: the Single rule tested, TabIndex in hex and an unquoted Tag (2026-11-21, #4988, `/make`, SP6)

**The question.** [What an accepted text reads as, and more texts by type](a-property-named-twice-and-a-measurement-vb6-cant-set-in-a-frm.md#what-an-accepted-text-reads-as-and-more-texts-by-type-2026-11-20-4988-make-sp6)
fitted a rule for a Single: VB6 refuses a text holding any character but digits, `.`, `+`, `-` and `e`, and otherwise
reads the longest number at the text's start, or 0. That page called the rule "fitted to these rows, not one I'd defend
beyond them". It left an upper-case `E` untried, and left three questions open:
- the value `TabIndex = &H3` sets;
- whether an unquoted text on a string property other than Caption is refused;
- more rows where candidate rules disagree.

**The probe:**
- **The software:** VB6 SP6, in the Win10 oracle guest.
- **Phase 1:** one `/make` of a form with:
  - 30 CommandButtons, each with one text as its `Left`, a Single;
  - five CommandButtons, each with one text as its `Tag`.
  The build failed, and its log listed the refused lines.
- **Phase 2:** a `/make` of the form without the refused lines, built native and p-code. Each EXE wrote every
  control's `Left` and `Tag` from `Form_Load`, and the two builds wrote the same 29 lines.
- **TabIndex:** a third form had five CommandButtons with `TabIndex` 0, 1, 2, `&H3` and 4. Native and p-code wrote the
  same five lines.

**`Left`, a Single.** Each value is in whole twips, because `Left` rounds half to even to whole twips (oracle PR
#5161). Where that rounding is what decided the value, the number before rounding is in brackets.

| Text | VB6 | Text | VB6 | Text | VB6 |
|---|---|---|---|---|---|
| `5E` | 5 | `+-5` | 0 | `1e2e3` | 100 |
| `5e2` | 500 | `-+5` | 0 | `.e5` | 0 |
| `5E2` | 500 | `++5` | 0 | `e5` | 0 |
| `5e+2` | 500 | `.5.5` | 0 (0.5) | `.` | 0 |
| `5e-1` | 0 (0.5) | `5..` | 5 | `-` | 0 |
| `1e+` | 1 | `1.5e` | 2 (1.5) | `+` | 0 |
| `1e-` | 1 | `5-5` | 5 | `-.` | 0 |
| `5.e2` | 500 | `5+` | 5 | `-5-` | −5 |
| `1.2.3e2` | 1 (1.2) | `1-2` | 1 | `--5.5e1` | 0 |
| `00012` | 12 | `1e39` | refused, "had an invalid value" | `-1e39` | refused, "had an invalid value" |

- **The rule holds on every new row.** The value is the longest number at the text's start, or 0 where none starts it.
  - An exponent needs a digit: `1e+` and `5E` read 1 and 5.
  - A second sign ends the number: `5-5` reads 5, and `-+5` reads 0.
  - A lone `.`, `+` or `-` reads 0, and so does a text starting with `e`.
- **An upper-case `E` works as `e` does:** `5E2` reads 500, as `5e2` does.
- **A number too big for a Single is refused:** `1e39` and `-1e39` both had "an invalid value".

**`Tag`, a string.**
- **Refused, logged as "Property Tag in G0 must be a quoted string":** `abc`, `5` and `(5)`.
- **`a b` was refused that way too,** and VB6 also logged "Syntax error: property b in G3 was missing an '='".
- **Read:** `"q"` reads `q`.
- So an unquoted Tag is refused as an unquoted Caption is, which answers #4988's open question for Tag.

**`TabIndex = &H3`.** With siblings at 0, 1, 2 and 4, the five buttons read 0, 1, 2, 3 and 4. `&H3` reads 3, and
nothing was renumbered.

**Not measured:**
- an unquoted text on string properties other than Caption and Tag;
- `&H` on other Integer properties;
- characters other than those tried, and texts with leading or trailing spaces.

**Where main differs.** Main `3f3ad5742` ran the phase 2 and TabIndex forms through a Debug `HexIDE.Standalone` on
Windows.
- **TabIndex:** main refuses `&H3`, and its renumbering then reads the buttons as 0, 1, 2, 0 and 3. VB6 reads 0, 1, 2,
  3 and 4.
- **Phase 2:** main never reached `Form_Load` and wrote nothing. It exited with code 0 and printed nothing about why.
  That wasn't investigated: main refuses most of the prefix-rule texts on purpose (slice 3, #5090).
