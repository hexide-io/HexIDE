## Text Clipboard (#1156, 2026-10-17)

Measured with `scripts/vb6-oracle.ps1` in the Windows oracle guest, native and p-code (`CompilationType=-1`).
Both modes returned the same values and errors. The clipboard belongs to the guest, not the development desktop.

That script runs in session 0. Re-run on the guest's console (2026-11-18, #4768), on the console user's clipboard,
the IDE, p-code and native agreeing: every row of the defaults, the format table, `U` and `KeepFormats` read the same,
and so did the late-bound `c.Clear 1`'s 450. `c.SetText` and `c.GetFormat` weren't repeated.

### Defaults, text and format presence

`Clipboard.Clear` followed by `Clipboard.GetText` returns an empty **String**, and `GetFormat(1)` is **Boolean False**.
`SetText "hello"` followed by `GetText`, `GetText()` or `GetText(1)` returns `hello`, **String**. Setting `""` or
`Empty` leaves `GetText` empty but makes `GetFormat(1)` **True**: empty text is a present format.
`SetText 42` reads back `42`; `SetText True` reads back `True`. `SetText "a" & Chr$(0) & "b"` reads back only `a`.

The reusable format probe (evaluate `Probe(mode, format, "new")`) was:

```vb
Function Probe(ByVal mode As Integer, ByVal fmt As Variant, ByVal txt As Variant) As String
    On Error Resume Next
    Dim v As Variant, n As Long
    Clipboard.Clear
    If mode = 1 Then
        v = Clipboard.GetFormat(fmt)
    ElseIf mode = 2 Then
        Clipboard.SetText "hello"
        Err.Clear
        v = Clipboard.GetText(fmt)
    ElseIf mode = 3 Then
        Clipboard.SetText "old"
        Err.Clear
        Clipboard.SetText txt, fmt
        n = Err.Number
        v = Clipboard.GetText
    End If
    If n = 0 Then n = Err.Number
    Err.Clear
    Probe = CStr(n) & ":" & TypeName(v) & ":" & CStr(v) & ":has=" & CStr(Clipboard.GetFormat(1))
End Function
```

| Format argument | GetFormat after Clear | GetText after SetText "hello" | SetText "new", format after SetText "old" |
|---|---|---|---|
| `1`, `1.4`, `"1"` | False, Boolean | hello, String | new, String |
| `0`, `2`, `3`, `7`, `8`, `9`, `13`, `14`, `15`, `16`, `17`, `-1`, `32767`, `1.5`, `Empty`, `True` | False, Boolean | error 460 | error 460; old text retained |
| `32768` | error 6 | error 6 | error 6; old text retained |
| `"bad"` | error 13 | error 13 | error 13; old text retained |
| `Null` | error 94 | error 13 | error 13; old text retained |
| `vbCFRTF` (`-16639`) | False, Boolean | empty, String | old plain text retained; RTF added |
| `vbCFLink` (`-16640`) | False, Boolean | empty, String | error 294; old text retained |

`SetText Null` raises 94 and leaves the cleared clipboard without text. Through `Dim c As Object: Set c = Clipboard`,
`c.Clear 1`, `c.SetText` and `c.GetFormat` each raise **450**, captured with On Error Resume Next.

