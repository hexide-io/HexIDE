## What a condition is (2026-10-12, #847)

**The question.** What every statement with a condition makes of a value that is not a Boolean. HexIDE accepted only
a Boolean in a block `If` or `ElseIf`, and raised "IF doesn't contain a bool expression" for anything else, an error
whose Number was 0. A one-line `If` raised 13 for anything but a Boolean, and the loops accepted a Boolean, an Integer,
a Byte, a string that reads as an integer, or Null, and raised 13 for anything else, Empty included. So `If Len(s) Then`, `If InStr(s, "x") Then` and `If flags And 4 Then` stopped the run, and under
`On Error Resume Next` the block form skipped its body with nothing in Err.

**Probe.** A Sub Main project with no `On Error Resume Next`: a function `P(form, v)` traps with `On Error GoTo` and
tests a Variant `v` in one of eight forms. The loops count their passes up to a limit of their own: 2 for a
pre-tested loop, 3 for a post-tested one, so a loop that holds shows its limit. Built as native code and also run in
the IDE; **every line was identical in the two modes**.

**How the IDE runs were made.** `VB6.EXE /runexit probe.vbp` runs a project in the IDE, as F5 does, and closes VB when
the program ends. It was started in the Win10 guest's session 0 over PowerShell Direct, with the project's files
copied into a fresh folder first. The program writes its log with `Print #` to a file beside the project, because
`Debug.Print` output goes to the Immediate window and is gone when VB closes. VB exits with 0xC000041D after the
program finishes, which is normal. A compile error or an unhandled run-time error leaves a modal dialog nobody can
answer in session 0, so a run still going after a timeout is killed and its log is missing or incomplete; every use is
made under `On Error GoTo` for that reason. A `probe.exe` left in the folder means the run was a `/make` build, not an
IDE run. #842 tracks making this, and the native `/make` harness, part of the tree.

| `v` | one-line If | block If | ElseIf | While | Do While | Do Until | Loop While | Loop Until |
|---|---|---|---|---|---|---|---|---|
| 5, 0.5, -1@, `#1/1/2000#`, `CLng(11)` | Then | Then | Then | 2 | 2 | 0 | 3 | 1 |
| `"True"`, `"1"`, `" 5 "`, `"&H10"` | Then | Then | Then | 2 | 2 | 0 | 3 | 1 |
| 0, `CByte(0)`, `"False"`, `"0"` | Else | Else | Else | 0 | 0 | 2 | 1 | 3 |
| **Null, Empty** | Else | Else | Else | 0 | 0 | **2** | 1 | **3** |
| `"abc"`, `""`, `CVErr(5)` | 13 | 13 | 13 | 13 | 13 | 13 | 13 | 13 |

**Typed values and the idioms,** each in a one-line and a block `If`, which agreed on every row:

| Condition | Holds |
|---|---|
| a Long 11, a Double 0.5, a String `"-1"` | yes |
| an Integer 0, a Variant holding a Long 0 | no |
| `Len(s)` with `s` "abc"; with `s` "" | yes; no |
| `InStr("abc", "b")`; `InStr("abc", "x")` | yes; no |
| `x And 4`, `x And 1`, with `x` a Long 6 | yes; no |
| `Err` and `Err.Number`, after `Err.Number = 11` | yes, both |
| `Err` after `Err.Clear` | no |
| the literal `Null` | no |
| a String `"abc"` | 13 |

**What it says.** Every form reads its condition as `CBool` reads it, except that Null is False where `CBool` raises
94 for Null (measured alongside, both modes: `CBool(Null)` raises 94, `CBool(Empty)` is False with no error). The Null row is the one worth a second look: Null is not a third state that makes every
condition fail. It is plain False, so `Do Until Null` runs and `Loop Until Null` does not end the loop.

**An object in a condition,** measured in a second probe, native and in the IDE, with a one-line `If` and `Do While`:

| `v` | VB6 |
|---|---|
| a Variant set to `Nothing`; an `Object` variable never set | 91 |
| a Variant holding a class with no default member | 438 |
| a Variant holding a class whose default member, a **Function**, returns 42 | `If` holds; `Do While` loops |

The default member was a Function, `Public Function Value() As Long` with `VB_UserMemId = 0`. That matters because a
Function default member gives 450 on some paths where a Property Get gives its value (#840's probes: `Format`, `Abs`,
`Not`). A condition is not one of those paths. `ConditionTests`' `Widget` is the same Function.

So a condition refuses an object as every conversion does. `CBool` alone in HexIDE did not ask: each of these, and a
class with a default member, a control and `Err`, fell through to a trappable 13.

**HexIDE, after this.** One reading, `ConditionHolds`, used by the block `If`, `ElseIf`, the one-line `If`, `While`,
and both `Do` forms. `CBool` now refuses what every conversion refuses: Nothing is 91, a class with no default member
438, and a class with a default member, a control and `Err` are refused, untrappably, naming #174, #790 or #840. An
error `CBool` raises in a condition names the condition's statement. `If Err Then` follows Err used as a value, which
#840 makes its Number. `CVErr` does not exist in HexIDE yet.

**After #840 (2026-10-13).** A condition is read as an operand is, through `ObjectAsValue`, so `Err` there is its
Number and is no longer refused. Measured again for the loops, native and in the IDE (`p25`, a Sub Main project under
`On Error Resume Next`, identical in both modes):

| Form | VB6 |
|---|---|
| `If Err Then`, one-line, block and `ElseIf` | Then after `Err.Raise 53`, Else after `Err.Clear` |
| `While Err`, `Do While Err` and `Do Until Err = 0`, entered after `Err.Raise 53`, clearing it in the body | one pass |
| `Do Until Err`, entered with no error, raising 53 in the body | one pass |
| `Do ... Loop While Err`, with no error | one pass |

A class with a default member and a control are still refused, naming #174 and #790. HexIDE answers every row. The
`While` one stopped with "Sub or Function not defined (Err)" until #882, which measuring it found: a member call such
as `Err.Clear` or `Debug.Print` inside `While...Wend` parsed as two statements, and the condition had nothing to do
with it.

