## A lone CR or LF in VB6 source (2026-10-22, #1020)

**The question.** Almost every HexIDE reader of a module's lines split on `\n` alone. A lone CR therefore ended no line
for them, though it did for the `.frm` designer reader and the code window. Which does VB6 do? Nothing here recorded a
lone CR, and the one line-ending fact recorded, that an LF-terminated module fails to load, turns out to be about classes.

**The probe.** Each case is one project, written byte for byte on the guest so nothing normalised the line endings under
test. `Sub Main` in `Module1.bas`, or `Form_Load` in a startup form, writes what it ran to a file. A class case also holds
`C.cls`, whose `Property Get Value` carries `Attribute Value.VB_UserMemId = 0`, and `Main` reads `n = c` from a `New C`,
under `On Error Resume Next`. So `n` is 42 when the attribute made `Value` the default member, and the error is 438 when
it did not. Each was built with `vb6.exe /make` (6.00.8176, the original release) and the result run.

| Probe | The file | `/make` | Ran |
|---|---|---|---|
| p1 | `.bas`, every line ending with a lone CR, the `VB_Name` line included | built | `x` = 7 |
| p6 | `.bas`, every line ending with a lone LF | built | `x` = 7 |
| p2 | CRLF `.bas`; `x = 1`, a lone CR, `x = x + 10` | built | `x` = 11 |
| p5 | CRLF `.bas`; the same with a lone LF | built | `x` = 11 |
| p3crlf | CRLF `.cls`; `Property Get Value() As Long`, then its `VB_UserMemId` line | built | `n` = 42, error 0 |
| p3 | the same, with a lone CR ending the `Property Get` line | built | `n` = 42, error 0 |
| p4 | the same, with a lone CR and then a CRLF ending it | built | `n` = 0, error 438 |
| p11 | `.cls`; `VERSION` … `END` CRLF, every line after it ending with a lone CR | built | `n` = 42, error 0 |
| p9 | `.cls`, every line ending with a lone CR | "`C.cls` could not be loaded" | |
| p10 | `.cls`, every line ending with a lone LF | "could not be loaded" | |
| p12 | `.cls`; `VERSION` … `END` with lone CRs, everything after it CRLF | "could not be loaded" | |
| p13 | `.cls`; only the `VERSION` line ending with a lone CR | "could not be loaded" | |
| p14crlf | CRLF `.frm` | built | ran |
| p14 | `.frm`; designer block CRLF, every code line after it ending with a lone CR | built | ran |
| p15 | `.frm`, every line ending with a lone CR | "Errors during load"; its log: "The file … could not be loaded", line 0 | |
| p16 | `.frm`, every line ending with a lone LF | the same | |

Two more probes, meant to end only the `VB_Name` line with a CRLF or with a lone CR, came out byte-identical to p6 and to
a plain CRLF module. Their regular expressions did not match, so they measured nothing and are left out.

**Continuations and comments, measured the same way for #1426.** Each is a CRLF `.bas` whose `Sub Main` starts with
`x` at 0. One terminator is a lone CR or LF, as named, and the byte counts of CR and LF were read back from the
written file before each build.

| Probe | The lines, and the terminator after the first | Ran |
|---|---|---|
| p17crlf | `x = 1 + _`, CRLF, `10` | `x` = 11 |
| p17 | the same, a lone CR | `x` = 11 |
| p18 | the same, a lone LF | `x` = 11 |
| p19 | `x = 1 ' note`, a lone CR, `x = x + 10` | `x` = 11 |
| p20 | `x = 1: Rem note`, a lone CR, `x = x + 10` | `x` = 11 |
| p21 | `x = 1 ' note`, a lone LF, `x = x + 10` | `x` = 11 |
| p22crlf | `x = 1 ' note _`, CRLF, `x = x + 10` | `x` = 1 |
| p22 | the same, a lone CR | `x` = 1 |
| p23 | the same, a lone LF | `x` = 1 |

All nine built.

**What it shows:**
- **In code, CRLF, a lone CR and a lone LF each end a line,** the `.bas` header's `Attribute VB_Name` line included.
  A module's lines can all end with a lone CR, or all with a lone LF, and it builds and runs as its CRLF twin.
- **The same three end a continued line and a comment.** ` _` and then a lone CR or LF continues the statement (p17,
  p18). A `'` or `Rem` comment stops at a lone CR or LF, and the next line runs (p19–p21). A comment ending in ` _`
  continues past any of the three, and the line after it is comment too (p22, p23, as p22crlf).
- **An attribute line binds by the line above it, with a lone CR counting as a line end.** A lone CR between a
  `Property Get` and its attribute changes nothing (p3). A lone CR and then a CRLF is a blank line, and the attribute
  under it binds to nothing (p4).
- **A designer block must be CRLF.** A class's `VERSION` … `END` and a form's designer block are refused unless every line
  ends CRLF. The `VERSION` line alone ending with a lone CR is enough (p13). Below the block, the code reads as a
  `.bas` does (p11, p14).

**Overturned.** The class-probe gotcha above says VB6 needs CRLF source "or the module fails to load". That is true of a
class, which fails as p10 does, and of a form; it is not true of a `.bas`, which loads with any of the three (p1, p6). Its
misleading "User-defined type not defined" is what the class's use reports when the class itself could not be loaded.

HexIDE now finds lines by this rule wherever it reads a module's or a form's lines: the header split, the `VB_Name`
retarget, the read-only regions and the guard, and the reduction of a formatting edit. Its readers accept a CR-only or
LF-only designer block, which VB6 refuses. A `.bas` or `.cls` saved after a load is written with CRLF throughout, since
the header split rejoins its lines that way. To VB6 that is the same file. The interpreter's grammar ends a line at a
lone CR too since #1426, and the corpus's three line-ending cases carry these verdicts. The bundled server's position
model still ends one only at `\n` (#1052). Not probed: the VB6 IDE's own
open and save, which may rewrite a lone CR as it loads.

