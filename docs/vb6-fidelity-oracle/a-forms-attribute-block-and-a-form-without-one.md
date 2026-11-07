## A form's Attribute block, and a form without one (2026-10-22, #516, #931)

**What VB6 writes.** A new form in VB6 carries five `Attribute` lines after its designer block:

```
Attribute VB_Name = "Form1"
Attribute VB_GlobalNameSpace = False
Attribute VB_Creatable = False
Attribute VB_PredeclaredId = True
Attribute VB_Exposed = False
```

Nineteen of the twenty `.frm` files in VB6's Template tree carry exactly these, the new project's `Form1.frm` among
them, and one carries six. Every one is CRLF throughout and ends in `0D 0A`. HexIDE wrote a form it created with no
block at all, and its code section in bare LF with no final line break (#516, #931).

**The obvious guesses, from #516 and from comments in this tree:** that `VB_Name` is a form's identity, and that
`VB_PredeclaredId = True` is what gives a form its default instance, so a form without the block might load but fail
at `Form1.Show`. **Both are wrong for `vb6.exe /make`.** The block is what VB6 writes, not what it needs.

**The probe.** VB6.EXE 6.00.8176, the original release. One `Sub Main` project per row, each with a `Form1.frm` and a `Module1.bas`,
built with `/make` both as native code and as p-code (`CompilationType=-1`), and the exe run in session 0. The form's
designer block is HexIDE's (Caption, ClientHeight 7200, ClientWidth 9600, ScaleHeight, ScaleWidth). `Sub Main`, under
`On Error Resume Next`, reads `X.Caption` and `X.Name`, calls `X.Show` and `DoEvents`, reads `X.Visible`,
`Forms.Count` and `Forms(0).Name`, then `Unload X`, where `X` is the name in the last column.

| Row | `Form1.frm`'s code section | `X` | Result, native and p-code alike |
|---|---|---|---|
| P1 | no `Attribute` block, CRLF stub | `Form1` | builds; every read as P2 |
| P2 | the five lines, then the stub (the control) | `Form1` | builds; `Caption=CapForm1`, `Name=Form1`, `Forms.Count=1`, no error anywhere |
| P3 | no block; the Begin line says `Form7` | `Form7` | builds; `Name=Form7` |
| P3b | the same file | `Form1`, the file's name | **does not build**: "Compile Error in File '…Module1.bas', Line 6 : Variable not defined" |
| P4 | the five lines with `VB_Name = "FormB"`; the Begin line says `FormA` | `FormB` | **does not build**: the log says "Conflicting names were found in '…Form1.frm'. The name 'FormA' will be used.", then the same "Variable not defined" |
| P4b | the same file | `FormA` | builds, with the same "Conflicting names" line in the log; `Name=FormA` |
| P5 | HexIDE's old seed byte for byte: no block, `Private Sub Form_Load()` LF LF `End Sub`, no final line break | `Form1` | builds; every read as P2 |

`X.Visible` read False after `Show` in every row, the control included, so it is a fact about session 0, which has no
desktop, and says nothing about the block.

- **A missing `VB_PredeclaredId` still gives a form its default instance** (P1). A form's default for it is True.
- **The Begin line names the form.** Without a block it is the only name (P3, P3b). Where `VB_Name` disagrees, vb6.exe
  takes the Begin line's name, logs that it did, and still builds (P4, P4b). So a form's `VB_Name` is a second copy of
  its name that has to agree, not its identity. A rename has to keep both in step, but it is the Begin line that
  decides.
- **An LF code section with no final line break does not stop a build** (P5). #931 left this unmeasured. VB6 writes
  CRLF throughout regardless, which is what a written file is compared against.

What this does not cover: the VB6 IDE opening such a file, where the "Conflicting names" notice is presumably a dialog
rather than a log line. That is unmeasured.

**In HexIDE (#516, #931):** a form HexIDE creates (Add Form, the Project Explorer's Add Form, a new Standard EXE) carries
the five lines, naming it as its Begin line does, and its file is CRLF throughout with a final line break. These match
what VB6 writes, which is the bar, though P1 and P5 show vb6.exe would have built the old shape. HexIDE names a form it
reads by its Begin line too, as vb6.exe does.

