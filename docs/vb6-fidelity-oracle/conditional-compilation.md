## Conditional compilation (2026-10-23, #1112)

Five `/make` batches, about 160 projects, every one built native and p-code. **The two agreed on every row.** A row that
printed is a built EXE's own log, on SP6's MSVBVM60. A compile error came from the oracle's release-build compiler (#1524),
and is quoted as `/make` wrote it. Every `#If` row was the shape below, so a printed `true` or `false` is which branch was
compiled:

```vb
#If <condition> Then
    L "<condition>: true"
#Else
    L "<condition>: false"
#End If
```

A shape that might not compile was a project of its own, since one compile error stops the build.

### What is a directive

| line | vb6.exe |
|---|---|
| `#If 1 Then`, `#ElseIf 1 Then`, `#Else`, `#End If` | directives |
| `#EndIf`, one word | a directive |
| indented, as `    #If 1 Then` | a directive |
| `#If 1 Then ' a comment` | a directive |
| `#If 1 _` then `And 1 Then` on the next line | one directive, continued |
| `#Const LC = _` then `1` on the next line | one directive, continued |
| `#Const W = 1 ' a comment` | a directive |
| `#If 1` with no `Then`, then `#End If` | not a directive: "An # ElseIf, # Else, or # EndIf must be preceded by an # If clause", at the `#End If` |
| `#If 1 Then L "x"`, and `#If 1 Then: L "x"` | not a directive: the same error, at the `#End If` |
| `10 #If 1 Then`, behind a line label | not a directive: the same error |
| `#ElseIf 1` with no `Then`, inside `#If 0` | not a directive: skipped as inactive text, so its branch never runs |
| `#Else whatever`, and `#Else If 1 Then`, inside an inactive branch | not a directive: skipped |
| `#Else If 0 Then` after an active branch | not a directive, so it is code: "Syntax error" |
| `#If (( Then` | not a directive: the stray-`#End If` error |
| `#If (( Then` nested inside `#If 0` | not a directive: its `#End If` closes the outer `#If`, and the outer's is then stray |
| `#ElseIf (( Then` after an active branch | not a directive, so it is code: "Syntax error" |
| `#Const NV`, no `=` | "Expected: =", as code |
| `#Const Z = ((` inside `#If 0` | builds: not a directive, and inactive |
| `' a comment _`, then `#If 0 Then` | the comment goes on to the next line, so the `#If` is part of it, and the code after runs |

So a line is a directive only when it is well formed: `#If e Then`, `#ElseIf e Then`, `#Else`, `#End If` or `#EndIf`, or
`#Const name = e`, indented or not, with nothing after but a comment. Any other `#` line is code, a syntax error where it is
active, and skipped where it is not.

### Inactive code is never parsed

| program | vb6.exe |
|---|---|
| `this is not valid vb ((` inside `#If 0`, at module level or in a procedure | builds |
| `#If VBA7 Then` / `Private Declare PtrSafe Function ...` / `#Else` / the same without `PtrSafe` | builds |
| an inactive `Declare`, `Dim` or garbage after the last `End Sub` | builds: no "Only comments may appear after End Sub" |
| `#If 1` / `Private Sub F(ByVal x As Integer)` / `#Else` / `Private Sub F()` / `#End If`, then one body | builds and runs the first |
| a whole procedure, `Both`, defined in each branch | the active one runs |
| `Private Const KX = 1` and `= 2` in the two branches | `KX` is 1 |

### Every directive is evaluated, active or not

The guess was that a nested `#If` inside an inactive branch is skipped with the rest. **It is not.**

| program | vb6.exe |
|---|---|
| `#If 0` / `#If Chr(65) = "A" Then` / `#End If` / `#End If` | "Variable not defined", at the nested `#If` |
| `#If 0` / `#If 1` / `#ElseIf 1 / 0 Then` / ... | "Division by zero" |
| `#If 1` / ... / `#ElseIf 1 / 0 Then`, after the taken branch | "Division by zero" |
| `#If 1` / ... / `#ElseIf Chr(65) = "A" Then`, after the taken branch | "Variable not defined" |
| `#If 0` / `#If 1 Then` / `#End If`, with the outer never closed | "You must terminate the # If block with an # EndIf" |

### `#Const` takes no notice of `#If`

The guess was that a `#Const` in an inactive branch defines nothing. **It defines its constant all the same.**

