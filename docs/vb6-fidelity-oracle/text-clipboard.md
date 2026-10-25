## Text Clipboard (#1156, 2026-10-17)

Measured with `scripts/vb6-oracle.ps1` in the Windows oracle guest, native and p-code (`CompilationType=-1`).
Both modes returned the same values and errors. The clipboard belongs to the guest, not the development desktop.

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

