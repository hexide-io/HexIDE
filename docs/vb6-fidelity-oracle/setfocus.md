## SetFocus (2026-10-15, #1061)

**The probe.** A compiled form, native and p-code, run on the oracle guest's **console** session through the agent.
In session 0 every SetFocus was 5, because no window there is ever foreground, so session 0 can't answer this. Form1
holds:
- `Text1` to `Text5`: `Text3` disabled, `Text4` invisible, `Text5` with `CausesValidation = False`;
- a `Timer` that takes one step each 300 ms.

Form2, loaded on its own, holds a `Text1`. Every handler logs its name, and each step logs `Err.Number` and
`Screen.ActiveControl.Name` straight after the call. Native and p-code logged the same, line for line:

```
load
load: Text2.SetFocus err 5 active (none)
load: Me.SetFocus err 5
Form_Activate active Text1
Text1_GotFocus
step1 foreground True active Text1
step1 Text2.SetFocus begins
step1 after, err 0 active Text2
Text1_LostFocus
Text2_GotFocus
step2 Text1.SetFocus begins
step2 after, err 0 active Text1
Text2_LostFocus
Text1_GotFocus
step3 disabled Text3.SetFocus err 5 active Text1
step4 hidden Text4.SetFocus err 5 active Text1
step5 hidden form's Text1.SetFocus err 5 active Text1
step6 Me.SetFocus err 0 active Text1
step7 Text2.SetFocus with Text1_Validate cancelling begins
step7 after, err 0 active Text2
Text1_LostFocus
Text2_GotFocus
step8 Text5.SetFocus (CausesValidation False) begins
step8 after, err 0 active Text5
Text2_LostFocus
Text5_GotFocus
step9 Text5.SetFocus again, already focused
step9 after, err 0 active Text5
step10 Text1 disabled at run time, SetFocus err 5 active Text5
```

A second run added steps 11 to 16:
- **Steps 11 to 13** focus `Text1` and send it a Tab key with `SendKeys`. Natively, the Tab raised `Text1_Validate`,
  then `Text1_LostFocus`, then `Text2_GotFocus`. That shows the probe's Validate handler works. As p-code, the Tab
  hadn't landed by step 13, so that run says nothing about Validate.
- **Steps 14 to 16** show Form2, then call `Form2.Text1.SetFocus` from Form1. Err is 0, and the active form becomes
  Form2. Form1's control raised LostFocus, then `Form2.Text1_GotFocus` came, both after the handler.

**What VB6 does:**
- **On a form that isn't shown, it is 5,** Invalid procedure call. That holds in `Form_Load` on a control, on the form
  itself (`Me.SetFocus`), and on a control of a form that is loaded but hidden.
- **On a disabled or invisible control it is 5,** and nothing changes. That holds whether it was disabled at design
  time or at run time.
- **Otherwise the control has the focus at once:** `Screen.ActiveControl` names it on the next line. If another of the
  program's forms had the focus, this form is activated.
- **LostFocus and GotFocus come after the handler that called SetFocus has returned,** never inside it. The old
  control's LostFocus comes first, then the new one's GotFocus. They are posted, as Windows posts the focus messages.
- **SetFocus raises no Validate,** even when the control losing the focus has a `Validate` handler and the one taking
  it has `CausesValidation` True (step 7). A key that moves the focus does raise Validate (step 12).
- **On the control that already has the focus, it raises nothing,** and Err stays 0.
- **The form's own SetFocus, on a shown form,** is 0 and changes nothing.

The obvious guess that GotFocus runs inside the SetFocus statement, as `Text1.Text = "x"` runs Change inside the
assignment, is wrong. So is the common description that SetFocus triggers Validation.

**Not measured:**
- **a container disabled around the control,** such as a Frame;
- **SetFocus while another form is modal;**
- **DoEvents straight after SetFocus**, which would deliver the posted events inside the handler;
- **a control that can't take the focus,** such as a Label, which has no SetFocus member in VB6 and is expected to be
  438.

**In HexIDE (#1061):**
- **SetFocus does all of the above.** Its LostFocus and GotFocus are posted to the program's event pump, which runs
  them after the handler.
- **A TextBox raises GotFocus and LostFocus,** which it didn't before. Only the CommandButton did. The other focusable
  controls are #1101: "GotFocus and LostFocus on each control, and Enabled saved False".
- **A TextBox saved with `Enabled = 0` is disabled when it runs.** Its component didn't declare Enabled, so the saved
  value was dropped and the control ran enabled. The Frame and the scroll bars had the same gap, and it is fixed for
  them too.
- **A control that can't take the focus** is refused as not implemented, pending a measurement of 438.

