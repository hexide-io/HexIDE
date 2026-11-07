## A numeric literal ending in its point (2026-10-23, #1615)

**The question.** #1611 found that vb6.exe reads `5.D1` as the Double 50, and neither of HexIDE's grammars lexed a
literal with no digit after its point, so the module failed to load. Does vb6.exe take `5.` itself, with each suffix and
exponent, and wherever a literal goes? And what of `%` and `&` on a literal with a point, which HexIDE rounded to an
Integer or a Long, and which #1610's test pinned as 2 for `1.5%` and `2.5&` without a measurement?

**The probe.** One `/make` project per form, each logging `TypeName` and `CStr` of it, built native and p-code with
vb6.exe 6.00.8176 (the release build) and run on the MSVBVM60 6.00.9848 (SP6) runtime, en-GB. There were two batches:
1. 20 projects: 12 literals, a `Const`, an `#If`, a `#Const`, two arguments, an array index, a concatenation,
   arithmetic, and `5..5`.
2. Six literals with a point and an Integer or Long suffix.

Native and p-code agreed on every row.

**Result: a literal may end in its point, and is then what it would be with a digit after it.**

| Form | vb6.exe |
|---|---|
| `5.`, `-5.`, `0.`, `123456789012.` | Double: 5, -5, 0, 123456789012 |
| `5.#`, `5.!`, `5.@` | Double 5, Single 5, Currency 5 |
| `5.E1`, `5.D1`, `5.E-1` | Double: 50, 50, 0.5 |
| `Const K = 5.` | Double 5 |
| `#If 5. = 5`, and `#Const C = 5.` then `#If C = 5` | true |
| `Two(5., 2.)` with `Two = a * 10 + b` | 52 |
| `a(2.) = 7`, then `a(2)` | 7 |
| `5. + 1`, `2.*3.` | Double 6, 6 |

**Refused, each "Syntax error":**
- `5..5`;
- a literal with a point and an Integer or Long suffix, whole or fractional, with or without an exponent: `5.%`,
  `5.&`, `2.5%`, `2.5&`, `.5%`, `5.0%`, `5.0&` and `2.5E1&`.

So `%` and `&` never take a literal with a point. The first guess, which HexIDE made and #1610's test pinned, was that
they round it, as `CInt` and `CLng` would: `1.5%` the Integer 2. vb6.exe refuses to compile it.

### In HexIDE (#1615)

- **Both lexers' `DOUBLELITERAL`** take digits, a point and no more digits, beside the forms they took: the interpreter's
  `('0'..'9')+ DOT ('0'..'9')* | DOT ('0'..'9')+`, and the language server's `[0-9]+ DOT [0-9]* | DOT [0-9]+`. `5..5` is
  then two literals side by side, which each parser refuses.
- **A literal with a point and `%` or `&`** is the compile error "Syntax error", where `ExpressionExecutor` rounded it to
  an Integer or a Long. #1610's culture test lost its `1.5%` and `2.5&` rows, which pinned that rounding.
- **`TrailingPointLiteralTests`** runs each probe program and must print vb6.exe's lines, and refuses each refused
  one. `TrailingPointLiteralGrammarTests` checks the language server's diagnostics, and `DExponentLiteralTests` now
  reads `5.D1`.
- **Divergence kept:** the language server's `DOUBLELITERAL` takes `&` but not `%`, so the editor marks `2.5%` and not
  `2.5&`. The interpreter refuses both as the module runs.

