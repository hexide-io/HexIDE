## A UserControl's own properties at run time (2026-10-26, #1631)

**The question.** A UserControl has properties of its own, set in its designer and kept in the `.ctl`. HexIDE keeps
each as a line of the file and acts on none of them. What does each change about a running program: which members its
instances have, how focus and access keys reach it, whether it is drawn, and what it may hold? This entry covers the
properties that change a running program. What the designer and the Properties window do with them is a separate
question.

**The probe.** A Standard EXE with eleven private UserControls, each 1500 by 900 twips with no constituent controls, and
each setting one property in its `.ctl`. The lines were written by hand in VB6's own spelling, and the build logged no
load error for any of them. Each property's effect below shows it was read.

| Control | Its `.ctl` sets |
|---|---|
| UCPlain | nothing, only `BackColor` green |
| UCNoFocus | `CanGetFocus = 0 'False` and `AccessKeys = "z"` |
| UCAccess | `AccessKeys = "q"` |
| UCForward | `AccessKeys = "w"` and `ForwardFocus = -1 'True` |
| UCDefCan | `DefaultCancel = -1 'True` |
| UCInvis | `InvisibleAtRuntime = -1 'True` |
| UCAlign | `Alignable = -1 'True` |
| UCCont | `ControlContainer = -1 'True`, with a CommandButton `cmdIn` inside its instance in the `.frm` |
| UCWless | `Windowless = -1 'True`, BackColor green |
| UCTrans | `BackStyle = 0 'Transparent`, BackColor green |
| UCBind | `DataBindingBehavior = 1 'vbSimpleBound` |

Form1, whose BackColor is red, holds one instance of each, named with a `1` suffix. It also holds a Label `&T`, Text1,
Text2, Command2 `&Cmd2`, Command1 `&End`, cmdOut `&Out` and a Timer. Every UserControl event appends to a log, and so do
the form's handlers for each instance's GotFocus and LostFocus. A 200 ms Timer logs `Screen.ActiveControl` whenever it
changes. The form reads each instance's extender late-bound through `CallByName`, logging the value with its `TypeName`,
or the error.

The program was built with `/make` as native code and as p-code. It ran in the guest's console session, where the
agent pressed keys with `SendKeys` and marked each key in the log. Native and p-code gave the same lines.
A second set of 92 builds, native and p-code, read or wrote each design-time property early-bound, one per project,
since a compile error stops the build. Every one of those builds gave the same answer in both forms.

### Which are members at run time

From the control's own code, `UserControl.<property>`:

| Property | Read, write |
|---|---|
| `AccessKeys` | both work. It read back `q` as a String, and `= "x"` raised nothing |
| `BackStyle`, `ClipBehavior`, `HitBehavior` | both work. Each reads as an Integer |
| `MaskColor` | both work. A Long |
| `MaskPicture` | both work. A `Picture`, and `Set … = Nothing` raised nothing |
| `PropertyPages` | the compile error "Argument not optional": it takes an index |
| `CanGetFocus`, `ForwardFocus`, `DefaultCancel`, `ControlContainer`, `Windowless`, `InvisibleAtRuntime`, `Alignable`, `EditAtDesignTime`, `Public`, `ToolboxBitmap`, `DataBindingBehavior`, `DataSourceBehavior` | the compile error "Method or data member not found", reading or writing |

So the run-time code of a control can't ask how it was built, apart from its access keys and the four drawing
properties. Passing `UserControl` itself as an argument, as `CallByName(UserControl, …)` does, is the compile error
"Invalid use of base class name".

### What each instance's extender has

Read late-bound from the form. "438" is "Object doesn't support this property or method":

| Extender member | UCPlain1, and the others not named | UCNoFocus1 | UCForward1 | UCInvis1 |
|---|---|---|---|---|
| Name, Parent, Object | yes | yes | yes | yes |
| Index | 343: not an array | 343 | 343 | 343 |
| Left, Top | Singles | Singles | Singles | **393**, "Get not supported at run time" |
| Width, Height, Visible, Container, DragMode, ToolTipText, WhatsThisHelpID | yes | yes | yes | **438** |
| TabIndex | an Integer | **438** | an Integer | **438** |
| TabStop, CausesValidation, HelpContextID | yes | **438** | **438** | **438** |
| SetFocus | works | **438** | **438** | **438** |

- **DefaultCancel adds Default and Cancel**, each False until set. On every other instance both are 438.
- **Alignable adds Align**, an Integer, 0, and Negotiate, which raises 393. On every other instance Align is 438.
- **On every instance:** Enabled and hWnd are 438, since a UserControl gets neither unless it declares one. DataField,
  DataSource, DataMember, DataFormat and DataChanged are 438 as well, UCBind1's included. So `DataBindingBehavior = 1`
  alone doesn't add them. Whether a property marked as bindable does wasn't measured.
- **Early-bound, each 438 above is the compile error "Method or data member not found."** That was measured for:
  - UCInvis1's `Visible` and `SetFocus`;
  - UCNoFocus1's `TabStop` and `TabIndex`;
  - UCForward1's `SetFocus`;
  - `Align` and `Default` on a control without Alignable or DefaultCancel.

  The data members on a control with `DataBindingBehavior = 1` gave the same error.
- **Visible read False on every instance in Form_Load,** before the form was shown, and True once it was.

### Focus, Tab and access keys

- **Tab skips** a control with CanGetFocus False, with ForwardFocus True, and with InvisibleAtRuntime. It stops on every
  other instance, the windowless one included, and on `cmdIn` inside UCCont1 as a stop of its own.
