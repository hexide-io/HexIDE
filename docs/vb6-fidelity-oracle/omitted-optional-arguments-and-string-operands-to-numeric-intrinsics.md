## Omitted optional arguments, and string operands to numeric intrinsics (2026-09-02)

Measured to check two defects an adversarial pass reported against HexIDE. Both were real; one was
described wrongly, in a way that would have produced the wrong fix.

| probe | VB6 | HexIDE |
|---|---|---|
| `Split("a b c", , 2)` | **`n=1 [a][b c]`** — 2 elements on the default space delimiter | returns the whole string as ONE element |
| `Split("a,b,c", ",", , 1)` | **`n=2 [a]`** — 3 elements, compare arg accepted | raises **Err 13** |
| `Abs("abc")` | **Err 13** | Err 13 — correct |
| `Abs("5")` | **`5`** | Err 13 — wrong |

### The omitted-argument hole

A skipped middle argument (`Split(s, , 2)`) must take the parameter's default. HexIDE materialises a blank
call-site slot as `Vb6Value.Missing`, but `VB6BuiltIns.Array.HasArg` tests `!= EmptyVariant`, so the guard
never fires: the delimiter becomes `AsStr(Missing)` = `""`, and the empty-delimiter branch returns the
input unsplit. Skipping `limit` is worse than a wrong answer — `a.Count >= 3` is satisfied by the blank
slot, `AsInt(Missing)` falls past every case, and it throws. `Filter` shares the bug.

The comment above `HasArg` asserts that a skipped optional "arrives as Empty". That is wrong about this
interpreter's own call machinery, and the wrong comment is what kept the bug alive.

### Why the `Abs` row needed the oracle

The report was "a String operand raises Err 13", offered as the defect. **VB6 raises Err 13 there too**,
so taken at face value it would have led to making `Abs("abc")` return something — a change away from
VB6, not toward it.

The real divergence is narrower and opposite in shape: VB6 accepts a *numeric* string and rejects a
non-numeric one; HexIDE rejects **both**, because `TryNumericToDouble` switches only over the numeric CLR
types plus `DateTime` and returns false for `String`. The same path is shared by `Sqr`, `Sgn`, `Sin`,
`Cos`, `Exp`, `Log`, `Tan`, `Atn`, `Hex`, `Oct`, `CDate` and `CBool`, so one fix covers all of them —
which is only visible once the boundary is stated correctly.

This is the second time a plausible-sounding defect report would have moved HexIDE *away* from VB6 if
implemented as written. Measure the claim, not just the behaviour it complains about.

### Return widths of the integer-returning intrinsics (2026-09-02)

Found while fixing #190, when a test asserted the wrong type and the oracle disagreed with both of us.
Then measured across the whole family before fixing (#193), which turned up two the original finding had
missed.

| `TypeName` in VB6 | intrinsics |
|---|---|
| **Long** | `Len`, `InStr`, `InStrRev`, `LBound`, `UBound`, `DateDiff`, **`VarType`** |
| **Integer** | `Asc`, `Sgn`, `Year`, `Month`, `Day`, `Hour`, `Minute`, `Second`, `Weekday`, **`DatePart`** |
| operand-dependent | `Int`, `Fix` — these preserve the operand's subtype, so `Int(3)` is Integer and `Int(3.5)` is Double |

**It is not one rule**, which is the whole point: a blanket "widen the integer-returning intrinsics" would
have been just as wrong as leaving them. Two pairs are worth remembering because they look like
inconsistencies somebody will later try to tidy away:

- **`DateDiff` is Long, `DatePart` is Integer.** Same family, same argument shapes, different widths.
- **`VarType` is Long**, even though every `vbXxx` code it can return fits comfortably in an Integer.

### What HexIDE had wrong, and why

None of these functions chose a type. They built their result from a C# `int`, and `Vb6Value(int)` applies
a **magnitude rule** — anything fitting Int16 is reported as an Integer. That rule is correct for an
arithmetic literal, whose type genuinely does follow its magnitude; it is wrong for a function with a
**fixed declared return type**, which is what these seven have.

Reachable with entirely ordinary arguments — `TypeName(InStr("hello", "l"))` was `Integer` — so not an
edge case, merely an invisible one until something asks for the type. It does not stay invisible: the
subtype feeds the arithmetic result-type ladder, so `Len("hi") + 1` was an Integer where VB6 gives a Long.

`Int` and `Fix` are the control. They must keep going through the magnitude rule, because for them the
operand really does decide.

### `Join(a, )` is a syntax error — only a MIDDLE argument can be omitted (2026-09-02)

Found while fixing #190, when a probe would not compile:

```
Compile Error in File 'Module1.bas', Line 10 : Syntax error
```

`Split(s, , 2)` is fine; `Join(arr, )` is not. VB6 permits an omitted argument only *between* supplied
ones, never trailing with a dangling comma. So a short argument list genuinely means "the rest were
omitted", and an interior blank is the only case an implementation has to model.

That is what makes the fix for #190 safe: `Supplied(a, i)` can test `a.Count > i` for the trailing case
and `Missing` for the interior one, without a third possibility to worry about.

### Numeric line labels (2026-09-02)

| probe | measured |
|---|---|
| `GoTo 20` … `20 s = …` … `GoTo 10` … `10 s = …` … `GoTo 99` | **`a-twenty-ten`** — labels are jump targets, not BASIC line numbers, so they need not ascend |
| handler at `50` doing `Resume 60` | **`resumed to 60`** — a numeric label is a valid Resume target |
| a numeric label and a named label in one procedure | **both work** — one label table, nothing distinguishes them at the jump |

Measured before implementing, because the alternative was assuming these behave like identifier labels.
They do — which is the useful result, since it means the whole downstream machinery (the label table, the
pc-driver, `GoTo`/`On Error GoTo`/`Resume`) is shared and only the declaration form differs.

Worth recording separately: this gap existed **only in the interpreter's grammar**. The LSP server's
already had a `lineNumber` rule, so the editor reported no syntax error on a file the interpreter could
not load. Grammar divergence between the two halves is now guarded by `GrammarParityTests`.

