## The OLE container, the Data control, and two controls with one name (2026-10-12, #856, `/make` and IDE, SP6)

**Where these come from.** These rows were measured on the oracle VM (VB6 6.00.8176 SP6) on 2026-10-12, while
surveying how HexIDE handles forms holding these controls. Until now they were only in #856's body. They're recorded
here as measured then, with the probe sources at the end. Two rows are still owed; see "Not measured".

**The builds.** The hand-written probe `.vbp` files have no `CompilationType` line, and VB6's own Save Project As
writes `CompilationType=0`. By #842 both mean native code, so every `/make` row here is native, and none was built as
p-code. The saved-form rows come from the VB6 IDE. The OLE run-time rows (31004, 31018, 424) are error paths read
under `On Error Resume Next`.

### VB.OLE

Probes A to E were in `C:\hexide-ole-probe\` on the VM, and P1 to P5 in `C:\hexide-ole-verify\`.

- **The type library,** a LoadTypeLibEx dump of VB6.OLB:
  - coclass `OLE` {33ad5000-6699-11cf-b70c-00aa0060d393};
  - default interface `_OLE`, with 122 funcdescs;
  - source interface `OLEEvents`, with 16 events, among them `Updated(Code)`, `ObjectMove` and `Validate`.
  - `DataSource` isn't a member of `_OLE`.
  - Every funcdesc in `_OLE` has `wFuncFlags` 0, even the read-only `hWnd` and `LpOleObject`. So the library doesn't
    mark which members a `.frm` may set, and FUNCFLAGS has no persistence bit anyway.
- **What `/make` accepts from a `.frm`.** Each of these built with no Form1.log, and every value read back as written.
  That's what VB6 accepts; only E says what it writes.
  - B's six: AutoActivate, Class, DisplayType, OLETypeAllowed, SizeMode and SourceDoc;
  - P2's twenty, listed with their values under the sources;
  - P5's `Verb = 2`.
- **What the IDE writes.** E was saved twice in the GUI with File > Save Form1.frm, and never built. The empty
  container came back exactly as authored, apart from `Left`, which was moved in the IDE before saving (240 to 360):
  ```
     Begin VB.OLE OLE1 
        AutoActivate    =   0  'Manual
        Class           =   "Package"
        DisplayType     =   1  'Icon
        Height          =   1335
        Left            =   360
        OLETypeAllowed  =   1  'Embedded
        SizeMode        =   1  'Stretch
        SourceDoc       =   "C:\hexide-ole-probe\note.txt"
        TabIndex        =   0
        Top             =   240
        Width           =   2175
     End
  ```
  - **The empty save cites no `.frx`.** Whether it wrote one isn't established: E's only `Form1.frx` is timestamped at
    the later embedded save.
  - **After right-click > Create Embedded Object** (a Package), the block gains
    `      OleObjectBlob   =   "Form1.frx":0000` between `Left` and `OLETypeAllowed`.
  - A stray keystroke during that capture renamed the control `s`. That's an artefact of the capture, not VB6
    behaviour.
- **The embedded `.frx` record** is 2584 bytes. It doesn't use the `[4-byte length][payload]` container in
  [The `.frx` payloads have a published specification — the container does not](serialization--file-format-fidelity.md#the-frx-payloads-have-a-published-specification--the-container-does-not-2026-08-19).
  - **Its first 24 bytes** are `18 00 00 0A 00 00 B0 76 54 E4 7F 5A DD 01 00 0A B4 14 80 5A DD 01 00 00`, and a
    compound-file signature `D0 CF 11 E0 A1 B1 1A E1` follows at 0x18.
  - **The first dword,** 0x0A000018, is larger than the file. 2584 − 24 = 2560 = 5 × 512, and bytes 0-1 read as 24 and
    bytes 2-5 as 2560. That's arithmetic fitting one sample, not a layout.
  - **Two timestamps.** Bytes 6-13 and 14-21 decode as little-endian FILETIMEs, 2026-10-12T19:28:42.907Z and
    19:30:04.064Z. The second matches the file's LastWriteTime (20:30:04) if the guest was on UTC+1.
  - So [the determinism finding](serialization--file-format-fidelity.md#frx-blob-encoding-is-deterministic-2026-08-11),
    which found no timestamp in one Icon record, doesn't extend to an embedded OLE record, which carries two.
- **A record in the generic framing whose payload isn't an OLE object is accepted, and no object results** (D1).
  - `OleObjectBlob = "Form1.frx":0000` cited a 27-byte `.frx`: `17 00 00 00` (a length of 23), then the ASCII
    `FAKE-OLE-OBJECT-PAYLOAD`.
  - It built with no log, and `OLEType` read back 3, which is `vbOLENone`, as for the empty container. No rule is
    offered.
- **Refused at load.** Each of these logged `Line 12: Cannot create embedded object in OLE1.` to Form1.log, and the
  build failed. P4's `/out` reads `Build of 'Probe.exe' failed.`. Line 12 is the `Begin VB.OLE OLE1` line, not the
  offending one, which was line 18 in D2.
  - **D2:** the unknown name `OleObjectBlobZz`, citing a `.frx` byte-identical to D1's.
  - **P4:** P2 with `Appearance` replaced by `ZzUnknown = 1`, a plain value with no citation. So the citation isn't
    the cause.
  - **P3:** DataChanged, DataText, FileNumber, Format and Verb together. P5 built with Verb alone, so at least one of
    the other four is refused. They weren't isolated.
- **Run time, the empty container (A):**
  - `TypeName` "OLE", `Controls.Count` 1, `OLEType` 3, and `Class` and `SourceDoc` both "".
  - `SizeMode` 0, `AutoActivate` 2, `DisplayType` 0 and `OLETypeAllowed` 2.
  - `Action = 7` and `DoVerb` raise 31004 "No object". `CreateEmbed ""` raises 31018 "Class is not set". `Delete`
    raises nothing.
  - The `OLEType after` line reads `OLEType after=3 Class=[] err=0`.
  - **Unexplained:** B and D1, whose `.frm` sets `Class = "Package"`, still raise 31018, and then read
    `OLEType after=3 Class=[Package] err=0`.

### Two controls with one name

Every pair logged `Line N: Did not find an index property and control <name> already exists.` to Form1.log, where N
is the second control's `Begin` line.

| Pair | `/make` | Built program |
|---|---|---|
| CommandButton `OLE1` + OLE `OLE1` (C) | `Line 20`; built | `Controls.Count` 0, `TypeName(OLE1)` "Empty"; `Action`, `DoVerb`, `CreateEmbed` and `Delete` raise 424 |
| C's pair as HexIDE writes it after a designer rename, and again with `ole1` beside `OLE1` | `Line 18`; `/out` reads `Errors during load. Refer to '…\Form1.log' for details`, then `Build of '…' succeeded.`; exit 0 | as C (with unique names: Count 2, TypeName "OLE") |
| TextBox `Data1` + Data `Data1`, beside Timer1 and Data2 | `Line 48`; the same `/out`; exit 0 in three fresh directories | Count 2 (Timer1, Data2) against a baseline of 4, so both `Data1`s are gone |
| TextBox `timer1` + Timer `Timer1`, same form | `Line 48`, naming `control timer1`; exit 0 | Count 2: both gone |
| CommandButton `Cmd1` + CommandButton `Cmd1` (P1) | `Line 20`, then no build-result line and no exe, watched for 120 s and on a clean re-run for 20 s; both killed | none; owed, see "Not measured" |

- **Every mixed-type pair measured built, and dropped both controls.**
- **The loader compares names case-insensitively,** and the message prints the second control's own spelling.
- **The one same-type pair produced no result.** Nothing beyond that is claimed.
- **Two raw outputs need care,** noted under the sources.

[Control arrays](control-arrays.md#control-arrays-2026-08-10) and
[What `/make` writes to `/out`](what-make-writes-to-out-and-what-line-n-counts.md) cover the neighbouring cases.

### VB.Data

The probe was a hand-written `.frm` (3084 bytes), opened in the VB6 IDE and saved with File > Save Form1.frm As, so VB6
rewrote the whole file (3422 bytes, `Saved.frm`, excerpted under the sources). Neither the round-trip corpus nor the
repository holds another VB6-saved `VB.Data` form.

1. **Order and layout.** The input and `Saved.frm` differ in four ways and no others, and their code sections are
   identical.
   - (a) None of the input's five `Begin` lines ended with a space, the form's included, and VB6 added one to all five.
   - (b) Data2's input had BOFAction, EOFAction, Height and Left after RecordSource, and VB6 moved them into
     alphabetical position. That's the only evidence of sorting, since Data1's input was already in order.
   - (c) and (d) are the properties VB6 added to Data1 and to Data2, listed in item 2.
   - Data1's `DefaultCursorType=`, which VB6 added, has no padding before `=`, because the name fills the column.
     Data2's input already spelled it so.
2. **Which properties VB6 writes.**
   - **Absent from the input, and written by VB6 at their defaults:** Data1's Connect, DefaultCursorType, DefaultType,
     Exclusive, Options, ReadOnly and RecordsetType, and Data2's DefaultType and Options.
   - **Present in the input, and kept:** Caption, DatabaseName, Height, Left, RecordSource, Top and Width. Whether VB6
     writes these at their defaults isn't measured.
   - **Left absent:** BOFAction and EOFAction were absent from Data1's input and stayed absent.
   - Data2's non-default values were kept.
3. **DefaultType.** VB6 writes `DefaultType = 2 'UseODBC`, and with that value the exe opened BIBLIO.MDB and read
   records.
   - DAO's own type libraries (dao350.dll, dao360.dll) define `WorkspaceTypeEnum` with `dbUseODBC = 1` and
     `dbUseJet = 2`. So 2 is Jet in DAO's numbering, and the `'UseODBC` comment is the likely mislabel.
   - The dumper used didn't find the enum the control's own `DefaultType` is declared against, in VB6.OLB or in
     MSVBVM60.DLL's type library. It's "not located", not "doesn't exist".
   - Item 6 shows the control doesn't always number things as DAO does.
4. **The bound TextBox** gets `DataField` and `DataSource` in alphabetical position, and keeps `Text = "Text1"`.
5. **No `.frx`.** Save Project As added no DAO `Reference=`, and the `.vbp` references only stdole2.
6. **Run time** (the log is under the sources).
   - Reposition fires once after Form_Load's line, before the bound control had its value. Then it fires once on the
     one MoveNext, and once on the one Refresh, that the program ran.
   - `RecordsetType = 0 'Table` gives `Recordset.Type = 1`, which is DAO's `RecordsetTypeEnum.dbOpenTable`. So the
     control and DAO number the same type differently.