- **A control with CanGetFocus False or InvisibleAtRuntime has no TabIndex**, as the extender table shows. VB6 also drops
  the `TabIndex` its `.frm` line gave it, which leaves a gap. The form listed cmdOut at TabIndex 17 and Command1 at 16,
  past the end, and they took the two free numbers instead, 3 and 8, in the order the form lists them. Setting
  `Text1.TabIndex = 12` later renumbered the rest from 0, without the two controls.
- **SetFocus** works on the windowed and the windowless controls alike. On UCNoFocus1, UCForward1 and UCInvis1 it is
  438.
- **The focus events.** Each row lists the events in the order they fired:

| Focus moving | Events |
|---|---|
| from an intrinsic control to a windowed UserControl | its `EnterFocus`, the form's `UCx1_GotFocus`, its `GotFocus` |
| from a windowed UserControl to an intrinsic | its `LostFocus`, its `ExitFocus`, the form's `UCx1_LostFocus`, then the intrinsic's GotFocus |
| from one UserControl to another | the new one's `EnterFocus`, the old one's `LostFocus` and `ExitFocus`, the form's LostFocus for the old one, the form's GotFocus for the new one, the new one's `GotFocus` |
| from an intrinsic to the windowless UserControl | the form's `UCWless1_GotFocus` first, then its `EnterFocus` and `GotFocus` |

- **An access key** is pressed with Alt. A plain `q` with focus on Command2 did nothing.
  - On a control that can take focus, Alt+Q raises `AccessKeyPress` with **113**, the lower-case letter, and then moves
    focus to it, `EnterFocus` coming after `AccessKeyPress`. With focus already there, a second Alt+Q raised
    `AccessKeyPress` again in two passes and nothing in a third.
  - **ForwardFocus True:** Alt+W moved focus to Text2, the next control in tab order, from Text1, from UCPlain1, from
    Command2 and from UCAccess1. UCForward raised no `AccessKeyPress` and no focus event. In one earlier pass, with focus
    on UCAccess1, Alt+W moved nothing. That wasn't seen again.
  - **CanGetFocus False:** Alt+Z raised nothing on UCNoFocus, and focus went to the first control in tab order that can
    take it. That was Text1, at TabIndex 1 after the Label. Once Text1 had been moved to 12, it was UCPlain1.
  - **A UserControl given focus by another control's access key gets that key.** UCPlain1 has no access keys, yet it
    raised `AccessKeyPress` 122 when Alt+Z forwarded focus to it, and 116 when the Label's `&T` did.
- **DefaultCancel True, with the form setting `UCDefCan1.Default = True` and `.Cancel = True`:**
  - `Ambient.DisplayAsDefault` became True at once, with `AmbientChanged "DisplayAsDefault"`.
  - Enter with focus in Text1 raised `AccessKeyPress` 13 on UCDefCan, and Text1's KeyPress saw nothing. Esc raised
    `AccessKeyPress` 27.
  - `DisplayAsDefault` went False each time a CommandButton took focus, and True again when focus left it, with
    `AmbientChanged` each time. That is the default CommandButton's own behaviour.
  - With focus on Command2, Enter clicked Command2 and reached UCDefCan not at all.

### InvisibleAtRuntime

- **UCInvis raised Initialize, ReadProperties and Terminate, and nothing else:** no Resize, Paint, Show or Hide.
  Every other control raised Resize as it loaded, Show after Form_Load and before Form_Activate, Paint once shown, and
  Hide before its Terminate when the form unloaded.
- **Its window exists and is never shown.** `UserControl.hWnd` is nonzero and `IsWindowVisible` is 0. The screen pixel
  at its centre was the form's red.
- **Its extender has almost nothing,** as the table shows. `Set cmdOut.Container = UCInvis1` is error 425.

### ControlContainer

- **`UserControl.ContainedControls.Count`** is 1 on UCCont, for `cmdIn`, and 0 on every other control. The others raised
  no error on reading it.
- **`cmdIn.Container.Name`** is `UCCont1`, and its Left and Top, 120 and 120, are measured inside the control.
- **`Set cmdOut.Container = UCCont1` works**, and cmdOut keeps its Left and Top, 2880 and 4000, now inside UCCont1.
  `Set cmdOut.Container = Me` puts it back.
- **`Set cmdOut.Container = UCPlain1`** is error **425**, "Invalid object use", and cmdOut stays on the form.

### Alignable

`UCAlign1.Align = 2` raised nothing and put the control at Left 0, Top 5100, Width 9000 and Height 900, on a form whose
ScaleWidth and ScaleHeight are 9000 and 6000. So it sits along the bottom edge, at the form's full width and its own
height. `UCPlain1.Align = 2` is 438.

### Windowless and BackStyle

- **Windowless:**
  - `UserControl.hWnd` is 0, with no error, and HasDC is True.
  - It takes focus by Tab and by SetFocus, as above.
  - The pixel at its centre was its own green, so its background is painted.
- **Every windowed control** has a nonzero hWnd, visible once the form is shown.
- **BackStyle 0, transparent:** the pixel at UCTrans1's centre was the form's red, not its green BackColor. With nothing
  drawn on it, the control's window shows what lies behind it. UCPlain1, opaque, showed green. ClipBehavior, HitBehavior,
  MaskColor and MaskPicture, which decide what a transparent control shows and where it takes clicks, aren't measured
  here.

### Event order, for reference

- **Initialize** ran in the form's order, with the windowless control last. Form1's Controls collection also lists the
  windowless controls last, the Label and UCWless1.
- Each control then raised **ReadProperties**, with **Resize** at its size, 1500×900. UCAlign raised Resize again when
  Form_Load set its Align.
- **Show** came after Form_Load and before Form_Activate, in the same order. The first **Paint** was the windowless
  control's, then the rest in order.
- **On Unload,** each raised **Hide** then **Terminate**, in the same order again.

### In HexIDE

None of these properties is acted on, so a control VB6 hides is drawn (#1631).
