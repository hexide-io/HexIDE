## GotFocus and LostFocus on each control, and Enabled saved False (2026-10-23, #1101)

**The question.** "SetFocus" measured the TextBox. Do the other focusable controls raise GotFocus and LostFocus the same
way? Does a ListBox or ComboBox, whose items or edit box take the focus themselves, raise one GotFocus or several? And
what does a Form or a UserControl saved with `Enabled = 0 'False` do when it runs?

**The probe.** A compiled form, native and p-code, run on the oracle guest's console session through the agent, as
"SetFocus" was. Form1 holds a TextBox, two CheckBoxes, two OptionButtons, two ListBoxes, a ComboBox of each Style (0
Dropdown Combo, 2 Dropdown List, 1 Simple Combo), an HScrollBar, a VScrollBar, an empty PictureBox, a PictureBox holding a
TextBox `Text2`, and a Timer. Every control's GotFocus and LostFocus log its name and `Screen.ActiveControl`, and so do
the option buttons', ListBoxes' and ComboBoxes' Click and the ComboBoxes' DropDown. The Timer takes one step each 300 ms:
- `SetFocus` to each control in turn, logging `Err.Number` and the active control straight after the call;
- `List1.ListIndex = 1`, then the Down key, with List1 focused;
- Alt+Down and then Esc on Combo1;
- the Tab key, 18 times, from Text1;
- `Load` Form2, saved disabled, then `Show` it and `DoEvents`, logging its `Enabled`, `IsWindowEnabled` of its `hWnd` and
  `Screen.ActiveForm`;
- `Load` and `Show` Form3, which holds `Gauge1`, a UserControl whose `.ctl` saves `Enabled = 0 'False`.

Native and p-code logged the same 135 lines, line for line.

**Each control:**
- **Every one raises GotFocus and LostFocus, in the order and at the time the TextBox does.** That holds for the CheckBox,
  OptionButton, ListBox, each ComboBox Style, both scroll bars and both PictureBoxes. The old control's LostFocus comes
  first, then the new one's GotFocus, both after the handler that called SetFocus, and `Screen.ActiveControl` names the
  new control at once.
- **An OptionButton that takes the focus is selected,** and raises Click after its GotFocus:
  `Option1_GotFocus`, `Option1_Click`, and then `Option1.Value` is True. That is Windows' automatic radio button, which
  checks itself as it is focused. Tab skips the option that isn't selected: from Check2 it went to Option2, the
  selected one, and raised no Click.
- **A ListBox's selection moved by code or by a key** raises Click and no focus event. `List1.ListIndex = 1` raised
  `List1_Click` inside the statement. The Down key raised it again, and `ListIndex` was 2.
- **A ComboBox dropped down with Alt+Down** raised DropDown and no focus event. Esc raised nothing.
- **A PictureBox takes the focus whether or not it holds a control.** Tab went from the empty Picture1 to Picture2,
  then into Picture2's Text2: `Picture2_LostFocus`, `Text2_GotFocus`. A control inside a container is its own.
- **The Tab order** was Text1, Check1, Check2, Option2, List1, List2, Combo1, Combo2, Combo3, HScroll1, VScroll1,
  Picture1, Picture2, Text2, and round to Text1.

**Enabled saved False:**
- **A form:** `Form2.Enabled` read False when loaded and when shown, and `IsWindowEnabled` was 0 both times. Shown, it
  did not become the active form. `Screen.ActiveForm` stayed Form1, and Form1's List1 kept the focus, raising no
  LostFocus. Showing Form3 straight after did take it: `List1_LostFocus` with no active control.
- **A UserControl:** `UserControl.Enabled` read False in `UserControl_Show`, and its window was disabled.
- **`Form3.Gauge1.Enabled` doesn't compile:** "Method or data member not found". A placed UserControl has no Enabled
  unless its author declares one. The probe reads `UserControl.Enabled` through a `Public Property Get` instead.

**Measured and not explained:** the probe's last line was `disabled err 91`. The `Err.Number` was read in the procedure
that loaded and showed Form3. That procedure's own statements raised nothing that was logged. The likeliest source is
`List1_LostFocus`, which `DoEvents` ran inside it, and whose `Screen.ActiveControl` read had nothing to name. That an
error handled in an event procedure leaves `Err` set in the procedure whose `DoEvents` ran it is a guess. It wasn't
measured.

**Not measured:**
- a click on a ListBox item or into a ComboBox's edit box, rather than SetFocus and Tab;
- `Enabled` set False at run time on a form or a UserControl;
- a disabled form shown modally, or shown as the startup form.

### In HexIDE (#1101)

- **The CheckBox, OptionButton, ListBox, ComboBox, both scroll bars and the PictureBox raise GotFocus and LostFocus.**
  They didn't. Only the TextBox and the CommandButton did.
- **Focus moving within one control raises nothing.** Before, the check was on the routed event's source. It is now
  on where the focus went from and to, by the walk that gives a pointer event its control. So a ListBox's item or a
  ComboBox's edit box taking the focus is still the ListBox's or ComboBox's. A control inside a PictureBox is its own.
- **The ListBox, the scroll bars and the PictureBox take the focus.** They weren't focusable, so SetFocus on one was
  refused as not implemented.
- **An OptionButton that takes the focus is selected**, raising its Click after its GotFocus. A press selects it as
  it did, with Click after MouseDown (#974), and the Tab key doesn't select it; see below.
- **A form or a UserControl saved disabled runs disabled.** The form's designer half, which a UserControl's shares,
  now declares Enabled, so the saved line is no longer dropped. A disabled form is shown without being activated.
- **Not followed: the Tab key's stop at an option button that isn't selected** (#1583). A headless check of the same
  form tabbed through Option1 as well as Option2. Otherwise it took the order above. So the Tab key doesn't select an
  option that takes the focus, as `SetFocus` does. Where VB6's Tab never lands, selecting would change the user's
  choice as they tabbed past. Followed since #1583, under "Tab and the arrow keys among option buttons".