7. **`/make` accepts the saved form.** A re-run on Saved.frm itself built, and its log matched the first run's, which
   was made on the hand-written input. The log under the sources is the same from both.
8. **`.vbp` order,** not specific to Data.
   - The hand-written `.vbp` had `Form=` before `Reference=`. Save Project As kept that order while regenerating the
     rest: IconForm, Command32 and the option set.
   - `Reference=` follows `Type=` directly in all six `.vbp` files under the corpus's `Template\Projects`.
   - But `IIS Application.vbp` puts `Designer=` between two `Reference=` lines, and the wizards corpus's
     `Template\WIZARD.VBP` puts `Module=` and two `Form=` lines before a later `Reference=`.
   - The probe held no custom section and no `RelatedDoc=`.

### Not measured

- **What `/make` waits on for a same-type pair (P1),** and **whether the empty container's save writes a `.frx`.** Both
  were measured on 2026-11-21, in the last section below.
- **Which of P3's four properties** (DataChanged, DataText, FileNumber, Format) are refused.
- **p-code builds** of any row.

### Probe sources

**OLE.** A's `Form1.frm` holds this `Form_Load`, and B, C, D1, D2 and E carry it unchanged. A's control is
`Begin VB.OLE OLE1` with Height 1335, Left 240, TabIndex 0, Top 240 and Width 2175, on line 12 of the file.

