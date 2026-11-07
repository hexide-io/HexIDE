## IIf and Choose (2026-10-22, #1461, #1464)

**The question.** HexIDE had neither, so a program that called either stopped there. Both are said to be simple; the
details that matter are the ones people get wrong, so they were measured before anything was written.

**Two obvious guesses, both wrong:**
- **The obvious guess is that IIf evaluates only the part it returns. It evaluates both.** `IIf(n <> 0, 1 / n, 0)` with
  n 0 is 11, Division by zero, and a part with a side effect runs whichever is chosen, the true part first. Code that guards
  a division with IIf is broken in VB6, and HexIDE raising 11 there is the faithful answer, though it will look like a
  HexIDE bug to someone who expects the guard to work.
- **The obvious guess, and the documentation's, is that Choose rounds a fractional index. It truncates.** `Choose(1.6, "a",
  "b", "c")` is "a", and `Choose(0.5, …)` is Null.

**The probe.** A Sub Main under `On Error Resume Next`, logging each result's `TypeName` and text, and three compile-error
projects. Made native and p-code under en-GB; every line was identical. The three programs `IIfChooseTests` runs were then
made the same way, and logged exactly what the tests pin.

| IIf | VB6 |
|---|---|
| `IIf(True, F("true part", 1), F("false part", 2))` | both evaluated, true part first; Integer 1 |
| `IIf(True, 1, 1 / 0)`; `IIf(n <> 0, 1 / n, 0)`, n 0 | 11 each |
| `IIf(True, 5, "a")`, `IIf(False, 5, "a")`, `IIf(False, 5, 2.5)`, `IIf(True, 5&, 2)` | Integer 5, String "a", Double 2.5, Long 5 |
| `IIf(True, Null, 1)`, `IIf(True, Empty, 1)` | Null, Empty |
| `Set o = IIf(True, c, Nothing)`, and `False` | the object (Collection, or a class), and Nothing |
| condition Null | the false part, no error |
| condition "True", "0", 2, 0.4, Empty | true, false, true, true, false |
| condition "abc" | 13 |
| condition a Collection; a class with no default member | 450 (its default member, `Item`, wants an index); 438 |
| `IIf(True, 5, 6) * 7000`; `IIf(True, i, 6) * 7000`, i an Integer 5 | Long 35000 each: the result is a Variant |
| `IIf(True, 1)`; `IIf(True, 1, 2, 3)` | compile errors: "Argument not optional"; "Wrong number of arguments or invalid property assignment" |

| Choose | VB6 |
|---|---|
| `Choose(2, F("one", "a"), F("two", "b"), F("three", "c"))` | all three evaluated, in order; String "b" |
| `Choose(1, 1, 1 / 0)` | 11 |
| index 1.4, 1.5, 1.6; 2.5; 3.5 | "a", "a", "a"; "b"; "c": truncated |
| index 0.5, -0.6, 0, -1, 4 (of three) | Null each |
| index True (-1), 40000, 1E+20 | Null each: no 6 |
| `Choose(1)`, no choices | Null |
| index Null; "2"; "x" | 94; "b"; 13 |
| `Choose(2, 5, "x")`; `Choose(1, 5, 6) * 7000` | String "x"; Long 35000 |
| `Set o = Choose(1, c, Nothing)` | the object |
| `Choose()` | compile error: "Argument not optional" |

### In HexIDE (#1461, #1464)

Both are intrinsics in `VB6BuiltIns.Choice.cs`. Their arguments are evaluated before the call, as every intrinsic's are,
which is what VB6 does here too. The registry hands them their parts as themselves (`TakesItsChoicesThemselves`), so an
object comes back as the object, and reads only the first argument as a value. Every row above is pinned in
`IIfChooseTests` except the Collection ones: HexIDE does not have `Collection` yet (#1010), so those use a class.

