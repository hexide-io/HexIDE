## A ParamArray parameter (2026-10-28, #2462)

**The question.** What does a procedure declared `ParamArray a() As Variant` receive, and what may it do with it? HexIDE
refuses every call to one (#2462).

**The probe.** Sub Main projects, one case to a project, so that a compile error names its own case, built native and
p-code. Each logged `LBound`, `UBound`, `TypeName`, `VarType` and `IsMissing` of the array, then each element's type and
value. Native and p-code logged the same lines in every case.

**What the procedure receives:**

| Call | vb6.exe |
|---|---|
| `Show "none"`, nothing for the ParamArray | `LBound` 0, `UBound` **-1**, `TypeName` "Variant()", `VarType` 8204, `IsMissing(a)` **True** |
| `Show "one", 1` | `IsMissing(a)` False, `UBound` 0, element Integer 1 |
| `Show "mixed", 1, "two", 3.5, Null, Empty, c` (a Collection) | Integer, String, Double, Null, Empty, an object, in order |
| The same with `Option Base 1` | `LBound` still **0** |
| `Count(7)` and `Count(7, 1, 2)` for `Count(x As Long, ParamArray a())` | 0 elements, then 2 |
| `Show "omitted middle", 1, , 3` | allowed: `UBound` 2, and `a(1)` is Missing |
| `Show "omitted last", 1, 2,` | compile error, "Syntax error" |

**Each element is the caller's argument, ByRef:**

| In the procedure | Called as | vb6.exe |
|---|---|---|
| `a(i) = a(i) + 1` for each element | `Bump i, n`, an Integer 1 and a Long 10 | **i 2, n 11** |
| the same | `Bump (i), n` | i unchanged, n 12: the parentheses pass a copy |
| the same | `Bump arr(2)`, an element holding 20 | **arr(2) 21** |
| `a(0) = 5.7` | an Integer variable | no error; the variable is 6, still an Integer |
| `a(0) = "abc"` | an Integer variable; a Double | **13**, and the variable keeps its value |
| `a(0) = "abc"` | a Variant | no error; it becomes the String |

**Using the array:**

| In the procedure | vb6.exe |
|---|---|
| `For Each v In a` | the elements in order |
| `PassOn b` passing it to another ParamArray, or `T a()` | the callee gets **one element, the array**, a `Variant()` |
| `Show b(0), b(1)` | two elements |
| passed `ByVal` to a Variant parameter | an array, `Variant()`, 0 to its last element |
| `copy = a`, then `copy(0) = 99` | `a(0)` unchanged: the Variant holds a copy |
| `a = Array(7)` | **allowed**: `UBound` 0 and `a(0)` 7 afterwards |
| `ReDim a(5)`; `Erase a` | compile error, **"Invalid ParamArray use"** |
| passed to `T(x() As Variant)`, or to a ByRef `T(v As Variant)` | compile error, **"Invalid ParamArray use"**, which `/make` reports at the declaration line of the procedure that passes it |

**What doesn't declare one.** `ByVal ParamArray a()`, `ParamArray a() As Long`, and `Optional x` before a `ParamArray` are
each not valid syntax. `/make` then reports the call to that procedure as "Sub or Function not defined". A named
argument to a ParamArray is "Argument in ParamArray may not be named", measured earlier (`NamedArguments`).
