## A UserControl's own properties in the designer and the files (2026-10-26, #1631, design-time, IDE)

**The question.** The companion to [A UserControl's own properties at run time](a-usercontrols-own-properties-at-run-time.md):
how VB6 writes these properties in the `.ctl` and the `.frm`, which combinations it refuses, what the Properties window
lists for an instance, and what the designer draws. The PropertyPage's two, HelpContextID and StandardSize, are here
too.

**The probes.**
- **The save:** the run-time entry's project of eleven UserControls was opened in VB6's IDE (SP6) on the guest's console
  through the agent. A comment was typed at the end of each file's code to make it dirty, since Ctrl+S writes nothing
  for a file that isn't. Each file was then saved with Ctrl+S, and the form likewise.
- **The Properties window:** each instance on the form was clicked in its designer. Its list was read with
  `LB_GETCOUNT` and captured eight rows at a time, as in [What VB6's Properties window lists for a UserControl and a
  PropertyPage](what-vb6s-properties-window-lists-for-a-usercontrol-and-a-propertypage.md).
- **The combinations:** projects whose `.ctl` pairs two settings, or holds a constituent control, built with `/make` as
  native code and as p-code. Both builds gave the same answers.
- **The PropertyPages:** three `.pag` files in a Standard EXE, opened and saved in the IDE the same way.

### The `.ctl` lines

VB6's save of each `.ctl` gave back the root's lines exactly as the probe had written them, apart from the spacing of one
comment. The lines were in alphabetical order going in, and stayed in it. Whether VB6 sorts lines given out of order
wasn't measured.

```
   AccessKeys      =   "q"
   Alignable       =   -1  'True
   BackStyle       =   0  'Transparent
   CanGetFocus     =   0   'False
   ControlContainer=   -1  'True
   DataBindingBehavior=   1  'vbSimpleBound
   DefaultCancel   =   -1  'True
   ForwardFocus    =   -1  'True
   InvisibleAtRuntime=   -1  'True
   Windowless      =   -1  'True
```

- A name of 16 characters or more is followed by `=` with no space, as for any long property name.
- **BackStyle's comment has two spaces before it**, `0  'Transparent`, where the probe had written three. A Boolean
  `False` has three, `0   'False`, and a `True` two.
- The `Begin VB.UserControl` line ends with a space after the name, as a form's `Begin` line does.

### Combinations VB6 won't load

Each row below loaded with errors. VB6 wrote a `.log` beside the file, `/make` reported "Errors during load", and the
build failed:

| `.ctl` | `UCX.log` | And then |
|---|---|---|
| `CanGetFocus = 0 'False`, holding a TextBox | "Can't have child controls capable of receiving focus on a control that cannot receive focus" | `Form1.log`: "Line 11: Cannot load control UCX1." |
| `Windowless = -1 'True`, holding a TextBox | "Line 10: Cannot create embedded object in Text1." | the `.ctl` could not be loaded |
| `Windowless` and `ControlContainer`, both True | "Line 2: Property ControlContainer in UCX could not be set." | `Form1.log`: "Line 11: Cannot load control UCX1." |

These loaded and built, each reading back as the run-time entry describes:

| `.ctl` | Its extender |
|---|---|
| CanGetFocus False, holding a Label | no TabIndex or TabStop. The Label's Caption reads back |
| Windowless True, holding a Label | `UserControl.hWnd` is 0 |
| Windowless True and BackStyle 0 | `UserControl.hWnd` is 0 |
| ForwardFocus True and CanGetFocus False | **a TabIndex, 0**, and no TabStop. ForwardFocus keeps the TabIndex that CanGetFocus False alone takes away |
| DefaultCancel True and CanGetFocus False | Default, and no TabIndex or TabStop |
| InvisibleAtRuntime True and Alignable True | no Align: InvisibleAtRuntime wins |
| InvisibleAtRuntime True and ControlContainer True | as InvisibleAtRuntime alone |
| `Attribute VB_Exposed = True`, a Public UserControl, in a Standard EXE | built and ran with no message |

### The `.frm`

VB6's save of the form differed from the hand-written one in four ways:
- **UCNoFocus1, CanGetFocus False, has no `TabIndex` line.**
- **UCInvis1, InvisibleAtRuntime, keeps only `Left`, `Top`, `_ExtentX` and `_ExtentY`.** Its Height, Width and TabIndex
  lines are gone, and its size is held in the extents alone:

  ```
     Begin Probe.UCInvis UCInvis1 
        Left            =   1800
        Top             =   1680
        _ExtentX        =   2646
        _ExtentY        =   1588
     End
  ```
- **The windowless controls are written last:** the Label and UCWless1 moved from their place in the file to the end of
  the form's block, in the order Form1's Controls collection lists them.
- **The TabIndex numbers VB6 gave at load were written:** cmdOut's 17 is now 3, and Command1's 16 is now 8, the gaps the
  two controls without a TabIndex left.

UCCont1's CommandButton stayed nested in its block, after the extents. The instances of DefaultCancel and Alignable
controls wrote no Default, Cancel or Align line, since each was at its default.

### The Properties window for an instance

| Instance | Rows | Its list |
|---|---|---|
| UCPlain1, and UCAccess1, UCCont1, UCWless1, UCTrans1, UCBind1 | 16 | (Name), CausesValidation, DragIcon, DragMode, Height, HelpContextID, Index, Left, TabIndex, TabStop, Tag, ToolTipText, Top, Visible, WhatsThisHelpID, Width |
| UCNoFocus1, CanGetFocus False | 12 | without CausesValidation, HelpContextID, TabIndex and TabStop |
| UCForward1, ForwardFocus True | 13 | without CausesValidation, HelpContextID and TabStop |
| UCDefCan1, DefaultCancel True | 18 | with Cancel and Default, both False |
| UCAlign1, Alignable True | 18 | with Align, `0 - vbAlignNone`, and Negotiate, False |
| UCInvis1, InvisibleAtRuntime True | 5 | (Name), Index, Left, Tag and Top |

So the list is the extender's members, as they are at run time. UCBind1's `DataBindingBehavior = 1` added no data rows.

### The designer

In Form1's designer every instance was drawn, UCInvis1 included, as a box of its own BackColor. UCTrans1, BackStyle 0,
was transparent there as at run time: the designer showed the form's red through it.

### The PropertyPage's HelpContextID and StandardSize

- **HelpContextID is a line of the file.** `HelpContextID = 7` was kept by VB6's save, after `ClientWidth`.
- **StandardSize is not.** A `.pag` holding `StandardSize = 1 'Small`, or `2 'Large`, loaded with a log reading "Line
  11: The property name StandardSize in PPLarge is invalid." VB6's save dropped the line. A page with no line showed
  `0 - Custom` in the Properties window. So StandardSize only sets the page's size when chosen in the designer, and the
  file holds the size it set. Which sizes Small and Large set wasn't measured: the rig's double-clicks on the row didn't change it.
- **VB6 writes `VB_Creatable = False` for a private PropertyPage,** where the probe had written True. A private
  UserControl's `VB_Creatable = True` was kept.
- A height of 2000 twips came back as 1995, which is 133 pixels, as a form's does.

### Not measured here

- ClipBehavior, HitBehavior, MaskColor and MaskPicture: what a transparent control shows and where it takes clicks.
- ToolboxBitmap and its `.ctx`, PropertyPages and its line, EditAtDesignTime, DataSourceBehavior, and a bound property
  under DataBindingBehavior.
- What the designer says when one of the refused combinations is chosen in the Properties window, rather than loaded
  from a file, and what it does with Public True in a Standard EXE.

### In HexIDE

A loaded `.ctl` or `.pag` keeps each of these lines as the file's own text, and nothing acts on it (#1631). What a HexIDE
save of the form above writes wasn't checked against VB6's.
