## The `Empty` literal (2026-08-22, #126)

| Probe | Result |
|---|---|
| `TypeName(Empty)` | `"Empty"` |
| `VarType(Empty)` | `0` |
| `IsEmpty(Empty)` / `IsNull(Empty)` | `True` / `False` |
| an un-assigned `Dim u` | `TypeName(u)` = `"Empty"`, `u = Empty` is `True` |
| `Empty = 0` | **True** |
| `Empty = ""` | **True** |
| `Empty = False` | **True** |
| `Empty = Null` | **Null** — neither True nor False |
| `v = 5 : v = Empty` | clears the Variant; `IsEmpty(v)` is `True` |
| `Empty + 1` | `1` Integer |
| `Empty * 2` | `0` Integer |
| `Empty & "x"` | `"x"` |
| `Dim Empty As Integer` | **Syntax error** — it is a reserved word, not a name |

Empty coerces to its partner's **zero**, whatever kind of zero that is — `0`, `""` or `False` — which is
what makes all three comparisons True at once. Against `Null` the comparison stays Null, because Null
propagates and Empty does not stop it.

That last row is why `Empty` belongs in the lexer beside `NOTHING`/`NULL` rather than in the built-in
constants table: VB6 reserves the word, so a user variable must not be able to shadow it. (In ANTLR the
token has to be named `EMPTY_`, because `ParserRuleContext` already has a static `EMPTY` — the same
collision the vendored LSP grammar already works around for `NULL_`.)

