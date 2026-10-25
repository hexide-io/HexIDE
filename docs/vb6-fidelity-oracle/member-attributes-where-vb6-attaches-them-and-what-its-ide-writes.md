## Member attributes: where VB6 attaches them, and what its IDE writes (2026-10-13, #273)

**The question, in two halves.** Below its header a module may carry `Attribute` lines for its members, and VB6
writes them: `Attribute Total.VB_Description = "..."` directly under `Public Function Total() As Long`. The first
half is the compiler's: given an `Attribute` line anywhere in a module's body, which member, if any, does VB6
attach it to? The second is the IDE's: what does VB6's own IDE write through Tools > Procedure Attributes, and what
does it keep, move or drop when it loads a file somebody wrote by hand, when it saves one, and when a described
member is edited in the code window?

**Why HexIDE needed to know.** Phase 4 of hexide-io/HexIDE#273 greys, folds and protects a member's attribute run
in the code window, as phase 3 did for the header. Each of those needs a verdict on which lines are a member's run
and which are text describing nothing, and a rename, a deletion and a save each need to know what VB6 does with
them. The rules HexIDE took from what follows are in the `design.md` of the openspec change
`2026-09-19-name-a-document-by-where-it-lives`, under "Settled before building phase 4". This entry is the
measurement, not the design.

**Probe names**, kept as recorded so each claim below traces to one: the compiler probes `p01` to `p14f`; the first
IDE session's by letter (A, the dialog; B and B2, hand-written shapes loaded and saved; C1 to C9b, edits; D, the
code window; E, a UserControl given every option the dialog left enabled); the second IDE session's V1 to V8; the
third's Q1 to Q3, the three questions it asked of VB6's saves. A module inside a probe is named where it matters
(`BlankGap`, `TarDD`).

### Method

**The compiler half.** VB6.EXE 6.00.8176 (the original release) in the Hyper-V guest `Win10` (locale en-GB), driven over PowerShell
Direct as `VB6.EXE /make <abs .vbp> /out <abs log>` with `CreateNoWindow` and a 90-second timeout, every project
under `C:\hexoracle-p4\make\<probe>-<variant>\`. Sixty probes, each one class (`Class1.cls` with the canonical
13-line header of the 2026-09-19 entry's b1) or, for p06e and p13d, a second standard module. p10a to p10f also
carry a `Class2.cls` that raises an event (`Public Event Fired()` and a `Public Sub Raise()`), MultiUse and, in the
DLL, `VB_Exposed = True`. Each was built in up to three variants:

| Variant | Builds | What it measures |
|---|---|---|
| Standard EXE, the class `VB_Exposed = False` | 54 | whether `/make` accepts the shape |
| ActiveX DLL (`Type=OleDll`, `CompatibleMode="0"`, the class `MultiUse`, `VB_Exposed = True`) | 52 | the same, plus the compiled **type library** |
| Standard EXE with `Public Sub Zed()` / `Debug.Print 0` / `zz = 1` / `End Sub` appended | 59 | `Line N` for `zz = 1`, a `Variable not defined` under `Option Explicit`, which shows which lines above it the code view counted |

All 165 builds were run twice, and the second run matched the first on every exit code, `/out` text and
type-library line. The type library was read in the guest through the in-box `oleaut32!LoadTypeLibEx` with
`REGKIND_NONE`: a `VB_Description` shows as its member's help string, `VB_UserMemId = 0` as memid 0, `VB_HelpID`
and `VB_VarHelpID` as the help context. That is the evidence that a line was **attached** to a member; that a
build succeeded shows only that the line was accepted. Nothing was run.

What this half cannot see: anything the IDE does, so whether a save keeps, moves or drops a line; where a line in a
standard module went, since a `.bas` has no type library; a `Private` member, which is not in the type library
(p10c); and which accessor of a property holds a description, because `ITypeInfo::GetDocumentation` looks a help
string up by memid and a property's Get and Let share one. Memids are per accessor and were read from each
`FUNCDESC`. Help strings and help contexts both came from `GetDocumentation` by memid, so a property's accessors,
or a variable's get and put, always show the same value, by construction of the reader.

**The IDE half.** The VB6 IDE, same build, same guest, driven in the guest's interactive console session through
the long-lived agent described under [Reaching the VB6 *IDE*, not just the
compiler](README.md#reaching-the-vb6-ide-not-just-the-compiler-2026-09-20), installed in this guest at
`C:\hexide-filewhen`. Each batch of steps was one `exec:` action running a driver script over a step file, one
step per line: `launch <vbp>` (records the PID of the VB6 it starts), `k <SendKeys>`, `kc <modifiers> <key>` (a
chord held through `keybd_event`, see *Rig shortcomings*), `t <text>`, `click x y`, `w <ms>`, `shot <label>`,
`files <dir>`, and in the second session `expect <title text>`, `clip <file>`, `clipdump` and `rclick x y`. Before
every keystroke the driver checked that the foreground window was that VB6's. In the second session an `expect`
step, placed before or after every `F7`, every return from a dialog and every `Alt+T`, checked that the foreground
title named the intended code window. Every step that mattered was confirmed from a screenshot, never from
SendKeys' return, and after every save the project directory was copied back to the host. Two sessions, with
projects under `C:\hexoracle-p4\ide\` and `C:\hexoracle-p4\ide2\`, every one a Standard EXE, and a third, below.

A member is reached through the Project Explorer: type-ahead the component's name, `F7`, then `Ctrl+F` with the
declaration's text, `Enter` and `Esc`, which leaves the caret on the declaration. Tools > Procedure Attributes is
**Alt+T, A**. The first session's description of `Total`, exactly as run:

```
k ^f
t Function Total
k {ENTER}
k {ESC}
k %t
k a                      <- Procedure Attributes
click 455 200            <- Description box
t Sum of every entry.
click 441 313            <- Procedure ID (after Advanced >>)
click 381 344            <- (Default)
click 629 217            <- Apply
```

The code was ours, written as plain text (CRLF, ASCII, no BOM). Every member `Attribute` line in a file the dialog
produced was written by VB6. The hand-written shapes are the inputs, and what VB6 saved from them is the result.
To make an untouched module save, each load-and-save project (B, B2 and V5's two) was dirtied by one Replace All
of a marker comment, `' p4 marker` to `' p4 marker2`, over the Current Project. Wherever the count was recorded,
VB6 reported one replacement per module (13, 8 and 10), and the marker is the only other change in any file saved
from them. Neither session measured that premise; the third did, and it holds for Save Project (*Which saves write
a module*, below).

**The third IDE session** (2026-10-14) used the same agent and rig as the second session, with the project at
`C:\hexoracle-p4\ide3\S`, a Standard EXE, in two VB6 launches, each recorded by PID and each closed through File >
Exit, and no `/make` running in the guest at any point, which every batch checked before it started. The one member
edited was opened as above, by type-ahead and `F7`; a member saved untouched was selected with one click on its
Project Explorer row and never opened. The File menu's `Save <file>` and `Save <file> As...` items name the member
the Project Explorer has selected, so each was read from a screenshot before Alt+F and **S** or **A** was sent
(*Rig shortcomings*, 12), and Save Project was Alt+F, **V**. A Save As's file name went
in through the clipboard and `Ctrl+V`, not typed, so a mixed-case name kept its case. Each file's SHA-256, length
and timestamp were read in the guest over PowerShell Direct before and after every step, not from the agent's log,
and the guest directory was copied back to the host after each.

What this half can see: what the dialog writes, what a save writes back, what the code window shows, and what the
dialog and the Object Browser report for a member. What it cannot: whether the compiler attaches what the IDE
keeps, except through those two windows; and anything a Standard EXE does not exercise (no ActiveX DLL, OCX, group
or `VB_Exposed = True` class in either session).

### Where the compiler attaches a line

p04, as compiled (the header's thirteen lines left out):

```vb
Option Explicit
Public Sub Foo()
Attribute Gone.VB_Description = "p04"
    Debug.Print 1