```vb
Private Sub Form_Load()
    Dim f As Integer
    On Error Resume Next
    f = FreeFile
    Open App.Path & "\out.txt" For Output As #f
    Print #f, "TypeName=" & TypeName(OLE1)
    Print #f, "ControlsCount=" & Me.Controls.Count
    Err.Clear: Print #f, "OLEType=" & OLE1.OLEType & " err=" & Err.Number
    Err.Clear: Print #f, "Class=[" & OLE1.Class & "] err=" & Err.Number
    Err.Clear: Print #f, "SourceDoc=[" & OLE1.SourceDoc & "] err=" & Err.Number
    Err.Clear: Print #f, "SizeMode=" & OLE1.SizeMode & " AutoActivate=" & OLE1.AutoActivate & " DisplayType=" & OLE1.DisplayType & " OLETypeAllowed=" & OLE1.OLETypeAllowed & " err=" & Err.Number
    Err.Clear: OLE1.Action = 7: Print #f, "Action7 err=" & Err.Number & " desc=" & Err.Description
    Err.Clear: OLE1.DoVerb: Print #f, "DoVerb err=" & Err.Number & " desc=" & Err.Description
    Err.Clear: OLE1.CreateEmbed "": Print #f, "CreateEmbed('') err=" & Err.Number & " desc=" & Err.Description
    Err.Clear: Print #f, "OLEType after=" & OLE1.OLEType & " Class=[" & OLE1.Class & "] err=" & Err.Number
    Err.Clear: OLE1.Delete: Print #f, "Delete err=" & Err.Number & " desc=" & Err.Description
    Close #f
    Unload Me
End Sub
```

