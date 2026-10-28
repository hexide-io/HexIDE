## A reference's path, and the line order a save writes (2026-10-27, #2274)

**The question.** When vb6.exe saves a project, it rewrites each `Reference=*\G{GUID}#version#lcid#path#description`
line. The path comes out relative to the project's folder, system files included, so `C:\Windows\System32\msi.dll` is
written `..\..\Windows\System32\msi.dll` (#2273, "A reference's LCID is lowercase hex, unpadded"). Three cases weren't
known:
- a library on another drive;
- a library on a UNC path;
- a reference with an empty path.

Separately: does a save reorder a hand-written `.vbp` that puts `Object=` before `Reference=`?

**The probe.** VB6 SP6's IDE on the oracle VM's console.
- **The project** was written by hand in `C:\hexoracle-2274x\w`, with its MSCOMCTL.OCX `Object=` line first and five
  `Reference=` lines after it.
- **The libraries:** three empty type libraries built for the probe with `CreateTypeLib2`, each registered where the case
  needed it, and a GUID that was never registered.
  - **Another drive:** the registry's path was `X:\HexIde2274D.tlb`. `X:` was a `subst` drive, made in the console session
    before VB6 started.
  - **UNC:** the registry's path was `\\localhost\hexoracle2274\HexIde2274U.tlb`, on a share made for the probe.
  - **Empty path:** registered at `C:\hexoracle-2274\tlb\HexIde2274E.tlb`, but listed in the `.vbp` as `#1.0#0##HexIDE
    2274 empty path`.
  - **Not registered:** listed with an empty path too.
- **The pass:** the IDE opened the project, msi.dll was checked in Project → References to make the project dirty, and
  File → Save Project saved it. The libraries, the share and the drive were removed afterwards.

**How the References dialog listed them:** the three registered libraries by name, and **`MISSING: HexIDE 2274 not
registered`**.

| Case | Written by hand | VB6 wrote |
|---|---|---|
| Another drive | `X:\HexIde2274D.tlb` | **`X:\HexIde2274D.tlb`**: absolute, as it can't be made relative |
| UNC | `\\localhost\hexoracle2274\HexIde2274U.tlb` | **`\\localhost\hexoracle2274\HexIde2274U.tlb`**, unchanged |
| Empty path, library registered | empty | **`..\..\hexoracle-2274\tlb\HexIde2274E.tlb`**: filled in from the registry, and made relative |
| Empty path, not registered | empty | **empty**: `#1.0#0##HexIDE 2274 not registered`, kept as it was |
| stdole2, for comparison | `C:\Windows\SysWOW64\stdole2.tlb` | `..\..\Windows\SysWOW64\stdole2.tlb` |

- **A path on the same drive is written relative, and any other path absolute.** That covers another drive letter and a
  UNC share.
  - A `subst` drive stood in for a second disk: to VB6 it is a different drive letter.
  - For the other-drive and UNC cases, the hand-written path and the registry's agreed, so these rows don't show which of
    the two VB6 writes.
- **The path comes from the registry when the library is registered.** The empty-path reference came back with the
  registered file's path. A reference VB6 can't resolve keeps the path it had, here none.

**The order.** VB6 wrote:

```
Type=Exe
Reference=...stdole2.tlb#OLE Automation
Reference=...#X:\HexIde2274D.tlb#HexIDE 2274 other drive
Reference=...#\\localhost\hexoracle2274\HexIde2274U.tlb#HexIDE 2274 UNC
Reference=...#..\..\hexoracle-2274\tlb\HexIde2274E.tlb#HexIDE 2274 empty path
Reference=...##HexIDE 2274 not registered
Reference=...#..\..\Windows\System32\msi.dll#Microsoft Windows Installer Object Library
Object={831FDD16-0C5C-11D2-A9FC-0000F8754DA1}#2.0#0; MSCOMCTL.OCX
Form=Form1.frm
```

- **The save moved the `Object=` line, read before the `Reference=` lines, to after all of them,** the newly added one
  included. It stayed before the `Form=` line.
- **The references kept their order, and the new one was appended.** The MISSING one kept its place.

**Beside "The Components dialog and an in-box library's lines" (#1103).** That project had no `Reference=` line. Its
`Object=` line, read after `Type=`, stayed there, and one added through the Components dialog went after the last
`Form=` line. Together the two showed this order after References was accepted (the follow-up below isolates that trigger):
1. `Type=`;
2. the references;
3. the `Object=` lines it read;
4. the `Form=` lines;
5. an `Object=` line added in this session.

In both, every `Reference=` line comes before every `Object=` line.

This bears on #2265. A bare class name binds to whichever library comes first in the `.vbp` ("In-box classes: a bare
name binds to the first library"), so after accepting References and saving, a referenced type library precedes every read component. An ordinary save
alone does not establish that precedence.

### A save with no edit keeps the order it read (2026-10-28)

**The question.** VB6's shipped project templates list lines out of the order above. IIS Application puts a `Reference=`
after its `Designer=` line, and Data Project puts an `Object=` after its `Designer=` and `Form=` lines. Where does a save
put them?

**The probe.** VB6 SP6's IDE on the oracle VM's console. Each template's whole folder was copied to a folder of its own
two levels below `C:\`, and the template opened in the IDE and saved with File → Save Project As, under a new name in
the same folder. A plain File → Save Project of IIS Application wrote the same bytes as its Save As. Five synthetic
projects with registered controls covered the shapes the templates need, MSCOMCTL.OCX and TABCTL32.OCX among them. Each
synthetic project was saved twice:
- **unedited**, with Save Project As;
- **after Project → References was opened and OKed with nothing changed,** then Save Project.

| Lines as read | Saved unedited | Saved after the References dialog's OK |
|---|---|---|
| `Object=` (A), `Form=`, `Module=`, `Object=` (B), `Reference=` | **the same order** | `Reference=` ×2, `Object=` A, B, `Form=`, `Module=` |
| `Form=`, `Object=`, `Module=` | **the same order** | not run |
| `Object=`, `Reference=` ×2, `Form=` | **the same order** | `Reference=` ×2, `Object=`, `Form=` |
| `Reference=`, `Object=`, `Reference=`, `Form=`, `Module=`, `Object=`, `IconForm=` | **the same order** | `Reference=` ×2, `Object=` ×2, `Form=`, `Module=`, `IconForm=` |
| IIS Application: `Reference=` ×2, `Designer=`, `Reference=` | **the same order** | not run |

So:
- **A save with no edit writes every line where it was read.** That held for an `Object=` after the members, an
  `Object=` between two members, and a `Reference=` after a `Designer=` or after the members.
- **The References dialog's OK, even with nothing changed, regroups them:** every `Reference=`, then every `Object=` in
  the order read, then the members in the order read, then the rest. The earlier probe above went through that dialog,
  which is why it showed this order.
- **The Components dialog does not regroup:** it adds its `Object=` line after the last `Form=` line (#1103).
- **Not measured:** whether another edit, such as Project Properties, regroups as the References dialog does.

**What else the unedited save changed in the templates.**

| Template | Change |
|---|---|
| All five | Every resolvable `Reference=` path rewritten from the registry and made relative, as above: the template's `..\VB\stdole2.tlb`, `D:\VB98\stdole2.tlb` and `..\..\..\..\..\VB6\stdole2.tlb` all became `..\..\Windows\SysWOW64\stdole2.tlb`. The description after the last `#` was kept as the file had it, a trailing space included |
| DHTML Application | The library's path came from the registry even where the file name differed: `MSHTMPGD.DLL` became `...\designer\MSHTMPGR.DLL` |
| ActiveX Document Dll | **The reference changed library:** `{BEF6E001-...}#2.0`, Standard OLE Types in `OLEPRO32.DLL`, was written as `{00020430-...}#2.0`, stdole2, with the description "Standard OLE Types" kept. It did so with `{BEF6E001-...}` registered and without it |
| IIS Application | The ASP library, `{D97A6DA0-...}#2.0`, was not resolvable: only version 3.0 was registered, or none. Its line kept `#2.0#`, and its absolute path on the same drive was made relative: `C:\WINNT40\System32\inetsrvsp.dll` became `..\..\WINNT40\System32\inetsrvsp.dll` |
| Activex Document Exe | **`Retained=0` added**, between `Unattended=0` and `ThreadPerObject=0`, where the template had none. The other four already had it |

Every line not listed kept its text and its place, and each file kept its final CR LF.

**Data Project could not be measured here.** VB6 reported "Error accessing the system registry" at its first `Object=`
line, MSDatRep.ocx, and stopped loading at "Continue Loading Project?". A synthetic project with its late `mshflxgd.ocx`
line gave the same error. The guest also lacks the Data Environment designer that its `DataEnv.Dsr` needs, and its run
time. Both come from the VB6 media, and the licence keys for its data controls are the likeliest cause of the registry
error, as with the MaskedEdBox (#1098). Before the load stopped, its ADO reference had been written as
`{00000205-...}#2.5#0#...\msado25.tlb#Microsoft ActiveX Data Objects 2.0 Library`: the library and version the registry
resolved, under the description the file had.

**The saved files** are kept beside the templates in the round-trip corpus, outside this tree because they are derived
from Microsoft's: `<template>.vbp.vb6-saved`, for the five templates above. That name keeps them out of the corpus's own
`*.vbp` search. Their `Reference=` paths are relative to the folder they were saved in on the oracle VM, two levels
below `C:\`, so those fields can't be compared byte for byte from another folder or machine.

### In HexIDE

`ProjectSerializer` now relativises supplied same-drive reference paths (#2274), without registry resolution.
It retains read line order unless References was accepted; the follow-up below corrects the earlier blanket grouping
rule. Components added in the session still follow the last Form (#1103).

### A save with no edit keeps the order it read (2026-10-28)

The follow-up SP6 console probes supplied in #2274 (#2279 at 66ec3741) isolated
References OK as the regrouping trigger. See the [corrected save rules](reference-paths-and-save-order.md).
A plain Save Project or Save Project As kept a late Object, an Object between
members, and a Reference after Designer or members in place. References OK, even
unchanged, grouped references, then read Objects, then members. Components did not
regroup. Other edits remain unmeasured. The earlier grouped-order probe used
References, so it does not establish a rule for an ordinary save.
