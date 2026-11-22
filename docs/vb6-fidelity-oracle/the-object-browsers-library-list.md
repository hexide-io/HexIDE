## The Object Browser's library list: its order (2026-11-16, #4113, design-time, IDE, SP6)

Measured on vb6.exe SP6's IDE in the oracle guest's console session.

**The setup:** a Standard EXE project, `ObProbe`, whose `.vbp` names its libraries in an order that is not alphabetical:
- `Reference=` lines in this order: OLE Automation (`stdole`), Microsoft Scripting Runtime (`Scripting`), Microsoft Windows Common Controls 6.0 (`MSComctlLib`);
- then `Object=` lines: `TABCTL32.OCX` (`TabDlg`), `comdlg32.ocx` (`MSComDlg`), `MSCOMCTL.OCX` (`MSComctlLib` again) and `RICHTX32.OCX` (`RichTextLib`).

The project loaded with nothing missing. I opened the Object Browser with F2 and read its library dropdown's rows through Win32 (`CB_GETLBTEXT`).

| Row | Library |
|---|---|
| 0 | `<All Libraries>`, selected |
| 1 | `MSComctlLib` |
| 2 | `MSComDlg` |
| 3 | `ObProbe`, the project itself |
| 4 | `RichTextLib` |
| 5 | `Scripting` |
| 6 | `stdole` |
| 7 | `TabDlg` |
| 8 | `VB` |
| 9 | `VBA` |
| 10 | `VBRUN` |

**The readings:**
- **`<All Libraries>`, then every library in case-insensitive alphabetical order,** whatever order the `.vbp` names them in.
  - Two rows tell this apart from an ordinal sort: `MSComctlLib` comes before `MSComDlg`, and `stdole` sits between `Scripting` and `TabDlg`.
  - Two rows tell it apart from file order: `stdole` was the first `Reference=`, and `TabDlg` the first `Object=`.
- **The project's own library is sorted in with the rest** (`ObProbe`, row 3). It isn't placed first.
- **A library named on both kinds of line appears once:** `MSComctlLib`.
- **The combo has no `CBS_SORT` style** (`0x50000203`), so the IDE inserts in this order itself.

**HexIDE differs in one respect.** `ObjectBrowserToolViewModel.ReorderLibraries` puts `<All Libraries>`, then the focused project, then the rest alphabetically, ignoring case. VB6 sorts the project in with the rest. That order came with the original import and has no recorded decision behind it, so I've filed a fix as #4202. The rest matches: the case-insensitive sort, and one row for a library named twice.

**Not measured:** a project group, where each project would add a library of its own.

## A form's members in the Object Browser (2026-11-21, #1968, design-time, IDE, SP6)

**The question.** What does VB6's Object Browser list as a project form's members? HexIDE lists only the procedures in
the form's code, so a form that has a control and no code lists nothing at all (#1968).

**The obvious guess was the form's code and its controls. VB6 also lists every property and method of the Form class,
in one alphabetical list with the rest.** It lists no Form events.

### The probe

VB6's IDE, SP6 (VB6.EXE 6.00.9782), in the console session of a clone of the oracle guest, driven through the agent. A
Standard EXE project, `Project1`, held one form, `Form1`, with one CommandButton, `Command1`.
1. F2 opened the Object Browser. A click on its library dropdown, the keys `Pr` and Enter set it to `Project1`.
2. The first class was clicked, and the Classes list walked with Down.
3. The first member was clicked, and the Members list walked with Down.

The lists are owner-drawn and hold no strings, so each item was read from the Details pane (`WM_GETTEXT` on its
`RichEdit20A`) after each Down. A walk ended when the pane stopped changing. Only each pane's first line, the
declaration, is recorded here: VB6's help text below it isn't copied.

**Classes under `Project1`:** `<globals>`, then `Form1`, shown as `Private Class Form1`, member of `Project1`.

### A form with no code: 87 members

They come in case-insensitive alphabetical order. Each names `Unknown1._Form` as the library it's a member of, and
`Unknown1` is unexplained. `Command1` sits between `Cls` and `ControlBox`.

