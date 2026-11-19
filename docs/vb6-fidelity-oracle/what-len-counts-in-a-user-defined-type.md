## What `Len` counts in a user-defined Type (2026-11-19, #179)

**The question.** Measuring #179's arrays turned up a `Len` question. HexIDE, at `9799137d2`, answered 32 for `Len` of
every Type with no fixed-length String field, whatever the Type held. Earlier pages give only a few Types' lengths:
- 6 for an Integer and a `String * 4` (`fixed-length-strings.md`);
- 6 and 4 for two nested Types (`a-nested-user-defined-type-is-a-record-of-its-own.md`).

So what does `Len` count for each kind of field, does it depend on what the fields hold, and what type is the answer?

**The probe.**
- **Build:** VB6 SP6, `/make`, native code and p-code, which logged the same lines. The class rows were a second
  program, with a class module `Class1`.
- **What was read:** `Len(u)` and its `TypeName`. Each Type was declared at module level, with a variable `u` of it.
- **The lengths:** most rows are one program, with every Type in it and none of them assigned. The rows that store a
  value say so.

| Fields | `Len` |
|---|---|
| Byte | 1 |
| Integer; Boolean | 2; 2 |
| Long; Single | 4; 4 |
| Double; Currency; Date | 8; 8; 8 |
| String, empty or holding "hello" | 4 |
| Variant, empty or holding 5 or "hello" | 16 |
| Object; Collection | 4; 4 |
| a project class, then an Integer | 6 |
| a dynamic array, `x() As Long` or `x() As String`, allocated or not | 4 |
| `x(2) As Integer`; `x(2) As String`; `x(1) As Variant` | 6; 12; 32 |
| `x(1, 2) As Byte`; `x(1 To 2) As String * 3` | 6; 6 |
| a nested Type of a Long and a Byte; an array `x(1)` of that Type | 5; 10 |
| Byte, Long, Byte; Double, Byte | 6; 9 |
| Integer, String | 6 |
| Integer, `String * 3`, String | 9, with `LenB` 12 |
| `String * 3`, Variant | 19 |
| `String * 3`, a dynamic Long array | 7 |
| a nested Type holding a `String * 3`, then a String | 7 |
| Integer, `String * 4` | 6 |
| `Len(arr(1))`, an element of `arr(2)`, an array of the Long Type | 4 |
| a `Private Type` in a class module: Integer, Variant, `String * 3`, read by a method of the class | 21 |

**Its type:**
- Every row above is an **Integer**, read straight into a Variant. The exception is the String holding "hello",
  which was read through a Function As Long. `Len` of the Integer and `String * 4` Type is 6, an Integer.
- A Type of `b(39999) As Byte` gives 40000, a **Long**.
- A Type with one `s As String * 40000` field doesn't compile at module level: "Fixed or static data can't be larger
  than 64K". That 40,000 characters take 80,000 bytes in memory is reasoned, not checked.

**Readings:**
- **`Len` of a Type is the sum of its fields' sizes in memory, with no padding.**
  - A String, an object reference and a dynamic array each count 4, whatever they hold.
  - A Variant counts 16.
  - A fixed-length String counts its width, and a fixed array its elements.
  - A nested Type counts its own `Len`.
  - `LenB` pads (`a-nested-user-defined-type-is-a-record-of-its-own.md`); `Len` doesn't.
- **It never depends on what the fields hold, and its type follows its size:** 6 is an Integer and 40,000 a Long,
  as a literal of that value would be. Where the line falls between them wasn't measured.
- `fixed-length-strings.md` found `LenB(fx)` an Integer where `Len(fx)` is a Long. A length fixed when the program is
  compiled would explain both. That's reasoned, not checked.

**Not measured:** the size between 32,767 and 40,000 where the type changes.
