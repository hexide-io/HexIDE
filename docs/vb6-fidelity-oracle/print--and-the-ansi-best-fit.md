## Print # and the ANSI best fit (2026-10-23, #1511)

Measured on vb6.exe under code page 1252, native and p-code identical. Each file was read back byte by byte with `Get`
in Binary mode.

| statement | bytes written |
|---|---|
| `Print #f, ChrW(&H100) & ChrW(&H416) & ChrW(&H2019) & ChrW(&H141) & ChrW(&H3B1) & ChrW(&H20AC);` | `41 3F 92 4C 61 80` |
| `Write #f, ChrW(&H100) & ChrW(&H416), ChrW(&H141)` | `22 41 3F 22 2C 22 4C 22 0D 0A` |
| `Print #f, ChrW(&H100);`, to a file opened For Append | `41` |

Taking the characters in turn:
- **Best fits:** Ā is "A", Ł is "L", and Greek α is "a".
- **No fit:** Ж is "?".
- **1252's own bytes:** ’ is 92 and € is 80.

`Write #` best-fits inside its quotes as `Print #` does, and Append as Output does. This is the table the clipboard
section above measured (U+0100 to A, U+0416 to ?), and the one `Asc` uses (#1134): Windows' own conversion to the ANSI
code page, not a `?` for every character the code page lacks.

### In HexIDE (#1511)

`VbFiles.Ansi()` is the code page's own encoding, cloned so that only its decoder fallback is replaced. Its encoder keeps
the table's best fit. Before, both directions had a `?` replacement, so every character 1252 lacked was written as `?`.
`Line Input #` reads as before. `Write #` is not implemented yet ([#1521](https://github.com/hexide-io/HexIDE/issues/1521)), so only its measurement is recorded.

