## What a `.vbp` tolerates, and the one key for a non-code file (2026-09-04)

54 compiles against real `vb6.exe`, asking what a project file may contain beyond the keys HexIDE writes.
The question came from wanting a VB6 project to be able to carry a Markdown, HTML or CSS file so the IDE
can offer language services for it.

### `RelatedDoc=` — VB6 already has a blessed key for exactly this

This is the finding. VB6 has a first-class **Related Documents** concept, and `VB6.EXE` carries both
`RelatedDoc` and `Related Documents` as string literals alongside `ExeName32` — and the IDE exposes it
directly as an **"Add As Related Document"** checkbox on the Add File dialog (see below).

| written | VB6 |
|---|---|
| `RelatedDoc=notes.md` | **legal** |
| three `RelatedDoc=` lines — `.md`, `.html`, `.css` | **legal** — extension-agnostic and repeatable |
| `RelatedDoc=docs\README.md` | **legal** — subfolders fine |
| `RelatedDoc=my notes.md` | **legal** — spaces fine, unquoted |
| `RelatedDoc="notes.md"` | **illegal** — `Path/File access error: '…\notes.md"'`; the quote is taken literally |
| `RelatedDoc=nosuch.md` | **illegal** — `File not found`. The compiler opens it at build time |
| `RelatedDoc=docs` (a directory) | **illegal** — `Path/File access error` |
| `RelatedDoc=` (empty) | **illegal** — `The project file '…' is corrupt, and can't be loaded.` |

The value is a **bare relative path**. It is a real build-time dependency, not an inert annotation.

### Everything else in the item block is rejected

An unknown key before the first section header is refused outright — `The project file '…' contains
invalid key 'Markdown'.` — and refused *before* any path lookup, so a missing target reports the same
error as a present one. `Document=`, an invented key with a VB6-ish shape, is rejected identically.

### The real parsing rule, and what it overturned

Two custom `[Section]`s at the end are legal while a custom section *before* the standard keys is not,
which reads like "sections at the end are fine". It is not the rule, and the cases that establish it also
differed in blank-line placement — a confound that nearly recorded the wrong one.

> **Key parsing ends at the first line beginning with `[`. Everything from there to EOF is ignored** —
> including *standard* keys.

A second `ExeName32="tail.exe"` after a header still produces `verify.exe`; a `Module=` after a header is
dropped, and the build then fails with `Must have startup form or Sub Main()`. Three further rules fell
out of the same sweep:

- **Blank and whitespace-only lines are skipped, not terminators** — including between standard keys. An
  unknown key after a blank line with no section header is still `invalid key`.
- **There is no comment syntax.** `; a comment` among the keys gives
  `contains invalid key '; a comment⏎Startup'` — the key-name scan runs on to the next `=`, swallowing the
  following line whole.
- **The ignored tail is still shape-checked.** A line with no `=`, or an empty key (`=orphan`), inside a
  trailing section gives `The project file '…' is corrupt, and can't be loaded.` Tail *values* are not
  path-resolved, so a nonexistent file named there builds fine.

### An unterminated item line crashes the compiler

> **An item line — `Module=`, `RelatedDoc=` — as the file's last line with no trailing CRLF kills
> `vb6.exe` with `0xC0000005` and writes nothing to the `/out` log.** Adding the terminator makes the
> byte-identical file legal.

It is specific to item lines: a final unterminated `Reference=` or `Name=` is fine. **Always end a written
`.vbp` with CRLF.** A missing one is not a diagnosable failure — it is a silent process kill with an empty
error log, which is the worst shape a serializer bug can take.

This also overturned a first reading of the same evidence. Three cases exiting `0xC0000005` looked like a
key-**ordering** rule (`Module=` must precede `Startup=`); further cases showed ordering is irrelevant and
the terminator is everything.

`ProjectSerializer` is not exposed today — every known line goes through `WriteLine`, and the last is
always `Name=`, which is safe unterminated regardless. But that is ordering luck rather than intent: move
an item line last, or append after `ExtensionTail`, and the crash arrives silently.

### The IDE offers both, on a checkbox — and the wrong one does not build

`RelatedDoc=` is not an obscure key recoverable only by probing: VB6's **Add File** dialog carries a
checkbox, **"Add As Related Document"**, and it is the switch between the two representations. Observed in
the shipped IDE.

**Unchecked**, the file is added as a *standard module with a synthetic positional name*:

```
Module=Module1; Module1.bas
Module=Module2; Test.md
```