End Sub
```

Type library: `Foo`, help string `[p04]`. Nothing in the class is called `Gone`.

**A procedure attribute binds by position, and its qualifier is not read.** Any dotted name not starting `VB_Var`,
in the run directly under a procedure's declaration, goes to that procedure whatever it names:

| Probe | Under `Public Sub Foo()` | Type library |
|---|---|---|
| p04 | `Attribute Gone.VB_Description`, `Gone` declared nowhere | `Foo` [p04] |
| p05a / p05b | `Attribute Other.VB_Description`, `Other` a real Sub declared after / before `Foo` | `Foo` [p05a] / [p05b]; `Other` none |
| p05d | `Foo.VB_Description = "p05d-foo"`, then `Other.VB_Description = "p05d-other"` | `Foo` [p05d-foo]; `Other` none |
| p05g | two `Foo.VB_Description` lines, `"p05g-first"` then `"p05g-second"` | `Foo` [p05g-first] |
| p12a to p12d | `attribute foo.`, `Attribute foo.`, `attribute Foo.`, and the line indented four spaces | `Foo` [p12a] to [p12d] |
| p08 | under the **last** physical line of `Public Sub Foo(ByVal a As Long, _` / `ByVal b As Long)` | `Foo` [p08] |
| p08b | under `Public Sub Foo() ' trailing comment` | `Foo` [p08b] |
| p10h | `Foo.VB_HelpID = 9` | `Foo` help context 9 |
| p09n | `A.VB_VarDescription`, then `Foo.VB_Description`, `A` a module variable | `A` [p09n-A] **and** `Foo` [p09n-foo] |

p09n shows that a `VB_Var` line inside a procedure's run neither breaks the run nor is taken by the procedure. Of
two `VB_Description` lines in one run the first stands (p05g, and p05d agrees).

**Away from the declaration line, a procedure attribute reaches nothing**, even when it names the procedure
nearest to it:

| Probe | Where `Attribute Foo.VB_Description` sat | Type library |
|---|---|---|
| p01 | under `Public Sub Foo()`, with a blank line between | `Foo` none |
| p02 | with a comment line between | `Foo` none |
| p03 | after the body's first statement | `Foo` none |
| p03b | the same, with a later `Sub Bar` | `Foo` none, `Bar` none: not held for the next member |
| p05e | under `End Sub`, between `Foo` and `Bar` | `Foo` none, `Bar` none |
| p05f | in the declarations section, above `Sub Foo` | `Foo` none, the class none |
| p05i | under `Public A As Long` | `A` none, `Foo` none |
| p05h | `Foo.VB_VarDescription` (a variable attribute naming a procedure) under `Public A As Long` | `A` none, `Foo` none |

Each of these compiled in every variant and was hidden from the code view. `/make` said nothing about any
attribute in any build, wherever it sat and whatever it named, and no build wrote a `.log` beside its sources,
timed out, or stopped on a dialog. The only failure an attribute line caused was p07's, below, and that is a syntax
error in code.

**A variable attribute binds by name, from anywhere after the declaration.** A `VB_Var…` line finds a module-level
variable by the name it gives, wherever it sits after that variable's declaration. p09e:

```vb
Option Explicit
Public A As Long
Public B As Long
Attribute A.VB_VarDescription = "p09e-A"
```

Type library: `A` [p09e-A], `B` none.

| Probe | Shape | Type library |
|---|---|---|
| p09c | control: `Public A As Long` / its line / `Public B As Long` / its line | `A` [p09c-A], `B` [p09c-B] |
| p09a | `Public A As Long, B As Long` / `B.VB_VarDescription` | `B` [p09a-B], `A` none |
| p09d | the same line / `A.VB_VarDescription`, the **first** declarator | `A` [p09d-A], `B` none |
| p09b | the same line / both lines | `A` [p09b-A], `B` [p09b-B] |
| p09g | `Public A` / a blank line / `A.VB_VarDescription` | `A` [p09g] |
| p09h | the `A` line directly under `Public Sub Foo()` | `A` [p09h], `Foo` none |
| p09j | the `A` line inside `Foo`'s body, after a statement | `A` [p09j] |
| p09l | the `A` line **before** `Public A As Long`, directly under `Option Explicit` | `A` none |
| p09f | `Gone.VB_VarDescription` under `Public A` | `A` none |
| p09i | `A.VB_Description`, not `VB_Var…`, directly under `Public A` | `A` none |
| p10g | `A.VB_VarHelpID = 7` | `A` help context 7 |

The type library lists each variable as a get and a put, and the description read back is the same on both, which
the reader guarantees (*Method*, above) rather than the compiler shows. Case was not varied in a `VB_Var` qualifier.

