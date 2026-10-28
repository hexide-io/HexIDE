## A Function that returns an array (2026-10-13, #975)

**The question.** A Function or Property Get declared `As Long()`: what does it return? HexIDE read the declaration as
its element type, so the array the body stored was narrowed into a Long on the way out: `x = RetFixed()` gave
`TypeName` "Long", 0, with no error, and `d = RetFixed()` left a `Dim d() As Long` a scalar.

**The probe.** One Sub Main project, built as native code and as p-code, the two agreeing on every row. `modArr` is a
module-level `Dim modArr(3) As Long` with `modArr(1) = 7`, and `RetFixed = modArr`:

| Program | VB6 |
|---|---|
| `x = RetFixed()` | `TypeName` "Long()", `UBound` 3, `x(1)` 7, no error |
| `TypeName(RetFixed())` | "Long()" |
| `d = RetFixed()`, `d` a `Dim d() As Long` | `UBound` 3, `d(1)` 7 |
| `Function RetEmpty() As Long()`, which never assigns its name: `TypeName(RetEmpty())` | "Long()" |
| `x = RetEmpty()`, then `UBound(x)` | 9 |
| `Function RetStr() As String()` returning `Split("a,b,c", ",")` | "String()", `UBound` 2, `x(2)` "c" |
| `Function RetVar() As Variant()` returning a `Variant` array holding "x" and 2 | "Variant()", "x", and `TypeName(x(1))` "Integer" |
| a standard module's `Property Get PG() As Long()`, `x = PG` | "Long()", `x(1)` 7 |
| a class's `Public Property Get Items() As Long()`, `x = o.Items` (a second probe) | "Long()", `UBound` 3, `x(1)` 7 |

