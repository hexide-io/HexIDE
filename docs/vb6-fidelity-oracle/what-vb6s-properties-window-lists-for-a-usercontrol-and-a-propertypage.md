## What VB6's Properties window lists for a UserControl and a PropertyPage (2026-10-23, #1585, design-time, IDE)

**The question.** HexIDE gave a UserControl's and a PropertyPage's designer root a form's component, so both
Properties windows listed a form's properties, Caption included, and none of their own. What does VB6's list for each?

**The probe.** VB6.EXE 6.00.8176, the IDE, in the guest's console session through the agent, the same rig as #1538's.
`probe.vbp` is a Standard EXE holding `Module1.bas` alone. Opened in the IDE, then Project > Add User Control (`u`),
or Add Property Page (`p`), Enter on the dialog's default, then F4. The Properties window opened on its Alphabetic
tab (`TCM_GETCURSEL` 0) with the new root selected.

**Reading the list, and the trap in it.** The Properties window is `wndclass_pbrs`. Its list is a `ListBox` of style
`0x54A10513`: owner-drawn, sorted and with no strings, since `LBS_HASSTRINGS` is clear. So `LB_GETCOUNT` gives the row
count, 51 and 33, but `LB_GETTEXT` returns each row's item data, 4 bytes read as text, and not the name. The rows were
read instead by scrolling the list with `LB_SETTOPINDEX`, eight rows at a time, and capturing the list's own
rectangle each time. In each capture, the first row's value column shows the root's name, from the in-place edit box
over the selected row, not that property's value.

**A new UserControl: 51 rows.** Each with its value as VB6 showed it:

| Property | Value | Property | Value | Property | Value |
|---|---|---|---|---|---|
| (Name) | UserControl1 | DrawWidth | 1 | MousePointer | 0 - Default |
| AccessKeys | | EditAtDesignTime | False | OLEDropMode | 0 - None |
| Alignable | False | Enabled | True | Palette | (None) |
| Appearance | 1 - 3D | FillColor | &H00000000 | PaletteMode | 3 - Container |
| AutoRedraw | False | FillStyle | 1 - Transparent | Picture | (None) |
| BackColor | &H8000000F | Font | MS Sans Serif | PropertyPages | (None) |
| BackStyle | 1 - Opaque | FontTransparent | True | Public | False |
| BorderStyle | 0 - None | ForeColor | &H80000012 | RightToLeft | False |
| CanGetFocus | True | ForwardFocus | False | ScaleHeight | 3600 |
| ClipBehavior | 1 - Use Region | HasDC | True | ScaleLeft | 0 |
| ClipControls | True | Height | 3600 | ScaleMode | 1 - Twip |
| ControlContainer | False | HitBehavior | 1 - Use Region | ScaleTop | 0 |
| DataBindingBehavior | 0 - vbNone | InvisibleAtRuntime | False | ScaleWidth | 4800 |
| DataSourceBehavior | 0 - vbNone | KeyPreview | False | Tag | |
| DefaultCancel | False | MaskColor | &H8000000F | ToolboxBitmap | (None) |
| DrawMode | 13 - Copy Pen | MaskPicture | (None) | Width | 4800 |
| DrawStyle | 0 - Solid | MouseIcon | (None) | Windowless | False |

Three names were cut at the column's edge in the capture: `DataBindingBehav`, `DataSourceBehav` and
`InvisibleAtRuntim`. They are completed here from VB6's own names, not read. The colours were cut at the list's right
edge too, and are given as far as they could be read.

**A new PropertyPage: 33 rows:**

| Property | Value | Property | Value | Property | Value |
|---|---|---|---|---|---|
| (Name) | PropertyPage1 | FontTransparent | True | PaletteMode | 0 - Halftone |
| Appearance | 1 - 3D | ForeColor | &H80000012 | Picture | (None) |
| AutoRedraw | False | HasDC | True | RightToLeft | False |
| BackColor | &H8000000F | Height | 3600 | ScaleHeight | 3600 |
| Caption | PropertyPage1 | HelpContextID | 0 | ScaleLeft | 0 |
| ClipControls | True | KeyPreview | False | ScaleMode | 1 - Twip |
| DrawMode | 13 - Copy Pen | MouseIcon | (None) | ScaleTop | 0 |
| DrawStyle | 0 - Solid | MousePointer | 0 - Default | ScaleWidth | 4800 |
| DrawWidth | 1 | OLEDropMode | 0 - None | StandardSize | 0 - Custom |
| FillColor | &H00000000 | Palette | (None) | Tag | |
| FillStyle | 1 - Transparent | | | Width | 4800 |
| Font | MS Sans Serif | | | | |

**What it shows:**
- **Neither has Left, Top or Visible**, which a form's list has.
- **A UserControl has no Caption, and a PropertyPage has one**, the tab it shows under. Neither has a form's Icon,
  ShowInTaskbar, StartUpPosition, MaxButton or MinButton. **A PropertyPage has no Enabled.**
- **`PaletteMode` differs:** 3 - Container for a new UserControl, 0 - Halftone for a new PropertyPage. A new `.pag`
  writes `PaletteMode = 0 'Halftone` (#1538), the value its window shows, and a new `.ctl` writes no `PaletteMode`.
  Why one is written and the other not was not measured.

**In HexIDE (#1585):** each root has a class of its own, `UserControlRootClass` and `PropertyPageRootClass`. Each lists
the rows of VB6's above that HexIDE models:
- the UserControl: (Name), BackColor, Enabled, Font, ForeColor, Height, KeyPreview, Tag and Width;
- the PropertyPage: (Name), BackColor, Caption, Font, ForeColor, Height, KeyPreview, Tag and Width.

The root's `ClientLeft` and `ClientTop` still bind to its geometry, though neither lists Left or Top. A line a file
holds that its root doesn't list is kept as the file's own text. That includes the Caption a UserControl HexIDE made
before #1538 carried. The rest of each list is #1631.

