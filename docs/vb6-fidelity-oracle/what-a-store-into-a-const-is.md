## What a store into a Const is (2026-10-14, #1003)

**The question.** #1002 measured `MK = 2` into a Const as VB6's compile error "Assignment to constant not permitted".
Refusing it needs the message for every statement that can store, and VB6 gives three.

**The probe.** One project per statement, built with `/make` as native code and as p-code, the two agreeing on every
row. Each statement is in `Sub Main` under `On Error Resume Next`, followed by a line logging `Err` and the constant; none
of them built, so nothing ran. `MK` is a module `Const MK = 1`, `MS` a module `Const MS = "abc"`, and `eOne` the member
of `Enum E` whose value is 1.

| Statement | `/make` |
|---|---|
| `MK = 2` | Assignment to constant not permitted |
| `Let MK = 2` | the same |
| `Const LK = 1` in `Sub Main`, then `LK = 2` | the same |
| `Set MK = Nothing` | the same |
| `Module1.MK = 2`, with `Public Const MK = 1` | the same |
| `MK = MK + 1` | the same |
| `CK = 2` in a class's method, with `Private Const CK = 1` in the class | the same |
| `eOne = 2` | the same |
| `For MK = 1 To 2` ... `Next` | Variable required - can't assign to this expression |
| `For Each MK In arr` ... `Next`, with `Dim arr(1)` | the same |
| `Mid(MS, 1, 1) = "x"` | the same |
| `LSet MS = "x"` | the same |
| `Line Input #1, MS` | the same |
| `For eOne = 1 To 2` ... `Next` | the same |
| `ReDim MK(2)` | Expected array |
| `Erase MK` | the same |
| `ReDim eOne(2)` | the same |
| `Erase eOne` | the same |

**What it says.** A Const and an Enum member are refused alike, and the message is the statement's, not the
constant's:
- **A Let or a Set** is "Assignment to constant not permitted", qualified or not, in a class or not.
- **A statement that stores into a variable its own syntax names**, a For counter, a For Each variable, the target of
  Mid, LSet or Line Input, is "Variable required - can't assign to this expression".
- **ReDim and Erase** want an array, so a constant is "Expected array".

**HexIDE, after #1003.**
- **Let and Set:** `ModuleExecutionContext.TryUpdateVariable` refuses a Const as it already refused an Enum member. The
  one store it lets through is the Const statement's own fill, which also runs again for each instance of a class.
- **The other statements:** For, For Each, Mid, ReDim and Erase check their target first, and give their own message, an
  Enum member's included. Before, `For eOne = 1 To 2` said "Assignment to constant not permitted".
- **When:** each is raised as the statement runs, which is how a compile-time error is translated here
  (interpreter-core:40-42).
- **Pinned:** in `ConstStoreTests`, except three rows. `Module1.MK = 2` is not reachable, because a module-qualified
  store raises 424 whatever it names (#1006). LSet and Line Input are not implemented.

**And a Const of another module** (#1007's review, native and p-code agreeing). Module1 declares `Const X = 1`, which is
Private by default, and Module2, without Option Explicit, runs `X = 5` and prints `X`. VB6 prints `5, Integer` from
Module2 and `1` from Module1: a Private Const is invisible outside its module, so Module2's `X` is a new variable of its
own. With `Public Const X = 1` instead, Module2 does not compile: "Assignment to constant not permitted". HexIDE gives
the Private row, pinned, since a store resolves the name in its own module first. It gave the same for the Public row,
where VB6 refuses, because it did not see another module's Public names unqualified at all. Since #880 it refuses the
store with VB6's message, pinned too.

