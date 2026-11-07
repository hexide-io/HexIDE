## A For counter written with a type character (2026-10-27, #739)

**The question.** The 2026-10-11 entry "A type character on a name the program also declares" measured `For i% = 1 To 2`
on a declared local Integer only. Does a counter written with each of `% & ! # @` reach the variable of the bare name
when that variable is a module's, another module's Public, or undeclared? And does `Next` take the character too?

**The probe.** One program and seven one-construct projects, each built native and p-code with the guest's vb6.exe and
run there. In the program, each loop is `For c<char> = 1 To 2`. Its body appends the bare `c` to a string, and the
program then logs that string with `TypeName` and `CStr` of the bare `c`. The counters are:
- locals;
- Private variables of Module2, looped in Module2;
- Public variables of Module2, looped in Form1;
- undeclared names in Module3, which has no Option Explicit.

Native and p-code logged the same 46 lines.

**Result: the counter is the variable of the bare name, at every scope.** For each character:

| Counter | `Next` | Body read | After the loop |
|---|---|---|---|
| A local, `Dim li As Integer`: `For li% = 1 To 2` | `Next` | 1,2 | Integer 3 |
| The same for `ll&`, `lg!`, `ld#`, `lc@` | `Next` | 1,2 | Long, Single, Double, Currency 3 |
| A Private module variable, `mi% … mc@` | `Next` | 1,2 | its declared type, 3 |
| A Public variable of another module, `pi% … pc@`, looped in the form | `Next` | 1,2 | its declared type, 3 |
| An undeclared name, `ui% … uc@`, without Option Explicit | `Next` | 1,2 | Integer, Long, Single, Double, Currency 3 |
| Each local and module counter above | `Next` with its character, `Next li%` | 1,2 | the same |
| `For li = 1 To 2` | `Next li%` | 1,2 | Integer 3 |
| `For li% = 1 To 2` | `Next li` | 1,2 | Integer 3 |
| An undeclared `wi%` | `Next wi%` | 1,2 | Integer 3 |
| `For li% = 1 To 2` around `For ll& = 1 To 2` | `Next ll&: Next li%`, and `Next ll&, li%` | 11,12,21,22 | Integer 3, Long 3 |
| `For lg! = 0.5 To 1.5 Step 0.5` | `Next lg!` | 0.5,1,1.5 | Single 2 |

A body that wrote `li% = li% + 1` read 1, 3 and 5 and left the Integer 7, as a bare `li = li + 1` does (#841).

**Refused, each a compile error, native and p-code alike.** Every project ran under Option Explicit:

| Construct | vb6.exe |
|---|---|
| `Dim i As Long`, then `For i% = 1 To 2: Next` | **"Type-declaration character does not match declared data type"** |
| `Dim v`, then `For v% = 1 To 2: Next` | the same: a Variant takes no character |
| `Dim i As Integer`, then `For i = 1 To 2: Next i&` | the same, for the character on `Next` |
| `Dim v As Variant`, then `For v = 1 To 2: Next v%` | the same |
| `Dim s As String`, then `For s$ = 1 To 2: Next` | **"Type mismatch"**: the character matches, and a String is not a counter |
| `For k% = 1 To 2: Next`, nothing declared | **"Variable not defined"** |
| `Dim i As Integer`, then `For i% = 1 To 2: Next j%` | **"Variable not defined"**, for the `j` on `Next` |

**In HexIDE (#739):**
- A counter with a character resolves to the bare name, at any scope. It is checked against the declared type, and
  without Option Explicit an undeclared one is declared with the character's type.
- `Next` with a character is checked in the same way.
- Not measured, and refused: an undeclared `$` counter, and a `Next` with a character naming a variable that does not
  exist, without Option Explicit.
- `Next ll&, li%`, two names on one Next, is not in the interpreter's grammar, with or without characters.
