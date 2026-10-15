# Member-attribute corpus

The only files in the repository whose code carries members' attribute lines, the runs VB6 writes below a
module's header to give a member its description, help topic, dispatch id or default-member status:

```vb
Public Function Average(ByVal First As Long, _
                        ByVal Second As Long) As Double
Attribute Average.VB_Description = "Mean of two values."
Attribute Average.VB_HelpID = 7
```

Phase 4 of hexide-io/HexIDE#273 greys, folds and protects such a run in the code window, so every lane that reads
whole VB6 files has to see one, and until these files no file in the tree had one. Inline fixtures could only
hold the shapes someone thought to write; these hold the shapes VB6 writes.

## Two halves

| Folder | What it is | Walked by the corpus lanes |
|---|---|---|
| `round-trip/` | Six projects whose every member `Attribute` line VB6's IDE wrote, through Tools > Procedure Attributes or by its own save | **Yes**: every member `Attribute` line in them is VB6's, and so is every file but the few named under Provenance, and HexIDE must give each back unchanged |
| `evidence/` | Four projects of hand-written shapes, each `input/` beside the `saved/` VB6 wrote back after loading it | **`saved/` and `saved-after-dialog-edit/` only**: those are VB6's output, its bytes as much as the round-trip half's. Not `input/`. Both halves are read by the server-parity check, which compares the two copies of the rule and is no round trip, and by `MemberAttributeCorpusTests` |

An input is not a round-trip fixture because VB6 rewrote it: it hides every `Attribute` line at load, drops a
procedure attribute that reaches nothing, and moves a variable attribute under its variable at its next save. The
inputs are the orphan shapes, a run VB6 gives to no member, and the pairs are the evidence for how HexIDE reads
them, which is `MemberAttributeCorpusTests.AHandWrittenRunDescribesAMemberExactlyWhereVb6KeptIt`. What VB6 wrote
back is walked with the round-trip half, because it carries shapes that half does not: runs under a `Declare`, a
`Friend Function`, a `Static Function`, a `Private Sub` and a declaration with a trailing comment, constants' runs,
private variables' runs in a module and a class, a property with a run under each accessor, the help id VB6 gave a
bare `WithEvents`, and a header holding the dotless `VB_Description` VB6 moved into it.

## Provenance, stated exactly