- **B:** A plus the six property lines of E's empty save. Left stays 240.
- **C:** A with `Begin VB.CommandButton OLE1` inserted before the OLE block: Caption "Command1", Height 495, Left 2640,
  TabIndex 1, Top 240 and Width 1215.
- **D1:** B plus `OleObjectBlob = "Form1.frx":0000` after `Left`, with the 27-byte `.frx` above.
- **D2:** D1 with `OleObjectBlobZz` in place of `OleObjectBlob`.

**P1 to P5** come from two scripts, not from A to E.
- **The scripts:** the first wrote P1 to P3 and ran P1 for 120 s. The second made P4 and P5 as edited copies of P2
  and P3, and re-ran P1 from clean for 20 s.
- **The `.vbp`,** shared by all five: `Type=Exe`, `Form=Form1.frm`, the stdole2 `Reference=`, `Startup="Form1"`,
  ExeName32 "Probe.exe" and Name "Probe".
- **The form header,** shared by all five: `VERSION 5.00`, then `Begin VB.Form Form1` with no trailing space, and
  Caption "Probe", ClientHeight 3195, ClientLeft 60, ClientTop 345, ClientWidth 4680, LinkTopic "Form1", ScaleHeight
  3195, ScaleWidth 4680 and StartUpPosition 3. It ends on line 11, so the first control's `Begin` is line 12.

- **P1:** two blocks, both `Begin VB.CommandButton Cmd1` with no trailing space and no Index, and both with Height
  495, Top 240 and Width 1215.
  - The first has Caption "First", Left 240 and TabIndex 0.
  - The second, whose `Begin` is line 20, has Caption "Second", Left 2640 and TabIndex 1.
  - Its `Form_Load` prints TypeName(Cmd1), Controls.Count and Cmd1.Caption under On Error Resume Next.
- **P2:** a `Begin VB.OLE OLE1 ` block, with a trailing space, holding Appearance=0, AutoVerbMenu=0,
  BackColor=&H000000FF&, BackStyle=0, BorderStyle=0, CausesValidation=0, DataField="fld", DragMode=1, Enabled=0,
  HelpContextID=7, HostName="HostX", MiscFlags=1, MousePointer=2, OLEDropAllowed=-1, SourceItem="R1C1", TabStop=0,
  Tag="tagX", UpdateOptions=2, Visible=0 and WhatsThisHelpID=9, plus Height, Left, TabIndex, Top and Width.
