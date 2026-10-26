## What Save Project As does to the paths in a project file (2026-10-22, #1406)

**The question.** A `.vbp` names files by paths relative to its own folder. Save Project As writes it into another
folder and moves nothing else. Which paths does VB6 rewrite to name the same file from the new folder? #737 settled
the member lines HexIDE keeps without loading. This settles the keys HexIDE does not model.

**The probe.** It started from the VB98 Template's add-in project (`Addin.vbp`, `ADDIN.FRM`, `ADDIN.DSR`, `ADDIN.DCA`),
copied to `proj` with these additions:
- `Module=Module1; sub\Module1.bas`;
- `ResFile32="res\probe.res"`, a 32-byte `.res` holding only the null header entry;
- `HelpFile="help\probe.hlp"`, naming a file that does not exist;
- `Path32="out"`.

The VB6 IDE (6.00.8176, the original release) opened it. File > Save Project As then saved it to `proj\deeper\Saved.vbp`, and again to a
sibling folder, `other\Saved2.vbp`. Each saved file was read back.

| Line read in `proj` | Written in `proj\deeper` | Written in `other` |
|---|---|---|
| `Form=AddIn.frm` | `Form=..\AddIn.frm` | `Form=..\proj\AddIn.frm` |
| `Designer=AddIn.Dsr` | `Designer=..\AddIn.Dsr` | `Designer=..\proj\AddIn.Dsr` |
| `Module=Module1; sub\Module1.bas` | `Module=Module1; ..\sub\Module1.bas` | `Module=Module1; ..\proj\sub\Module1.bas` |
| `ResFile32="res\probe.res"` | `ResFile32="..\res\probe.res"` | `ResFile32="..\proj\res\probe.res"` |
| `Path32="out"`, with `ExeName32="Probe.dll"` | `Path32="..\out"` | `Path32="..\proj\out"` |
| `Path32="out"`, with no `ExeName32` | no `Path32` line | no `Path32` line |
| `HelpFile="help\probe.hlp"` | `HelpFile="help\probe.hlp"` | `HelpFile="help\probe.hlp"` |
| `Reference=…#..\..\STDOLE2.TLB#OLE Automation` | `…#..\..\..\Windows\SysWOW64\stdole2.tlb#…` | `…#..\..\Windows\SysWOW64\stdole2.tlb#…` |

**What it shows:**
- **Rebased:** every member line, `Designer=`, `ResFile32=` and `Path32=` are named from the new folder.
- **Kept as typed:** `HelpFile=`.
- **Dropped:** `Path32=` without an `ExeName32=`. The first run's project had no `ExeName32`, and neither of its saves
  kept the line. With `ExeName32` added, both saves rebased it. Why VB6 ties the two together was not examined.
- **Re-derived, not rebased:** a reference's library path. The Template's `..\..\STDOLE2.TLB` names no file from
  `proj`, and VB6 wrote the path of the library registered for that GUID, named from the new folder, for all four
  references. So VB6 consults the registry, and does not resolve the old path.

HexIDE now rebases `Designer=`, `ResFile32=`, `Path32=` and a reference's relative path the way it rebases member
lines (`SerializedProject.RebaseItemLine` and `RebasePath`), and leaves `HelpFile=` as it was. Two differences from
VB6 remain:
- **A reference's path is the file's own, rebased.** HexIDE cannot read the registry on every host. Where the library
  has not moved, the path is the same one VB6 writes.
- **`Path32` is kept without an `ExeName32`,** where VB6 drops it. The line does nothing without a target, so this is
  left alone.

