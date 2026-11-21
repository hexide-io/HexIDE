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