**Written by the VB6 IDE, not only accepted by the compiler.** VB6 6.00.8176 (SP6) on the oracle VM (locale
en-GB), two sessions on 2026-10-13, driven in the guest's interactive console session by keystrokes and clicks,
every step that mattered confirmed from a screenshot. That is what `corpus/designer/README.md` said the next
question of this kind would need. The probes, the steps and every measurement are in
[`docs/vb6-fidelity-oracle.md`](../../docs/vb6-fidelity-oracle.md#member-attributes-where-vb6-attaches-them-and-what-its-ide-writes-2026-10-13-273),
under the probe names used below: the first session's by letter (A, B, B2, E), the second's by number (V1 to V8).

The code was authored here as plain text (CRLF, ASCII, no BOM), with no member attribute in it. Each project was
opened in the IDE, its members described through **Tools > Procedure Attributes**, edited where a probe called
for it, and saved by VB6. So in `round-trip/`:

- **every member `Attribute` line was written by VB6.** One began as text: `Probe2/PasteMod4.bas`'s run was pasted
  into the code window, which VB6 keeps as code, and became an attribute only when VB6 reloaded the project and
  saved it again (V4). It is VB6's output either way, since a save after a load regenerates every run.
- **every `.cls`, `.bas`, `.frm` and `.ctl` was saved by VB6**, apart from the `Starter.bas` files, and so was
  `MemberAttributes/MemberAttributes.vbp`, which VB6 changed in one place: its `Reference=` path, absolute as
  written and relative as saved, as [the IDE normalises](../../docs/vb6-fidelity-oracle.md#absolute-paths-are-legal-and-the-ide-normalises-away-the-ones-it-can)
  every path it can. `..\..\..\Windows\SysWOW64\stdole2.tlb` is relative to where the project lived on the
  guest, three folders below the drive's root, not to where it is here; nothing in HexIDE resolves it.
- **`Acc2.vbp`, `Probe2.vbp` and `Tar2.vbp` were written here and saved again by VB6**, which gave back the same
  bytes: their timestamps on the guest moved at VB6's save and their contents did not. Their `Reference=` path
  was written relative, as VB6 would write it.
- **`Extras.vbp` and every `Starter.bas` are as written here**, in the shape VB6 writes them, and never saved by
  VB6: each left the guest with the timestamp it went in with. `MemberAttributes-stage1/MemberAttributes.vbp` is
  that project's file before VB6 saved the project, so it is the file written here too, and its `Reference=` path
  is still the absolute one, to the system folder.

Nothing here was copied from VB6's Template tree or any other Microsoft file; the code is this repository's own
and MIT, as [the corpus is authored rather than borrowed](../../docs/vb6-grammar-fixes.md#why-the-corpus-is-authored-rather-than-borrowed-2026-09-02).
Window state (`.vbw`) is left out.

## `round-trip/`

| Project | Probe | What its files carry |
|---|---|---|
| `MemberAttributes` | A | `Ledger.cls`: an `Event`'s run; one run under `Public A As Long, B As Long` for two of its declarators, grouped by declarator; a `Function` left with `VB_UserMemId = 0` alone after its description was cleared; a `Property Get`'s; a run under a declaration continued with ` _`; `VB_HelpID` with Hide. `Tariff.cls`: `WithEvents` with the `VB_VarHelpID = -1` VB6 adds by itself, and a Let-first property described with `(Default)` in one visit, whose run went under the `Get`. `Helpers.bas`: a standard module. `Main.frm`: a form, `WithEvents` again. `Gauge.ctl`: a UserControl property with a category and User Interface Default |
| `MemberAttributes-stage1` | A | The same project after the first stage, each component saved with Ctrl+S and the project not yet saved. Only `Ledger.cls` differs from the folder above: a description holding a doubled quote, `"Name of the ""owner""."`, on a one-name variable; a `Function`'s description beside its `(Default)`; and `B`'s run alone under the comma list, keyed by the declarator it names |
| `Extras` | E | `Panel.ctl`: every option the dialog leaves enabled, on a property (`Full`) and on a variable (`Spare`, whose lines come in the reverse order); an event's; a run for the `B` after the colon of `Public A As Long: Public B As Long`; a Let-only property's, under the `Let`; a description cut to the 254 characters VB6 keeps |
| `Tar2` | V6 | Let-first properties: a description alone under the `Let` (`TarD`, `TarV`), `(Default)` added to a run already there (`TarVD`), and description with `(Default)` in one visit under the `Get` (`TarDD`, the `Tariff` replica). `TarV` was edited after describing: `Private mExtra As Long` typed into its declarations and the class saved, and the run stayed under the `Let` |
| `Acc2` | V6, V2 | Properties of every accessor pairing, each run under the first accessor in the file: `Set` first (`SetGetB`, `SetGetC`, `SetLetB`), `Let` first (`LetGetA`, `LetGetB`, `LetGetC`, `LetSetB`, `LetSetC`), `Get` first (`GetSetB`); in `GetSetB` and `LetGetA` the dialog opened on another member and a `Sub` took a description. Comma lists edited after describing: `MultiP`, `MultiXY`, and `MultiMN`, which VB6 left with no run |
| `Probe2` | V1 to V8 | Get, Set and Let first (`GetSet6`, `SetGet6`, `LetSet6`, `SetLet6`); a variable renamed by retyping (`Var1`) and one whose type was retyped (`Retype8`), each run carried and re-qualified; a procedure retyped `Function` to `Sub` and back (`FuncSub3`, `SubFunc3`); the paste probes (`Paste4`, `PasteMod4`). `Decl7` and `Multi2` carry no run, VB6 having dropped theirs (V7, V2), and are here because the project must be whole to open |

`Gauge.ctl` is in `MemberAttributes`, in its stage-1 copy, and in `corpus/designer`; the last is a different control
in a different project.

## `evidence/`

| Project | Probe | Hand-written in `input/`, what VB6's save did in `saved/` |
|---|---|---|
| `LoadShapes` | B | A run under a blank line, a comment or a statement, dropped; one naming nothing, another member, or its member in lower case, kept and re-qualified; two descriptions in one run, the first kept; a dotless line below code, moved into the header; `WithEvents` with no line, given one; variable lines away from their variables, moved under them; a run under a `Let` ahead of its `Get`, kept there. `saved-after-dialog-edit/LetFirst.cls` is that run after a dialog edit, still under the `Let` |
| `MoreShapes` | B2 | A property whose `Get` and `Let` each carry a run, in both orders, both kept; a run split by a blank line, the half after it dropped; runs under a `Declare`, a `Private Sub`, a `Friend Function`, a `Static Function` and a declaration with a trailing comment, kept; one indented and one with extra spaces, rewritten at column 1 and single-spaced; a `Public Const` in a class, dropped; a standard module's variable and `Sub Main`, kept |
| `Load2` | V5 | Runs under `Enum` and `Type` lines, in a module and a class, dropped; a `Const` taking a `VB_VarDescription`, kept, and a `VB_Description`, dropped |
| `Const2` | V5 | Constants: typed, private, the second of a comma list, private in a class, all kept; one described from under a procedure, moved under its `Const`; a `Public Const` in a class, which VB6 rejects, dropped; an enum member's, dropped; private variables', kept |

Every `.vbp` in `evidence/` came back from VB6's save byte-identical. HexIDE does not drop or move what it cannot
show the developer, so an orphan stays visible and editable where VB6 discards it (design.md, "Settled before
building phase 4"). One shape is read the other way, and is recorded there too: a run under a `Public Const` in a
class (`MoreShapes/DeclShapes.cls`, `Const2/ConstClsPub.cls`) is read as the constant's and protected, where VB6
drops it, because the reader does not know the module's kind. And a run giving one attribute twice (`DoubleDesc`) is
protected whole, where VB6's save keeps the first line and drops the second.

## Not here

The intermediate states the probes passed through (the inputs before describing, the snapshots of every stage but
`MemberAttributes`' first, and the code-window edits of the first session's C probes) are recorded in the oracle
file, not kept as files.
