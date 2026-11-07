## The control a shown form focuses (2026-10-28, #2436)

**The question.** When a form is shown, which of its controls gets the focus, and when, against `Form_Load` and
`Form_Activate`? HexIDE gives none of them the focus (#2436).

**The probe.** Eight projects, built native and p-code, run in the guest's console session. In most, Form1 holds Text1
(TabIndex 0), Text2 (1) and Command1 (2). Each logs `Form_Load`, `Form_Activate` and every control's `GotFocus`. A
Timer then reads `Screen.ActiveControl` and `Me.ActiveControl` and unloads the form. Native and p-code logged the same
lines in every case.

| Case | Logged |
|---|---|
| Form1 the startup form | `Form_Load`, `Form_Activate`, `Text1_GotFocus`; ActiveControl Text1 |
| Text1 `Enabled = False` | `Form_Load`, `Form_Activate`, **`Text2_GotFocus`**; Text2 |
| Text1 `Visible = False` | the same: Text2 |
| Text1 `TabStop = False` | the same: Text2 |
| A Label at TabIndex 0, Text1 at 1, Text2 at 2 | `Text1_GotFocus`; Text1 |
| `Text2.SetFocus` in `Form_Load` | **5**, "Invalid procedure call or argument", then `Form_Activate`, `Text1_GotFocus`; Text1 |
| `Me.Show` then `Text2.SetFocus` in `Form_Load` | `Form_Activate`, `Text1_GotFocus`, `Text2_GotFocus`, all inside `Form_Load`; Text2 |
| Sub Main: `Form1.Show vbModal` | `Form_Load`, `Form_Activate`, `Text1_GotFocus`; Text1; then the line after the Show |

`Me.ActiveControl` read the same as `Screen.ActiveControl` in every case.

So:
- **The first control in tab order that can take the focus gets it:** enabled, visible, with TabStop True, and not a
  Label.
- **Its `GotFocus` comes after `Form_Activate`,** for a modal form too.
- **`SetFocus` before the form is visible is error 5.** After `Me.Show` in the same `Form_Load`, the activation and the
  first control's `GotFocus` have already happened, and `SetFocus` moves the focus on from there.
