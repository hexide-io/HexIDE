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
- a step off the end of a handler entered at a breakpoint rather than by a step. A handler entered at a `Stop` was
  measured on 2026-11-18, in the last section: the step carries;
- Step Out (Ctrl+Shift+F8) off the end of a handler. Measured on 2026-11-18, in the last section: it carries;
- an event with no handler between the two clicks;
- a program whose next event is a Timer's. Measured on 2026-11-18, in the last section.

## Which window comes to the front: Continue, a step, and a modal form (2026-11-17, #4262, IDE)

**The question.** The section above found that F8 from an idle break leaves the IDE in front and F5 brings the form
forward. Does Shift+F8 do the same as F8? Do several steps through an event, and out of it, change that? And with a
modal form open at the break, which window does F5 bring forward?

**The probe:**
- **The software:** VB6's IDE, SP6, on the oracle VM (Windows 10). Measured by the hexide-int session, at hexide-mcp's
  request.
- **The project:** a Standard EXE. Form1 has Timer1, Interval 500, whose Timer event runs `n = n + 1`, `x = n * 2` and
  `Caption = "Form1 " & n`. Form1's Click runs `Form2.Show vbModal`.
- **How it was read:** the foreground window by `GetForegroundWindow`'s title, the z-order by `EnumWindows`' order, and a
  screenshot after each step.

**What it showed:**

| Step | Foreground after it |
|---|---|
| Run, Form1 active | `Form1 15` |
| Ctrl+Break | the IDE, [break], with no execution arrow: an idle break |
| F8 | **the IDE**. The step waited for the next tick and stopped on `Private Sub Timer1_Timer()`; Form1 stayed behind |
| F8 five more times | the IDE after each one. After the sixth, the arrow was on the `Sub` line again, at the next tick |
| F5 | **`Form1 40`**: Continue brought Form1 back, after the steps too |
| Ctrl+Break again, Form1 active | the IDE, with no arrow |
| Shift+F8 | **the IDE**, as F8: it stopped on the `Sub` line at the next tick, and Form1 stayed behind |
| F5, a click on Form1, so modal Form2 is in front; then Ctrl+Break | the IDE. Z-order: the IDE, Form2, Form1 |
| F5 | **`Form2`**. Z-order: Form2, Form1, the IDE |

**What it shows:**
- **A step from break mode doesn't bring the program forward; Continue does.** That held for F8 and Shift+F8, from an
  idle Ctrl+Break, and for the step that left the event. Continue after several steps still brought the form back.
- **With a modal form open, Continue brings the modal form forward,** with its owner directly behind it, not the
  startup form.
- The intermediate lines of the five F8s weren't read, only the foreground after each and the arrow after the last.

**Not measured:**
- a step that runs to completion without re-entering code, such as a program with no Timer;
- Ctrl+F8 and Step Out. Step Out was measured on 2026-11-18, in the last section; Ctrl+F8 is still unmeasured;
- Continue when the form that was active at the break has been hidden or unloaded.

## Step Out across idle, and stepping through a startup Sub Main or form (2026-11-18, #4449, IDE)

**The questions:**
- **Step Out across idle:** what does Step Out do when the handler it leaves returns the program to idle?
- **Startup positions:** where do Step Into and Step Over stop through a startup `Sub Main` that shows a form, and through a startup form?
- **Past `Sub Main`'s end:** does a step carry into the next event, as #4270 found it does past an event handler's end?

**The rig:**
- **The IDE:** VB6.EXE (file version 6.00.9782) in the guest's console session, driven through the agent as in the first section, with keys sent by `SendKeys`.
- **Recorded after each key:** the IDE's title, the code window's current-statement bar (read from a screenshot), and each program's log.
- **Builds:** this is IDE behaviour, so there is no native or p-code build to compare.

**The four programs:**
- **A:** a form with a Timer at 700 ms. Its handler logs `tick n first`, runs `If n = 2 Then Stop`, then logs `tick n second`.
- **B:** startup `Sub Main` in Module1, which logs `main 1`, runs `Form1.Show` and logs `main 2`. Form1 logs from `Form_Load` and `Form_Activate`.
- **C:** B with a Timer at 1500 ms on the form in place of `Form_Activate`, so that an event follows `Sub Main`.
- **D:** a startup form whose `Form_Initialize` and `Form_Load` each log one line.

### Step Out across idle (program A)

