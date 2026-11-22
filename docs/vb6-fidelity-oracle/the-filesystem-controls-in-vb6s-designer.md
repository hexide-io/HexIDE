## The filesystem controls in VB6's designer (2026-11-21, #164, SP6)

**The question.** What do the DriveListBox, DirListBox and FileListBox offer at design time? What does VB6's save write
for each, in what order, and how does the chain behave once wired? HexIDE doesn't model them yet (#164), so these
rows are what its model and writer will be checked against.

**The rig.** VB6.EXE 6.00.9782 (SP6) in a clone of the oracle VM, driven on its console through Hyper-V's synthetic
keyboard and mouse. Files were read in the guest over PowerShell Direct. Each project was a one-form Standard EXE,
the form's client area 6000 by 4500 twips.

### What the Properties window lists

Read from the Alphabetic tab with each control selected:

| Control | Rows | Its properties |
|---|---|---|
| DriveListBox | 24 | `(Name)`, Appearance, BackColor, CausesValidation, DragIcon, DragMode, Enabled, Font, ForeColor, Height, HelpContextID, Index, Left, MouseIcon, MousePointer, OLEDropMode, TabIndex, TabStop, Tag, ToolTipText, Top, Visible, WhatsThisHelpID, Width |
| DirListBox | 25 | the DriveListBox's, and OLEDragMode |
| FileListBox | 32 | the DirListBox's, and Archive, Hidden, MultiSelect, Normal, Pattern, ReadOnly and System |

- **The DriveListBox has no OLEDragMode.** The other two have it.
- **`Drive`, `Path`, `FileName` and the lists aren't design-time properties.** None of them is listed.
- **The defaults shown:**
  - Appearance `1 - 3D`, BackColor `&H80000005`, ForeColor `&H80000008`, Font MS Sans Serif;
  - CausesValidation, Enabled, TabStop and Visible True;
  - DragMode `0 - Manual`, MousePointer `0 - Default`, OLEDragMode `0 - Manual`, OLEDropMode `0 - None`;
  - HelpContextID and WhatsThisHelpID 0.
- **The FileListBox's own defaults:** Archive True, Hidden False, MultiSelect `0 - None`, Normal True, Pattern `*.*`,
  ReadOnly True and System False.

### What a save writes

**Defaults only.** Each control was added with a double-click on its toolbox button and the form saved. Each block
held only these five lines, in alphabetical order:

| Control | Height | Left | TabIndex | Top | Width |
|---|---|---|---|---|---|
| FileListBox | 480 | 2400 | 2 | 2010 | 1215 |
| DirListBox | 315 | 2400 | 1 | 2010 | 1215 |
| DriveListBox | 315 | 2400 | 0 | 2010 | 1215 |

The blocks come last-added first, as above.

**Every property set.** A hand-written form set every listed property except DragIcon, MouseIcon and Index to a
value other than its default, writing the lines in **reverse** alphabetical order, so that the order VB6 saved them in
would be its own. The Font was Arial 9.75, bold and italic.
- **The load:** clean, with no message and no `.log`.
- **The edit:** a Replace All of a comment in the form's code.
- **The save:** Ctrl+S.

VB6 wrote the FileListBox like this:

```
   Begin VB.FileListBox File1 
      Appearance      =   0  'Flat
      Archive         =   0   'False
      BackColor       =   &H00C0FFFF&
      CausesValidation=   0   'False
      DragMode        =   1  'Automatic
      Enabled         =   0   'False
      BeginProperty Font 
         Name            =   "Arial"
         Size            =   9.75
         Charset         =   0
         Weight          =   700
         Underline       =   0   'False
         Italic          =   -1  'True
         Strikethrough   =   0   'False
      EndProperty
      ForeColor       =   &H000000FF&
      Height          =   2430
      HelpContextID   =   5
      Hidden          =   -1  'True
      Left            =   3480
      MousePointer    =   2  'Cross
      MultiSelect     =   2  'Extended
      Normal          =   0   'False
      OLEDragMode     =   1  'Automatic
      OLEDropMode     =   1  'Manual
      Pattern         =   "*.txt;*.frm"
      ReadOnly        =   0   'False
      System          =   -1  'True
      TabIndex        =   2
      TabStop         =   0   'False
      Tag             =   "tag"
      ToolTipText     =   "tip"
      Top             =   1185
      Visible         =   0   'False
      WhatsThisHelpID =   7
      Width           =   2535
   End
```