`Module2` is not derived from `Test.md` — it is the next free `ModuleN`, so the IDE is running its
ordinary add-a-module path pointed at a different file. The extension buys nothing: that line naming a
`.md` whose content is prose fails with
`Compile Error in File '…\notes.md', Line 0 : Expected: If or Else or ElseIf or End or EndIf or Const`,
while the same line naming a `.md` that happens to hold a valid `.bas` body **builds**. VB6 never looks at
the extension; it compiles whatever the line points to.

So VB6 does have the right answer, and hands the user a project that cannot be compiled if they miss one
checkbox. **Corrects an earlier reading of the same evidence in this file**, which said the shipped
gesture simply produced a broken project — true only of the unchecked path.

Consequences for HexIDE: **read both forms**, since a user can produce either today; **write
`RelatedDoc=`**, because the other propagates the broken one; and if an equivalent affordance is offered,
do not repeat a default that silently produces an unbuildable project.

**The checkbox defaults OFF and is not sticky** — so the default path, every time, is the one that
produces a project that will not build, and a user has to know to tick a box they have no reason to notice.
That makes the case against reproducing the choice: HexIDE should always write `RelatedDoc=`.

### What the IDE does with one

Observed in the shipped IDE, and the two facts that decide how HexIDE should present these:

- **A `RelatedDoc=` entry appears under its own "Related Documents" node** in the Project Explorer — a
  grouping distinct from Forms and Modules rather than a file mixed in among them.
- **Double-clicking one launches it externally**, through the shell — or raises Windows' *Open With* for
  an extension the machine does not recognise.

The second is the more consequential. **VB6's own answer to a related document is "not mine to edit"**; it
hands the file to the OS. So an IDE that opened a `.md` in its own editor and offered language services
for it would be *exceeding* VB6, not reproducing it — worth stating plainly, because the fidelity
principle distinguishes reproducing intended behaviour from reproducing defects, and this is neither. It
is a deliberate VB6 limitation, and going past it is a product decision rather than a fidelity question.

The first is a happy convergence: a dedicated grouping node is simultaneously what VB6 did and what a
modern IDE does for files that belong to a project without being code.

### Absolute paths are legal, and the IDE normalises away the ones it can

Observed in the shipped IDE, which is the only place this is visible — the compiler's tolerance says
nothing about what gets written. The rule is a single sentence:

> **VB6 writes a relative path whenever one is expressible, and an absolute path only when it is not.**

| file added | written as |
|---|---|
| on a **different drive** — `D:\setup.tdf` | **absolute**, and it survives save |
| **same drive, outside the project cone** — `C:\Windows\WindowsUpdate.log` from a project in `C:\…\Temp` | **relative** — `..\Windows\WindowsUpdate.log` |

```
RelatedDoc=Test.md
RelatedDoc=D:\setup.tdf
RelatedDoc=..\Windows\WindowsUpdate.log
```

`RelatedDoc=` follows the same rule as the item lines; there is not a second policy for it.

**The compiler accepts far more than the IDE writes**, and normalises none of it — 21 further compiles:

| written | vb6.exe |
|---|---|
| absolute `Module=`, `Form=`, `RelatedDoc=` | **legal**, all three |
| forward slashes — `C:/dir/Mod.bas` | **legal** |
| embedded spaces — `C:\some dir\Mod.bas` | **legal**, unquoted |
| UNC — `\\localhost\C$\dir\Mod.bas` | **legal** (loopback admin share; a syntax pass, not a remote test) |
| one relative member and one absolute in the same project | **legal** |
| **any path in quotes** | **illegal** — see below |

The load-bearing case put the **only** `Sub Main` in an absolute-path module with no other member, so the
build succeeding proves VB6 followed the path rather than falling back to the project directory.

**Never quote a path, in any key.** All three keys fail, by two different mechanisms — which is why this
reads as one rule rather than a quirk of one key. `Module=` **keeps** the leading quote, so the value stops
looking rooted and VB6 prepends the project directory
(`Path/File access error: 'C:\proj\"C:\dir\Mod.bas"'`); `Form=` **drops** the leading quote, stays
absolute, and keeps the trailing one. `RelatedDoc=` was already measured to fail the same way.

The error vocabulary is consistent across item lines and `RelatedDoc=`, and distinguishes three failures:
**`File not found`** — the file is absent but its directory exists; **`Path not found`** — the directory or
the drive letter is absent; **`Path/File access error`** — the target exists as text but cannot be opened
(a directory, or a name mangled by quotes).

