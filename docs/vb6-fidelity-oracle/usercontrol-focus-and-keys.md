# A UserControl's focus and access keys at run time

Provenance: hexide-io/HexIDE#1631, issuecomment-6425849231 (hexide-int, 2026-10-26),
reported against vb6.exe SP6 in the oracle VM's console session with real keystrokes. Native and
p-code builds agreed. Eleven private controls in a Standard EXE each varied one `.ctl` setting.
This records the supplied measurements; no new VB6 probe was run for part B.

- Tab passes over CanGetFocus False, ForwardFocus True and InvisibleAtRuntime controls. A windowless
  UserControl is a stop. A child of a ControlContainer is a separate stop.
- Removing a control's TabIndex leaves a hole. Indices beyond the remaining end take free places in
  form order: cmdOut's 17 became 3 and Command1's 16 became 8.
- AccessKeys `q` responds to Alt+Q, raising AccessKeyPress with Integer 113 before taking focus.
  Q alone does not activate it.
- ForwardFocus retains TabIndex but removes TabStop, SetFocus, CausesValidation and HelpContextID.
  The later "Group 2 of 2" comment (same issue, 2026-10-26) measured ForwardFocus True combined with
  CanGetFocus False: TabIndex is still present and TabStop absent.
- With ForwardFocus True, Alt+W goes to the next control in tab order without raising an event on
  the forwarding control. With CanGetFocus False, Alt+Z goes to the first focusable control, not the
  control after it. That was Text1, and became UCPlain1 when Text1 moved to TabIndex 12.
- A UserControl reached by someone else's key receives AccessKeyPress for that key: UCPlain1, with
  no AccessKeys of its own, received 122 for Alt+Z and 116 for a Label's `&T`.
- DefaultCancel adds Default and Cancel, initially False. Setting Default True makes
  Ambient.DisplayAsDefault True and raises AmbientChanged with `DisplayAsDefault`. With Default and
  Cancel set True, Enter in a TextBox sends AccessKeyPress 13 to the control (no TextBox KeyPress),
  and Esc sends 27. CommandButton focus makes DisplayAsDefault False; Enter clicks that button.

Part B does not generalise these measurements to duplicate access keys, competing default controls,
modified Enter/Esc, multiline TextBoxes or other focused control types, or non-letter AccessKeys. Drawing, lifecycle ordering and file/designer behaviour
belong to the later parts of #1631. A placed control's TypeName is also unmeasured here; HexIDE
keeps its existing Canvas name rather than substituting a guessed VB6 result.

Construction with `New` was not measured for this split. Registering the handler template does not
enable it; HexIDE keeps its existing refusal.