| Kind | Members |
|---|---|
| The control, read-only | `WithEvents Command1 As CommandButton` |
| Read-only properties, 17 | ActiveControl, ControlBox, Controls, Count, HasDC, hDC, hWnd, Image, MaxButton, MDIChild, MinButton, Moveable, Name, ShowInTaskbar, StartUpPosition, WhatsThisButton, WhatsThisHelp |
| Other properties, 47 | Appearance, AutoRedraw, BackColor, BorderStyle, Caption, ClipControls, CurrentX, CurrentY, DrawMode, DrawStyle, DrawWidth, Enabled, FillColor, FillStyle, Font, FontBold, FontItalic, FontName, FontSize, FontStrikethru, FontTransparent, FontUnderline, ForeColor, Height, HelpContextID, Icon, KeyPreview, Left, LinkMode, LinkTopic, MouseIcon, MousePointer, OLEDropMode, Palette, PaletteMode, Picture, RightToLeft, ScaleHeight, ScaleLeft, ScaleMode, ScaleTop, ScaleWidth, Tag, Top, Visible, Width, WindowState |
| Subs, 17 | Circle, Cls, Hide, Line, Move, OLEDrag, PaintPicture, PopupMenu, PrintForm, PSet, Refresh, Scale, SetFocus, Show, ValidateControls, WhatsThisMode, ZOrder |
| Functions, 5 | Point, ScaleX, ScaleY, TextHeight, TextWidth |

The pane gives each property's type, as in `Property Caption As String`, and each method's parameters.

### The same form with code: 96 members

`Form1` was given this code, and the walk repeated:

```vb
Option Explicit
Public PubVar As Long
Private PrivVar As Long
Public Event Ping(ByVal n As Long)

Public Sub PubSub()
End Sub

Private Sub PrivSub()
End Sub

Friend Function FriendFn() As Long
End Function

Public Property Get PubProp() As Long
End Property

Public Property Let PubProp(ByVal v As Long)
End Property

Private Sub Command1_Click()
End Sub

Private Sub Form_Load()
End Sub
```

The list held the 87 above and nine more, each a member of `Project1.Form1`, sorted in among the rest:
- `Private Sub Command1_Click()` and `Private Sub Form_Load()`;
- `Friend Function FriendFn() As Long`;
- `Public Event Ping(ByVal n As Long)`;
- `Private Sub PrivSub()` and `Private PrivVar As Long`;
- `Public Property PubProp As Long`, one member for the Get and the Let;
- `Public Sub PubSub()` and `Public PubVar As Long`.

### Readings

- **A form's members are the Form class's properties and methods, its controls, and everything its code declares, in
  one alphabetical list.**
- **Private members of the project's own form are listed**, event procedures included.
- **A property's accessors are one member.**
- **The Form class's events aren't listed.** An event the code declares is.

**Not measured:**
- a control array, a menu, and a form that is an MDIForm or MDI child;
- the icons that mark each kind;
- the members of a class module or a UserControl;
- whether Show Hidden Members adds any.

**In HexIDE.** #1968 tracks listing a form's members.

## The VB library's Form class (2026-11-22, #5293, design-time, IDE, SP6)

