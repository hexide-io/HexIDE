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
  which was read through a Function As Long. `Len` of the Integer and `String * 4` Type is 6, an Integer. The
  "hello" rows' own type is measured in the next section: an Integer.
- A Type of `b(39999) As Byte` gives 40000, a **Long**.
- **The line is at 32,767:** `b(32766) As Byte` gives 32767, an Integer, and `b(32767) As Byte` gives 32768, a Long. A
  single `s As String * 20000` field gives 20000, an Integer. Two nested Types, each holding `v(1049) As Variant`,
  give 33600, a Long.
- A Type with one `s As String * 40000` field doesn't compile at module level: "Fixed or static data can't be larger
  than 64K". That 40,000 characters take 80,000 bytes in memory is reasoned, not checked.

**Readings:**
- **`Len` of a Type is the sum of its fields' sizes in memory, with no padding.**
  - A String, an object reference and a dynamic array each count 4, whatever they hold.
  - A Variant counts 16.
  - A fixed-length String counts its width, and a fixed array its elements.
  - A nested Type counts its own `Len`.
  - `LenB` pads (`a-nested-user-defined-type-is-a-record-of-its-own.md`); `Len` doesn't.
- **It never depends on what the fields hold, and its type follows its size:** an Integer up to 32,767, and a Long
  from 32,768, as a literal of that value would be.
- `fixed-length-strings.md` found `LenB(fx)` an Integer where `Len(fx)` is a Long. A length fixed when the program is
  compiled would explain both. That's reasoned, not checked.

**Not measured:** a Type in a form or a UserControl.

## More `Len` fields, and `VBA.Len` of a Type (2026-11-19, #4924)

**Why a second pass.** Reviewing #4924's change raised two questions the section above can't answer:
- **The dynamic-array rows can't separate two rules.** `x() As Long` and `x() As String` both give 4. So does "a
  dynamic array counts 4", and so does "a dynamic array counts its element's size", since a Long and a String
  reference are both 4 bytes.
- **The change counted a Variant holding an array as an array.** No row stored an array in a Variant field.

**The probe.** VB6 SP6, `/make`, native code and p-code, which logged the same lines. Each Type was declared at module
level. The rows that store a value did so in a Function As Variant that returned `Len(u)`, so its `TypeName` is the
type `Len` gave. The class rows were separate programs, with a class module `Class1` holding `Public A As Long`.

| Fields | `Len` |
|---|---|
| `x() As Byte`; `x() As Double`; `x() As Variant`; `x() As` a Type of a Long and a Byte, never allocated | 4; 4; 4; 4 |
| `x() As Byte` after `ReDim u.x(9)`; `x() As Double` and `x() As Variant` after `ReDim u.x(2)` | 4; 4; 4 |
| a Variant holding `Array(1, 2)`; a Variant `Set` to a `New Collection` | 16; 16 |
| an Enum field; `x(2)` of the Enum | 4; 12 |
| `x(1) As Boolean`; `x(1) As Object`; `x(1) As Collection` | 4; 8; 8 |
| `x As New Class1`, then an Integer | 6 |
| `x(1) As Class1`; `x() As Class1` | 8; 4 |
| a String holding "hello" | 4 |
| an Integer, then a String holding "hello" | 6 |

Every row is an **Integer**, the two "hello" rows included. Those were read through a Function As Long the first
time, so their type is measured only here.

**`VBA.Len(u)`**, on a module's `Private Type` of one Integer, doesn't compile, native or p-code: "Only user-defined
types defined in public object modules can be coerced to or from a variant or passed to late-bound functions". The
unqualified `Len(u)` gives 2.

**Readings:**
- **A dynamic array counts 4 whatever its element type,** a Byte of 1, a Double of 8, a Variant of 16 or a Type, and
  however many elements it holds. So it counts the reference, not an element.
- **A Variant field counts 16 whatever it holds,** an array or an object included.
- An Enum counts as a Long, an `As New` object as any object reference, and a fixed array of objects 4 per element.
- `VBA.Len` is an ordinary function taking a Variant, so a Type can't be passed to it. That's reasoned from the
  message, not checked.
