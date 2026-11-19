## A free-standing StdFont: Bold, Weight 400 and 700, and one assignment after another (2026-11-19, #4807)

**The probe.**
- **Build:** vb6.exe 6.00.9782 with MSVBVM60 6.00.9848, `/make` native and p-code in session 0. The two builds logged all 27 rows identically.
- **Each row:** a fresh `Set f = New StdFont`, which reads MS Sans Serif, 8.25, Weight 400, Charset 0, no flags. Then the row's assignments under `On Error Resume Next`, and every member read back.
- **No row raised an error.** Members a row doesn't mention read as they started.
- **Context:** the single assignments, errors included, are a separate section of [a-controls-font-object.md](a-controls-font-object.md) (oracle PR #4808).

| Assignment(s) | Reads afterwards |
|---|---|
| `Bold = True`; `Bold = -1` | **Arial**, 8.25, Bold True, Weight 700 |
| `Bold = False`; `Bold = 0` | **Arial**, 8.25, Bold False, Weight 400 |
| `Weight = 400` | **Arial**, 8.25, Weight 400 |
| `Weight = 600` | MS Sans Serif, 8.25, Weight 700, Bold True |
| `Weight = 700: Bold = False` | Arial, Weight 400 |
| `Bold = True: Weight = 400` | Arial, Weight 400 |
| `Italic = True: Bold = True` | Arial, Bold and Italic |
| `Italic = True`; `Underline = True`; `Strikethrough = True` | MS Sans Serif, that flag True |
| `Size = 12: Weight = 700`; `Size = 12: Bold = True`; `Bold = True: Size = 12` | Arial, 12, Weight 700 |
| `Bold = True: Name = "MS Sans Serif"` | MS Sans Serif, 8.25, Bold True, Weight 700 |
| `Name = "Arial": Bold = True` | Arial, 8.25, Weight 700 |
| `Name = "Arial": Bold = True: Bold = False` | Arial, 8.25, Weight 400 |
| `Size = 10`; `Size = "10"`; `Size = 9.75` | MS Sans Serif, 9.75 |
| `Size = 10.5` | **Arial**, 10.5 |
| `Size = 13` | Arial, 12.75 |
| `Size = 8` | MS Sans Serif, 8.25 |
| `Size = 9: Name = "Verdana"` | Verdana, 9 |
| `Name = "Verdana": Size = 12` | Verdana, 12 |
| `Name = "Wingdings": Size = 9` | Wingdings, 9, Charset 2 |

**A rule fitted to these rows, for MS Sans Serif only:** setting Weight to exactly 400 or 700 makes the name read Arial.
- **Either way counts:** directly, or through Bold, True or False. That includes the 400 a fresh font already reads.
- **The other weights keep MS Sans Serif:** 600 here; 0, 100, 550, 551, 900 and 1000 in #4808's rows.
- **Naming MS Sans Serif again afterwards** keeps both the name and the weight.
- **Why 400 and 700 alone: no rule I would defend.** It fits every Bold and Weight row measured so far, and was fitted to them.

**The obvious guess, that `Bold = True` leaves the name alone as `Italic = True` does, is wrong.** HexIDE made that guess in #4781, and these rows overturned it.

**A name set after a size keeps the size,** and a size set after a name keeps the name, for Verdana and Wingdings.

**Not measured:**
- Weights between those above;
- Bold on a font other than MS Sans Serif or Arial;
- a control's own Font.
