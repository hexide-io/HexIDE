## A member call inside While...Wend (2026-10-13, #882)

**The question.** Whether each shape of member call runs as one statement in a `While...Wend` body. HexIDE parsed
`k.Bump` there as a call of a procedure named `k` followed by `.Bump`, and stopped with "Sub or Function not defined
(k)". Nobody doubted VB6 runs these; the probe pins the counts the tests assert.

**Probe** (`p26`). A Sub Main project with a class `Counter` (`Public N As Long`, `Sub Bump()` adds 1, `Sub Add(ByVal
x As Long)` adds x), and a module Sub `Helper(o As Counter)` that calls `o.Bump`. Each row starts from a new
`Counter`, `i = 0`, `j = 0`, `c = 0`, and writes the value shown with `Print #`. Built as native code and also run in
the IDE with `/runexit`; **every row was identical in the two modes**, with Err 0 throughout.

| Body | Written | VB6 |
|---|---|---|
| `While i < 2` / `Debug.Print i` / `i = i + 1` / `Wend` | `i` | 2 |
| `Err.Raise 5` under Resume Next, then `While Err.Number <> 0` / `Err.Clear` / `c = c + 1` / `Wend` | `c` | 1 |
| `While i < 3` / `k.Bump` / `i = i + 1` / `Wend` | `k.N` | 3 |
| `While i < 2` / `k.Add 10` / `i = i + 1` / `Wend` | `k.N` | 20 |
| `While i < 2` / `Call k.Add(10)` / `i = i + 1` / `Wend` | `k.N` | 20 |
| `While i < 2` / `Call Helper(k)` / `i = i + 1` / `Wend` | `k.N` | 2 |
| `While i < 2` / `With k` / `.Bump` / `.Add 10` / `End With` / `i = i + 1` / `Wend` | `k.N` | 22 |
| `While i < 2` / `i = i + 1` / `k.Bump` / `Wend` | `k.N` | 2 |
| `While False` / `Wend` | a string | written, no error |
| `While i < 2: k.Bump: i = i + 1: Wend`, one line | `k.N` | 2 |
| a `While j < 2` holding `k.Bump`, nested in a `While i < 2` | `k.N` | 4 |

**HexIDE, after this.** Every row answers as VB6 does. The grammar rule's body was `block*`, which lets two blocks sit
side by side with no separator; it is now one optional block followed by a separator, as a `Do` loop's is. The LSP
server's copy of the rule had the same shape, and is fixed the same way (`docs/vb6-grammar-fixes.md`, fix 8).

