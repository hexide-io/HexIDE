## A long date pattern with an era (2026-11-14, #3946)

**The question.** Some locales' long date pattern ends with the era, `gg`. What does VB6's `Format$(d, "Long Date")`
write for it? The obvious guess, in #3946 itself, was Windows' era text, because VB6 takes the named formats' patterns
from Windows. That guess is wrong.

**The probe.**
- **Build:** VB6 SP6, `/make`, native code and p-code, which logged the same lines.
- **The locales:** the Win10 guest (build 19044) with its user locale set to `byn-ER` (Blin) for one run and `wal-ET`
  (Wolaytta) for another, then set back to `en-GB`. Both are locales Windows gives no LCID of their own (4096).
- **The date:** 31 December 2000.
- **The record:** each result as text, with every character outside printable ASCII written as its UTF-16 code in
  braces, since `Print #` writes ANSI. Windows' own text came from `GetDateFormatEx` in the same guest.

In that build, `byn-ER`'s long date pattern is `dddd፡ dd MMMM ግርጋ yyyy gg`, and `wal-ET`'s is `dddd፥ dd MMMM ጋላሳ yyyy gg`.
`la-VA`, whose long date carries an era on Windows Server 2025, has none in this build: `dddd d MMMM yyyy`.

| Locale | `Format$(d, "Long Date")` | `GetDateFormatEx`, `DATE_LONGDATE` |
|---|---|---|
| `byn-ER` | `{1230}{1295}{1260}{122D} {1245}{12F3}{12C5}{1361} 31 {1270}{1215}{1233}{1235}{122A} {130D}{122D}{130B} 2000 gg` | the same, ending `2000 {12A3}{12F5}` (ኣድ) |
| `wal-ET` | `{12C8}{130B}{1365} 31 {12F2}{1234}{121D}{1260}{122D} {130B}{120B}{1233} 2000 gg` | the same, ending `2000 {130D}{122E}{1270}{1273} {120B}{12ED}{1273}` (ግሮተታ ላይታ) |

In both locales:
- `Format$(d, "Short Date")`, `CStr(d)` and `Format$(d)` give `31/12/2000`, as `GetDateFormatEx`'s short date does.
- `CDate` of the short date gives 31 December 2000.
- `CDate` of the long date raises 13.

**Readings:**
- **VB6 writes `gg` as two literal letters.** It expands the day name, the month name, the day and the year, and keeps the
  pattern's literal text, but its formatter has no era token. So everything except the era matches Windows.
- **The era is the only difference from Windows** in these two locales.
- **Which locales carry an era depends on the Windows build.** `la-VA`'s long date has none in Windows 10 19044.

**What HexIDE does with this.** Writing the token unexpanded is a VB6 defect, not a behaviour a program relies on. Under
the Fidelity Principle in `CLAUDE.md`, HexIDE writes Windows' era text, and that divergence is recorded in #3946.

**Not measured:**
- the era in a format string a program writes itself (`Format$(d, "gg yyyy")`);
- a locale with a real LCID whose long date carries an era;
- Japanese and other non-Gregorian calendars.
