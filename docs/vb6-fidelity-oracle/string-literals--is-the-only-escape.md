## String literals: `""` is the only escape (2026-09-04)

| written | VB6 | `Len` |
|---|---|---|
| `"he said ""hi"""` | `he said "hi"` | 12 |
| `""""` | one quote (`Asc` 34) | 1 |
| `""` | **empty** | 0 |
| `""""""` | two quotes | 2 |
| `"""a"""` | `"a"` | 3 |
| `"a""b"` | `a"b` | 3 |
| `"a""" & """b"` | `a""b` — two REAL adjacent quotes | 4 |
| `"""" = Chr(34)` | True | |

`""` inside the delimiters is one quote, and it is the only escape VB6's string literals have.

**The order is what the degenerate cases pin.** The delimiters must come off *before* the unescape:
applied to the raw token text, `""` — the empty literal — would become a single quote. `Len("")` is 0 and
`Len("""")` is 1, and those two are indistinguishable until the outer pair is gone. Nothing shorter than
these two cases can establish that, which is why both are recorded rather than just the readable one.

### What this overturned

HexIDE stripped the delimiters with `Substring(1, len - 2)` and did nothing else, so **no string literal
was ever unescaped, anywhere**. `MsgBox "He said ""hello"""` displayed the doubles. A wrong value on
entirely ordinary code.

It had been recorded as a known divergence named `STRING-ESCAPE` against **one** corpus case, whose area
was line continuations — so it read as a continuation quirk. It was nothing of the sort. The lesson is the
same one the `Select Case` cluster taught in the other direction: **the corpus area a defect surfaces in
is not evidence about its cause.** There is exactly one place in the interpreter that reads a
`STRINGLITERAL`, so there was exactly one place it was wrong, and it was reachable from every string in
every program.

### A second, independent instance — in serialization

The same rule is applied inconsistently across three sites that unquote a VB6 quoted value:

| site | unescapes `""`? |
|---|---|
| `ProjectDeserializer` (`.vbp`) | **yes** |
| `ExpressionExecutor` (literals) | no → fixed |
| `VbFrmFormatDeserializer` (`.frm`) | **no** |

The `.frm` *writer* does not escape either (`Sink.WriteLine($"{field}=   \"{value}\"")`), so HexIDE
round-trips its own files consistently while disagreeing with VB6 in both directions: a real `.frm`
carrying `Caption = "He said ""hi"""` reads back with the doubles intact, and a caption containing a quote
is written as output VB6 cannot load. Filed separately — it belongs to `serialization-outcomes.md` and
needs its own measurement of what VB6 actually writes.

