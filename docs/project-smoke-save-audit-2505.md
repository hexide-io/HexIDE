# #2505: the five demo-form save differences

Measured in this clone on Linux on 2026-10-28, before normalizing the demo files. All 29 projects opened,
none crashed, and three projects changed five forms. There were **66 differing lines**, each gaining exactly
one ASCII space immediately before CRLF: 62 `Begin` lines and four `BeginProperty Font` lines.
No line was inserted, deleted or moved. No property, default, number, encoding, code or companion changed.

## Which side is wrong

The hand-authored demo files omitted VB6's trailing space. HexIDE's saved spelling agrees with the recorded
format; this finding does not identify a writer defect. The five demos are normalized in this change by
adding only those 66 spaces, keeping their CRLF bytes. This is formatting normalization, not a new VB6
measurement or a claim that VB6 itself re-saved these five complete demos.

Evidence for the spelling:

- [Serialization / file-format fidelity](vb6-fidelity-oracle/serialization--file-format-fidelity.md), Q1a,
  explicitly records VB6's `Begin VB.Form Form1 ` spelling and measures that omitting the space still compiles.
  The corpus-wide follow-up classifies trailing `Begin` spaces as cosmetic.
- [What VB6's Add Form saves](vb6-fidelity-oracle/what-vb6s-add-form-saves.md) records the same trailing space
  in the IDE's actual saved form, with every line CRLF.
- [The CommonDialog save](vb6-fidelity-oracle/in-box-controls-the-commondialog.md) records a trailing space
  on `Begin MSComDlg.CommonDialog CommonDialog4 `.
- [The TabStrip save](vb6-fidelity-oracle/in-box-controls-the-tabstrip.md) records the trailing space on a
  `BeginProperty Font {0BE35203-8F91-11CE-9DE3-00AA004BB851} ` line; the
  [SSTab save](vb6-fidelity-oracle/in-box-controls-the-sstab.md) likewise records a child CommandButton's
  trailing `Begin` space. These are the recorded block-opening whitespace convention; no GUID or other
  content is being added to the demos' intrinsic Font blocks.

## Linux versus Windows checkout

All five inputs already had CRLF before this run. `git ls-files --eol` reported `i/lf w/crlf attr/text eol=crlf`
for each: the index stores LF, while `.gitattributes` checks these VB6 sources out with CRLF on Linux as well
as Windows. The saved copies also had CRLF on every line, with the same number of lines as the originals.
Thus these differences reproduce with Windows-style CRLF input and are not caused by the LF checkout of C#
source. A separate `git -c core.autocrlf=true checkout-index --prefix=... -- <the five paths>`
checkout of the unchanged index into the ignored artifacts directory reproduced the original input bytes
exactly (5020, 3255, 13507, 3317 and 14298 bytes respectively). No second save was needed to establish the
line-ending comparison: that checkout's inputs are identical to the inputs already swept. This is a Linux
run using CRLF inputs, not a claim to have run the Windows IDE.

## Every differing line

Every row below is **other formatting: one trailing ASCII space added**, and has the same verdict above:
the original demo lacks the recorded block-opening space. Original and saved are JSON string literals,
excluding their identical CRLF terminators. Spaces inside the closing quote are significant; line numbers
are one-based and identical in both files. All other lines match byte for byte.

### `demo/battleship-forms/frmBoard.frm`

3 lines; 5020 → 5023 bytes; 152 CRLF line endings before and after.

| Line | Original | Saved |
|---|---|---|
| 2 | `"Begin VB.Form frmBoard"` | `"Begin VB.Form frmBoard "` |
| 13 | `"   Begin VB.Label lblStatus"` | `"   Begin VB.Label lblStatus "` |
| 20 | `"   Begin VB.Label lblCell"` | `"   Begin VB.Label lblCell "` |

### `demo/battleship-forms/frmSetup.frm`

9 lines; 3255 → 3264 bytes; 115 CRLF line endings before and after.

| Line | Original | Saved |
|---|---|---|
| 2 | `"Begin VB.Form frmSetup"` | `"Begin VB.Form frmSetup "` |
| 15 | `"   Begin VB.CommandButton cmdCancel"` | `"   Begin VB.CommandButton cmdCancel "` |
| 24 | `"   Begin VB.CommandButton cmdOK"` | `"   Begin VB.CommandButton cmdOK "` |
| 33 | `"   Begin VB.OptionButton optSize"` | `"   Begin VB.OptionButton optSize "` |
| 42 | `"   Begin VB.OptionButton optSize"` | `"   Begin VB.OptionButton optSize "` |
| 51 | `"   Begin VB.OptionButton optSize"` | `"   Begin VB.OptionButton optSize "` |
| 61 | `"   Begin VB.TextBox txtName"` | `"   Begin VB.TextBox txtName "` |
| 68 | `"   Begin VB.Label lblSize"` | `"   Begin VB.Label lblSize "` |
| 76 | `"   Begin VB.Label lblName"` | `"   Begin VB.Label lblName "` |

### `demo/minesweeper-forms/frmMines.frm`

16 lines; 13507 → 13523 bytes; 421 CRLF line endings before and after.

| Line | Original | Saved |
|---|---|---|
| 2 | `"Begin VB.Form frmMines"` | `"Begin VB.Form frmMines "` |
| 13 | `"   Begin VB.Timer tmrClock"` | `"   Begin VB.Timer tmrClock "` |
| 19 | `"   Begin VB.CommandButton cmdFace"` | `"   Begin VB.CommandButton cmdFace "` |
| 21 | `"      BeginProperty Font"` | `"      BeginProperty Font "` |
| 36 | `"   Begin VB.Label lblTime"` | `"   Begin VB.Label lblTime "` |
| 46 | `"   Begin VB.Label lblMines"` | `"   Begin VB.Label lblMines "` |
| 55 | `"   Begin VB.Label lblCell"` | `"   Begin VB.Label lblCell "` |
| 59 | `"      BeginProperty Font"` | `"      BeginProperty Font "` |
| 75 | `"   Begin VB.Menu mnuGame"` | `"   Begin VB.Menu mnuGame "` |
| 77 | `"      Begin VB.Menu mnuNew"` | `"      Begin VB.Menu mnuNew "` |
| 81 | `"      Begin VB.Menu mnuSep1"` | `"      Begin VB.Menu mnuSep1 "` |
| 84 | `"      Begin VB.Menu mnuLevel"` | `"      Begin VB.Menu mnuLevel "` |
| 89 | `"      Begin VB.Menu mnuLevel"` | `"      Begin VB.Menu mnuLevel "` |
| 93 | `"      Begin VB.Menu mnuLevel"` | `"      Begin VB.Menu mnuLevel "` |
| 97 | `"      Begin VB.Menu mnuSep2"` | `"      Begin VB.Menu mnuSep2 "` |
| 100 | `"      Begin VB.Menu mnuExit"` | `"      Begin VB.Menu mnuExit "` |

### `demo/notepad-forms/frmFind.frm`

9 lines; 3317 → 3326 bytes; 109 CRLF line endings before and after.

| Line | Original | Saved |
|---|---|---|
| 2 | `"Begin VB.Form frmFind"` | `"Begin VB.Form frmFind "` |
| 15 | `"   Begin VB.CommandButton cmdCancel"` | `"   Begin VB.CommandButton cmdCancel "` |
| 24 | `"   Begin VB.CommandButton cmdFindNext"` | `"   Begin VB.CommandButton cmdFindNext "` |
| 34 | `"   Begin VB.Frame fraDirection"` | `"   Begin VB.Frame fraDirection "` |
| 41 | `"      Begin VB.OptionButton optDown"` | `"      Begin VB.OptionButton optDown "` |
| 50 | `"      Begin VB.OptionButton optUp"` | `"      Begin VB.OptionButton optUp "` |
| 59 | `"   Begin VB.CheckBox chkMatchCase"` | `"   Begin VB.CheckBox chkMatchCase "` |
| 67 | `"   Begin VB.TextBox txtFind"` | `"   Begin VB.TextBox txtFind "` |
| 74 | `"   Begin VB.Label lblFind"` | `"   Begin VB.Label lblFind "` |

### `demo/notepad-forms/frmNotepad.frm`

29 lines; 14298 → 14327 bytes; 489 CRLF line endings before and after.

| Line | Original | Saved |
|---|---|---|
| 3 | `"Begin VB.Form frmNotepad"` | `"Begin VB.Form frmNotepad "` |
| 12 | `"   Begin MSComDlg.CommonDialog dlgCommon"` | `"   Begin MSComDlg.CommonDialog dlgCommon "` |
| 19 | `"   Begin VB.TextBox txtEdit"` | `"   Begin VB.TextBox txtEdit "` |
| 20 | `"      BeginProperty Font"` | `"      BeginProperty Font "` |
| 40 | `"   Begin VB.TextBox txtEdit"` | `"   Begin VB.TextBox txtEdit "` |
| 41 | `"      BeginProperty Font"` | `"      BeginProperty Font "` |
| 60 | `"   Begin VB.Menu mnuFile"` | `"   Begin VB.Menu mnuFile "` |
| 62 | `"      Begin VB.Menu mnuFileNew"` | `"      Begin VB.Menu mnuFileNew "` |
| 66 | `"      Begin VB.Menu mnuFileOpen"` | `"      Begin VB.Menu mnuFileOpen "` |
| 70 | `"      Begin VB.Menu mnuFileSave"` | `"      Begin VB.Menu mnuFileSave "` |
| 74 | `"      Begin VB.Menu mnuFileSaveAs"` | `"      Begin VB.Menu mnuFileSaveAs "` |
| 77 | `"      Begin VB.Menu mnuFileSep1"` | `"      Begin VB.Menu mnuFileSep1 "` |
| 80 | `"      Begin VB.Menu mnuFileExit"` | `"      Begin VB.Menu mnuFileExit "` |
| 84 | `"   Begin VB.Menu mnuEdit"` | `"   Begin VB.Menu mnuEdit "` |
| 86 | `"      Begin VB.Menu mnuEditCut"` | `"      Begin VB.Menu mnuEditCut "` |
| 90 | `"      Begin VB.Menu mnuEditCopy"` | `"      Begin VB.Menu mnuEditCopy "` |
| 94 | `"      Begin VB.Menu mnuEditPaste"` | `"      Begin VB.Menu mnuEditPaste "` |
| 98 | `"      Begin VB.Menu mnuEditDelete"` | `"      Begin VB.Menu mnuEditDelete "` |
| 102 | `"      Begin VB.Menu mnuEditSep1"` | `"      Begin VB.Menu mnuEditSep1 "` |
| 105 | `"      Begin VB.Menu mnuEditFind"` | `"      Begin VB.Menu mnuEditFind "` |
| 109 | `"      Begin VB.Menu mnuEditFindNext"` | `"      Begin VB.Menu mnuEditFindNext "` |
| 113 | `"      Begin VB.Menu mnuEditSep2"` | `"      Begin VB.Menu mnuEditSep2 "` |
| 116 | `"      Begin VB.Menu mnuEditSelectAll"` | `"      Begin VB.Menu mnuEditSelectAll "` |
| 120 | `"      Begin VB.Menu mnuEditTimeDate"` | `"      Begin VB.Menu mnuEditTimeDate "` |
| 125 | `"   Begin VB.Menu mnuFormat"` | `"   Begin VB.Menu mnuFormat "` |
| 127 | `"      Begin VB.Menu mnuFormatWordWrap"` | `"      Begin VB.Menu mnuFormatWordWrap "` |
| 130 | `"      Begin VB.Menu mnuFormatFont"` | `"      Begin VB.Menu mnuFormatFont "` |
| 134 | `"   Begin VB.Menu mnuHelp"` | `"   Begin VB.Menu mnuHelp "` |
| 136 | `"      Begin VB.Menu mnuHelpAbout"` | `"      Begin VB.Menu mnuHelpAbout "` |

## Verification

After normalization, the normal sweep opened and saved **29 of 29 projects byte for byte**, with no crash
and no refusal. Before normalization, the same sweep failed on the five files above. Three new Linux
regression cases exercise exact original/saved evidence for a changed, deleted or added file; all three
fail with the evidence-writing block removed. Together with the corpus sweep, that is four cases with
observed failing controls, rather than tests that only passed against the final tree.

The full `HexIDE.Tests` executable run with `HEXIDE_FOREIGN_LSP_DOWNLOAD=0` and a private temporary
directory completed with 4598 passed, 28 skipped, zero errors and zero failures (4626 total; the CI floor
is 4249). The skipped cases are 18 Windows-only checks and ten checks needing the absent VB6 Template tree; none
of the three new evidence cases or the corpus sweep skipped. The Desktop Debug build finished with zero
warnings and zero errors. Tree hygiene passed with nine existing, nonblocking warnings.

## Remaining

The normal open/save smoke lane can now enforce byte identity without exceptions for these demos. Exact
before/after files are retained under the test report's `Evidence` directory for any future difference.
Bounded startup through Standalone and Make reporting remain separate implementation work for #2505.
No runtime result, native handle or unmeasured conversion is pinned by this change; language coverage and
`MISSING_LANGUAGE.md` counts do not change. No IDE feature status changes in `MISSING_FEATURES.md`.
