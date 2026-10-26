## Attribute lines inside a procedure (2026-10-12, #832)

**The question.** VB6 writes `Attribute` lines in two places past a module's header. One is inside a procedure,
right after its first line: a default member (`VB_UserMemId`), a description from Tools → Procedure Attributes
(`VB_Description`), a property category (`VB_ProcData`), a help context (`VB_HelpID`) and flags (`VB_MemberFlags`).
The other is after a module-level variable, for its description and help context. Does any of them do anything
when the procedure is called, or when the module's declarations are run?

**The probe.** A Sub Main project, compiled with `/make` and run, writing each answer to a log file:

- `Module1.bas`: `Function Described() As Long` carrying a `VB_Description` line, returning 7.
- `Widget.cls`:
  - `Public Tag As String`, followed by `VB_VarDescription` and `VB_VarHelpID` lines;
  - `Function Value() As Long` with `VB_UserMemId = 0`, returning 42;
  - `Property Get Caption` with `VB_Description` and `VB_ProcData.VB_Invoke_Property = ";Appearance"` lines, and
    `Property Let Caption` with a `VB_Description` line;
  - `Function Note() As String` with `VB_Description`, `VB_HelpID = 12` and `VB_MemberFlags = "40"` lines.

| Read | Logged |
|---|---|
| `Described()` | 7 |
| `w.Value` | 42 |
| `w.Caption` | `cap:` |
| `w.Caption` after `w.Caption = "set"` | `cap:set` |
| `w.Note` | `noted` |
| `w` as a value, in `"..." & w` | 42 |
| `w.Tag` after `w.Tag = "tagged"` | `tagged` |

**Measured again for the review** (`a01`, `a02`, native and in the IDE, identical), each value written with its
`TypeName`. `Widget` also had `Public Flagged As Long` followed by `Attribute Flagged.VB_VarMemberFlags = "40"`, and
`a02` was a `.bas` alone:

| Read | VB6 |
|---|---|
| `Described()` | 7, Long |
| `w.Value` | 42, Long |
| `w.Caption`; after `w.Caption = "set"` | `cap:`; `cap:set`, String |
| `w.Note` | `noted`, String |
| `"v=" & w` | `v=42`, String |
| `x = w`, `x` a Variant | 42, Long |
| `w + 1` | 43, Long |
| `Print #f, w` | ` 42 `, as Print writes a number, with Err 0 |
| `w.Tag` after `w.Tag = "tagged"` | `tagged`, String |
| `w.Flagged` after `w.Flagged = 5` | 5, Long |
| `a02`: `Public Counter As Long` then `Attribute Counter.VB_VarDescription = "How many"` in Module1.bas; `Counter = 3` | builds; 3, Long, Err 0 |

**The answer.** Every procedure and variable behaves as if its attribute lines were not there. Of those probed, the
attribute that does something at run time is `VB_UserMemId = 0`, and what it does is make `Value` the default
member, so `w` used as a value is 42. The line itself does nothing when `Value` runs. `VB_VarUserMemId = 0` does the
same for a variable (#820's `k5` row, in "RGB, QBColor and MsgBox's style"); it was not among these probes.

**HexIDE, after #832.** It threw "Attribute not implemented" for each of these lines, so calling the procedure
stopped the run. An attribute line is now metadata: running it does nothing. The debugger treats it as it treats a
blank line: a step goes from a procedure's first line to its first statement, and passes over one nested in a block
too; Set Next Statement onto one is refused; and a breakpoint or Run To Cursor on one is accepted and never
reached, because neither checks its line. VB6's code window hides these lines, so that has no VB6 counterpart.
HexIDE's shows them until #273, which makes each run read-only, folds it into the line it describes, and refuses a
breakpoint on it. The default member is not used as one
yet: since #840, a use of `w` itself as a value (`"v=" & w`, `x = w`, `w + 1`) is refused, untrappably, naming #174.
`Print #f, w` stops before it gets there, at `Open`, which HexIDE does not implement yet.

