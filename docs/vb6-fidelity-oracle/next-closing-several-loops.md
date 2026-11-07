## Next closing several loops (2026-10-27, #2393)

**The question.** `Next j, i` stopped its whole module loading in HexIDE. Both grammars end a loop in one `Next` with at
most one name. Does vb6.exe take a list, and if so, is it exactly `Next j: Next i`? Which lists does it refuse?

**The probe.** One program and six one-construct projects, each built native and p-code with the guest's vb6.exe and
run there. Each loop appends its counters to a string, and the program logs the string and the counters after the
loops. Native and p-code logged the same lines for every project.

**Result: a list closes its loops innermost first, exactly as one Next per name would.**

| Program | vb6.exe |
|---|---|
| `For i = 1 To 2` around `For j = 1 To 2`, closed by `Next j, i` | `11,12,21,22,`, and after it `i` 3, `j` 3 |
| The same with `Next j,i`, and with the loops on one line ending `Next j , i` | the same |
| `Next j, _` with `i` on the next line, and `Next j _` with `, i` on the next line | the same |
| Three loops closed by `Next k, j, i` | `111,112,121,122,211,212,221,222,`, and after it 3, 3, 3 |
| `Next k`, then `Next j, i` | the same |
| `Next k, j`, then `s = s & "\|"`, then `Next i` | `111,112,121,122,\|211,212,221,222,\|`: the statement runs once per pass of `i` |
| `Exit For` at `j = 2` in `For i = 1 To 3` around `For j = 1 To 3`, closed by `Next j, i` | `11,21,31,`, and after it `i` 4, `j` 2 |
| `For Each b` inside `For Each a` over `Array("x", "y")`, closed by `Next b, a` | `xx,xy,yx,yy,` |
| `For i = 1 To 2` inside `For Each a`, closed by `Next i, a` | `x1,x2,y1,y2,`, and after it `i` 3 |

**Refused, each a compile error:**

| Construct | vb6.exe |
|---|---|
| `Next i, j`, the names in the wrong order | **"Invalid Next control variable reference"** |
| `Next j, k`, `k` not an open loop's counter | **"Invalid Next control variable reference"** |
| `Next j, i` with only `For j` open | **"Next without For"** |
| `Next j, i, k` with two loops open | **"Next without For"** |
| `Next j,`, and `Next , i` | **"Syntax error"** |

**In HexIDE (#2393):**
- Before the parse, each comma in a list like these becomes a statement separator and a `Next` of its own, at the
  comma's position, in the interpreter and in the LSP server alike. So both read `Next j, i` as `Next j: Next i`.
- `Next j,` and `Next , i` are left as written, and stay syntax errors.
- The names on a `Next` are not checked against the open loops, with or without a list. So the four refusals above
  that name a counter are missing errors here: `Next i, j` runs as `Next i: Next j`.
