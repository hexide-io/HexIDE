## ObjPtr, StrPtr and VarPtr, by their relations (2026-10-28, #2622)

**The question.** HexIDE has none of the three. A program uses them for identity rather than memory: an object's `ObjPtr`
as a Collection key, `StrPtr(s) = 0` to tell `vbNullString` from `""`, `VarPtr` to see that two names are one variable.
Each value is an address, so what can be measured is the relations between values, not the values.

**The probe.** A Standard EXE `Sub Main` logging each relation as a Boolean or a difference. The two `Collection`s are
`New`, `o` is an `Object`, and the arrays are `(3)` of each type. A second project passes a `Long` and a `String` to a Sub
`ByRef` and `ByVal` and compares inside it. The literal arguments are each a project of their own, since a compile error
stops the build.

| Probe line | vb6.exe |
|---|---|
| objptr Nothing | 0 |
| objptr unset Object | 0 |
| objptr object nonzero | True |
| objptr same object, two variables | True |
| objptr two objects differ | True |
| objptr type | Long |
| objptr after Set Nothing | 0 |
| strptr vbNullString | 0 |
| strptr unassigned String | 0 |
| strptr empty string nonzero | True |
| strptr copy differs | True |
| strptr same variable twice | True |
| strptr type | Long |
| varptr nonzero | True |
| varptr same variable twice | True |
| varptr two variables differ | True |
| varptr Long array step | 4 |
| varptr Integer array step | 2 |
| varptr Currency array step | 8 |
| varptr Variant array step | 16 |
| varptr type | Long |
| varptr of a String variable is not its StrPtr | True |
| varptr ByRef param is the caller's | True |
| varptr ByVal param is not | True |
| strptr ByRef String param is the caller's | True |
| strptr ByVal String param is not | True |
| `ObjPtr(5)` | native: compile error "Type mismatch"; p-code: compile error "Type mismatch" |
| `ObjPtr("a")` | native: compile error "Type mismatch"; p-code: compile error "Type mismatch" |
| `StrPtr(5)` | native: [6363804] Long/3 err 0; p-code: [5511596] Long/3 err 0 |
| `VarPtr(5)` | native: [1703144] Long/3 err 0; p-code: [1703066] Long/3 err 0 |

So `ObjPtr` is 0 for Nothing and the same for one object through two variables; `StrPtr` is 0 for a String never
assigned and for `vbNullString`, non-zero for `""`, and different after an assignment copies the string; `VarPtr` names
the variable, so a `ByRef` parameter has its caller's and a `ByVal` one doesn't, and an array's elements step by their
size. All three are Longs. `ObjPtr` of a non-object is a compile-time Type mismatch, while `StrPtr` and `VarPtr` of a
literal compile and point at a temporary, at an address that differs between builds.
