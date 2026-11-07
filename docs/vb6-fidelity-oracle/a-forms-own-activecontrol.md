## A form's own ActiveControl (2026-10-28, #2437)

**The question.** What does a form's own `ActiveControl` give when the form is not the active one, when it is hidden,
when it is loaded and never shown, and when it is read after the form unloads? HexIDE doesn't implement it (#2437).

**The probe.** Three forms, each with two TextBoxes: Form1 (Text1, Text2), Form2 (Text3, Text4) and Form3 (Text5,
Text6). They sit side by side, built native and p-code, and run in the guest's console session. A Timer on Form1 takes
one step a tick, so the focus moves between steps as Windows moves it. Each step logs:
- `Screen.ActiveForm` and `Screen.ActiveControl`;
- every loaded form's `ActiveControl`, through a function that sets a `Control` from it and logs Nothing or the name;
- every `GotFocus` and `Form_Load`.

Native and p-code logged the same lines.

| Step | `GotFocus` and `Load` | Screen | Each form's `ActiveControl` |
|---|---|---|---|
| Form1 shown | Text1 | Form1, Text1 | Form1 Text1 |
| `Text2.SetFocus` | Text2 | Form1, Text2 | Form1 Text2 |
| `Form2.Show` | Form2 Load, Text3 | Form2, Text3 | **Form1 Text2**, Form2 Text3 |
| `Form2.Text4.SetFocus` | Text4 | Form2, Text4 | Form1 Text2, Form2 Text4 |
| `Form2.Hide` | **Text2** | Form1, Text2 | Form1 Text2, **Form2 (hidden) Text4** |
| `Load Form3` | Form3 Load | | **Form3 Nothing** |
| `Form3.Show` | Text5 | Form3, Text5 | Form1 Text2, Form2 Text4, Form3 Text5 |
| `Form1.SetFocus` | **Text2** | Form1, Text2 | Form1 Text2, Form2 Text4, **Form3 Text5** |
| `Unload Form3`, `Unload Form2`, then read `Form2.ActiveControl` | **Form2 Load** | | Form2 Nothing; `Forms.Count` 1, then 2 |
| `Form1.Hide`, no form visible | | **Form1, Text2** | Form1 (hidden) Text2, Form2 (hidden) Nothing |

So:
- **A form keeps its own `ActiveControl`:** the control that last had the focus on it, whether the form is active, not
  active or hidden. `Screen.ActiveControl` is the active form's.
- **A form loaded and never shown has none:** Nothing, with no error.
- **Reading it on an unloaded form loads the form again,** as any member read does, and the new instance has none.
- **The active form gets its own control back:** Windows reactivates Form1 when Form2 hides, and so does
  `Form1.SetFocus`. Each time Text2 raises `GotFocus` again.
- **Hiding the last visible form leaves it the active one:** `Screen.ActiveForm` still reads Form1 and
  `Screen.ActiveControl` still reads Text2.
