## Break on an idle program, and a step off the end of an event handler (2026-11-16, #4261, IDE)

**The question.** A program whose form is shown and has no code running sits idle, waiting for an event. What does
Ctrl+Break do there: does the IDE enter break mode at once, with what as the current statement? And what do F5, F8 and
Shift+F8 do next? #1297's run answered the first part only for a program with a Timer.

**The probe.**
- **The IDE:** VB6.EXE 6.00.8176 SP6, in the guest's console session through the agent ("Reaching the VB6 *IDE*, not
  just the compiler" in the README). The IDE was maximised.
- **The project:** a Standard EXE with one form, `IdleForm`, which has no Timer and no controls. Its code:
  ```vb
  Private Sub Note(ByVal s As String)
      Dim f As Integer
      f = FreeFile
      Open App.Path & "\IdleForm.log" For Append As #f
      Print #f, s
      Close #f
  End Sub

  Private Sub Form_Load()
      Note "load"
  End Sub

  Private Sub Form_Click()
      Note "click first"
      Note "click second"
  End Sub
  ```
- **The run:** started with F5. After Load the form is shown and in the foreground, and no code runs.
- **What was recorded after each step:** the IDE's title, the foreground window, the log, and a screenshot.
- **Keys and clicks:** keys went through `SendKeys` to the window named. A click was a real mouse press and release at
  the form's centre, after bringing the form forward.

This is IDE behaviour, so there is no native or p-code build to compare.

| Step, from the state the row above left | VB6 |
|---|---|
| Ctrl+Break, with the idle form active | **[break] at once.** The IDE comes forward and is the foreground, with no dialog. The form stays shown behind it. No code window opens, and none was open before. |
| The Debug menu in that state | Step Into and Step Over enabled. **Step Out, Run To Cursor, Set Next Statement and Show Next Statement greyed.** |
| The View menu | **Call Stack greyed.** Ctrl+L opens nothing. |
| View Code | The form's code window opens with **no current-statement arrow and no highlighted line.** |
| F8 | **[run] at once.** The IDE stays the foreground, and the form isn't brought forward. The log is unchanged: no statement ran. |
| A click on the form | **[break], with the arrow on `Private Sub Form_Click()`**, the declaration line. The log is unchanged: none of the handler's statements ran. |
| F5 | [run]. The form is the foreground, and the handler runs to its end: the log gains "click first" and "click second". |
| Ctrl+Break on the idle form again, then F5 | [break], then **[run] with the form the foreground**, as #1297 found. |
| A click on the form | The handler runs to its end with no break. |
| Run > Break, with the IDE active and the program idle | [break] at once, as Ctrl+Break gives. |
| Shift+F8 | [run], as F8 gives. |
| A click on the form | [break], with the arrow on `Private Sub Form_Click()`, as after F8. |

**A step off the end of the event handler.** The run was started again, and Ctrl+Break, F8 and a click brought it to
`Form_Click`'s declaration line. Then Shift+F8 four times, with the title and log read after each press:

| Shift+F8 | Title | Log |
|---|---|---|
| 1 | [break] | (empty): at `Note "click first"` |
| 2 | [break] | click first |
| 3 | [break] | click first, click second: at `End Sub` |
| 4 | **[run]** | click first, click second |
| then a click on the form | **[break]**, the arrow on `Private Sub Form_Click()` | (empty) |

This was done twice, with the same result. **The control:** F5 from that break ran the handler to its end and left
[run], and the next click ran it again with no break.

**Readings:**
- **An idle program enters break mode at once.** Nothing waits for the next statement. **There is no current
  statement**: no arrow, Show Next Statement and Set Next Statement greyed, and no call stack.
- **F8 and Shift+F8 from there resume, and arm a step.** The next event that runs Basic code breaks at its handler's
  **declaration line**, before any of its statements. F8 leaves the IDE in front; it doesn't bring the program
  forward.
- **F5 resumes with nothing armed,** and brings the program's form forward.
- **A step off the end of the outermost event handler stays armed.** The IDE shows [run], and the next event breaks at
  its handler's declaration line.
  - **This overturns `docs/debugger-vb6-divergences.md`'s D4,** which says VB6 "clears the step and the next event
    runs free". That sentence wasn't a measurement. It was the expectation the P5 reviews held when they called
    HexIDE's carried step a leak, and HexIDE now disarms the step at the dispatch boundary on its strength.
  - Only the continue command clears the step, as the control shows.

**Not measured:**
- a step off the end of a handler entered at a breakpoint rather than by a step;
- Step Out (Ctrl+Shift+F8) off the end of a handler;
- an event with no handler between the two clicks;
- a program whose next event is a Timer's.