The IDE carries the same rule to constants (V5, under *What the IDE writes*). A `VB_VarDescription` naming a
module-level `Const` is kept, public or private, typed or not, the second of `Const C1 = 1, C2 = 2` included
(`ConstV`, `ConstTyped`, `ConstPriv`, `ConstClsPriv`, `ConstMulti`); it is moved under its `Const` from elsewhere
(`ConstAway`, public and untyped, in a `.bas`); and the Object Browser shows it as the constant's description
(`ConstV`'s `Limit`, the one constant looked at there). This holds for a legal declaration only: under a `Public
Const` in a class, which VB6 rejects, the run is dropped. An `Enum` or `Type` line takes neither kind in a `.bas`,
an `Enum` line takes no `VB_Description` in a class, and an enum member takes no `VB_VarDescription`; a
`VB_Description` under a `Const` reaches nothing. Whether a constant's description reaches a compiled type library
was not measured, and probably cannot be: a `Public Const` is rejected in an object module, a private member is not
in the type library (p10c), and a standard module has none.

**A dotless line below code is a module attribute, wherever it is.** p06a (after `Option Explicit` and a
declaration), p06b (between two procedures) and p06c:

```vb
Option Explicit
Public Sub Foo()
Attribute VB_Description = "p06c"
    Debug.Print 1
End Sub
```

Type library: `_Class1` and `Class1` both [p06c], `Foo` none. p06e, the same between two procedures of a `.bas`,
compiled and was hidden; a standard module has no type library to show where it went.

**An `Attribute` line inside a continued declaration is code, and `Attribute` is a reserved word.** p07 fails in
all three variants with `Line 2 : Expected: identifier`:

```vb
Option Explicit
Public Sub Foo(ByVal a As Long, _
Attribute Foo.VB_Description = "p07"
ByVal b As Long)
```

Had VB6 lifted the attribute line out first, the declaration would have rejoined and compiled. p07c shows the
mechanism from the other side: `' a comment _` followed by `Attribute Foo.VB_Description = "p07c"` compiles, and
`Foo` has no help string, because the second line is the rest of the comment. p07e, `Dim Attribute As Long` in a
procedure body, fails with `Line 2 : Syntax error`, which is why p07's parser wanted an identifier. VB6 reads
logical lines before it decides what an attribute line is.

**A continued attribute line is one attribute line.** p11a, `Attribute Foo.VB_Description = _` / `"p11a"`, and
p11b, `Attribute _` / `Foo.VB_Description = "p11b"`, both compile and both attach (`Foo` [p11a], [p11b]). A module
attribute continued the same way, `Attribute VB_Name = _` / `"Mod1"`, was already recorded as legal under [Two
asymmetries that were sitting in the corpus unread](line-continuations-and-statement-separators.md#two-asymmetries-that-were-sitting-in-the-corpus-unread-2026-09-02).
In the one long description tried through VB6's own dialog (E's `Longest`, below), the dialog cut the text to 254
characters and did not continue the line.

**`WithEvents`, and `VB_VarHelpID = -1`.**

| Probe | Shape | `T` (or `O`) in the type library |
|---|---|---|
| p10a | `Public WithEvents T As Class2` / `Attribute T.VB_VarHelpID = -1` | get and putref, help context **-1** |
| p10b | the same without the attribute line | help context **-1** |
| p10f | the same with `VB_VarHelpID = 7` | help context **-1** |
| p10e | `Public O As Class2`, not `WithEvents`, no attribute line | help context 0 |
| p10c / p10d | `Private WithEvents T As Class2`, with and without the line | not in the type library; both compile, and p10c's line is hidden |

The line VB6 writes under a `WithEvents` declaration changes nothing that compiles: such a variable's help context
is -1 with the line, without it, and with 7 in its place, while a plain variable takes the 7 (p10g). The IDE adds
the line by itself (below).

**Property Let before Get, and `VB_UserMemId = 0`.** All six shapes compiled as EXE and as DLL:

| Probe | Shape | memid of put / get | Help string read back |
|---|---|---|---|
| p14a | Let first, carrying `VB_Description` and `VB_UserMemId = 0`; Get after, bare | 0 / 0 | [p14a-let] |
| p14c | control: Get first carrying the run; Let after, bare | 0 / 0 | [p14c-get] |
| p14d | Let first, bare; Get second, carrying the run | 0 / 0 | **none** |
| p14e | Get first, bare; Let second, carrying the run | 0 / 0 | **none** |
| p14b | a run under each, Let first, `"p14b-let"` and `"p14b-get"` | 0 / 0 | [p14b-let] |
| p14f | the mirror, Get first | 0 / 0 | [p14f-get] |

`VB_UserMemId = 0` under either accessor makes the property the default member, both accessors memid 0. p14a is
the `k4` shape of [An argument that is not a number](rgb-qbcolor-and-msgboxs-style.md#an-argument-that-is-not-a-number), whose Let-first default
member was measured to work at run time, with a description added: `k4`'s Let carries `VB_UserMemId = 0` alone.
The description is not stored once per property name: if it were, p14d and p14e would show it from whichever
accessor the reader picked, and they show nothing. The string read back is always the first accessor's own. The
IDE shows what it holds: when both accessors carry a description, the dialog shows the first accessor's in file
order, `let` for Let-then-Get and `get` for Get-then-Let, and a save keeps both runs, each under its own accessor
(B2, `BothRuns` and `BothRunsGetFirst`). Whether the second accessor's string is in the compiled type library is
still unseen: the reader and the dialog both key a description to the property, so neither can show it.

### What `Line N` counts, refined (hexide-io/HexIDE#968)

[What `/make` writes to `/out`](what-make-writes-to-out-and-what-line-n-counts.md#what-make-writes-to-out-and-what-line-n-counts-2026-09-19) found `N` to be a
0-based index into the code view, which leaves out every `Attribute` line, physical lines counted. All 58 error
variants here that reached `zz = 1` fit that, refined in one respect: **a logical line whose first word is
`Attribute` is left out together with its continuation lines, and every other physical line is counted.** Two
predictions were computed for every probe before it ran, and logged beside each build: (a) every line counted, and
(b) each physical line starting `Attribute` left out. A third, (c), each logical line starting `Attribute` left out
with its continuations, is the rule the results support; it was fitted to them, and applied to every probe
afterwards, when the per-probe record was composed. Three shapes separate (b) from (c):

| Probe | Shape | (a) | (b) | (c) | VB6 printed |
|---|---|---|---|---|---|
| p11a | `Attribute Foo.VB_Description = _` / `"p11a"` | 8 | 7 | 6 | **Line 6** |
| p11b | `Attribute _` / `Foo.VB_Description = "p11b"` | 8 | 7 | 6 | **Line 6** |
| p07c | `' a comment _` / `Attribute Foo.VB_Description = "p07c"` | 8 | 7 | 8 | **Line 8** |

The other 55 agree with (b) and (c) alike, whether or not the line was attached to anything: a declarations-section
`VB_VarDescription` (p13a); a three-line procedure run with the error in a later procedure (p13b); both together
(p13c); a `.bas` (p13d); the 2026-09-19 entry's d1 reproduced, `Line 5` again (p13e); every misplaced and
orphaned line in the tables above; the lower-case and indented ones (p12a to p12d); and the dotless lines below
code (p06a to p06c, p06e). Four of the 55 (p10b, p10d, p10e, p13f) have no attribute line in the code at all, so
there all three predictions agree.

Against the 2026-09-19 entry:

- Its "physical lines are counted; a `_` continuation is not collapsed" stands for code, as its e1 found, and
  p07c's continued comment counts both its lines. What is new is that an **attribute** line's continuation lines
  are left out with it (p11a, p11b).
- Its open question, whether a module-level `Attribute` after code has started is skipped the same way, is
  answered: it is (p06a to p06c, p06e).
- Its f3 found a `Syntax error` in a continued statement carrying the statement's first physical line. p07's
  `Expected: identifier` carried `Line 2`, the attribute line itself, which under (c) is the offending token's own
  physical line, not the start of the logical line (`Line 1`). A different message, so no conflict, but "a syntax
  error carries the logical line's start" is not general.

The IDE's status bar counts the same way, 1-based where `Line N` is 0-based: C1's caret on `Total = mSum`, below a
hidden run, read `Ln 7`, and V1's read `Ln 4` on the blank line under a described variable. A pasted attribute line,
which the IDE holds as code until a reload, is counted (V4, below).

Before #968, HexIDE's `Vb6ToolchainService.IsHiddenFromVb6LineCount` tested each physical line for `Attribute `, which is
(b): one line off below p11a, p11b and p07c, and on p07's own error. VB6 printed `Line 2` for p07's attribute line,
which continues the declaration and so is counted; `EditorLineOf` skipped that line as hidden and put `Line 2` on
`ByVal b As Long)`, the line after it. All four are hand-made shapes, and VB6's dialog was not seen to write any of
them. Fixed in hexide-io/HexIDE#968: the mapper inherits the first physical line's hidden verdict across
continuations, using the same continuation rule as `ReadOnlyRegions`.

### What the IDE writes

**Format.** VB6 never keeps an attribute line it loaded as text. It hides every one at load, attached or not, and
writes each member's run afresh at save:

- at column 1, directly under the **last** physical line of the member's declaration (A's `Average`, whose
  declaration is continued over two lines);
- qualified with the member's name as declared, in its declared casing;
- as `Attribute <name>.<attribute> = <value>`, one space either side of `=`, a string value in double quotes with
  an embedded quote doubled (A's `Owner`, described `Name of the "owner".`, written `"Name of the ""owner""."`);
- `VB_MemberFlags` and `VB_VarMemberFlags` as **lower-case hexadecimal in a string**: `"40"` for Hide, `"200"` for
  User Interface Default, and `"127c"` for E's `Full` with every option the dialog left enabled (*Don't show in
  Property Browser* was greyed once Hide was ticked, so it was not set, and 0x400, `FUNCFLAG_FNONBROWSABLE`, is not
  among the bits), which is 0x1000 + 0x200 + 0x40
  + 0x20 + 0x10 + 0x8 + 0x4 (immediate bind, UI default, hidden, default bind, display bind, request edit,
  bindable);
- `VB_UserMemId` as a decimal (`0` for `(Default)`, `-520` for the Procedure ID `Appearance`);
  `VB_ProcData.VB_Invoke_Property = "<page>;<category>"` for a procedure (`";Behavior"`,
  `"StandardColor;Appearance"`), and `VB_VarProcData = ";Behavior"`, with no `.VB_Invoke_Property`, for a variable;
- a description cut to its **first 254 characters**, silently, and not continued with ` _`, in the one case tried
  (E's `Longest`: 1100 characters typed in blocks of 100, all of them shown when Apply was pressed; 254 in the file,
  counted, and the dialog re-opened later showed the text ending inside the third block);
- CRLF throughout, no BOM, and a final CRLF, in every module file either session had VB6 save.

Apart from the `Attribute` lines and the two `WithEvents` declarations the rig typed, VB6 changed no byte of the
code in any of A's files, and the hand-written shapes changed only where the tables below say. The `.vbp` of
every load-and-save project came back byte-identical. A's changed only its `Reference=` path, absolute as written
and relative as saved (`..\..\..\Windows\SysWOW64\stdole2.tlb`), as [the IDE
normalises](what-a-vbp-tolerates-and-the-one-key-for-a-non-code-file.md#absolute-paths-are-legal-and-the-ide-normalises-away-the-ones-it-can) every path it can.

A's first stage, some of the lines VB6 inserted, in context (`Ledger.cls`):

```vb
Public Event Changed(ByVal Amount As Long)
Attribute Changed.VB_Description = "Raised after an entry."

Public Owner As String
Attribute Owner.VB_VarDescription = "Name of the ""owner""."
Public A As Long, B As Long
Attribute B.VB_VarDescription = "Second counter."
...
Public Function Average(ByVal First As Long, _
                        ByVal Second As Long) As Double
Attribute Average.VB_Description = "Mean of two values."
...
Public Sub Reset()
Attribute Reset.VB_HelpID = 42
Attribute Reset.VB_MemberFlags = "40"
```

**Order within a run is fixed by kind, and reversed for variables.** For a procedure: `VB_Description`,
`VB_HelpID`, `VB_ProcData.VB_Invoke_Property`, `VB_UserMemId`, `VB_MemberFlags` (E's `Full`; A's `Reset`, `Total`,
`Average` and `Level` agree). For a variable: `VB_VarMemberFlags`, `VB_VarUserMemId`, `VB_VarProcData`,
`VB_VarHelpID`, `VB_VarDescription` (E's `Spare`; A's `A` agrees). The order the options were ticked made no
difference: Hide was ticked before Help Context ID for both `Reset` and `A`, and the two came out in opposite
orders. A run under a line that declares several variables holds each declarator's lines grouped by declarator, in
declaration order (A, second stage):

```vb
Public A As Long, B As Long
Attribute A.VB_VarMemberFlags = "40"
Attribute A.VB_VarHelpID = 5
Attribute B.VB_VarDescription = "Second counter."
```

Clearing a value removes its line, and a member left with none has no run: `Total`'s description cleared leaves
`VB_UserMemId = 0` alone under it, and `Owner`'s leaves nothing (A, second stage).

**Where the dialog puts a run.**

- Under the last physical line of a continued declaration (A's `Average`, above).
- Under a line holding two `:`-separated declarations, for the one **after** the colon (E):
  `Public A As Long: Public B As Long` / `Attribute B.VB_VarDescription = "After a colon."`.
- Under an `Event` line (A's `Changed`), under a `Public Sub` in a standard module (`Helpers.bas`), and in a form
  (`Main.frm`) and a UserControl (`Gauge.ctl`): the dialog is enabled in each.
- For a property, under the **first accessor in the file**, Get, Let or Set alike. That held in all eighteen
  description-only cases of V6, across five orderings, Get then Set, Set then Get, Let then Set, Set then Let and
  Let then Get, with the dialog opened from either accessor or from its Name list; and A's `Rate` (Get then Let,
  picked from the Name list) went under the Get. **Except** that Description and Procedure ID `(Default)` set
  together on a property with no run yet put the run under the Get, twice: A's `Tariff` and V6's `TarDD`, both Let
  then Get. `TarD`, the same shape cleared the same way and described without `(Default)`, went under the Let.
  `(Default)` added later to a run already under a Let left the run there (`TarVD`), and so did a dialog edit to a
  loaded run under a Let (B's `LetFirst`), and so did an edit elsewhere in the class and a save: `Private mExtra As
  Long` typed into `TarV`'s declarations, after its description had gone under the Let, and the project saved
  (V6, the step between `TarVD`'s `(Default)` and the clearing of `TarDD`). A Let-only property's run is under the
  Let (E's `WriteOnly`).

`TarDD` and `TarD`, as VB6 saved them:

```vb
Public Property Let RateDD(ByVal Value As Double)
    mRateDD = Value
End Property

Public Property Get RateDD() As Double
Attribute RateDD.VB_Description = "Tariff replica, both at once."
Attribute RateDD.VB_UserMemId = 0
    RateDD = mRateDD
End Property
```

```vb
Public Property Let RateD(ByVal Value As Double)
Attribute RateD.VB_Description = "Cleared, then description only."
    mRateD = Value
End Property
```

`TarDD`'s saved shape, and `Tariff`'s, is p14d's, whose compiled type library returned no help string by memid. So
the shape VB6's own dialog writes when Description and `(Default)` are set together on a fresh Let-then-Get
property is one whose description a by-memid lookup did not find in a compiled DLL. Neither `TarDD` nor `Tariff`
was itself compiled.

`TarVD`'s route, a description first and `(Default)` added in a later visit, leaves `VB_Description` and
`VB_UserMemId = 0` under the Let of a Let-then-Get property: p14a's shape, which contains `k4`'s, written by VB6's
own dialog.

**`WithEvents`.** VB6 adds `Attribute <name>.VB_VarHelpID = -1` under every `WithEvents` declaration it saves,
whether typed into the code window (A's `Tariff` and `Main.frm`) or loaded without one (B's `BareWithEvents`,
`Public` and `Private`), in a class and in a form.

**The dialog itself.** Its Name list holds the module's public members, sorted, a property once, events and public
variables included. Private variables are not listed, and nor are constants (V5). On a comma list it opens on the
line's first declarator wherever the caret is (V2). For a property it opens on the property from either accessor,
except that with a second public member in the class it fell back to the first entry of the Name list in two of
six cases (V6: `GetSetB`, Get then Set, caret in the Set; `LetGetA`, Let then Get, caret in the Let), and a
description typed straight in went to that other member. In a class, User Interface Default, the property page and
the category are disabled for a `Function` and enabled for a variable or a property. On a UserControl property,
*Don't show in Property Browser* greyed out once Hide was ticked.

**What the code window shows.** No `Attribute` line that came from a file, attached or not, and nothing that marks
a member as described (D, and every code view in B and V5, misplaced lines included). None of the four
load-and-save projects (B, B2 and V5's two) raised a message or wrote a `.log` at load; for the other loads (A, C,
E and V4's reload) neither was recorded.

**What a save drops, moves and rewrites from a hand-written file** (B, B2 and V5; each shape its own module,
dirtied by the marker and saved with File > Save Project):

| Shape (module) | Under `Public Function Total() As Long` unless stated | After VB6's save |
|---|---|---|
| `BlankGap` | a blank line, then `Attribute Total.VB_Description = ...` | **dropped**; the blank line stays |
| `CommentGap` | a comment line, then the attribute line | **dropped**; the comment stays |
| `InBody` | after `Total = 1` | **dropped** |
| `SplitRun` | `Total.VB_Description` / a blank line / `Total.VB_UserMemId = 0` | the first kept; the half after the blank **dropped** |
| `DoubleDesc` | `"first"`, then `"second"` | `"first"` kept, `"second"` **dropped** |
| `NamesNothing` | `Attribute Gone.VB_Description` | kept, **re-qualified** `Attribute Total.` |
| `NamesOther` | `Attribute Other.VB_Description`, `Other` declared below | kept on `Total`, re-qualified; `Other` gets nothing |
| `LowerCase` | `Attribute total.VB_Description` | re-cased `Attribute Total.` |
| `DeclShapes` | a line indented four spaces; `Attribute   Spaced.VB_Description   =   "Extra spaces."` | written at column 1, with single spaces |
| `DeclShapes` | under `Private Declare Function GetTickCount Lib "kernel32" () As Long`, a `Private Sub`, a `Friend Function`, a `Public Static Function`, and a declaration with a trailing comment | all kept, in place |
| `Dotless` | `Attribute VB_Description = "Dotless below code."` between two procedures | **moved into the header**, after `VB_Exposed`, before `Option Explicit` |
| `VarAway` | `A`'s `VB_VarDescription` under a blank line; `B`'s under `Public Sub Foo()` | both **moved**, each directly under its declaration |
| `LetFirst` | the Let first, carrying the run; the Get after, bare | unchanged, under the Let |
| `BothRuns`, `BothRunsGetFirst` | each accessor with its own `VB_Description` | both kept, each under its own accessor |
| `Mod.bas` | a variable's and a procedure's run in a standard module | both kept |
| `ConstV`, `ConstTyped`, `ConstPriv`, `ConstClsPriv` | a `VB_VarDescription` under a `Const`: public, typed `As Long`, private in a `.bas`, private in a class | kept |
| `ConstMulti` | `Public Const C1 = 1, C2 = 2` / `Attribute C2.VB_VarDescription` | kept |
| `ConstAway` | `Far.VB_VarDescription` under a later `Public Sub UseFar()` | **moved** under `Public Const Far = 1` |
| `ConstD` | a `VB_Description` under `Public Const Cap = 5` | **dropped** |
| `ConstClsPub`, `DeclShapes` | a `VB_VarDescription` under a `Public Const` in a **class** (`Outer = 5`, untyped; `K As Long = 1`, typed), a declaration VB6 rejects there (`ConstClsPub`'s line showed red) | **dropped** |
| `EnumD`, `EnumV`, `TypeD`, `TypeV` | a `VB_Description` or a `VB_VarDescription` under an `Enum` or `Type` line, in a `.bas` | **dropped** |
| `EnumCls` | a `VB_Description` under an `Enum` line in a class | **dropped** |
| `EnumMem` | `Small.VB_VarDescription` under the enum member `Small = 1` | **dropped** |
| `VarModPriv`, `VarClsPriv` | a `VB_VarDescription` under a private variable, in a `.bas` and a class | kept |

`VarAway` as written:

```vb
Public A As Long

Attribute A.VB_VarDescription = "Blank line above."
Public B As Long

Public Sub Foo()
Attribute B.VB_VarDescription = "Under a Sub header."
End Sub
```

and as VB6 saved it:

```vb
Public A As Long
Attribute A.VB_VarDescription = "Blank line above."

Public B As Long
Attribute B.VB_VarDescription = "Under a Sub header."

Public Sub Foo()
End Sub
```

Where a shape was run in both halves, the IDE's verdict is the compiler's. What `/make` attaches, the IDE keeps and
writes under the declaration (`NamesNothing` and p04, `NamesOther` and p05a, `LowerCase` and p12b, `DoubleDesc` and
p05g); what `/make` attaches to nothing, the IDE drops at the next save of that module, without a word (p01, p02 and
p03 as `BlankGap`, `CommentGap` and `InBody`; and `SplitRun`'s second half). Binding by name shows up as moving
(`VarAway`, p09g and p09h), and a module attribute found below code goes back to where module attributes live
(`Dotless`, p06b). p05e's and p05f's shapes, a procedure attribute under `End Sub` or in the declarations section,
had no IDE counterpart.

**A constant's description is real, not preserved text.** In `ConstV` the Object Browser (`F2`, `Limit`) shows
`Public Const Limit = 5` / `Member of Load2.ConstV` / `Const, VB_VarDescription.`, and nothing for `ConstD`'s
`Cap`. Its context menu's *Properties...* is greyed for a constant, and the Procedure Attributes dialog does not
list one: VB6 keeps and shows a constant's description, and offers no way to make one.

**A rename carries the description.** For a procedure, in the first session (each class `CalcC<n>` holding
`Public Function Total() As Long` described `"Adds things up."`, each saved with `Ctrl+S`):

| Probe | Edit | After the save |
|---|---|---|
| C2 | Find `Total()`, which selects it; type `GrandTotal()` | `Attribute GrandTotal.VB_Description = "Adds things up."` under the renamed declaration |
| C3 | `Ctrl+H`, `Total` to `GrandTotal`, Current Module, Replace All | VB6 reported **2** replacements, the declaration and the body's `Total = mSum`: the hidden line is neither searched nor counted. Carried, re-qualified |
| C8 | the whole declaration selected (`Home`, `Shift+End`) and retyped `Public Function Total() As Long` | carried, re-qualified back to `Total.` |

For a variable, in the second:

| Probe | Edit | After the save |
|---|---|---|
| V1 | `Public Count As Long`, described; `Count` retyped `Tally` | `Attribute Tally.VB_VarDescription = "Var1 Count, described."`; the Name list then holds `Tally` and no `Count` |
| V2 (`Multi2`) | `Public A As Long, B As Long`, `B` described; `B` retyped `C` | **dropped** |
| V2 (`MultiP`) | `Public P As Long, Q As Long`, `P` described; `P` retyped | carried, re-qualified |
| V2 (`MultiXY`) | `X` and `Y` described; `Y` retyped | `X`'s kept, `Y`'s **dropped** |
| V2 (`MultiMN`) | `N` described; `M` retyped, `N` untouched | `N`'s **dropped** |

So when a line declaring several variables is edited, VB6 carries the first declarator's attributes to whatever the
first declarator now is, and drops every other declarator's, renamed or not. A one-name line is the degenerate
case. All four measurements were renames. (`MultiP`'s new name came out `r` and `MultiXY`'s `z`, lower case,
because SendKeys dropped the Shift; each is still a rename.) V1's carrying is of a rename VB6 saw being made: the
same declaration over the same description, loaded from a file with `Count` declared nowhere, is given to no member
and dropped at the module's next write (the third session's Q2, *Which saves write a module*, below).

**A retyped type keeps the run** (V8). `Public Size As Long`, described `Retype8 Size, a Long.`, with `Long` retyped
`String`: VB6 saved `Attribute Size.VB_VarDescription = "Retype8 Size, a Long."` under `Public Size As String`.
Not a rename, so nothing was re-qualified.

**A change of kind keeps the run** (V3). `Public Function Total() As Long`, described, retyped over a selection as
`Public Sub Total()`: VB6 rewrote `End Function` to `End Sub` by itself when the caret left the line, and saved
`Attribute Total.VB_Description = "Total was a Function."` under the Sub. The reverse, `Public Sub Runner()` retyped
`Public Function Runner() As Long`, the same.

**Enter at the end of a described declaration** puts the new line after the run (C1): declaration, `Attribute`
line, the new statement, the old body.

**Deleting the declaration loses the description for good.**

| Probe | Edit | Description after the save |
|---|---|---|
| C4a | the three-line procedure selected and deleted | gone with it |
| C4b | then the procedure typed back | does not return |
| C7 | deleted and typed back, with **no save between** | does not return |
| C9a | the declaration line alone deleted, the body left in the declarations section | gone |
| C9b | then the line typed back | does not return |
| V7 | the declaration line deleted and typed back identically, no save between, the text then exactly the original | does not return; the saved file is byte for byte the input before it was described, 505 bytes |
| C6 | the procedure deleted, then `Ctrl+Z`, the text back identical | **lost**: Undo restores the text, not the description |
| C5 | the procedure cut and pasted at the end of the module | **lost**: the clipboard held exactly the three visible lines, with `Text` its only format |

In VB6 a member's attributes live with the member, not with any text. They follow a rename of a procedure, of a
one-name variable line, or of a comma list's first declarator, by every route tried; they survive edits inside the
procedure, a retyped type and a change of kind; and they die with the declaration. On a comma list, every
declarator's but the first's die with any rename on the line (V2). No edit in the code window brings them back
before a reload. Pasted attribute text becomes a description only through a load (V4, below).

**Attribute text pasted into the code window stays code until a reload** (V4). The clipboard held

```
Public Function Pasted() As Long
Attribute Pasted.VB_Description = "From the clipboard."
    Pasted = 1
End Function
```

and was pasted at the end of `Paste4.cls`, and of `PasteMod4.bas`. In both, the `Attribute` line showed **in
red**, VB6's syntax-error colour, with no message box, and `Ctrl+S` wrote the four lines verbatim, the line still red
after the save. In `Paste4` the status bar, with the caret below the pasted lines, read `Ln 11`, so it counted the
line, and the dialog on `Pasted` showed no description. On disk that is byte for byte a real run under its
declaration. After VB6 was closed and the project reopened, the line was hidden in both, and the dialog on
`PasteMod4.Pasted` showed `From the clipboard.`; `Paste4` by then also carried a description set through the
dialog, as follows. With the red line present in `Paste4`, that description was written **above** it:

```vb
Public Function Pasted() As Long
Attribute Pasted.VB_Description = "Set in the dialog."
Attribute Pasted.VB_Description = "From the clipboard."
    Pasted = 1
End Function
```

After a reload the dialog showed `Set in the dialog.`, and the next save dropped the pasted line: `DoubleDesc`'s
rule, the first stands. This refines the first session's "it never preserves an attribute line as text": VB6 never
keeps one it **loaded** as text, but one put into the code window is text, written back as it stands, and becomes an
attribute only by passing through a load.

### Which saves write a module (the third IDE session, 2026-10-14)

**The question.** Both sessions above dirtied every module before saving it, on the premise that VB6 writes no
module it holds as unchanged, so nothing above shows what VB6 does with a module nobody touched: whether Save Project
writes it, and what an explicit Save or Save As of it writes. HexIDE's save re-qualifies a stale run only where VB6
would write the module, so it needed both answers.

**The project**, `S.vbp`, a Standard EXE with `Startup="Sub Main"` and no `Main`, which VB6 loaded with no prompt. Five
members, CRLF, ASCII, no BOM:

- `Touched.bas`: `Attribute VB_Name = "Touched"`, `Option Explicit`, `' p4 marker`, a blank line, and an empty
  `Public Sub Poke()`.
- `Untouched.bas`, the evidence's shapes put together, byte for byte:

  ```vb
  Attribute VB_Name = "Untouched"
  Option Explicit
  ' p4 marker

  Public Tally As Long
  Attribute Count.VB_VarDescription = "Var1 Count, described."

  Public Function Total() As Long
  Attribute Gone.VB_Description = "Names nothing."
      Total = 1
  End Function

  Public Sub Indented()
      Attribute Indented.VB_Description = "Indented."
  End Sub

  Public Sub Spaced()
  Attribute   Spaced.VB_Description   =   "Extra spaces."
  End Sub
  ```

  `Count` is declared nowhere. The two lines under `Tally` are V1's `Var1` with only its declaration retyped, which
  is the text a code window that shows attribute lines, as HexIDE's does, holds after V1's edit.
- `UntouchedC.cls`: the same body under the class header, `VB_Name = "UntouchedC"`.
- `NamesNothing.cls` and `DeclShapes.cls`: B's and B2's inputs, byte for byte, so each has VB6's dirtied output
  beside it to compare with.

| Probe | Step | Written |
|---|---|---|
| Q1 | `Touched` edited (a comment typed at its end), then File > Save Project | `Touched.bas`, 91 bytes to 104; `S.vbp`, 16 ms later, with a new timestamp and **identical bytes**. Nothing else: `Untouched.bas`, `UntouchedC.cls`, `NamesNothing.cls` and `DeclShapes.cls` kept their bytes **and their timestamps**. No `.vbw`. |
| Q2 | `Untouched` selected; the File menu read **`Save Untouched.bas    Ctrl+S`**, enabled; Alt+F, **S** | `Untouched.bas`, 439 bytes to 368 |
| Q2, again | VB6 relaunched on the same `S.vbp`, nothing saved first; `DeclShapes` and `UntouchedC` each selected and saved the same way | `DeclShapes.cls`, 1216 to 1156; `UntouchedC.cls`, 782 to 711 |
| Q3 | `UntouchedC` selected; File > **Save UntouchedC.cls As...**, the dialog titled `Save File As`, `UntouchedCAs.cls`; then the same for `Untouched` and for `NamesNothing` | each new file; the file each was read from untouched, bytes and timestamp |

`Untouched.bas` after Q2, against what it was:

```
6d5
< Attribute Count.VB_VarDescription = "Var1 Count, described."
9c8
< Attribute Gone.VB_Description = "Names nothing."
---
> Attribute Total.VB_Description = "Names nothing."
14c13
<     Attribute Indented.VB_Description = "Indented."
---
> Attribute Indented.VB_Description = "Indented."
18c17
< Attribute   Spaced.VB_Description   =   "Extra spaces."
---
> Attribute Spaced.VB_Description = "Extra spaces."
```

`UntouchedC.cls`'s diff is the same at the class's line numbers, and its plain Save and its Save As wrote one file:
`UntouchedC.cls` and `UntouchedCAs.cls`, both 711 bytes, both SHA-256 `E9BCDC45...`, `VB_Name` still `"UntouchedC"`.
`DeclShapes.cls` lost `Attribute K.VB_VarDescription = "Under a Const."`, the run under its `Public Const` in a class,
and had its indented and spaced lines written afresh; against B2's dirtied output it differs only in the marker
comment B2's edit changed. `NamesNothingAs.cls` (523 bytes, SHA-256 `1F48BA4F...`) is the input with `Gone`
re-qualified `Total`, and nothing else, and against B's dirtied output differs only in that marker. `UntouchedAs.bas`
is byte for byte `Untouched.bas` after Q2, which is confounded: Q2 had already rewritten the file it was compared
against, so it shows only that a Save As did not reach back to the bytes VB6 loaded. Q3's answer rests on the two
classes, whose files still held their input when they were saved as.

So:

- **Save Project writes only a module VB6 holds as changed**, and the project file, which it writes even when nothing
  in it changed. The premise both earlier sessions dirtied every module on holds.
- **A module's own Save writes it whatever its state.** The item is enabled for a module nobody changed, and the file
  is written.
- **Every write of a module writes its runs afresh**, as the earlier sessions saw at a Save Project of a dirtied one:
  re-qualified, at column 1, with single spaces, the orphans dropped. The runs an untouched module's Save writes are
  the runs a dirtied module's save writes (`DeclShapes` by its Save, `NamesNothing` by its Save As), and its Save As
  writes the same bytes as its Save (`UntouchedC`). VB6's only byte-preserving behaviour is not writing the module at all.
- **A `VB_Var` line naming a name declared nowhere, loaded under a line declaring one variable, is given to no
  member**, and the next write of the module drops it. V1's retype of `Count` to `Tally` in the code window left the
  same declaration over the same description, and VB6 carried that description to `Tally`. So VB6 carries a
  variable's description across a rename it saw being made, not across text that looks like one, and the file alone
  cannot tell which a line is.
- **A Save As re-points the member and leaves the project to save.** After the three Save As steps the Project
  Explorer named `Untouched (UntouchedAs.b...`, and File > Exit's prompt, *Save changes to the following files?*,
  listed `S.vbp` alone. Answered No, `S.vbp` kept naming the original files, and `S.vbw` (144 bytes) was written at
  exit even so. The second launch, with nothing left unsaved, exited with no prompt and wrote `S.vbw` again.

### What this overturned

**Ours, in the first draft of `design.md`'s "Settled before building phase 4"**, written on 2026-10-13
before any of this was measured, and in the notes added to it while task 4.1 was built:

- *"The run describes a member when that logical line lexically declares the qualifier of every line in the
  run."* For a procedure VB6 does not read the qualifier at all (p04, p05a, p05b; `NamesNothing`, `NamesOther` and
  `LowerCase` kept and re-qualified). For a variable it reads only the qualifier, and position after the
  declaration does not matter (p09e, p09g, p09h, p09j; `VarAway` moved); before it, p09l, the line reaches nothing.
  The rule erred towards an orphan in both cases, the opposite of the aim it stated, that "it errs towards
  *describes*".
- *"A dotless `Attribute` line below code describes nothing either."* It is a module attribute (p06a to p06c), and
  the IDE moves it into the header (`Dotless`).
- *"A rename the developer makes by retyping the declaration gets no fix-up: it leaves an orphan."* VB6 carries
  the description across a retyped name, Replace All and a retyped declaration, and re-qualifies it (C2, C3, C8),
  for a one-name variable line too (V1), and a variable's retyped type keeps it (V8). On a comma list only the first
  declarator's is carried (V2).
- *"Only the first statement on the described line is read ... VB6 writes no such line under an attribute run that
  anyone has seen."* VB6's own dialog writes one, for the declaration after the colon (E).
- *"`Const`, `Enum` and `Type` declare nothing a run can describe."* Right for `Enum` and `Type`; wrong for `Const`
  (V5).

**Answered rather than overturned**: the first draft's open **(oracle)** question, whether a run separated from its
member by a blank line or a comment is honoured, in which case the described line would widen past the gap. It is
not: VB6 attaches nothing across either (p01, p02) and drops the line at its next save (`BlankGap`, `CommentGap`).
The draft's rule, that such a run is an orphan, stands. Its "nothing the IDE does removes or moves it" stands too,
as a recorded divergence from VB6, which removes or moves one at every save.

**Ours, in the code before #968.** `Vb6ToolchainService` counted physical lines starting `Attribute`, not logical ones
(fixed as described above). Task 4.1 was first built judging whether a run describes its member by matching every
qualifier against the line above, the first draft's rule, and was rebuilt to the revised rules before the rest of phase 4 (*HexIDE, after
#273*, below).

**The first IDE session's, by the second.**

- *Const dropped* (B2's `DeclShapes`). The only constant that session measured was a `Public Const` in a class,
  a declaration VB6 rejects in an object module (`Public Const K As Long = 1`). The second session's
  `ConstClsPub`, the same kind of declaration but untyped (`Public Const Outer = 5`), showed red and was dropped the
  same way; the colour of `K`'s line was not recorded. A legal constant keeps its `VB_VarDescription`, by name
  (V5).
- *"A run created by the dialog for a property goes under the `Property Get`, even when the `Let` comes first."*
  Drawn from one property, `Tariff`, described with `(Default)` in the same visit. Eighteen description-only cases
  put the run under the first accessor (V6), and `TarDD` reproduces `Tariff`'s result only with `(Default)` set
  alongside.
- *"[k4's shape runs and compiles,] but VB6's own dialog never writes it."* `TarVD` is VB6's dialog writing it,
  with a description added: a description, then `(Default)` in a later visit, both under the Let of a Let-then-Get
  property (V6). That is p14a's shape, which contains `k4`'s.
- *The dialog opens on "the same entry whichever accessor holds the caret".* It was seen in `LetFirst` and
  `Tariff`, where the property is the only public member, so a fall-back to the first entry of the Name list looks
  identical. With a second public member it fell back twice in six (V6, `GetSetB` and `LetGetA`).

**Ours, by the third session.**

- *HexIDE's gate, "only a document with unsaved changes"* (task 4.4), stood in for "a module VB6 writes" on the
  premise above. Q1 bore the premise out for Save Project, and Q2 and Q3 showed what it never covered: VB6's own
  Save and Save As of an untouched module write it, and write its runs afresh, `Gone` re-qualified `Total` as at
  any save. HexIDE wrote such a module byte for byte at both, and now re-qualifies it (*HexIDE, after #273*).
- *The open question in `design.md` kept that gate for a Save As until it was measured*, on the ground that the
  copy "writes nothing VB6's own next load reads differently". True of the file, and not what VB6 writes: the option
  it set aside, that a write somewhere else should re-qualify, "which is what VB6 presumably does", is what VB6 does.
- *Reading V1 as "a stale one-name `VB_Var` run is carried to the variable"*, which the renamed kind of run in
  `design.md` rests on. V1 measured a rename made in VB6's code window. The same text loaded from a file is given to
  no member and dropped (Q2). The reader cannot see the difference, so it is kept as a divergence (*HexIDE, after
  #273*).

**The 2026-09-19 entry** is refined rather than overturned (*What `Line N` counts, refined*, above).

### What resisted explanation

- **Why the two kinds bind so differently**: a procedure attribute by position, its name ignored; a variable
  attribute by name, its position after the declaration ignored. Measured, and no rule beyond the measurement is
  offered. A load-time lookup that resolves a `VB_Var` name against the declarations read so far would explain
  p09l, but nothing here shows one. And p09l does not isolate "before its declaration": its line was also directly under `Option
  Explicit`, the first line of the declarations section, where p05f's line sat. A `VB_Var` line placed after
  another declaration but before its own was not tried.
- **`VB_VarHelpID` on a `WithEvents` variable** is accepted, hidden, added by the IDE under every such declaration,
  and has no visible effect: -1 with it, without it, and with 7 in its place (p10a, p10b, p10f). Why VB6 writes a
  line that restates what it would do anyway is not known.
- **Two `VB_Description` lines in one run keep the first** (p05g, `DoubleDesc`, and the dialog over the pasted
  line in V4). These probes cannot tell "the first wins" from "a later line for an attribute already set is
  discarded".
- **Variables are written in exactly the reverse order of procedures.** Measured once per kind, with all five
  attribute kinds present (E's `Full` and `Spare`), consistent with A's partial runs, and independent of the order
  the options were ticked. No rule beyond "reversed" is offered.
- **The 254-character cap** is one measurement, with ASCII text, through the dialog.
- ***Don't show in Property Browser*** greyed out once Hide was ticked on a UserControl property, and was enabled
  before. Not investigated.
- **`(Default)` moves a fresh property run to the Get.** Seen twice (`Tariff`, `TarDD`), against a control without
  it (`TarD`). The caret was in the Let each time, and for `Tariff` that is known from the screenshot: the code
  window's procedure box read `Rate [PropertyLet]` when the dialog was opened. Seen once the other way (`TarVD`:
  added to a run already under the Let, it leaves the run there). Nothing beyond the table is offered as a rule.
- **The dialog's initial member.** From the caret in a property accessor it found the property in four of six cases
  with a second public member present, and fell back to the first entry of the Name list in two: Get then Set with
  the caret in the Set, and Let then Get with the caret in the Let. "The caret must be in the Get, when there is
  one" fits all six. It is a fit, not a mechanism. A property without a Get was tried only with the caret in its
  second accessor, and the caret was in a first accessor only once (`LetGetA`). The caret in the first accessor of a
  property without a Get, or in a leading Get, with a second public member present, was not tried.
- **A comma list keeps only its first declarator's attributes across an edit.** Four measurements, all consistent,
  all renames.
- **A constant's description is kept and shown, but cannot be made.** Why the IDE keeps an attribute it offers no
  way to create is not visible from here.
- **p07's line.** `Expected: identifier` named the attribute line inside the continued declaration, where f3's
  `Syntax error` named the statement's first line. One measurement of each message.

### Not measured

- A `VB_Var` qualifier in a case other than its declaration's, for the compiler; and in the IDE, whether a
  variable's qualifier is re-cased as `LowerCase` showed a procedure's is.
- A description holding a line break (the Description box takes several lines) or non-ASCII characters.
- A long `VB_Description` loaded from a file rather than typed into the dialog; and a continued attribute line
  (p11a, p11b) loaded and saved, so whether the IDE rejoins it.
- p07's and p07c's shapes in the code window. Whether the IDE shows p07's attribute line as red code and p07c's as a
  two-line comment was not seen.
- A `VB_Var` line naming nothing, or placed before its declaration (p09f, p09l), loaded and saved; and, for the
  compiler, a `VB_Var` line after another declaration but before its own, which p09l's place under `Option Explicit`
  does not separate from "the first line of the declarations section".
- p05e's and p05f's shapes loaded and saved: a procedure attribute under `End Sub`, or in the declarations section
  naming a later procedure.
- A hand-written run under a `Property Set`, loaded and saved; a property with all three accessors.
- Whether the second accessor's own description reaches the compiled type library (p14b, p14f), which a by-memid
  read cannot show.
- `Tariff` or `TarDD` as VB6 saved it, compiled in an ActiveX DLL, and the description a referencing project's
  Object Browser shows for it.
- A private procedure in the dialog's Name list, since none was present to be offered.
- The Object Browser's *Properties...* as a route to a procedure's or a variable's description; Edit > Undo straight
  after a Procedure Attributes change; dragging a procedure's text.
- A retype that turns a variable into a procedure or back.
- `(Default)` alone, Hide alone or a Help Context ID alone on a fresh Let-then-Get property; and the dialog opened
  from the Get with `(Default)`.
- A comma-list line edited without a rename (a type changed, a declarator added), in a `.bas`, or with `Dim` or
  `Private`; a `:`-separated line edited.
- An `Attribute` line typed key by key, where Auto Syntax Check may object on leaving the line; one pasted directly
  under an existing declaration or inside a body; a pasted `VB_VarDescription` line.
- Any run under a `Type` in a class; a `VB_VarDescription` under an `Enum` in a class; a `VB_Description` under an
  enum member; a typed `Private Const` in a class; both constants of one `Const` line described.
- Whether a constant's `VB_VarDescription` reaches a compiled type library. Probably it cannot be measured: a
  `Public Const` is rejected in an object module, a private member is not in the type library, and a standard
  module has none.
- E's UserControl loaded and saved again, to see that the reversed variable order is stable.
- `Ctrl+S` with the focus in an untouched module's **code window**, rather than with the module selected in the
  Project Explorer. The menu item shows the same accelerator, so the outcome is presumably Q2's; that is inferred,
  not seen.
- Whether Save Project writes an untouched module that **is open** in a code window but not edited. In Q1 only
  `Touched`'s window was open.
- A `.frm` member, untouched, through Save Project, its own Save, or Save As; and a `.ctl`.
- A Save As of an untouched module whose file had not already been rewritten, in a `.bas`: `Untouched.bas`'s was
  confounded by Q2's save before it, so Q3 rests on two classes.
- ActiveX DLL, OCX and group projects, and `VB_Exposed = True` classes, in the IDE: every IDE probe was a Standard
  EXE, its UserControls private to it.

### Rig shortcomings

Recorded so the next person driving VB6's IDE through the agent need not find them again.

1. **SendKeys drops modifiers.** `^f` sent just after a code window opened arrived as `f`, and `^s` after a
   `{DELETE}` as `s`, each typing a letter into the source (`fOption Explicit`). In the second session `t R` and
   `t Z` arrived as `r` and `z`, in two of four single-letter retypes; longer strings came through intact. The
   agent's own `keys:` action has the same exposure, and its screenshot is taken after the damage. The fix is a
   chord step that presses the modifier with `keybd_event`, holds it, sends the key and releases last: none of the
   79 chords sent that way in the first session lost its modifier. Use it for Shift too, and never trust a `t` step
   for case.
2. **The agent's log collides with the host reading it.** With the host polling `log.txt`, even through a
   `FileShare.ReadWrite` open, the agent's `Add-Content` threw "being used by another process" three times in the
   first session and in several batches of the second, each time losing the line it was writing (a `steps
   complete` marker, an `expect` confirmation, a screenshot's record; the screenshots themselves were taken).
   Reading `heartbeat.txt` from the host once produced an `AGENT LOOP ERROR`. The agent should hold one
   `StreamWriter` opened with `FileShare.ReadWrite`, or retry its write. Until it does, trust the screenshots and
   the files over the log's completeness.
3. **"The VB6 process" is ambiguous, and so is "the VB6 window".** The agent's focus, launch and `keys:` helpers
   take `Get-Process VB6 | Select-Object -First 1`, which picks a concurrent headless `/make` as readily as the
   IDE. Launch VB6 from `exec:` and record its PID; check before every keystroke that the foreground window belongs
   to it, and before every batch that no `VB6.EXE ... /make` is running. Even that cannot see which window inside
   VB6 has the keys: once, type-ahead and `F7` did not open the intended code window, and the keys that followed
   went into a modeless Find dialog. The second session's `expect` step aborts a batch unless the foreground title
   contains the given text, and with the code window maximised the main title ends `- [Var1 (Code)]`, which names
   the MDI child. None of its 61 batches sent a key to the wrong window.
4. **A fresh launch opens the first code window un-maximised**, and the main title then does not name the child
   (`Probe2 - Microsoft Visual Basic [design]`), so `expect` fails. Click the window's maximise box once per
   launch; the `.vbw` VB6 writes at exit reopens every window un-maximised next time.
5. **Procedure Attributes re-opens collapsed.** *Advanced >>* does not persist between openings, so clicks meant
   for Procedure ID landed on an empty dialog and did nothing, silently (`TarDD`'s and `TarVD`'s first passes);
   only the saved bytes showed it. Click *Advanced >>* in every batch that needs it, and take a screenshot after.
6. **Menu mnemonics from memory were wrong.** Procedure Attributes is Alt+T, **A**, and the first attempt left the
   Tools menu open. Read every menu from a screenshot first.
7. **The Name list is sorted**, so a module's other public members change how many `{DOWN}` reach a member (an
   `Other...` Sub sorts before `P...`).
8. **A screenshot's clock is not evidence.** The first one's taskbar read 03:51 against a log time of 16:25, a
   stale paint.
9. **`Add-Type` in `exec:` lasts as long as the agent**, so a changed helper type silently keeps its first
   definition; renaming it (`P4Win` to `P4Win2`) is what loaded the chord support.
10. **The clipboard** was set from the agent's own thread, an STA, then read back and compared before the paste
    (`equal=True` logged), so a paste probe tests VB6 rather than the clipboard.
11. **In the compiler half**, the registered `TLBINF32.DLL` crashed 32-bit PowerShell with an access violation, so
    the type libraries were read through `oleaut32` alone. `/make` registers every ActiveX DLL it builds, so the 51
    were unregistered afterwards with `regsvr32 /u /s`: 51 registered before, none after.
12. **The title check cannot see which Project Explorer item is selected** (the third session). With a code window
    maximised the main title names that window, `... - [Touched (Code)]`, whichever member the Project Explorer has
    selected, so an `expect` step passes for a Save that will write another member. Confirm the target from the
    File menu's item text instead, `Save Untouched.bas` or `Save UntouchedC.cls As...`, read from a screenshot
    taken with the menu open, before the item's letter is sent; the `Properties - <name>` caption agrees, and the
    written file's hash and `VB_Name` confirm it afterwards. The same session put each Save As file name in through
    the clipboard and `Ctrl+V`, which shortcoming 1 cannot touch, and the case on disk matched every time.

### HexIDE, after #273

The rules HexIDE takes from these measurements are in the change's `design.md`, under "Settled before building
phase 4". Task 4.1 was first built to the first draft while the measurements were being taken, committed after
the compiler half and before the IDE half had finished, and judged a run by a qualifier match. It was rebuilt to
the revised rules on 2026-10-14: a procedure run's qualifiers are not consulted, every `:` statement on the line is
read, a constant takes a `VB_Var` run, and a one-name line takes the run a rename by retyping leaves when the name
it carries is declared nowhere in the module. The save's re-qualification of a procedure's run and a one-name
variable's, as VB6 does, is settled in `design.md` and was built in task 4.4, first for a module with unsaved
changes only, on the premise the first two sessions dirtied every module on. The third session measured the premise
(*Which saves write a module*) and HexIDE now re-qualifies wherever VB6 writes the module: a document with unsaved
changes, at any save; a document written by its own Save or Save As, whatever its state; and a document written to
a file other than its own, which a write of the project to another directory does and VB6 does only by a Save As.
A project's or the group's save of an unchanged member at its own file writes the bytes it read, a stale qualifier
included. HexIDE's Save Project writes every member, so that is as near as it comes to VB6 leaving the file
unwritten, and the timestamp is the only difference. Make's copies re-qualify nothing. The save rewrites a run's
qualifiers and nothing else of it, where every write of VB6's writes the run afresh: an indented or oddly spaced
line stays as written where VB6 writes it at column 1 with single spaces (B2's `DeclShapes`, and the third
session's `Untouched` saved untouched), and no `VB_VarHelpID = -1` is added under a `WithEvents` declaration loaded
without one (`BareWithEvents`), both of which VB6 reads the same way at its next load. The files VB6 wrote are the
corpus of task 4.6, `corpus/member-attributes/round-trip` and the saved output in `evidence/`, which every corpus
lane walks; the hand-written load shapes VB6 was given are beside that output, and a test reads each against it.
The third session's files are not added to the corpus; a test pins its two clean results instead: VB6's
`NamesNothingAs.cls`, which HexIDE's Save As of the same input writes byte for byte, and VB6's `UntouchedC.cls`,
which HexIDE's Save of the same module does not write, for the two reasons given here. Where HexIDE
departs from VB6 it does so on purpose: an orphan stays visible and editable, where VB6 hides it and drops or moves
it at its next write of the module; Undo restores a run and a cut carries one, where VB6 loses the description to
both; a comma list keeps every declarator's run that still names a declarator on the line, where VB6 keeps only
the first's; and an `Attribute` line pasted directly under a declaration is read as a run at once, which is what
VB6's next load makes of the same bytes. A run mixing the two kinds is left as ordinary text, where VB6 gives each
of its lines to its own member (p09n): the text stands, so VB6 binds it the same way at its next load. Four go the
other way, keeping less than VB6 keeps or protecting what VB6 drops: a comma list's renamed first declarator leaves
its run naming the old name, an orphan, where VB6 carries it and re-qualifies it; a run under a `Public Const` in a
class, form or UserControl is read as describing and protected, where VB6 drops it (V5, and the third session's
`DeclShapes` saved untouched); a run giving one attribute twice describes its member whole, where VB6's save keeps
the first line and drops the second (p05g, B's `DoubleDesc`); and a one-name `VB_Var` line read from a file, naming
a name the module declares nowhere, is protected as the line's variable's and re-qualified to it at the next save
that writes the module, where VB6's load gives it to no member and its next write drops it (the third session's
`Count` under `Tally`). Only a rename made in VB6's own code window carries such a line (V1), and HexIDE's reader
cannot tell the two apart from the text, which is all it reads. Unlike an orphan, `DoubleDesc`'s second line is
protected and folded with the run, so it cannot be seen or removed by hand until the Procedure Attributes dialog
exists (#62), and a save keeps it, as settled in `design.md`; VB6 reads the first at its next load either way.