- **The order is alphabetical,** whatever order the file gave. The Font block sits where `Font` falls, between
  `Enabled` and `ForeColor`.
- **`CausesValidation=`** has no space before the `=`, since the name fills the 16-character column.
- **The DirListBox and DriveListBox blocks are the same,** without the FileListBox's seven properties. The DriveListBox
  also has no `OLEDragMode`.
- **Pattern keeps a list of two patterns,** `"*.txt;*.frm"`, as given.

### What the designer shows

- **The DriveListBox** shows a drive icon and `c:`, in lower case.
- **The DirListBox** shows the VB6 process's current folder, here the console user's profile folder.
  - Each ancestor, from `C:\` down, is an open folder, each one indented a step further than the one above.
  - The current folder is an open folder, highlighted.
  - Its subfolders are closed folders, indented one step further again.
- **The non-default values show in the designer:** the colours, the bold italic Arial and the flat border.
  - **`Enabled = False` doesn't grey the text** at design time; it stays in the ForeColor.
  - **`Visible = False` doesn't hide the control.**
- **The FileListBox was empty in both forms.** Its folder held no file it would list: only hidden and system files for
  the defaults, and no `.txt` or `.frm` for the pattern. So whether the designer lists files at all isn't settled.

### The chain at run time

The defaults-only form was given `Dir1.Path = Drive1.Drive` in `Drive1_Change`, and `File1.Path = Dir1.Path` in
`Dir1_Change`, written into the file. It was run with F5 in an IDE started unelevated.
- **At start,** the DriveListBox had the focus, with `c:` highlighted. The DirListBox showed the current folder, as in
  the designer. The FileListBox was empty, since that folder holds only hidden and system files.
- **A double-click on `C:\`** made the DirListBox list `C:\`'s subfolders, each a closed folder under it. They were
  sorted, in their own case, with `~MSSETUP.T` first.
- **The FileListBox then listed `Form1.frm` and `Form1.frx`.** `C:\` holds neither: its only files are `pagefile.sys`,
  `swapfile.sys` and `DumpStack.log.tmp`, all hidden and system. The two files are in the console user's VirtualStore,
  left by an earlier probe. The IDE runs without a manifest, so Windows' file virtualisation shows them in `C:\`.
  That is Windows, not VB6: an elevated IDE, or a program with a manifest, wouldn't see them.

### Not measured

- **DragIcon and MouseIcon,** whose values go to the `.frx`, and an `Index`.
- **What the FileListBox shows in the designer** for a folder with files it would list.
- **A single click in the DirListBox,** and the Drive list's drop-down.
- **The release build.** Every row here is SP6.

### The designer's FileListBox lists files (2026-11-21, #164, SP6)

**The question.** Above, the designer's FileListBox showed nothing. Its folder held no file it would list, so whether
the designer lists files at all was left open.

**The probe.** The same defaults-only form, opened in the same way. Before the project opened, a plain file,
`aaa-fileprobe.txt`, with only the Archive attribute, was put in the folder VB6 starts in, which is the console user's
profile folder.

**What the designer showed.** The FileListBox listed `aaa-fileprobe.txt`, and the DirListBox showed the same folder as
before. So at design time the FileListBox lists the files in VB6's current folder, as it does at run time. Those are
the files on disk when the designer is drawn, not anything saved in the form.

The file was deleted afterwards.
