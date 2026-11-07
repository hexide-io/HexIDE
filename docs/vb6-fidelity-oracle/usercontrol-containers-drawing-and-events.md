# A UserControl's containers, drawing and events

Provenance: hexide-io/HexIDE#1631, issuecomment-6425849231, measured by hexide-int on
vb6.exe SP6 in the oracle VM's console session, with real keyboard input. Native and p-code agreed.
This records the part-C subset of that comment; the full oracle subject was supplied in PR #2206.

- With a CommandButton placed inside a ControlContainer, UserControl.ContainedControls.Count was 1;
  the other controls returned 0 without error. The child's Container was the UserControl, with Left and
  Top relative to it. Moving cmdOut into that container preserved Left and Top. Moving it into a plain
  UserControl, or an InvisibleAtRuntime control, raised 425 (Invalid object use).
- An Alignable instance's Align initially read Integer 0. Setting Align to 2 gave Left 0, Top 5100,
  Width 9000 and Height 900: bottom alignment across the form's ScaleWidth, preserving height.
  Reading Negotiate at run time raised 393. No other alignment placement was measured here.
- A windowless control accepted focus and painted its BackColor. A windowed control with BackStyle 0
  and nothing drawn showed the form's colour at its centre instead of its own BackColor.
- Intrinsic to UserControl focus raised the control's EnterFocus, the form's instance GotFocus, then
  the control's GotFocus. Leaving for an intrinsic raised the control's LostFocus and ExitFocus, then
  the form's instance LostFocus. Between UserControls the order was new EnterFocus, old LostFocus,
  old ExitFocus, form old LostFocus, form new GotFocus, new GotFocus. For entry to a windowless
  UserControl, the form's GotFocus preceded the control's focus events.
- Startup raised Initialize, ReadProperties and Resize in form order, with windowless controls last,
  matching Controls collection order. Show followed Form_Load and preceded Form_Activate.
  Unloading raised Hide then Terminate for each. InvisibleAtRuntime instances raised only Initialize,
  ReadProperties and Terminate, with no Resize, Paint, Show or Hide.
- The extender's Visible read False during Form_Load and True after showing the form.

Native handles remain subject to the supervisor's recorded decision: HexIDE does not manufacture HWNDs.
Unmeasured Align assignments other than 2, the subtype returned by BackStyle, invalid BackStyle values,
property-bag operations, and other drawing settings
are not inferred from these observations.
