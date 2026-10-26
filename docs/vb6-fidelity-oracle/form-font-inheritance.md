# A control without a Font block takes the form's font (#2062)

Provenance: the implementation brief comment on hexide-io/HexIDE#2062 reports measurements on
vb6.exe SP6, with native and p-code agreeing, from #1556's "Found along the way" probe. That
probe's subject file was on `feat/1556-controls-add` when this change was written; this record
preserves the supplied measurements without claiming a new local oracle run.

The .frm declares Form1's font as Arial 12. Picture1 declares Courier New 9.75;
Frame1 declares Times New Roman 9.75 with Weight 700. All other controls omit their Font block.

| Control | Location | Font.Name / FontName | Font.Size / FontSize | Font.Bold |
|---|---|---|---|---|
| Command1 | Form1 | Arial | 12 | False |
| Label1 | Form1 | Arial | 12 | False |
| Command2 | Picture1 | Arial | 12 | False |
| Frame2 | Picture1 | Arial | 12 | False |
| Command4 | Frame2 inside Picture1 | Arial | 12 | False |
| Command3 | Frame1 | Arial | 12 | False |
| Picture1 | Form1, explicit font | Courier New | 9.75 | False |
| Frame1 | Form1, explicit font | Times New Roman | 9.75 | True |

The issue body additionally reports Text1 without a Font block reading Arial 12.
The missing block takes the form's font at any container depth, rather than the nearest
container's font. HexIDE previously supplied MS Sans Serif 8.25 instead.

`Command1.Font Is Label1.Font` and `Command1.Font Is Form1.Font` both returned False.
After `Form1.Font.Name = "Tahoma"`, Command1 and Command2 still read Arial: the inherited
font is a copy made at load, not a continuing link. HexIDE's font descriptor is a value type;
control Font object member access remains separate work (#934).

Designer rendering, UserControl contents, and a form omitting its own Font block were not
measured by this probe and are not pinned here.