- **P3:** the same `Begin` line, with DataChanged=-1, DataText="abc", FileNumber=3, Format="CF_TEXT" and Verb=2, plus
  Height, Left, TabIndex, Top and Width.
- **P4:** P2 with `ZzUnknown = 1` in place of Appearance.
- **P5:** the geometry lines, TabIndex and `Verb = 2`.

**The duplicate-name rows on the Data form.**
- **The project:** a Standard EXE `.vbp` (`Type=Exe`, `Form=Form1.frm`, `Startup="Form1"`).
- **The form:** Form1 holds Timer1, Data2, Data1 and the TextBox, whose `Begin` is at line 48.
- **What it wrote:** `Form_Load` writes `Me.Controls.Count`, and each control's Name and TypeName, to `out.txt` under
  On Error Resume Next, then calls `Unload Me`.

**Two raw outputs that need care.**
- **An earlier run of the Data pair** was on a longer file, with the TextBox at line 51. It exited 1, but not over the
  duplicate.
  - Its `/out` reads `Compile Error in File '…\Form1.frm', Line 11 : Variable not defined`.
  - Code-view line 11 is that program's first `Text1.Text`, a name that renaming the TextBox to `Data1` had removed,
    under Option Explicit.
- **C's `out.txt`** holds only the TypeName, ControlsCount and four method lines. The OLEType, Class, SourceDoc,
  SizeMode and `OLEType after` lines each raised inside their `Print`, and On Error Resume Next skipped the whole
  statement.

**Data.** `Saved.frm` lines 12-61, the controls:

```
   Begin VB.Timer Timer1 
      Interval        =   1500
      Left            =   4200
      Top             =   2400
   End
   Begin VB.Data Data2 
      BOFAction       =   1  'BOF
      Caption         =   "Data2"
      Connect         =   "Access"
      DatabaseName    =   "C:\Program Files (x86)\Microsoft Visual Studio\VB98\NWIND.MDB"
      DefaultCursorType=   1  'ODBCCursor
      DefaultType     =   2  'UseODBC
      EOFAction       =   2  'Add New
      Exclusive       =   -1  'True
      Height          =   345
      Left            =   240
      Options         =   0
      ReadOnly        =   -1  'True
      RecordsetType   =   0  'Table
      RecordSource    =   "Customers"
      Top             =   2040
      Width           =   3000
   End
   Begin VB.Data Data1 
      Caption         =   "Data1"
      Connect         =   "Access"
      DatabaseName    =   "C:\Program Files (x86)\Microsoft Visual Studio\VB98\BIBLIO.MDB"
      DefaultCursorType=   0  'DefaultCursor
      DefaultType     =   2  'UseODBC
      Exclusive       =   0   'False
      Height          =   345
      Left            =   240
      Options         =   0
      ReadOnly        =   0   'False
      RecordsetType   =   1  'Dynaset
      RecordSource    =   "Authors"
      Top             =   1200
      Width           =   3000
   End
   Begin VB.TextBox Text1 
      DataField       =   "Author"
      DataSource      =   "Data1"
      Height          =   375
      Left            =   240
      TabIndex        =   0
      Text            =   "Text1"
      Top             =   360
      Width           =   3000
   End
End
```

From line 67, after the five `Attribute` lines:

