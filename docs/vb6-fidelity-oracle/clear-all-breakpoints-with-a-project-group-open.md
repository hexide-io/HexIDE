## Clear All Breakpoints with a project group open (2026-11-21, #492, IDE, SP6)

**The question.** HexIDE's Debug > Clear All Breakpoints removes the breakpoints of every loaded project
(`IBreakpointService.ClearAll`). #492 asked whether VB6's clears the whole group or only the active project.

**The probe.** `gen492.vbg` named two Standard EXE projects, PA and PB, each with one module (`ModPA`, `ModPB`):

```vb
Sub Main()
    Dim n As Long
    n = 1
    n = 2
End Sub
```

The group was opened in VB6's IDE (VB6.EXE and VBA6.DLL 6.0.9782, SP6) through the console agent, which sent keys
with .NET's `SendKeys`. For each module:
1. Its Project Explorer node was selected (Ctrl+R, then the arrow keys until the Properties window's caption named it).
2. View Code (F7) opened it.
3. The caret was moved to line 3, `n = 1`, and F9 pressed.

Then PA's node was selected, so the main window's title read "PA - Microsoft Visual Basic [design]", and Ctrl+Shift+F9,
Clear All Breakpoints' shortcut, was pressed. Each module's code window was brought up again.

The screen was read two ways:
- **A count of pixels** in VB6's default breakpoint colour, RGB 128,0,0, sampling every second pixel.
- **A screenshot,** read by eye.

| | ModPA's window in front | ModPB's window in front |
|---|---|---|
| Both breakpoints set | 277 | 293 |
| After Clear All, with PA active | 2 | 2 |

The 2 left over were in both windows. In the screenshots after the clear, neither window has a breakpoint dot in its
margin or a highlighted line, and before it both did. What the two remaining pixels are wasn't identified.

**What it shows:** **VB6's Clear All Breakpoints clears every project in the group,** not just the active one. PB's
breakpoint went too, although PA was the active project.

**In HexIDE (#492):** `ClearAll()` already clears every loaded project, which is what VB6 does. #492's remaining
point, that HexIDE's per-project breakpoint files are saved unevenly afterwards, is HexIDE's own and not a VB6 question
(reasoned from the issue, not checked here).

**Not measured:**
- The Debug menu itself: the shortcut was used, and it reached the command, since the breakpoints went.
- Clear All in break mode, with a program running.
- Whether VB6 keeps breakpoints across closing and reopening a group.