**HexIDE, after #975.** The pre-pass reads a Function's or Property Get's `As T()` as the array type, so the return slot
starts as an empty array of it and the array the body stores comes back as it is. Every row is pinned except the
standard module's Property Get, which HexIDE does not reach by name at all: a bare `PG`, or a scalar `PS`, reads Empty
with no error, and `PG()` is "Sub or Function not defined" (#977, found here). Whether the caller gets a copy of the
array is #970's.

### What a typed-array return accepts (2026-10-14, #986)

**The question.** #981 made the return slot an array, and then accepted whatever the body stored in it: `F = Array(1, 2)`
in a Function `As Integer()` returned a Variant array with no error, and `F = 5` in one `As Long()` returned an
Integer. The review of #981 measured those two; this measures the rest.

**The probe.** A runtime project, each Function called as `x = F()` under `On Error Resume Next` in its caller, writing
`Err.Number`, `TypeName(x)` and its bounds. Each case that might not compile is a project of its own, so one refusal
cannot hide another. A third project repeats two rows through a class's Property Get. All built as native code and as
p-code, the two agreeing on every row.

| Body of a Function `As Integer()`, unless shown | VB6 |
|---|---|
| `F = ia`, `Dim ia(1 To 3) As Integer` | Err 0; `x` is Integer() 1 To 3: the bounds are kept |
| `F = v`, `v` a Variant holding that Integer(1 To 3) | Err 0; Integer() 1 To 3 |
| `F = d`, `Dim d() As Integer` never allocated | Err 0; Integer(), its bounds raising 9 |
| `F = Array(1, 2)`; `F = v` with `v` holding `Array(1, 2)` | 13, and `x` stays Empty |
| `F = v`, `v` holding a Long(0 To 1); holding 5; Empty | 13, and `x` stays Empty, each |
| `F = Split("1,2", ",")` | 13 |
| `As Long()`, `F = Array(1, 2)` | 13 |
| `As Variant()`, `F = Array(1, 2)` | Err 0; Variant() 0 To 1 |
| `As Variant()`, `F = v`, `v` holding an Integer(0 To 1) | 13 |
| `As String()`, `F = Split("a,b", ",")` | Err 0; String() 0 To 1 |
| `As Long()`, `F = 5` | does not compile: "Can't assign to array" |
| `F = la`, `Dim la(1) As Long` | does not compile: "Can't assign to array" |
| `F = va`, `Dim va(1) As Variant` | does not compile: "Can't assign to array" |
| `As Variant()`, `F = ia`, `Dim ia(1) As Integer` | does not compile: "Can't assign to array" |
| a class's `Property Get PG() As Integer()`, `PG = Array(1, 2)`, read as `o.PG` | 13 |
| the same class's `PB`, storing an Integer(1 To 3) | Err 0; Integer() 1 To 3 |
| `Dim d() As Integer`, `d = F()` with `F` storing an Integer(1 To 3) | Err 0; 1 To 3 |

**What it says.** A typed-array return takes an array of exactly its element type and nothing else, keeping the bounds,
from a fixed array as readily as a dynamic one. A mismatch VB6 can see while compiling, a scalar or an array variable
declared with another element type, is the compile error "Can't assign to array". A mismatch it can only find at run
time is 13, raised at the store, so the caller's target keeps what it had: through a Variant, and through Array and
Split, which are declared to return a Variant even when what they return is a typed array.

**Measured in #997's review.** A project for each case, native and p-code agreeing on every row:

| Body of a Function `As Integer()`, unless shown | VB6 |
|---|---|
| `As Byte()`: `F = "ab"`; `F = s`, `s As String` holding "ab"; `F = v`, `v` a Variant holding "ab" | Err 0; Byte() 0 To 3, holding 97 0 98 0, each |
| `As Byte()`, `F = ""` | Err 0; Byte() 0 To -1 |
| `As Byte()`, `F = StrConv("ab", vbFromUnicode)` | Err 0; Byte() 0 To 1, holding 97 98 |
| `As Byte()`, `F = v`, `v` holding 5 | 13 |
| `F = w(0)`, `Dim w(1)` with `w(0) = 5`; `F = t.f`, a Variant field of a Type holding 5 | 13, each |
| `F = Empty`; `F = Null` | 13, each |
| `Function F(ByVal K)`, called `F(5)`, `F = K`, with a module `Const K = 5` the parameter hides | 13 |
| `F = "ab"`; `F = -5`; `F = (5)`; `F = True`; `As Byte()`, `F = 5`; `As Variant()`, `F = 5` | does not compile: "Can't assign to array" |
| `F = n`, `n As Integer`; `F = s`, `s As String`; `As Variant()`, `F = n` | does not compile: "Can't assign to array" |
| `F = K`, with `Const K = 5`, or `Const K As Integer = 5`, in the Function; or `Const K = 5` in the module | does not compile: "Can't assign to array" |
| `F = G()`, `G` declared `As Integer` | does not compile: "Can't assign to array" |
| `Function F(p)`, `F = p`, called `F(k)` with `k As Integer`; called `F(la)` with `Dim la(1) As Long` | 13, each |
| `Function F(p As Integer)`, `F = p`; `Function F(p() As Long)`, `F = p`, given `la` | does not compile: "Can't assign to array" |
| `As Object()`, `F = Array(1, 2)`; `As Collection()`, `F = Array(1, 2)` | 13, each |
| `As Object()`, `F = v`, `v` holding a `Dim oa(1) As Object` | Err 0; Object() 0 To 1 |

The same project measured the variable path, with `Dim b() As Byte`: `b = "ab"`, `b = s` and `b = v` each give
Byte() 0 To 3 holding 97 0 98 0, and `b = ""` gives Byte() 0 To -1. With `Dim a() As Integer`, `a = Array(1, 2)` is 13 and
`a` stays unallocated.

**What it adds.** A String into a Byte() is the one conversion: it takes the String's bytes, two per character, and a
Byte() variable takes them the same way. Otherwise the line between the compile error and 13 is what the statement
shows. A literal, signed or in parentheses, a variable declared with a scalar type, a Const and a typed Function's call
are all refused while compiling. A Variant holding a literal is 13, whether it's a variable, an array element or a
field, and so are Empty and Null. A parameter is what it was declared as: an untyped one is a Variant, whatever its caller
passed it, and one that hides a module Const is the parameter.

**HexIDE, after #986 and its review.** A store into the name of a Function or Property Get declared `As T()`, for a
scalar `T` or Variant, checks the value's type against it:
- **An array variable declared with another element type**, or a scalar whose type the statement shows, is "Can't
  assign to array", raised as the statement runs. That covers a literal, signed or parenthesised; a bare name for a
  variable declared with a scalar type; and a Const, local or the module's, unless a local or a parameter hides it. Only
  the procedure's own declarations and the module's count: a ByRef parameter's name reads its caller's slot, whose
  marks are the caller's declaration, so every parameter falls to 13.
- **Anything else** is 13. The decision is read from the statement, not from the value's fixed flag, which a Variant
  element or field can carry from the literal stored into it (#987's "What main does").
- **A String into a Byte()** stopped the run with a NotImplementedException naming #998, since neither this path nor
  a Byte() variable converted yet, and raising an error VB6 doesn't would be worse. Both convert since #998.

Every row is pinned, the typed parameters as HexIDE's answer, except the cases below. The Property Get rows are pinned through the class, since a standard module's
Property Get is #977's. Not pinned, and known to differ:
- **`F = G()`**, with `G` declared `As Integer`, is 13 here where VB6 refuses to compile it, because the statement
  executor doesn't read a callee's declared type. Wrong only on a program VB6 does not compile.
- **A typed parameter**, `p As Integer` or `p() As Long`, stored into the return is 13 here where VB6 refuses to
  compile it, for the reason above. Pinned as HexIDE's answer, so that changing it is a decision.
- **`StrConv`** isn't implemented, so its row stops the run before the store.

### An array of objects (2026-10-27, #999)

**The question.** #999 measured that `As Object()` and `As Collection()` refuse `Array(1, 2)` with 13 and that an
`As Object()` takes an Object() through a Variant. What does a return of objects accept across element types: an
Object() for a Collection(), a Collection() for an Object(), and an array of one class for another class's?

**The probe.** A project for each case, with classes Class1 and Class2. Each Function is in a standard module and is
called as `x = F()` under `On Error Resume Next`, logging `Err.Number` and `TypeName(x)`. Seven more projects make the
same assignments between dynamic arrays, `b = a`, with no Function. Native and p-code agreed on every row.

| Return type, and the array stored | vb6.exe |
|---|---|
| `As Collection()` storing a Collection(); `As Class1()` storing a Class1() | Err 0; `x` is **"Object()"** |
| `As Collection()` storing an Object() variable | does not compile: "Can't assign to array" |
| `As Object()` storing a Collection() or a Class1() variable | does not compile: "Can't assign to array" |
| `As Class1()` storing an Object() or a Class2() variable | does not compile: "Can't assign to array" |
| `As Object()` storing a Variant() variable | does not compile: "Can't assign to array" |
| Through a Variant, `v = a: F = v`: Class1() into Class1(), Object() into Class1(), Object() into Collection(), Collection() into Object(), Class2() into Class1(), Class1() into Object() | **Err 0** each; "Object()" |
| `As Class1()`, through a Variant, an Object() whose element 0 holds a Collection | Err 0; `TypeName(x(0))` "Collection"; `Set y = x(0)`, `y As Class1`, then raises 13 |
| `b = a`, dynamic arrays: Object() into Collection(), Collection() into Object(), Class1() into Object(), Class2() into Class1(), Object() into Class1() | does not compile: "Can't assign to array" |
| `b = v`, `v` a Variant holding an Object() or a Class2(), into `b() As Class1` | Err 0; "Object()" |

**What it says.**
- **At compile time,** an array of objects goes only to the same element type, as for the scalar element types above.
  Object is not a wildcard here, in either direction.
- **At run time,** through a Variant, any array of objects is accepted by any return or array variable of objects. The
  class is not checked, not even for an element that holds an object of another class. That surfaces only when the
  element is used as the class, as 13 on the `Set`.
- **The 13 that #999 measured** is for an array whose elements are not objects: `Array(1, 2)` is a Variant(). So the
  run-time check needs only "an array of objects", not the class.
- **`TypeName` of a Collection() or a Class1() is "Object()".**

**HexIDE, after #999.** Object and Collection returns use the object array type; a class-array return resolves its
class when called. A named array with another declared object element type is refused as the store executes, for
returns and dynamic array variables alike. Through a Variant the check accepts any object array, keeping the
destination declaration without inspecting its elements. The measurements above are pinned in `ObjectArrayReturnTests`.