| program | vb6.exe |
|---|---|
| `#If 0` / `#Const Z = 1` / `#End If`, then `#If Z` | true |
| `#Const Y = 1`, then `#If 0` / `#Const Y = 2` / `#End If` | "Duplicate definition" |
| `#If 0` / `#Const Z = Chr(65)` / `#End If` | "Variable not defined" |
| `#Const A = 1` then `#Const A = 2` | "Duplicate definition" |
| `#Const Q = 1` inside `Sub Main`, then `#Const Q = 2` at module level | "Duplicate definition" |
| `#If LATE` before `#Const LATE = 1`, and after | false, then true: a `#Const` holds from its line on |
| `#Const P = 1` inside `Sub Main`, read in `Sub Other` below it | true: across procedures |
| Module1's `#Const A = 1`, read in Module2 | false: the module's alone |
| `#Const R = R + 1` | `R` is 1: the right side reads `R` before it is defined |
| `#Const ARG = ARG + 1`, with `CondComp="ARG = 5"` | `ARG` is 6 |
| `#Const Win32 = 0` | `Win32` false: a built-in may be shadowed |
| `#Const ARG = 7`, with `CondComp="ARG = 5"` | `ARG = 7` true, `ARG = 5` false |
| Module2's `#Const Q = 9`, with `CondComp="Q = 4"` | 4 in Module1, 9 in Module2 |
| `#Const SC = "a" & 1` | `SC = "a1"` true |
| `#Const K = v`, with `Dim v As Integer` | `K = 0` true: a variable is an undefined name to a directive |
| `#Const BIG = 32767 * 2` | "Overflow" |

### What a name is

| condition | vb6.exe |
|---|---|
| `Foo`, `Not Foo`, `Foo = 0` | false, true, true |
| `CStr(Foo) = "0"`, `CStr(Foo) = ""` | true, false: an undefined name is 0, not Empty |
| `Abs(Foo) = 0`, `Foo + 1 = 1`, `Foo & "x" = "0x"` | true, true, true |
| `Len(Foo) = 0` | false |
| `Empty`, `Empty = 0`, `Empty = ""`, `CStr(Empty) = ""` | false, true, true, true: `Empty` is Empty |
| `a = 1`, with `#Const A = 1` | true: names in any case |
| `Const KK = 3`, then `#If KK = 3` | false: an ordinary `Const` is undefined to a directive |
| `Win16`, `Win32`, `Not Win32`, `Not Win16` | false, true, **true**, true: `Not` is bitwise, so `Not 1` is -2 |
| `Win32 = 1`, `Win32 = -1`, `Win32 = True` | true, false, false |
| `VBA6`, `VBA7`, `Win64`, `Mac`, `Not Win64` | false, false, false, false, true: not defined under VB6 |
| `CBool(Win32)`, `Not CBool(Win16)` | true, true: a constant through `CBool` first gives `Not` a Boolean to work on |

### The project's arguments

`CondComp` is the `.vbp` line Project Properties' Make tab writes.

| `CondComp` | vb6.exe |
|---|---|
| `ARG = 5 : FLAG = 1` | `ARG = 5`, `FLAG`, and `ARG = "5"` true; Module2 reads `ARG` too |
| `A=1:B=2` | builds: the spaces are optional |
| `X = +1` | `X = 1` |
| `x = 3` | `X` is 3: names in any case |
| `NEG = -1`, `M = 32767`, `N = -32768`, `ZERO = 0` | as written |
| `Win32 = 0` | `Win32` false: an argument may shadow a built-in |
| `VBA7 = 1` | `VBA7` true |
| `""`, empty | no arguments |
| `BIG = 70000`, `X = 32768`, `X = -32769` | refused |
| `HEX = &H10`, `T = True`, `DD = 1.5`, `SARG = ""x""` | refused |
| `X = 1 : X = 2`, `X =`, `X = 1; Y = 2`, `X = 1 :` | refused |

A refusal is "Invalid syntax for constant declarations", then "One or more of the properties in 'probe.vbp' was bad. Some or
all of the properties might not be set correctly.", and `/make` built nothing.

