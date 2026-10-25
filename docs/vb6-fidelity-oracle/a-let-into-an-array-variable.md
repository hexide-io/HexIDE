## A Let into an array variable (2026-10-23, #998, #1588)

**The question.** #998 measured two rows. A String into a `Byte()` becomes its bytes, and `Array(1, 2)` into an
`Integer()` is 13. #1588 found a third while GetAllSettings was being measured for #1562: Empty into a `String()` is 13,
and leaves the array unallocated. HexIDE stored whatever it was given, so the array became Empty, a String or an array of
another type. What does a Let into an array variable take, for each element type, and what does it refuse?

**The probes.** Four batches, built native and p-code with vb6.exe 6.00.8176 (the release build) and run on the MSVBVM60
6.00.9848 (SP6) runtime, en-GB. Native and p-code agreed on every row.
1. Fourteen values, each reaching `a = v` through a Variant `v`, into a dynamic array of each of String, Long, Integer,
   Double, Variant and Byte. Each target was tried unallocated and then holding two elements. Then the same statements
   written directly, and through ByRef parameters `a() As String` and `a() As Byte`. Then twelve one-statement projects
   for what might not compile.
2. Nine more scalar types into a `Byte()`, a `String()` and a `Variant()`. Then Boolean, Date, Currency, Single and Object
   arrays, each given its own type's array, a `Variant()` and Empty.
3. A fixed-size `Dim f(1) As String`, named and through a ByRef parameter.
4. A Function declared `As String()` given Nothing and Empty through a Variant, and a UDT's array fields.

The String() rows #1588 first measured, for #1562, are batch 1's String rows; it repeats each of them.

**Result: one rule, for every element type.**
- **An array of exactly the target's element type is copied,** with its own bounds, over whatever the target held. `Split`
  gives a `String()` and `Array` a `Variant()`.
- **A `Byte()` also takes a String, as its bytes:** two to a character, low byte first, from 0. "ab" is 0 To 3 holding
  97 0 98 0, "12" is 49 0 50 0, `Chr(0) & ChrW(256)` is 0 0 0 1, and "" is 0 To -1. The String may be a literal, a Const,
  a String variable or a Variant, and the target a ByRef `Byte()` parameter.
- **Anything else is 13, and the target is left as it was.** An allocated target keeps its bounds and elements, and an
  unallocated one stays unallocated.
  - The values measured: Empty, Null, Nothing, Integer, Long, Single, Double, Currency, Date, Boolean, Byte, Decimal,
    Error and String.
  - An array of another element type is 13 too: a `Variant()` into every typed array, and every typed array into a
    `Variant()`.
  - No other conversion is made. A String into a `String()` is 13, and so is a number into a `Byte()`.
- **A ByRef array parameter** takes exactly the same.
- **A fixed-size array takes nothing.** Named, `f = v` does not compile, "Can't assign to array", whatever `v` holds, a
  Variant included. Through a ByRef parameter it is 13, an array of its own type included, and it keeps its elements.
- **A Function declared `As String()`** given Nothing or Empty through a Variant is 13, as #986 measured for its other
  rows.
- **A UDT's array field** takes the same as an array variable. With `s() As String` and `b() As Byte` in a `Type`,
  `u.s = v` copies only a `String()`, `u.b = v` converts a String, and every other value is 13 and leaves the field as it
  was. `u.s = 5` does not compile.

**Where the statement shows the type, vb6.exe refuses to build instead, with "Can't assign to array":**

| Target | Statement |
|---|---|
| `Long()` | `a = 5`, `a = "ab"`, and `a = i` for an `Integer()` `i` |
| `Byte()` | `a = 5`, `a = n` for an Integer `n`, `a = s` for a `String()`, and `a = w` for a `Variant()` |
| `Variant()` | `a = 5`, `a = "x"`, and `a = s` for a `String()` |
| `String()` | `a = s` for a String `s`, and `u.s = 5` for a UDT's field |
| `Dim f(1) As String` | `f = v`, for any Variant `v` |

`a = Nothing` into a `Long()`, and `F = Nothing` in a Function declared `As String()`, are "Invalid use of object". The
statements `t = Empty`, `t = Null` and `t = Array("a")` compile, and are 13 when they run. A String Const into a
`Byte()` compiles and converts, as a literal does.

### In HexIDE (#998)

- **`StatementExecutor.ArrayStore`** is the one check. It covers a variable declared as an array, a ByRef array
  parameter, and the name of a Function or Property Get declared to return an array, where it replaces #986's check. The
  target's type is that of the array its slot holds, since nothing else is ever stored there.
- **A String into a `Byte()`** becomes `VB6BuiltIns.StringBytes`. #986's refusal, a NotImplementedException naming #998,
  is gone.
- **The compile errors** are read from the statement, as #986's were, and raised as the statement runs. A `Nothing`
  literal is "Invalid use of object".
- **An object is not read for its default member** when the target is an array, so Nothing through a Variant is 13, not
  438.
- **A named fixed array** is "Can't assign to array" whatever it is given. HexIDE refused only an array there.
- **`TypedArrayLetTests`** runs the programs from batches 1 to 4 and must print vb6.exe's lines. It also refuses 16 of
  the one-statement projects with vb6.exe's message. Batch 2's CVErr row is left out, since HexIDE has no CVErr, and
  batch 4's UDT rows wait for #178.
- **Divergences kept:**
  - a ByRef parameter named as the value is 13 here, where VB6 refuses to compile, as #986 recorded;
  - a UDT's array field is refused as the module loads (#178).