**Error 294, from `SetText` with `vbCFLink`** (2026-11-20, #4955, SP6, `/make` native and p-code in session 0, the two agreeing):
- `SetText "x", vbCFLink` and `SetText "", vbCFLink`, after `SetText "hello"`, each raised **294** with `Err.Description`
  **"Invalid DDE Link format"** and `Err.Source` the project's name. `GetText` read `hello` afterwards.
- `Error$(294)` reads "Application-defined or object-defined error": the description comes with the raised error only.
- `SetText "x", 2` raised 460 with "Invalid Clipboard format".

### ANSI best-fit conversion and multiple formats

This helper returned `65`, `79`, `63`, `8364`, `63` (all **Long**) for arguments `256`, `937`, `1046`, `8364`, `20013`:

```vb
Function U(ByVal n As Long) As Long
    Clipboard.Clear
    Clipboard.SetText ChrW$(n)
    U = AscW(Clipboard.GetText)
End Function
```

The guest's Windows-1252 clipboard therefore maps U+0100 to A, U+03A9 to O, U+0416 and U+4E2D to ?, and keeps the euro
sign. This is best-fit ANSI conversion, not simply replacing every unencodable character with ?.

The following helper returned `True:{\rtf1 old}:new`, **String**, in both modes:

```vb
Function KeepFormats() As String
    Clipboard.Clear
    Clipboard.SetText "{\rtf1 old}", vbCFRTF
    Clipboard.SetText "new"
    KeepFormats = CStr(Clipboard.GetFormat(vbCFRTF)) & ":" & Clipboard.GetText(vbCFRTF) & ":" & Clipboard.GetText
End Function
```

**HexIDE's supported slice:** the Clipboard object, Clear, default/vbCFText GetText and SetText, and GetFormat(vbCFText).
Its methods await the host under the event pump's suspension boundary. The desktop and IDE hosts use Avalonia's
window clipboard. RTF, Link, image/data methods and other format queries are explicitly refused, not reported absent.
The host snapshots readable non-text formats before replacing text and writes them back with the new text (#1246).
If a format cannot be read, it refuses before writing. Clear removes all formats, as the Notepad demo requires.
The measurement above covers program-supplied RTF; preservation of external HTML and Windows-synthesized text
formats has not been measured against VB6.

**Live Windows follow-up, 2026-10-18:** built the PR's desktop application at `905e3506` and launched it with an
isolated user-data directory and copies of the unmodified Notepad demo forms. HexIDE MCP was now available.
`dump_visual_tree`, `inspect_element` and `take_snapshot` confirmed the running form and text. Breakpoints in
`mnuEdit_Click`, `mnuEditCopy_Click` and `mnuEditPaste_Click` confirmed the VB6 handlers executed, rather than merely
the TextBox's native editing commands. The following checks passed with no runtime error (`raised:false`, sequence 0):

- Copy placed `Clipboard live check` on the native Windows clipboard, independently read back with PowerShell.
- Cut emptied the selected editor text; Paste restored it, verified with `inspect_element`.
- Text set by an external PowerShell process pasted as `External clipboard text`.
- Clearing the native clipboard externally and invoking the Edit handler disabled the Paste menu item.
- Pausing after Clipboard.Clear returned False from `Clipboard.GetFormat(vbCFText)`. Setting the selection length
  to zero before the next SetText stored empty text: the following GetFormat was True and Len(GetText) was 0.
- External Unicode text `unicode: ĀΩЖ€中` pasted as `unicode: AO?€?`, matching the measured ANSI best-fit conversion.
- Save wrote that text in Windows-1252 through the actual file flow (the MCP picker answer supplied the path),
  and Exit ended the run. Breakpoints and pending picker answers were cleared afterwards.

The Windows native clipboard is verified; Linux host tests remain headless, and no native Linux/macOS clipboard
verification is claimed. Headless tests now cover replacing text while preserving non-text string and byte payloads
(#1246). The existing grammar refuses empty parentheses after `Call` (for example `Call Clipboard.Clear()`); `Call Clipboard.Clear` and
`Call Clipboard.SetText("hello")` are covered. That pre-existing grammar restriction is outside this clipboard slice.

A function also shadows Clipboard when it supplies the object of a member chain. With class `Box` containing
`Public Function GetText() As String: GetText = "shadow": End Function`, and a module function
`Function Clipboard() As Box: Set Clipboard = New Box: End Function` (each statement on its own line), evaluating
`Clipboard.GetText` returned `shadow`, **String**, native and p-code. The member-lead resolver must check that
function before returning the host Clipboard object.

### Pictures: `GetData` and `SetData` (2026-11-18, #4682, SP6)

**The rig:**
- **Build and run:** vb6.exe 6.00.9782 with MSVBVM60 6.00.9848, `/make` native and p-code in session 0 of the oracle guest. Both builds gave identical answers.
- **Whose clipboard:** session 0's window station's, not the console user's. Re-run on the guest's console
  (2026-11-18, #4768), the IDE, p-code and native agreeing, on the console user's clipboard, the rows it repeated
  read the same: an empty clipboard's Type 0 picture; formats 2 and 8 after `SetData p`; the Type 1 picture,
  2540 × 1482, back as a bitmap or a DIB; the empty picture for a metafile; 461 for `SetData p, vbCFMetafile`; and
  1, 2 and 8 after `SetText "abc"` and `SetData p`, with `GetText` still `abc`.
- **The program:** from a startup form, `Picture1.AutoRedraw = True` and a red filled box were drawn into a PictureBox, and `Picture1.Image` gave the source, `p`.
  - `p` is a bitmap (Type 1), Width 2540 and Height 1482 in HiMetric.
  - Each statement was logged with its error under Resume Next.
  - `GetFormat` was read for formats 1, 2, 3, 8, 9, 14, `vbCFLink` and `vbCFRTF`.
  - A returned picture was described by its `Type`, whether `Handle` is nonzero, and its `Width` and `Height`.

| Probe | VB6 |
|---|---|
| `Clipboard.Clear`, then the formats | none |
| `Set q = Clipboard.GetData()` on the empty clipboard | ok: **a Picture of Type 0**, Handle 0, Width and Height 0. Not Nothing |
| `Set q = Clipboard.GetData(vbCFBitmap)` on the empty clipboard | the same empty picture |
| `Clipboard.SetData p` | ok. The formats are **2 and 8**: a bitmap and, synthesised from it, a DIB |
| `GetData()`, `GetData(vbCFBitmap)`, `GetData(vbCFDIB)` | each a **Type 1** picture, Width 2540, Height 1482 |
| `GetData(vbCFPalette)`, `GetData(vbCFMetafile)`, `GetData(vbCFEMetafile)` | each the empty Type 0 picture, no error |
| `GetData(vbCFText)` | **460** Invalid Clipboard format |
| `GetData(99)` | **460** |
| `v = Clipboard.GetData()`, a Let into a Variant | ok: a **Long**, nonzero (the picture's handle, its default member) |
| `Clipboard.SetData p, vbCFDIB` | ok. The formats are 2 and 8, and `GetData(vbCFDIB)` is the Type 1 picture |
| `Clipboard.SetData p, vbCFMetafile`, a bitmap as a metafile | **461** Specified format doesn't match format of data; nothing is put on the clipboard |
| `Clipboard.SetData p, vbCFText` | **461** |
| `Clipboard.SetData p, 99` | **461** |
| `Clipboard.SetData Me.Icon` | **460** Invalid Clipboard format, and `GetData()` afterwards is the empty picture |
| `Clipboard.SetData q` with `q` Nothing | **91** |
| `Clipboard.SetData v` with `v` a Variant holding "text" | **424** Object required |
| `Clipboard.SetText "abc"`, then `Set q = Clipboard.GetData()` | the empty Type 0 picture |
| then `Clipboard.SetData p` | the formats are **1, 2 and 8**, and `GetText` is still `abc`: **`SetData` does not clear the clipboard** |

- **A missing format is not an error for `GetData`;** an empty picture comes back. Only a format that is not a picture
  format (text, or a number VB6 doesn't know) raises 460.
- **`SetData` checks the format against the picture.** A bitmap can be set as a bitmap or a DIB, not as a metafile.
- **A literal String passed to `SetData` is a compile error,** "Type mismatch", so the String row goes through a Variant.
- **Not measured:**
  - a metafile or an icon picture from `LoadPicture`;
  - `SetData` of an icon as `vbCFBitmap`;
  - the console session's own clipboard, beyond the rows *Whose clipboard* lists.

### Format presence and RTF text (2026-11-20, #4955, SP6)

**The rig:** vb6.exe 6.00.9782 with MSVBVM60 6.00.9848, `/make` native and p-code, run in session 0 of the oracle guest, on that window station's clipboard. Both builds gave identical answers, line for line.

**The probe,** a Standard EXE with `Sub Main`. Each row clears the clipboard, makes the changes it names, and then logs three things:
- which formats `GetFormat` reports True, from 0, 1, 2, 3, 7, 8, 9, 13, 14, 15, 16, 17, -1, 32767, `vbCFRTF` and `vbCFLink`;
- what `GetText`, `GetText(vbCFRTF)` and `GetText(vbCFLink)` return, by `TypeName` and value;
- for a `SetText`, its error under `On Error Resume Next`.

```vb
Private Function Formats() As String
    Dim fs As Variant, i As Long, s As String, v As Variant
    fs = Array(0, 1, 2, 3, 7, 8, 9, 13, 14, 15, 16, 17, -1, 32767, vbCFRTF, vbCFLink)
    On Error Resume Next
    For i = 0 To UBound(fs)
        Err.Clear
        v = Clipboard.GetFormat(fs(i))
        If Err.Number <> 0 Then
            s = s & " " & fs(i) & "=e" & Err.Number
        ElseIf v Then
            s = s & " " & fs(i) & "=T"
        End If
    Next
    Formats = "present:" & s
End Function
```

| Row | After `Clipboard.Clear`, then | `GetFormat` True for | `GetText` | `GetText(vbCFRTF)` | `GetText(vbCFLink)` |
|---|---|---|---|---|---|
| R0 | nothing | none | `""` | `""` | `""` |
| R1 | `SetText "hello"` | **1, 7, 13 and 16** | `hello` | `""` | `""` |
| R2 | `SetText ""` | 1, 7, 13 and 16 | `""` | `""` | `""` |
| R3 | `SetText "{\rtf1 x}", vbCFRTF` | `vbCFRTF` only | `""` | `{\rtf1 x}` | `""` |
| R4 | `SetText "", vbCFRTF` | `vbCFRTF` only | `""` | `""` | `""` |
| R5 | `SetText "hello"`, then `SetText "{\rtf1 x}", vbCFRTF` | 1, 7, 13, 16 and `vbCFRTF` | `hello` | `{\rtf1 x}` | `""` |
| R6 | `SetText "{\rtf1 a}", vbCFRTF`, then `SetText "{\rtf1 b}", vbCFRTF` | `vbCFRTF` only | `""` | `{\rtf1 b}` | `""` |
| R7 | `SetText "t1"`, `"{\rtf1 a}"` as RTF, `"t2"`, then `"{\rtf1 b}"` as RTF | 1, 7, 13, 16 and `vbCFRTF` | `t2` | `{\rtf1 b}` | `""` |
| R9 | `SetText "hello"`, then `SetText "x", vbCFLink`: **error 294** | 1, 7, 13 and 16 | `hello` | `""` | `""` |
| R10 | `SetText 42, vbCFRTF` | `vbCFRTF` only | `""` | `42` | `""` |
| R11 | `SetText Null, vbCFRTF`: **error 94** | none | `""` | `""` | `""` |
| R12 | `SetText "{\rtf1 a}", vbCFRTF`, then `Clear` | none | `""` | `""` | `""` |

Every value read back was a **String**. No `GetFormat` call raised an error, and no `SetText` did except R9's and R11's.

**R8, RTF text in the ANSI code page:** `SetText ChrW$(937) & ChrW$(256) & "a" & Chr$(0) & "b", vbCFRTF` gave no error, and `GetText(vbCFRTF)` read back `OAa`, length 3. So RTF text goes through the same best-fit conversion as plain text (U+03A9 to O, U+0100 to A), and stops at the NUL as plain text does.

**What this shows:**
- **`GetFormat` reports what Windows holds, not only the formats VB6 names.** Text set by VB6 makes 7 (`CF_OEMTEXT`), 13 (`CF_UNICODETEXT`) and 16 (`CF_LOCALE`) present as well as 1. Windows synthesizes those three from text, and VB6 reports them.
  - The obvious guess was that `GetFormat` would answer only for its own constants, and False for the rest. It doesn't.
  - Formats nothing put there, such as 2, 8, 15, -1 and 32767, read False, without an error.
- **RTF is a format of its own.** Setting RTF alone leaves no text: `GetFormat(1)` is False and `GetText` is empty. A later RTF replaces the earlier one (R6), and plain text and RTF each keep the other (R5, R7, and `KeepFormats` above).
- **`vbCFLink` is never present after VB6's own `SetText`,** and `GetText(vbCFLink)` reads an empty String. `SetText` with it is 294, and the text stays (R9).
- **Not measured:**
  - **another application's data on the clipboard,** such as a picture, files, HTML, or Excel's Link format: whether `GetFormat` reports each standard format it holds, and what `GetText(vbCFLink)` reads then;
  - the console session's own clipboard, for these rows.
