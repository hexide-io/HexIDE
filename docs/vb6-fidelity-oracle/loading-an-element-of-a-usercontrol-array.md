## Loading an element of a UserControl array (2026-11-16, #4337, SP6)

**The question.** When a program makes a new element of a UserControl array with `Load`, which of the UserControl's
own events run, in what order, and where do its properties come from? When does it get Show and Resize, given a
loaded element starts hidden?

**The probe.**
- **The compiler:** vb6.exe SP6, `/make`, run in the guest's console session. Native and p-code agree on every line.
- **The UserControl, `UcProbe`:** it has a Public `Value` property, persisted with `WriteProperties`. It logs its
  `Extender.Index` and `Extender.Width` from each of Initialize, InitProperties, ReadProperties, Show, Hide, Resize and
  Terminate.
  - **InitProperties** sets `Value` to 7.
  - **ReadProperties** reads it with a default of 0.
- **The form:** it holds the template element `Uc(0)`, saved with `Width = 1500`, `Top = 240` and `Value = 42`.
  - **Form_Load:** `Load Uc(1)`, then `Uc(1).Top = 1000` and `Uc(1).Visible = True`.
  - **The Timer's first tick,** with the form showing: `Load Uc(2)` and `Uc(2).Visible = True`.
  - **Its second tick:** `Unload Me`.

**What happened,** in order. A dash means the UserControl's code had no extender to read yet.

| When | Logged |
|---|---|
| The form loads, before Form_Load | `Uc(-)` Initialize; `Uc(0)` Resize, W 1500; `Uc(0)` ReadProperties, Value 42 |
| `Load Uc(1)` | `Uc(-)` Initialize; `Uc(1)` Resize, **W 0**; `Uc(1)` ReadProperties, **Value 42** |
| right after it, in Form_Load | `Uc(1).Value` 42, `Width` 1500, `Height` 600, **`Visible` False**, `Top` 240 |
| `Uc(1).Top = 1000`, then `Uc(1).Visible = True` | nothing logged |
| the form shows | `Uc(0)` Show, then `Uc(1)` Show, then Form_Activate |
| `Load Uc(2)` with the form showing | `Uc(-)` Initialize; `Uc(2)` Resize, W 0; `Uc(2)` ReadProperties, Value 42 |
| `Uc(2).Visible = True` | nothing during the statement; `Uc(2)` Show **after it returns** |
| `Unload Me` | Form_Unload, then for each of 0, 1 and 2 in turn: Hide, then Terminate |

**Readings:**
- **A loaded element is made as the template was loaded,** from the template's saved properties. It runs Initialize,
  Resize and ReadProperties, never InitProperties. So `Value` is 42, the template's saved value, not InitProperties' 7.
- **Its extender properties are the template's:** Width, Height and Top, except `Visible`, which is False.
- **Its Resize comes inside `Load`, with the extender's Width still 0,** before ReadProperties. The template's Resize
  saw its real Width.
- **Show waits until the element is visible on a showing form:**
  - with the form not shown yet, it comes when the form shows, in index order, before Form_Activate;
  - with the form already showing, it comes after the `Visible = True` statement has returned, not during it.
- **Unload hides and terminates each element in index order,** the loaded ones included.

**Not measured:**
- **`Unload Uc(1)` on its own.**
- **A loaded element whose template has no saved custom property,** to see whether ReadProperties still runs or
  InitProperties does.
- **The VB6 IDE's run, rather than a built program.**
