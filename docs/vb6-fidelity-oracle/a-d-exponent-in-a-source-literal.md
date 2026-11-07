## A D exponent in a source literal (2026-10-23, #1611)

**The question.** Does vb6.exe compile a `D` exponent in a numeric literal, as in `2.5D2` or `1D3`? If so, with what type
and value? Run-time conversion reads one: `CDbl("1D3")` and `Val` were measured earlier. HexIDE's grammars did not. The
language server's lexed one in a decimal literal only, and the interpreter's in neither, so `x = 1D3` failed to load. A
first draft of #781 extended the interpreter's grammar from an unmeasured brief, and it was taken out in review (#1610).

**The probe.** One `/make` project per literal, each logging `TypeName` and `CStr` of it, built native and p-code with
vb6.exe 6.00.8176 (the release build) and run on the MSVBVM60 6.00.9848 (SP6) runtime, en-GB. Every project was a `.bas`
written outside the IDE. There were two batches:
1. 21 projects: 17 literals, a `Const`, a `#Const`, an `#If`, and a variable named `D2`.
2. 15 edge cases, beside their E forms.

Native and p-code agreed on every row.

**Result: D is E.** Every row with D gave what the same row with E gives, a refusal included.

| Literal | vb6.exe |
|---|---|
| `2.5D2`, `1D3`, `1d3`, `1D+3`, `1e3` | Double: 250, 1000, 1000, 1000, 1000 |
| `1.5D-2`, `.5D1`, `5.D1` | Double: 0.015, 5, 50 |
| `1D300`, `1D-400`, `-1D3` | Double: 1E+300, 0, -1000 |
| `1D3!`, `1D3#`, `1D3@` | Single 1000, Double 1000, Currency 1000 |
| `Const K = 1D3` | Double 1000 |
| `Const KS! = 2.5D2` | Single 250 |
| `#Const K = 1D3`, then `#If K = 1000` | true |
| `#If 2.5D2 = 250` | true |
| a variable `D2` holding 7, then `1 + D2` | 8 |

**Refused, each "Syntax error":**
- an exponent with an Integer or Long suffix: `1D3%`, `1D3&`, `1E3%` and `1E3&`;
- a value beyond its type: `1D400` and `1E400` for a Double, `1D39!` and `1E39!` for a Single, `1D20@` and `1E20@` for
  a Currency;
- two exponents, as in `1D2D2`;
- an exponent with no digits: `1D`, `1.5D`, `1D-`, and `1 D3`.

A value below its type's range is not refused: `1D-400` and `1E-400` are each 0.

Not measured: what the IDE's pretty-lister rewrites `1D3` to when it loads a file, since that needs the guest's console.
`/make` compiles the file as it was written.

### In HexIDE (#1611)

- **Both lexers take `D` or `d` wherever they take `E`.** That is the interpreter's `INTEGERLITERAL` and `DOUBLELITERAL`,
  and the language server's `INTEGERLITERAL`, whose `DOUBLELITERAL` had it already (grammar fix 5). A `.frx` offset such
  as `:0D2C` is unaffected: `FRX_OFFSET` takes its colon too, so it is the longer match.
- **`ExpressionExecutor.ExponentAsE`** reads the marker as E for every reader of a literal, the Enum fold in `PrePass`
  included.
- **The refusals were HexIDE's divergence for E too, and are fixed with D.** `1E3%` stopped the program with a .NET
  `FormatException`, and `1E3&` was the Long 1000. `1E400` and `1E39!` were infinity, and `1E20@` was a Currency beyond
  Currency's range. Each is now vb6.exe's "Syntax error", raised as a compile error where the literal is read. A literal
  below its type's range is still not refused. Not measured: whether a long string of digits with no exponent, beyond
  its type, is refused the same way. HexIDE refuses any literal whose value .NET reads as infinity, and any Currency
  beyond Currency's range.
- **`DExponentLiteralTests`** checks each accepted literal's type and value against vb6.exe's line, and that each refused
  one is refused. `DExponentLiteralGrammarTests` checks the language server's diagnostics.
- **Divergences kept, each filed:**
  - `5.D1` is a literal ending in its point, which neither grammar parses (#1615);
  - `Const KS! = 2.5D2` stops as the module loads, since a `Const` with a type character is not implemented (#1614);
  - `1D`, `1.5D`, `1D-` and `1 D3` are refused in the parser's words, not as "Syntax error".

