## An undeclared name before a dot (2026-11-12, #3778)

**The question.** In a module without `Option Explicit`, a name nothing declares is an implicit Variant, Empty until
something is stored in it (#171). What happens when such a name is used as an object: before a `.`, before a `!`, or as
a `With` object? HexIDE refused some of these as "Variable not defined" before running them, as if `Option Explicit` were
on.

**The probe.** vb6.exe SP6 (6.00.9782), `/make`, on the Win10 oracle guest. Each project had one `Sub Main`, in a module
with no `Option Explicit` unless the row says otherwise, and a module `Real` holding `Public Function F() As Long`, which
returns 7. The program ran under `On Error Resume Next`, after setting `v = "unset"`:
1. the row's statement or statements;
2. `Print #f, "v=" & v & "|err=" & Err.Number & "|" & Err.Description`;
3. `Err.Clear`, then `TypeName(Nope)`;
4. a line saying the next statement ran.

`Nope` is declared nowhere. Each row was its own project, so a compile error in one couldn't hide another, and each was
built as native code and as p-code. The two agreed in every row.

| Row | Statements | Built | Logged |
|---|---|---|---|
| N1 | `v = Nope.F()` | yes | `v=unset`, err 424 "Object required"; `TypeName(Nope)` `Empty`; the next statement ran |
| N2 | `v = Nope.F(1)` | yes | the same |
| N3 | `v = Nope.P` | yes | the same |
| N4 | `Nope.P = 5`, then `v = "after-let"` | yes | `v=after-let`, err 424; the rest the same |
| N5 | `Nope.F`, then `v = "after-call"` | yes | `v=after-call`, err 424 |
| N6 | `Call Nope.F`, then `v = "after-call"` | yes | `v=after-call`, err 424 |
| N7 | `Call Nope.F()`, then `v = "after-call"` | yes | `v=after-call`, err 424 |
| N8 | `Set o = Nope.P`, then `v = "after-set"` | yes | `v=after-set`, err 424 |
| N9 | `With Nope` / `v = .P` / `End With` | yes | `v=unset`, err 424 |
| N10 | `v = Nope!Key` | yes | `v=unset`, err 424 |
| N11 | `v = Nope.P.Q` | yes | `v=unset`, err 424 |
| N12 | `v = Real.F()` (the control) | yes | `v=7`, err 0 |
| E1 | `v = Nope.F()`, under `Option Explicit` | no | "Compile Error … : Variable not defined" |
| E5 | `Nope.F`, under `Option Explicit` | no | "Compile Error … : Variable not defined" |

**The answer.** Without `Option Explicit`, an undeclared name used as an object compiles in every position probed:
calling a member with or without arguments, reading, writing or `Set`-ting a property, `Call`, `With`, `!` and a chain.
At run time the name is an Empty Variant, so each raises 424, "Object required", which `On Error Resume Next` traps, and
the next statement runs. The name stays Empty. With `Option Explicit`, each is a compile error, "Variable not defined".

**In HexIDE (#3778, at `b681449ea`).** The same rows, run by the interpreter:
- **Matched VB6** (trapped 424, the next statement ran): N4, N5, N6, N9, and the control N12.
- **Stopped the run** with "Compile error: Variable not defined (Nope)": N1, N2, N3, N8, N11. This is every row where
  the name heads a member chain in an expression.
- **N10**, `Nope!Key`, stopped with "`Nope!Key` is supported only on a Collection and a form's Controls".
- **N7** didn't parse; see the next section.
- **E1 and E5** gave "Variable not defined", when the statement ran rather than at build. That is the translation
  `interpreter-core` prescribes for a compile-time error.

## `Call` with empty parentheses (2026-11-12, #3779)

**The probe.** As above:
- `Call Helper()`, where `Helper` is a `Sub` in the same module, with and without `Option Explicit`;
- `Call Real.F()`, the function from the other module.

**VB6:** every row built and ran, native code and p-code alike, and the next statement ran with err 0.

**In HexIDE** (`b681449ea`), `Call Helper()` and `Call Real.F()` are syntax errors in both grammars. The interpreter
refused to run the program, and the bundled language server reported "Syntax error" on the line. `Call Helper` and
`Call Helper(1)` were fine. Both grammars require at least one argument inside a `Call`'s parentheses (#3779).

**Something that resisted explanation.** A first version of this probe named the `Sub` `Local`. vb6.exe refused all three
projects, each of which declared `Sub Local()`, with "Syntax error" on a line that wasn't the `Call`. With the `Sub`
renamed to `Helper`, all three built. `Local` isn't a VB6 keyword that this oracle has recorded, and why the name was refused wasn't
looked into.