| From | Key | VB6 |
|---|---|---|
| `Stop`, on tick 2 | Ctrl+Shift+F8 | The rest of the handler ran (`tick 2 second`), then **[break] at `Private Sub Timer1_Timer()` for tick 3,** before any of its statements |
| that declaration | Ctrl+Shift+F8 | Tick 3 ran whole, then [break] at the declaration again, for tick 4 |
| that declaration | F5 | [run]. The ticks went on unbroken |
| `Stop`, on tick 2 (a fresh run) | Shift+F8, twice | `Note "tick " & n & " second"`, then `End Sub` |
| the handler's `End Sub` | Shift+F8 | [break] at `Private Sub Timer1_Timer()` for tick 3 |

- **Step Out off the outermost handler carries,** like Step Into and Step Over (#4270). It arms for the next event, which breaks at its declaration.
- **A handler entered at a `Stop`,** not by a step, carries the same way.

### Through a startup `Sub Main` (programs B and C)

| From | Key | VB6 stops at |
|---|---|---|
| design mode | F8 | **`Sub Main()`, the declaration line** |
| design mode | Shift+F8 | `Sub Main()` |
| `Sub Main()` | F8 or Shift+F8 | `Note "main 1"` |
| `Note "main 1"` | Shift+F8 | `Form1.Show` |
| `Form1.Show` | F8 | **`Private Sub Form_Load()`**, in Form1's code window |
| `Private Sub Form_Load()` | F8 | `Note "load"` |
| `Note "load"` | Shift+F8 | `End Sub` of `Form_Load` |
| that `End Sub` | F8 | `Note "main 2"`, back in `Sub Main`. `Form_Activate` did not run |
| `Form1.Show` | Shift+F8 | `Note "main 2"`. Load ran without a stop |
| `Note "main 2"` | Shift+F8 | `End Sub` |
| `Sub Main`'s `End Sub` | F8 | [run], then **[break] at `Private Sub Timer1_Timer()`** before the first tick ran (C) |
| `Note "main 1"` | Ctrl+Shift+F8 | The rest of `Sub Main` ran (`main 1`, `load`, `main 2`), then **[break] at `Private Sub Timer1_Timer()`** (C) |
| `Sub Main`'s `End Sub` | Shift+F8 | **[run], and the Timer ran freely: the step did not carry** (C) |

- **Step Over past `Sub Main`'s end doesn't carry, but Step Into and Step Out do.**
  - **Step Over:** measured twice, once from a run started with Shift+F8 and once from a run started with F8 with only the last key Shift+F8. Both times the IDE went to [run] and the ticks were logged.
  - **The contrast:** Step Over past an event handler's `End Sub` carried in the same rig (program A), as #4270 found for a Click handler.
  - **No rule I would defend.** `Sub Main` has no caller in the program, while an event handler is called by the runtime's dispatch, but that is a guess, not a measurement.
- **The form never raised `Form_Activate` in program B's stepped run.**
  - It showed while the IDE held the focus.
  - With the IDE in [run] after `Sub Main` returned, bringing the form forward with `SetForegroundWindow` raised nothing either.
  - So program B, which had no other event, gave no answer on carrying. Program C's Timer answered it.
- **Unexplained:** in both Shift+F8 runs of C, three ticks were logged within about three seconds of [run], at a 1500 ms interval. The cause wasn't measured.

### Through a startup form (program D)

| From | Key | VB6 stops at |
|---|---|---|
| design mode | F8 | **`Private Sub Form_Initialize()`** |
| that declaration | Shift+F8 | `Note "init"` |
| `Note "init"` | Shift+F8 | `End Sub` of `Form_Initialize` |
| that `End Sub` | Shift+F8 | **`Private Sub Form_Load()`** |
| that declaration | Shift+F8 | `Note "load"` |

### What this overturns

`docs/debugger-vb6-divergences.md` D19 records that HexIDE's start skips declarations, "as VB6 never stops on one".
VB6 stopped on a declaration at every entry measured here:
- **The start:** `Sub Main()`, or `Form_Initialize`.
- **A Step Into on a call:** `Form1.Show` into `Form_Load`.
- **From one start event to the next:** `Form_Initialize`'s end into `Form_Load`.
- **The next event after a carried step:** `Timer1_Timer`.

**Not measured:**
- a breakpoint (F9), as opposed to `Stop`, at the start of the carried handler;
- a Click or other user event after `Sub Main`, rather than a Timer;
- `Sub Main` with no form;
- Step Out from a procedure called by a handler (an ordinary return, not across idle);
- Ctrl+F8.
