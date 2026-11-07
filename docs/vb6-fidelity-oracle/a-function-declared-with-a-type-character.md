## A Function declared with a type character (2026-10-27, #740)

**The question.** `Function GetName$()` stopped its whole module loading in HexIDE, and only `$` had been measured on a
declaration (TR_01, TR_02 and TB_01, in "A type character on a name the program also declares"). What do `% & ! # @` do
on a Function's name? Is each the `As` clause it stands for, with that clause's conversions, and how does it meet a call
written with a character, a DefType, an `As` clause beside it, and the same name declared twice?

**The probe.** One program and seven one-construct projects, each built native and p-code with the guest's vb6.exe and
run there. The program's Form1 calls Functions declared in Module2, and one declared in the form itself, and logs
`TypeName` and `CStr` of each result. Native and p-code logged the same lines for every project.

**Result: the character declares the return, as its `As` clause would.** Each Function below assigns its return by the
bare name:

| Declaration and body | Called as | vb6.exe |
|---|---|---|
| `FS$()`: `FS = "s"` | `FS$()`, `FS()`, `FS` | String "s" each time |
| `FI%()`: `FI = 41` | `FI%()`, `FI()`, `FI` | Integer 41 |
| `FL&()`: `FL = 70000` | `FL&()`, `FL()` | Long 70000 |
| `FG!()`: `FG = 1.5` | `FG!()`, `FG()` | Single 1.5 |
| `FD#()`: `FD = 2.25` | `FD#()`, `FD()` | Double 2.25 |
| `FC@()`: `FC = 3.5` | `FC@()`, `FC()` | Currency 3.5 |
| `Private Function FormF$()` in the form | `FormF$()`, `FormF()` | String "form" |
| `NS$() … NC@()`, never assigned | | String "" (Len 0), Integer 0, Long 0, Single 0, Double 0, Currency 0 |

- **A call may carry the character, with or without parentheses.** `FI% + 1` is the Integer 42. `FI + FG` is the Single
  42.5.
- **A Function calls itself through it:** `RI%(n)` with `If n > 0 Then RI = n + RI%(n - 1)` gives the Integer 10 for 4.
- **The return assigned through the character works too:** `BI% = 7` in `Function BI%()` returns the Integer 7, and
  `BS$ = "b"` in `Function BS$()` the String "b".

**What is stored into the return is converted to its type there,** as for a variable of that type. Each Function is
`F = v`, with `v` a Variant argument:

| Call | vb6.exe |
|---|---|
| `AI%(2.5)`, `AI%("12")`, `AL&(1.5)` | Integer 2, Integer 12, Long 2: banker's rounding |
| `AS$(5)`, `AG!(1.25)`, `AC@(1.23456)`, `AD#(#1/2/2001#)` | String "5", Single 1.25, Currency 1.2346, Double 36893 |
| `AI%(40000)`, `AI%("x")`, `AS$(Null)` | **6**, **13** and **94**, raised to the caller's handler |

**The character wins over a DefType.** In a module under `DefInt A-Z`:
- `Function FS$()` returned a String;
- `Function FG!()`, given 2.6, returned the Single 2.6;
- `Function FN()`, with neither, returned the Integer 3, as #169 measured.

**Refused, each a compile error, native and p-code alike:**

| Construct | vb6.exe |
|---|---|
| `Function F%() As Integer`: a character and an `As` clause | **"Expected: end of statement"**, on the declaration |
| `F&()` called, for `Function F%()` | **"Type-declaration character does not match declared data type"**, on the call |
| `F& = 1` in the body of `Function F%()` | the same, on the assignment |
| `Function F$()` and `Function F%()` in one module | **"Ambiguous name detected: F"** |
| `Sub S$()` | **"Expected: Identifier without type suffix"** |

`Dim F%` in another procedure, beside `Function F%()` in the module, compiled, and `F` there read the local's 0.

**In HexIDE (#740):**
- The interpreter's grammar takes the character after a Function's name, as the LSP server's grammar already did.
- The pre-pass takes it as the return type ahead of a DefType, and refuses it beside an `As` clause.
- A call with the character to the program's own Function is checked against the return type, with or without
  parentheses.
- The return assigned through the character, `BI% = 7`, sets it, as vb6.exe does (#739). Until #739 it was refused, and
  before #740 it wrote a variable named `BI%` and left the Function returning 0.
- A `Sub` with a character still fails to parse, where vb6.exe refuses to compile it. Both refuse the program.
