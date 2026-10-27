## A control's Enabled inside a disabled container (2026-10-27)

**The probe:**
- **The software:** VB6 6.00.8176 SP6.
- **The builds:** `/make` as native code and as p-code. The two logged the same lines.
- **The form:**
  - Frame1, holding Text1, Command1 and Frame2;
  - Frame2, holding Check1;
  - Picture1, a PictureBox holding Text2;
  - Text3, on the form.
- **What it logged:** after each step, every control's `Enabled`, and, for Text1, whether its window was enabled
  (`IsWindowEnabled`).

**A control's Enabled reads False while any container above it is disabled.** Its own value is kept underneath, and it
reads that again once every container above it is enabled.

| Step | Frame1 | Text1 (window) | Command1 | Frame2 | Check1 | Picture1 | Text2 | Text3 |
|---|---|---|---|---|---|---|---|---|
| As loaded | True | True (1) | True | True | True | True | True | True |
| `Frame1.Enabled = False` | False | **False (1)** | **False** | **False** | **False** | True | True | True |
| `Text1.Enabled = True`, Frame1 still disabled | False | **False (1)** | False | False | False | True | True | True |
| `Text1.Enabled = False` | False | False (0) | False | False | False | True | True | True |
| `Frame1.Enabled = True` | True | **False (0)**, its own value | True | True | True | True | True | True |
| `Frame2.Enabled = False`, Text1 enabled again | True | True (1) | True | False | **False** | True | True | True |
| `Picture1.Enabled = False` | True | True (1) | True | True | True | False | **False** | True |

- **The child's window is not disabled.** Text1's window stayed enabled while it read False. Windows disables input to
  the container's children through the container's own window.
- **Assigning True inside a disabled container still reads False.** The assignment is kept: once Frame1 was enabled, a
  child the program had never touched read True.
- **It reaches through nesting.** Check1, inside Frame2 inside Frame1, read False when either Frame was disabled.
- **`For Each c In Me.Controls`** gives the same answers: with Frame1 disabled, every control inside it read False and
  Picture1, Text2 and Text3 read True.
- **An SSTab, from TABCTL32.OCX, does the same** with its children (the oracle's "In-box controls: the SSTab").

**The obvious guess,** that a control's Enabled is its own property and a container's doesn't show in it, **is wrong**.
It is the guess HexIDE makes.

**In HexIDE:** Text1.Enabled read True inside a disabled Frame, measured with a headless test against `FrameWithChildren`
(#2245). The Frame does disable its children's input, which `ADisabledFrame_DisablesItsContents` asserts, but the value
VB6 code reads is the control's own.
