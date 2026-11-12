## A module's name line below the top of its file (2026-11-12, #3757)

**The question.** VB6 writes a `.bas` with `Attribute VB_Name` as its first line, and a `.cls` with it right after the
`VERSION`…`END` block. A file edited by hand, or by another tool, can have something above it: a comment, a blank line,
`Option Explicit`. Does vb6.exe still load such a file? What does it call the module, and what does its IDE write back
on a save and after a rename?

**Why it was asked.** HexIDE's reader takes a header only when the file's first non-blank line starts one: an
`Attribute` line, or a class's `VERSION` line. Otherwise nothing is split off, and the whole file is code. Then a rename
rewrites nothing, and the saved file keeps the old name (#3757). Whether to rewrite the line where it is, or read the file
differently, depended on what VB6 does with it.

**The obvious guesses:**
- that VB6 reads attributes only from the top of the file, as HexIDE does, so a name line below a comment would be
  ignored or refused;
- or that VB6 would accept it but leave it where it was.

**Both are wrong.** VB6 takes the line from anywhere at module level, and its IDE moves it to the top when it saves.

### The probes

vb6.exe SP6 (6.00.9782), on the Win10 oracle guest.

**`/make`.** Every probed file names its module three ways, so whichever name wins is visible:
- the file name: `Odd.bas` or `Odd.cls`;
- the `.vbp` line: `Module=ModV; Odd.bas` or `Class=ClsV; Odd.cls`;
- the `VB_Name`: `ModA` or `ClsA`.

A `Sub Main` in a second module ran each case under `On Error Resume Next`:
- **For a `.bas`:** it called `OddOnly()` unqualified, which shows that the module loaded, then `ModA.OddOnly()`,
  `ModV.OddOnly()` and `Odd.OddOnly()`. The calling module had no `Option Explicit`, so a name that isn't a module
  compiles as an implicit Variant and raises 424 when it is called.
- **For a `.cls`:** one project per candidate name, each running `Set o = New <name>`, then `TypeName(o)` and `o.Who()`.

Each row was built as native code and as p-code. The two agreed in every row.

| Row | Above the module's code | Built | The module is called | Log |
|---|---|---|---|---|
| B0 | `Attribute VB_Name = "ModA"` as line 1 (the control) | yes | `ModA` (`OddOnly()` 7; `ModA.` 7; `ModV.` and `Odd.` 424) | clean |
| B1 | a comment, then the name line | yes | `ModA` | clean |
| B2 | a blank line, then the name line | yes | `ModA` | clean |
| B3 | `Option Explicit`, then the name line | yes | `ModA` | clean |
| B4 | no name line anywhere | yes | `ModV`, the `.vbp`'s name | clean |
| B5 | `Public Counter As Long`, then the name line | yes | `ModA` | clean |
| B6 | the name line after the last `End Function` | yes | `ModA` | clean |
| C0 | the class header as VB6 writes it (the control) | `ClsA` only | `ClsA`: `TypeName` `ClsA`, `Who` returned | clean; `ClsV` and `Odd`: "User-defined type not defined" |
| C1 | a comment above `VERSION 1.0 CLASS` | `ClsA` only | `ClsA` | as C0 |
| C2 | a blank line above `VERSION` | `ClsA` only | `ClsA` | as C0 |
| C3 | a comment between `END` and the name line | `ClsA` only | `ClsA` | as C0 |
| C4 | a blank line between `END` and the name line | `ClsA` only | `ClsA` | as C0 |
| C5 | `Option Explicit` between `END` and the name line | `ClsA` only | `ClsA` | as C0 |
| C6 | no name line anywhere | `ClsV` only | `ClsV`, the `.vbp`'s name | as C0, with `ClsA` and `Odd` failing |

**The IDE.** The VB6 IDE, same build, in the guest's console session, driven by the agent described under
[Reaching the VB6 *IDE*](README.md#reaching-the-vb6-ide-not-just-the-compiler-2026-09-20). Each project was a Standard
EXE holding `Starter.bas` (`Sub Main`) and the probed file. The names were changed so that type-ahead in the Project
Explorer reached only the probed module: `.vbp` name `WName` or `WClass`, `VB_Name` `QName` or `QClass`. The steps for
each row:

1. Open the `.vbp` and take a screenshot.
2. Select the module in the Project Explorer and open its code window with F7.
3. Press Ctrl+End, type `' dirty`, and save with Ctrl+S. This is **save 1**.
4. Rename the module to `QRenamed` in the Properties window, select it in the Project Explorer, and save with Ctrl+S.
   This is **save 2**.
5. For B0 to B4, C0, C1 and C3, also run File, Save Project.

Every file was copied back after each save, and the saved bytes are the evidence for the save columns. The
screenshots read were every row's code window, which also shows its Project Explorer, and the Properties window for
B1, C1 and C3.

| Row | On opening | The code window shows | Save 1 begins | Save 2 begins |
|---|---|---|---|---|
| B0 | no dialog; `QName (Odd.bas)` | the function | `Attribute VB_Name = "QName"`, then the function | `Attribute VB_Name = "QRenamed"` |
| B1 | no dialog; `QName (Odd.bas)` | the comment, then the function; no `Attribute` line | `Attribute VB_Name = "QName"`, **then the comment** | `Attribute VB_Name = "QRenamed"`, then the comment |
| B2 | no dialog; `QName (Odd.bas)` | the blank line, then the function | `Attribute VB_Name = "QName"`, **then the blank line** | the same, renamed |
| B3 | no dialog; `QName (Odd.bas)` | `Option Explicit`, then the function | `Attribute VB_Name = "QName"`, **then `Option Explicit`** | the same, renamed |
| B4 | no dialog; `WName (Odd.bas)` | the function | **`Attribute VB_Name = "WName"`**, added, then the function | `Attribute VB_Name = "QRenamed"` |
| B5 | no dialog; `QName (Odd.bas)` | the declaration, then the function | `Attribute VB_Name = "QName"`, **then `Public Counter As Long`** | the same, renamed |
| B6 | no dialog; `QName (Odd.bas)` | the function; no `Attribute` line after it | `Attribute VB_Name = "QName"`, then the function; **nothing after `End Function`** but `' dirty` | the same, renamed |
| C0 | no dialog; `QClass (Odd.cls)` | the function | the header as read | `Attribute VB_Name = "QRenamed"` in the header |
| C1 | no dialog; `QClass (Odd.cls)` | the function; **no comment** | `VERSION 1.0 CLASS` first: **the comment is gone**, and the file is C0's | byte for byte C0's save 2 |
| C3 | no dialog; `QClass (Odd.cls)` | the comment, then the function | `VERSION`…`END`, the five `Attribute` lines, **then the comment** | the same, renamed |

In every row the line ends were CRLF, and the rest of the file was as read. After Save Project every `.vbp` named the
module `QRenamed`, as in `Module=QRenamed; Odd.bas`, though its line had said `WName` or `WClass` before.

### What VB6 does

- **The name line is found anywhere at module level, and it decides the module's name.** Neither `/make` nor the IDE
  needs it at the top. It can follow a comment, a blank line, an `Option` statement, a declaration, or the last
  procedure (B1 to B6, C3 to C5).
- **`VB_Name` beats the `.vbp`'s name, silently.** The `.vbp` line and the file name are ignored wherever the file has
  a name line (B0, C0). Neither `/make` nor the IDE warned. Save Project then rewrites the `.vbp` to the module's name.
  The `.vbp`'s name is used only for a file with no name line (B4, C6), and the IDE's next save writes that name into
  the file. This is the reverse of a form, whose `Begin` line wins, with a warning
  ([A form whose two names disagree](a-form-whose-two-names-disagree.md)).
- **The IDE holds the name line apart from the code, and writes it in its place.** The code window never showed it.
  Every save put it first in a `.bas`, and in a `.cls` right after `END` with the class's other `Attribute` lines (C3).
  Everything that was above it stays, in order, below it: a comment, a blank line, `Option Explicit`, a declaration.
  A rename rewrites that line, wherever the file had it.
- **Text above a class's `VERSION` line is dropped at load** (C1). The comment was not in the code window, and no save
  wrote it. That loses the comment, so it reads as a defect rather than a rule.

### In HexIDE (#3757, at `b681449ea`)

The probed files were loaded in HexIDE, with the `.vbp` naming each module as its `VB_Name` did. Each module was renamed
to `QRenamed` in the Properties window, and the project was saved with File > Save Probe:

| File | Rename made | The saved file's name line |
|---|---|---|
| B1's shape | with the code window closed | `QName`, under the comment, unchanged |
| B1's shape | with the code window open | `QName`, unchanged |
| B1's shape, with the comment deleted in the code window and the window closed before the rename | with the window closed | `QName`, now the first line, unchanged |
| C3's shape | with the code window closed | `QClass`, under the comment, unchanged |
| C1's shape | with the code window closed | `QClass`, unchanged; HexIDE keeps the comment, and the program runs |
| B4's shape, no name line, with `Attribute VB_Name = "WName"` typed as the code window's first line and the window closed | with the window closed | `WName`, unchanged |

Each `.vbp` said `QRenamed`, so the saved files disagree with their project. VB6 reads them back by their old names, the
opposite of what was asked. The third and last rows are #3757's case 1, the name line in the code of a module with no
header split off: deleting a comment in the code window reaches it, and so does typing the line into a module that had
none. A whole-file write through the automation surface (`set_file_content`) that adds the line is refused.

HexIDE also names a loaded `.bas` or `.cls` by its `.vbp` line rather than its `VB_Name`. Its next save rewrites the
file's `VB_Name` to match the `.vbp`, the reverse of VB6 (#3774).

### Not measured

- A name line inside a procedure, and two name lines in one file.
- A class whose other `Attribute` lines are apart from its name line, or with a comment between two of them. In C3 all
  five were together.
- Add File (Project, Add File) of such a file, and Add Module's Existing tab.
- Whether Save Project writes a module that is open and unedited. Every row here edited the module before saving it.

The probes and the IDE driver were scratch files and are not in the tree. The step language is the one
[Member attributes](member-attributes-where-vb6-attaches-them-and-what-its-ide-writes.md) describes, plus a `copy`
step that copies a file after each save.