**Nothing is normalised by the compiler.** Across all 76 cases in this investigation, `/make` left the
`.vbp` byte-identical. So the relativisation above belongs to `File → Save Project`, not to the format —
and both shapes therefore occur in real-world `.vbp` files regardless of which one VB6's IDE prefers.

**And the IDE actively re-normalises.** A `.vbp` hand-edited to make a same-drive path absolute **opens
fine** — so absolute is accepted on load — but the IDE **writes it back as relative on save**. The path is
treated as derived, not as user content.

That is a direct conflict with HexIDE's own serialization contract, and worth deciding rather than
falling into. `serialization-outcomes.md` says give back what came in; here VB6 itself does not. Preserving
a hand-authored absolute same-drive path verbatim is faithful to the *file* and divergent from *VB6*;
normalising it matches VB6 and means HexIDE modified a file it was asked to round-trip. Recorded here
without settling it, because the argument runs both ways and the measurement does not decide it.

Two consequences elsewhere. The `project-explorer` spec requires an out-of-cone member to show "its
**relative** location" in its caption — but a different-drive member has no relative location, which is
exactly why VB6 wrote it absolute, so that requirement assumes something untrue for one of the two shapes.
And the path-leak defect recorded as #149 is wider than "traversal depth": a cross-volume reference
publishes a **full absolute path**, not merely how far up the tree a developer's project sat.

### The harness has now invalidated itself three times, and that is the finding

Worth stating as a rule, because it has cost more wrong answers today than the compiler has produced
surprises:

1. **`needsMain` scanned only the primary module**, so a case whose `Sub Main` lived in Module2 got a
   second one appended and came back "illegal — ambiguous". Then the fix appeared not to work because the
   same decision was derived twice, once more inside the behaviour-capture path.
2. **A tab-separated transport** ate the values of `VBA.Constants`, whose members *are* tabs and newlines,
   and reported `vbTab` as the empty string — with identical row counts and every numeric constant intact.
3. **A lost backslash in a cleanup line** — `Get-ChildItem 'C:'` lists the runspace's current directory on
   drive C:, not the root — so `verify.exe` survived between cases and every case inherited the previous
   one's build. All sixteen reported legal.

Each was silent, each produced a plausible-looking result, and none would have been caught by reading the
output. The mitigation that worked is cheap and should be standard: **put a known-illegal case second in
every batch and require it to fail.** A run where the guard passes is a run that measured nothing. The
general form: a harness that must synthesise or clean up part of the experiment can only be trusted for
questions that do not touch the synthesised part — and the way to find out is to ask it a question whose
answer you already know.

### What this could not determine

The harness compiles; it never opens the VB6 IDE. So nothing here says whether the IDE **preserves** a
trailing custom section or a `RelatedDoc=` line **on save**, or whether it reorders them. `/make` left all
54 `.vbp` files byte-identical, but that is the compiler's load path and not `File → Save Project`. Also
unmeasured: whether `RelatedDoc=` behaves the same outside `Type=Exe`, and whether such files are locked
or copied rather than merely opened.

On paths specifically: the UNC pass used the loopback admin share, so a genuinely remote server — redirector
round-trip, credential prompt, slow I/O — is untested, as are a mapped network drive letter, paths beyond
`MAX_PATH`, 8.3 short names, and drive-letter case. And whether the IDE displays or round-trips a UNC path,
or rewrites it to a mapped letter, is unknown.

### The IDE can keep Form before Reference on Save Project As (#855)

Measured on the oracle VM, VB6 6.00.8176 SP6, 2026-10-12 (issue #855). A hand-written Standard EXE project
was opened in the IDE with keys in this order: `Type`, `Form=Form1.frm`, `Reference`, `Startup`, `ExeName32`,
`Name`. File → Save Form1.frm As renamed the form to `Saved.frm`; File → Save Project As then wrote a
594-byte project. Its first three lines remained `Type=Exe`, `Form=Saved.frm`, and
`Reference=*\G{00020430-0000-0000-C000-000000000046}#2.0#0#..\..\Windows\SysWOW64\stdole2.tlb#OLE Automation`.
The renamed form stayed on line 2. VB6 added `IconForm` between `Reference` and `Startup`, `Command32`
after `ExeName32`, and the option block after `Name`.

This overturns the assumption that VB6 always writes references before forms: its templates' usual order
is not universal. It does not resolve the custom-section or `RelatedDoc` save questions above. A plain
Save Project, other non-canonical orders, placement of newly added items, and project types other than
Standard EXE remain unmeasured. HexIDE's slot retention for other recognised keys is its round-trip
contract, not an additional VB6 measurement.