**IDE, release build** (#1524). The same project, `CondComp="ARG = 70000"`, opened in the oracle's IDE in the guest's console
session, beside a control with `CondComp="ARG = 5"`. The module logged `ARG` and then `ran`:

| run | VB6's IDE |
|---|---|
| the control, `VB6.EXE /runexit probe.vbp` | ran: `ARG: true`, `ran`, and VB6 closed |
| `VB6.EXE /runexit probe.vbp` | opened the project, titled "Probe - Microsoft Visual Basic [design]", behind a message box: "Invalid syntax for constant declarations", with OK and Help |
| ... OK | a second: "One or more of the properties in 'C:\...\probe.vbp' was bad. Some or all of the properties might not be set correctly." |
| ... OK | VB6 closed, and the program never ran: no log at all |
| `VB6.EXE probe.vbp`, both boxes dismissed, then F5 sent | no log either, and the IDE stayed in design. That F5 reached the IDE is not established, so this row decides nothing |

So the IDE opens the project and says so twice, and `/runexit` then runs nothing: what HexIDE does, refusing to run with the
first message, is the measured `/runexit` answer. Whether F5 afterwards runs with the arguments dropped is still open.

### Values and operators

Constants, an argument or a built-in or a `#Const`, are Variants. Literals are typed, as in running code.

| condition | vb6.exe |
|---|---|
| `1 + 1 = 2`, `5 \ 2 = 2`, `7 Mod 3 = 1`, `2 ^ 3 = 8`, `7 / 2 = 3.5` | true |
| `-1`, `0`, `0.4` | true, false, true: any number but 0 holds |
| `1 And 2`, `1 Or 2`, `3 Xor 3`, `1 Eqv 1`, `0 Imp 0` | false, true, false, true, true: bitwise |
| `Not 0`, `Not 1`, `Not -1` | true, true, false |
| `True`, `False`, `True = -1`, `True + True = -2` | true, false, true, true |
| `-2 ^ 2 = 4` | **false**: `^` binds tighter than the minus, so it is -4 |
| `7 \ 2 * 2 = 1`, `2 + 3 * 4 = 14`, `10 - 2 - 3 = 5` | true |
| `2.5 \ 1 = 2`, `3.5 \ 1 = 4`, `-7 Mod 3 = -1` | true: banker's rounding |
| `Not 1 = 2`, `(Not 1) = -2`, `1 < 2 = True`, `1 = 1 = True` | true: `Not` binds below the comparisons |
| `&H10 = 16`, `1.5 = 1.5`, `#1/2/2000# > #1/1/2000#`, `#1/1/2000# < 40000` | true |
| `"1" + 1 = 2`, `"1" & 1 = "11"` | true |
| `32767 + 1 > 0`, `2147483647 + 1 > 0` | "Overflow": typed |
| `M + 1 = 32768`, `M * 2 = 65534`, with `#Const M = 32767` | true: a Variant widens |
| `ML + 1 = 2147483648#`, with `#Const ML = 2147483647` | true |
| `1 / 0` | "Division by zero" |
| `1 Is 1` | "Type mismatch" |

### Comparison

Strings compare as text, whatever `Option Compare` says, `Like` included:

| condition | vb6.exe |
|---|---|
| `"a" = "A"`, `"B" > "a"`, `"a" < "B"`, `"10" < "9"` | true |
| `"Z" < "a"`, under Binary and under `Option Compare Text` | false |
| `"ABC" Like "a*"`, `"abc" Like "a*"` | true |
| `SA = "A"`, `SA < "B"`, with `#Const SA = "a"` | true |

A String against a number depends on which side is the Variant. With `#Const N5 = 5`, `#Const S5 = "5"`, `#Const SA = "a"`
and `#Const S10 = "10"`:

| condition | vb6.exe | so |
|---|---|---|
| `5 < "10"`, `"5" = 5`, `1 = "1.0"`, `1 = " 1"` | true | typed against typed: the String read as a number |
| `0 = ""`, `0 = "a"`, `0 < "a"`, `"abc" > 5` | "Type mismatch" | ... and Type mismatch where it is not one |
| `#1/1/2000# = "1/1/2000"` | true | ... or as a date beside a Date |
| `N5 < "10"`, `N5 = "5.0"` | false | a Variant number against a typed String: as text |
| `Foo = ""`, `Foo < ""`, `Foo > ""` | false, false, true | ... "0" against "" |
| `Win32 = "1"`, `ARG = "5"` | true | ... |
| `S5 = 5`, `S5 < 10`, `S10 > 9`, `S5 = 5.0` | true | a Variant String against a typed number: as numbers where it reads as one |
| `SA = 5`, `5 < SA`, `SA > 5` | false, true, true | ... and the number first where it does not, with no error |
| `N5 = S5`, `N5 < S5`, `N5 > SA` | false, true, false | Variant against Variant: the number first, whatever the String |
| `N5 + S5 = 10`, `N5 & S5 = "55"`, `S5 + 1 = 6`, `Not S5 = -6` | true | arithmetic reads the String as a number |
| `SA + 1` | "Type mismatch" | |

That is the language reference's rule except in one place: it has a typed number against a Variant String that does not
read as a number be Type mismatch, and here it is the number first, with no error.

### Functions

| called | vb6.exe |
|---|---|
| `Int(2.5) = 2`, `Fix(-2.5) = -2`, `Abs(-1) = 1`, `Sgn(-5) = -1`, `Int(Win32) = 1` | true |
| `Len("abc") = 3`, `LenB("ab") = 4` | true |
| `CBool`, `CByte(200)`, `CCur(1.5)`, `CDate(1) = #12/31/1899#`, `CDbl`, `CInt("5")`, `CLng(70000)`, `CSng(1.5)`, `CStr(5) = "5"`, `CVar(5)` | each true |
| `CInt(2.5) = 2` | true: banker's rounding |
| `Len(5)` | "Variable required - can't assign to this expression", as in code |
| `CVErr(5) = 5` | "Type mismatch": CVErr is accepted, and an Error is no number |
| `CDec`, `Asc`, `Chr`, `Left`, `UCase`, `Val`, `IIf`, `Hex`, `Str`, `Sqr`, `Round`, `VarType`, `TypeName`, `Year(Now)` | "Variable not defined" |
| a user's `Function MyF()` | "Variable not defined" |

So a directive may call `Int`, `Fix`, `Abs`, `Sgn`, `Len`, `LenB`, `CBool`, `CByte`, `CCur`, `CDate`, `CDbl`, `CInt`, `CLng`,
`CSng`, `CStr`, `CVar` and `CVErr`, and nothing else. `CDec` is the one conversion missing.

### The condition

| condition | vb6.exe |
|---|---|
| `"1"`, `"True"`, `#1/1/2000#` | true |
| `"False"` | false |
| `""`, `"abc"` | "Type mismatch" |
| `Null` | "Invalid use of Null" |
| `Nothing` | "Invalid use of object" |

### Structure

| program | vb6.exe |
|---|---|
| `#End If` with no `#If` | "An # ElseIf, # Else, or # EndIf must be preceded by an # If clause" |
| `#Else` then `#ElseIf 1 Then` | "An # ElseIf must be preceded by an # If or # ElseIf and followed by an # Else or # EndIf" |
| `#Else` twice | "An # Else clause must be preceded by an # If" |
| an `#If` never closed | "You must terminate the # If block with an # EndIf", reported at the module's last line |
| nesting, and `#ElseIf` after a false `#If` | the first branch that holds, and only that one |

`/make` numbered each compile error from 0, counting from the line after `Attribute VB_Name`, so its number is two less
than the file's own line number; every error row fits that. A directive error came before any syntax error elsewhere in the
module, so the directives are processed first.

### In HexIDE (#1112)

- **`ConditionalCompiler.Preprocess`** applies a module's conditional compilation to its text before the parse. Every
  directive line, and every line of an inactive branch, is made empty, so the lines keep their numbers and an inactive branch
  is never parsed. The text is the parser's alone: the code window shows the module as written, and a save writes it
  byte for byte (`ConditionalCompilerTests`). It also gives the inactive lines, for the code window to grey (#1113).
- **A directive's error is a compile error as the module loads**, before anything runs, naming the module and the line. The
  unterminated `#If` is reported at its own line rather than the module's last.
- **The expression** is read by the interpreter's own parser and evaluated by `ConstantExpression`, with the interpreter's
  own operators, made static to share them: the overflow, the widening and the rounding are the runtime's. Comparison and
  the function list are its own, as above. The text comparison was HexIDE's approximation, `OrdinalIgnoreCase`, as for a
  Collection's keys (#1479), until #1598 made it vb6.exe's measured order.
- **`CompilationArguments`** reads the `.vbp`'s `CondComp`, which round-trips verbatim, and refuses what vb6.exe refuses,
  with its message, before the program runs.
- **Divergences:**
  - every personality takes VB6's built-ins. VBA documents `VBA6`, `VBA7` and `Win32` as 1, and `Win64` as 1 in a 64-bit
    host, unmeasured here, but `VBA7 = 1` would select declarations HexIDE cannot parse (#1548). That waits for #1113's
    "view as" work;
  - `Like` was refused, not implemented, until HexIDE had `Like` (#1547). It runs since #1598;
  - `CVErr` is refused, not implemented: HexIDE has no `CVErr`;
  - `-2 ^ 2` is 4 here, where it is -4, because the lexer reads the minus as part of the number, here as everywhere (#1073);
  - a `#` line that is not a directive fails in HexIDE's parser, with its own message, where vb6.exe's is "Syntax error" or
    "Expected: =".

