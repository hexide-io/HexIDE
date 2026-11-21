## The Error Trapping option: where each setting breaks, and what F5 does from the break (2026-11-21, #1070, IDE, SP6)

**The question.** VB6's Tools > Options > General has an **Error Trapping** setting: *Break on All Errors*, *Break in
Class Module* (the default) or *Break on Unhandled Errors*. HexIDE has no such option (#1070). Before one is built:
- Under *Break on All Errors,* does an error that `On Error Resume Next` or an `On Error GoTo` handler would trap still
  break?
- From that break, does F5 run the handler, or retry the statement?
- Under *Break in Class Module,* what does Continue do from the in-class break when the caller has a handler?

**The probe:**
- **The IDE:** VB6.EXE 6.00.9782 (SP6), in the console session of a clone of the oracle guest. It was driven through
  the console agent, as "Reaching the VB6 *IDE*, not just the compiler" in the README describes.
- **The setting:** chosen in Tools > Options > General, with the radio button clicked and then OK, at the start of
  each row's IDE.
- **Then:** F5, the Debug button on the run-time error dialog, and F5 again.
- **What was recorded** after each of those:
  - the IDE's title;
  - every window of the VB6 process, with its texts;
  - the log;
  - a screenshot;
  - the caret's line at the break.
- **Builds:** this is IDE behaviour, so there is no native or p-code build to compare.
- **The program:** a Standard EXE whose startup is `Sub Main`, with `Module1` and `Class1`. `Sub Main` logs
  `main` with `Command$`, and the command line (the `.vbp`'s `Command32`) picks one of three procedures. `zero` and `x`
  are module-level `Long`s, so `zero` is 0.

```vb
Sub G1()                         ' command line A
    On Error Resume Next
    L "g1 start"
    x = 1 \ zero
    L "g1 after, err " & Err.Number
End Sub

Sub G2()                         ' command line B
    On Error GoTo h2
    L "g2 start"
    x = 1 \ zero
    L "g2 after"
    Exit Sub
h2:
    L "g2 handler " & Err.Number
End Sub

Sub G3()                         ' command line C
    Dim c As Class1
    Set c = New Class1
    On Error GoTo h3
    L "g3 call"
    c.Fail
    L "g3 after"
    Exit Sub
h3:
    L "g3 handler " & Err.Number
End Sub

' Class1
Public Sub Fail()
    Dim z As Long
    L "in fail"
    z = 1 \ z
    L "fail after"
End Sub
```

**Where VB6 keeps the setting.** VB6 wrote two values under `HKCU\Software\Microsoft\VBA\Microsoft Visual Basic` when
OK was pressed:

| Setting | `BreakOnAllErrors` | `BreakOnServerErrors` |
|---|---|---|
| Break on All Errors | 1 | 0 |
| Break in Class Module | 0 | 1 |
| Break on Unhandled Errors | 0 | 0 |

The guest held neither value beforehand, and the default is *Break in Class Module*, so a missing pair reads as the
default. Both values were deleted afterwards, to leave the guest as it was.

| Program | Setting | After F5 | Debug broke on | F5 from that break | Log at the end |
|---|---|---|---|---|---|
| G1, `On Error Resume Next` | All Errors | **"Run-time error '11': Division by zero"** | `x = 1 \ zero` | **the same dialog again** | `main A`, `g1 start` |
| G2, `On Error GoTo h2` | All Errors | the same dialog | `x = 1 \ zero` | the same dialog again | `main B`, `g2 start` |
| G3, the caller's `On Error GoTo h3` | Class Module | the same dialog | **`z = 1 \ z`, in `Class1`** | the same dialog again | `main C`, `g3 call`, `in fail` |
| G3 | All Errors | the same dialog | `z = 1 \ z`, in `Class1` | the same dialog again | the same |
| G3 | Unhandled Errors | no dialog. The run ended in design mode | | | `main C`, `g3 call`, `in fail`, `g3 handler 11`, `end` |

**Readings:**
- **Break on All Errors breaks on a trapped error.** That held under `On Error Resume Next` and under an `On Error GoTo`
  handler alike. The dialog is the ordinary run-time error dialog, and Debug breaks on the faulting statement.
- **F5 from that break retries the statement.** It doesn't resume next, and it doesn't jump to the handler. Here the
  divisor was still 0, so the retry raised the same dialog, and neither `g1 after` nor `g2 handler` was logged.
- **Break in Class Module stops in the class** when the caller has a handler, as
  [An unhandled run-time error](an-unhandled-run-time-error.md)'s c1 found. **Continue from there retries the class's
  statement.** It doesn't hand the error to the caller's handler, which never ran.
- **Break on Unhandled Errors** lets the caller's handler catch the class's error, with no dialog, as a compiled
  program does.

### In HexIDE

Per #1070, HexIDE has no Error Trapping option. Its halt behaves as one fixed mix: an error nothing handles breaks at
the innermost faulting line, and a class's error that its caller handles goes to the caller's handler, as *Break on
Unhandled Errors* does. That wasn't checked again for this page.

### Not measured

- Continue from these breaks after the divisor was made non-zero. #1079's page has a retry that succeeds, under the
  default setting;
- Step Into (F8) from these breaks;
- an error raised by `Err.Raise` in the class, and an error raised inside a handler;
- a class's error under *Break on All Errors* when the class has its own handler.