```vb
Option Explicit

Private Sub L(s As String)
    Dim f As Integer
    f = FreeFile
    Open App.Path & "\log.txt" For Append As #f
    Print #f, s
    Close #f
End Sub

Private Sub Data1_Reposition()
    L "Reposition: Text1=" & Text1.Text
End Sub

Private Sub Form_Load()
    On Error Resume Next
    L "Load: Text1=" & Text1.Text
End Sub

Private Sub Timer1_Timer()
    On Error Resume Next
    Timer1.Enabled = False
    L "Timer: Text1=" & Text1.Text
    Err.Clear: L "Fields(0)=" & Data1.Recordset.Fields(0) & " err=" & Err.Number
    Err.Clear: L "Fields(0).Name=" & Data1.Recordset.Fields(0).Name & " err=" & Err.Number
    Err.Clear: L "Caption=" & Data1.Caption & " err=" & Err.Number
    Err.Clear: L "RecordSource=" & Data1.RecordSource & " err=" & Err.Number
    Err.Clear: L "Text1.DataField=" & Text1.DataField & " err=" & Err.Number
    Err.Clear: L "Text1.DataChanged=" & Text1.DataChanged & " err=" & Err.Number
    Err.Clear: Data1.Recordset.MoveNext: L "MoveNext err=" & Err.Number & " Text1=" & Text1.Text
    Err.Clear: Data1.Refresh: L "Refresh err=" & Err.Number & " Text1=" & Text1.Text
    Err.Clear: L "Data2.Recordset.Type=" & Data2.Recordset.Type & " err=" & Err.Number
    L "end"
    Unload Me
End Sub
```

The compiled exe's `log.txt`, the same from the hand-written input and from Saved.frm:

```
Load: Text1=Text1
Reposition: Text1=Text1
Timer: Text1=Jacobs, Russell
Fields(0)=1 err=0
Fields(0).Name=Au_ID err=0
Caption=Data1 err=0
RecordSource=Authors err=0
Text1.DataField=Author err=0
Text1.DataChanged=False err=0
Reposition: Text1=Metzger, Philip W.
MoveNext err=0 Text1=Metzger, Philip W.
Reposition: Text1=Jacobs, Russell
Refresh err=0 Text1=Jacobs, Russell
Data2.Recordset.Type=1 err=0
end
```

## The rows that were owed: a same-type pair under `/make`, and the empty container's save (2026-11-21, #856, SP6)

**Which VB6.** VB6.EXE 6.00.9782 (SP6) in a clone of the oracle VM, driven through the guest's console session, with
screens taken from the host's framebuffer. Both projects were the hand-written `.vbp` described under P1 above, with no
`CompilationType` line, so native code.

**P1 under `/make`, in the console session.** It is the same two-CommandButton form, both `Cmd1` with no Index. It was
started from the console's Run box as `VB6.EXE /make … /out …`.
- **Form1.log** read `Line 20: Did not find an index property and control Cmd1 already exists.`, as before.
- **The `/out` file** read `Errors during load. Refer to '…\Form1.log' for details` 15 seconds in and again 75 seconds
  in, with no build-result line. No exe was written.
- **The console showed nothing.** No dialog and no window appeared. Between the pictures at 15 and 75 seconds, the only
  pixels that changed were the taskbar clock's.
- **VB6 was busy.** It reported itself responding, with two threads, and had used 118.9 seconds of processor time in
  119 seconds; over one 10-second window it used 10.1. It was stopped by its process id at 119 seconds.

So `/make` on a same-type pair writes the log line and the load message, then spins one processor and shows nothing.
The mixed-type pairs above, by contrast, went on to build. What it is doing wasn't established.

**E's empty container, saved in a fresh folder.** The folder held only `Form1.frm` (909 bytes) and `Probe.vbp`. E's form
was opened in VB6's IDE, its designer shown, and the form saved with Ctrl+S.
- **No `.frx` was written.** The folder afterwards held `Form1.frm` (911 bytes) and the unchanged `Probe.vbp`.
- **The two bytes are trailing spaces.** The file differed only in `Begin VB.Form Form1` and `Begin VB.OLE OLE1`, which
  gained one each.
- **The designer drew the empty container as a white box of the control's size** (145 × 89 pixels for 2175 × 1335
  twips). It has the sunken two-pixel edge a 3-D TextBox has:
  `A0A0A0` and `696969` on the top and left, and `E3E3E3` and white on the bottom and right, read through RGB565.
  Nothing was inside it.