**The question.** The section above lists what a project form shows, and no Form events among it. What does the
Object Browser list for the class itself, `Form` under the `VB` library? HexIDE lists that class's properties and
events, but none of its methods (#5293).

**The obvious guess was the same 86 members as a project form's. It lists those 86 and the Form class's 31 events,
117 in all, in one alphabetical list.**

### The probe

VB6's IDE, SP6 (VB6.EXE 6.00.9782), in the console session of a clone of the oracle guest, driven through the agent, on
the same `Project1` as above. One fresh vb6.exe per run.
1. F2 opened the Object Browser. A click on its library dropdown, the key `V` and Enter set it to `VB`, the fourth of
   six entries.
2. **The classes:** the second entry of the Classes list was clicked, and the list walked with Down.
3. **The members:** in a second run, Home and eleven Downs in the Classes list selected `Form`, and the Details pane
   read `Class Form`, member of `VB`. Then the first member was clicked, Home pressed, and the Members list walked with
   Down.

Each item was read from the Details pane, as above, and only its first line is recorded.

### The classes under `VB`: 35 entries

The list's count was 35. The walk started at the second entry, so the first wasn't read; under `Project1` the first
was `<globals>`. The other 34, in order, each `Class <name>`, member of `VB`:

App, CheckBox, Clipboard, ComboBox, CommandButton, Control, Data, DirListBox, DriveListBox, FileListBox, Form, Frame,
Global, HScrollBar, Image, Label, Licenses, Line, ListBox, MDIForm, Menu, OLE, OptionButton, PictureBox, Printer,
PropertyPage, Screen, Shape, TextBox, Timer, UserControl, UserDocument, VBControlExtender, VScrollBar.

### `Form`'s members: 117

Each was shown as a member of `VB.Form`. A project form's members read `Unknown1._Form` there, in the section above.

| Kind | Count |
|---|---|
| Events | 31 |
| Properties, read-only | 17 |
| Other properties | 47 |
| Subs | 17 |
| Functions | 5 |

**The 86 that aren't events** have the same names as the 86 Form members a project form lists, in the section above.
Their read-only flags are the same.

**Eight declarations read differently** from a project form's:

| Member | `VB.Form` | A project form |
|---|---|---|
| `Icon`, `Image`, `MouseIcon`, `Palette`, `Picture` | `Property Picture As IPictureDisp` | `Property Picture As StdPicture` |
| `PaintPicture` | `Sub PaintPicture(Picture As IPictureDisp, X1 As Single, ...)` | `... (Picture As StdPicture, ...)` |
| `Controls` | `Property Controls As object` | `Property Controls As Object` |
| `PopupMenu` | `Sub PopupMenu(Menu As object, [Flags], [X], [Y], [DefaultMenu])` | `... (Menu As Object, ...)` |

The first row shows `Picture`'s declaration; the other four differ in the same way.

**The 31 events,** as declared:

| Event | Declaration |
|---|---|
| Activate, Click, DblClick, Deactivate, GotFocus, Initialize, LinkClose, Load, LostFocus, Paint, Resize, Terminate | `Event <name>()` |
| DragDrop | `Event DragDrop(Source As Control, X As Single, Y As Single)` |
| DragOver | `Event DragOver(Source As Control, X As Single, Y As Single, State As Integer)` |
| KeyDown, KeyUp | `Event KeyDown(KeyCode As Integer, Shift As Integer)` |
| KeyPress | `Event KeyPress(KeyAscii As Integer)` |
| LinkError | `Event LinkError(LinkErr As Integer)` |
| LinkExecute | `Event LinkExecute(CmdStr As String, Cancel As Integer)` |
| LinkOpen | `Event LinkOpen(Cancel As Integer)` |
| MouseDown, MouseMove, MouseUp | `Event MouseDown(Button As Integer, Shift As Integer, X As Single, Y As Single)` |
| OLECompleteDrag | `Event OLECompleteDrag(Effect As Long)` |
| OLEDragDrop | `Event OLEDragDrop(Data As DataObject, Effect As Long, Button As Integer, Shift As Integer, X As Single, Y As Single)` |
| OLEDragOver | `Event OLEDragOver(Data As DataObject, Effect As Long, Button As Integer, Shift As Integer, X As Single, Y As Single, State As Integer)` |
| OLEGiveFeedback | `Event OLEGiveFeedback(Effect As Long, DefaultCursors As Boolean)` |
| OLESetData | `Event OLESetData(Data As DataObject, DataFormat As Integer)` |
| OLEStartDrag | `Event OLEStartDrag(Data As DataObject, AllowedEffects As Long)` |
| QueryUnload | `Event QueryUnload(Cancel As Integer, UnloadMode As Integer)` |
| Unload | `Event Unload(Cancel As Integer)` |

### Readings

- **The VB library's `Form` class lists its events, its properties and its methods together, in one alphabetical
  list.** A project form lists the same properties and methods and leaves the events out.
- **A picture-typed member is `IPictureDisp` on the class, and `StdPicture` on a project form.**

**Not measured:**
- the other classes' members, MDIForm's among them;
- whether Show Hidden Members adds any;
- the icons that mark each kind.

**In HexIDE.** #5293 tracks the Form class's member table.

## A control array, menus, a UserControl and a class module in the Object Browser (2026-11-22, #1968, design-time, IDE, SP6)

**The question.** Four of the cases "A form's members in the Object Browser" left unmeasured: how a control array and
menus are listed among a form's members, and what a UserControl and a class module list.

### The probe

As in that section, with VB6's IDE, SP6 (VB6.EXE 6.00.9782), in a clone of the oracle guest, and one fresh vb6.exe. A
Standard EXE project, `Project1`, held three components:
- **`Form1`:** a CommandButton array, `Command1(0)` and `Command1(1)`, and a menu, `mnuFile`, holding `mnuOpen`. No code.
- **`UserControl1`,** private to the project: one TextBox, `Text1`, and `Public Sub Ping()`.
- **`Class1`:** `Public Sub Ping()` and `Private Sub Class_Initialize()`.

With the library set to `Project1`, the Classes list was walked. Then each class was selected with Home and Downs, and
its Members list walked from Home.

### What VB6 lists

**The classes:** `<globals>`, then `Private Class Class1`, `Private Class Form1` and `Private Class UserControl1`, each
a member of `Project1`.

| Class | Members | What they are |
|---|---|---|
| `Class1` | 2 | `Private Sub Class_Initialize()` and `Public Sub Ping()`, members of `Project1.Class1` |
| `Form1` | 89 | the 86 Form members; `WithEvents Command1 As CommandButton`, **once**; `WithEvents mnuFile As Menu`; `WithEvents mnuOpen As Menu` |
| `UserControl1` | 86 | the UserControl class's 84; `WithEvents Text1 As TextBox`; `Public Sub Ping()` |

- **Every list was in case-insensitive alphabetical order.**
- **Each `WithEvents` line was read-only.** On `UserControl1`, `Text1` was a member of `Unknown2._UserControl`, as the
  class's own members were, and `Ping` a member of `Project1.UserControl1`.
- **The control array's line** was the same as a single control's in the earlier section.

**The UserControl class's 84 members:**

| Kind | Members |
|---|---|
| Read-only properties, 17 | ActiveControl, Ambient, ContainedControls, ContainerHwnd, Controls, Count, DataMembers, EventsFrozen, Extender, HasDC, hDC, hWnd, Hyperlink, Image, Name, Parent, ParentControls |
| Other properties, 45 | AccessKeys, Appearance, AutoRedraw, BackColor, BackStyle, BorderStyle, ClipBehavior, ClipControls, CurrentX, CurrentY, DrawMode, DrawStyle, DrawWidth, Enabled, FillColor, FillStyle, Font, FontBold, FontItalic, FontName, FontSize, FontStrikethru, FontTransparent, FontUnderline, ForeColor, Height, HitBehavior, KeyPreview, MaskColor, MaskPicture, MouseIcon, MousePointer, OLEDropMode, Palette, PaletteMode, Picture, PropertyPages, RightToLeft, ScaleHeight, ScaleLeft, ScaleMode, ScaleTop, ScaleWidth, Tag, Width |
| Subs, 16 | AsyncRead, CancelAsyncRead, Circle, Cls, DataMemberChanged, Line, OLEDrag, PaintPicture, PopupMenu, PropertyChanged, PSet, Refresh, Scale, SetFocus, Size, ValidateControls |
| Functions, 6 | CanPropertyChange, Point, ScaleX, ScaleY, TextHeight, TextWidth |

`PropertyPages` is declared `Property PropertyPages(Integer) As String`.

### Readings

- **A control array is one member,** declared as a single control is.
- **Every menu item is a member,** a submenu as well as its parent.
- **A UserControl lists as a form does,** with its own class in place of Form: the class's properties and methods, its
  controls and its code, and no class events.
- **A class module lists only what its code declares,** a private event procedure included.

**Not measured:**
- an MDIForm, an MDI child, and a PropertyPage;
- a UserControl's `Event` declared in its code;
- whether Show Hidden Members adds any.
