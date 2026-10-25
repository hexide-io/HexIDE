## A UserControl's and a PropertyPage's own events (2026-10-24, #1632, design-time, IDE, release build and SP6)

**The question.** HexIDE's designer wrote `Form_Load` for a double-click on any designer's background, a UserControl's
and a PropertyPage's included. The code window offered a root a form's five events, Click, DblClick and the mouse
three. Which procedure does VB6's double-click write for each root, and which events does each offer?

**The probe.** The IDE, in the guest's console session through the agent, on both builds:
- the release build, VB6.EXE 6.00.8176;
- SP6, VB6.EXE 6.00.9782 with VBA6.DLL 6.0.9782.

`probe.vbp` is an ActiveX Control project (`Type=Control`) holding one root, read from its file:
`UserControl1.ctl`, or `PropertyPage1.pag` with `Attribute VB_Exposed = False`. The steps:
1. The designer was opened from the Project window, as in "What VB6's Properties window shows a position in".
2. The real pointer double-clicked the middle of the root's surface. That is the window of class
   `ThunderUserControl` or `ThunderPropertyPage`.
3. The code window's Object and Procedure boxes were read with `CB_GETLBTEXT`. The Procedure box is filled only when
   its button is clicked, so it was clicked with the real pointer before it was read.
4. Ctrl+S saved, and the saved file was read.

A second run on SP6 chose every event in the Procedure box in turn and saved, so that every stub VB6 writes is in the
file.

**The double-click.**

| Root | Object box | Procedure box | The saved file gained |
|---|---|---|---|
| UserControl | `UserControl` | `Initialize` | `Private Sub UserControl_Initialize()`, a blank line, `End Sub` |
| PropertyPage | `PropertyPage` | `SelectionChanged` | `Private Sub PropertyPage_SelectionChanged()`, a blank line, `End Sub` |

**The events, in the Procedure box's order, with the arguments VB6's stubs declare.** A UserControl offers 35:

| Event | Arguments | Event | Arguments |
|---|---|---|---|
| AccessKeyPress | `KeyAscii As Integer` | KeyUp | `KeyCode As Integer, Shift As Integer` |
| AmbientChanged | `PropertyName As String` | LostFocus | |
| AsyncReadComplete | `AsyncProp As AsyncProperty` | MouseDown | `Button As Integer, Shift As Integer, X As Single, Y As Single` |
| AsyncReadProgress | `AsyncProp As AsyncProperty` | MouseMove | the same |
| Click | | MouseUp | the same |
| DblClick | | OLECompleteDrag | `Effect As Long` |
| DragDrop | `Source As Control, X As Single, Y As Single` | OLEDragDrop | `Data As DataObject, Effect As Long, Button As Integer, Shift As Integer, X As Single, Y As Single` |
| DragOver | `Source As Control, X As Single, Y As Single, State As Integer` | OLEDragOver | as OLEDragDrop, then `State As Integer` |
| EnterFocus | | OLEGiveFeedback | `Effect As Long, DefaultCursors As Boolean` |
| ExitFocus | | OLESetData | `Data As DataObject, DataFormat As Integer` |
| GetDataMember | `DataMember As String, Data As Object` | OLEStartDrag | `Data As DataObject, AllowedEffects As Long` |
| GotFocus | | Paint | |
| Hide | | ReadProperties | `PropBag As PropertyBag` |
| HitTest | `X As Single, Y As Single, HitResult As Integer` | Resize | |
| Initialize | | Show | |
| InitProperties | | Terminate | |
| KeyDown | `KeyCode As Integer, Shift As Integer` | WriteProperties | `PropBag As PropertyBag` |
| KeyPress | `KeyAscii As Integer` | | |

A PropertyPage offers 24. The 21 it shares with a UserControl have the same arguments: Click, DblClick, DragDrop,
DragOver, GotFocus, Initialize, KeyDown, KeyPress, KeyUp, LostFocus, MouseDown, MouseMove, MouseUp, the six OLE events,
Paint and Terminate. Its three of its own are `ApplyChanges()`, `EditProperty(PropertyName As String)` and
`SelectionChanged()`.

**What it shows:**
- **A double-click on a designer's background writes the root's own event:** Initialize for a UserControl and
  SelectionChanged for a PropertyPage, as a form's writes Load (#1532). Neither root has a Load.
- **Each root has events of its own,** 35 and 24, and neither has a form's Activate, Deactivate, Load, QueryUnload or
  Unload.
- **Release and SP6 agree** on all of it: the double-click, both lists, and the save below.

**And something the save did besides.** The PropertyPage's save rewrote `Attribute VB_Exposed = False` as `True`, on
both builds. The UserControl's kept `False`. That is #1661.

**In HexIDE (#1632):**
- `UserControlRootClass` and `PropertyPageRootClass` carry the events above, in that order and with those arguments.
  The code window offers them for the root and writes their stubs as VB6 does.
- A double-click on the background writes the root's own procedure: `Form_Load`, `UserControl_Initialize` or
  `PropertyPage_SelectionChanged`.
- The code window lists a root under its own name (#937), so a stub chosen there is `UserControl1_Initialize` where
  VB6 writes `UserControl_Initialize`. The double-click writes VB6's name.
- None of these events is raised yet. HexIDE has no UserControl run time: `docs/MISSING_LANGUAGE.md` records
  `UserControl` and `Ambient` as undefined names.

