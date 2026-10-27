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
`Form=` line. Together the two show the order VB6 writes:
1. `Type=`;
2. the references;
3. the `Object=` lines it read;
4. the `Form=` lines;
5. an `Object=` line added in this session.

In both, every `Reference=` line comes before every `Object=` line.

This bears on #2265. A bare class name binds to whichever library comes first in the `.vbp` ("In-box classes: a bare
name binds to the first library"), so once VB6 has saved a project, a referenced type library outranks every component.

### In HexIDE

`ProjectSerializer` keeps an absolute reference path absolute and rebases only a relative one (#2274). It writes the lines
in the order they were read, apart from an `Object=` line the Components dialog added, which goes after the last `Form=`
line as #1103 measured. So an `Object=` line written before the references stays before them.
