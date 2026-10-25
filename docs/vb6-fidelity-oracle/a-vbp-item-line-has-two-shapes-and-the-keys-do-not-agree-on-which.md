## A `.vbp` item line has two shapes, and the keys do not agree on which (2026-09-20)

Ten compiles against real `vb6.exe`, one variable each, on a hand-authored ActiveX control project
(`corpus/designer/`, which exists because nothing else in the corpus had a `UserControl` or a
`PropertyPage` in it). `/out` log quoted verbatim; a build that succeeds writes `Build of 'X.ocx'
succeeded.` and nothing else.

| `.vbp` item line | `vb6.exe` says |
|---|---|
| `UserControl=Gauge.ctl` | builds |
| `UserControl=Gauge; Gauge.ctl` | `File not found: 'C:\pagprobe\Gauge; Gauge.ctl'` |
| `PropertyPage=GaugeGeneral.pag` | builds |
| `PropertyPage=GaugeGeneral; GaugeGeneral.pag` | `File not found: 'C:\pagprobe\GaugeGeneral; GaugeGeneral.pag'` |
| `Form=Form1.frm` | builds |
| `Form=Form1; Form1.frm` | `File not found: 'C:\pagprobe\Form1; Form1.frm'` |
| `Module=Mod1; Mod1.bas` | builds |
| `Module=Mod1.bas` | `The project file 'C:\pagprobe\D.vbp' is corrupt, and can't be loaded.` |
| `Class=Cls1; Cls1.cls` | builds |
| `Class=Cls1.cls` | `The project file 'C:\pagprobe\F2.vbp' is corrupt, and can't be loaded.` |
| `UserControl=Gauge.ctl ` (one trailing space) | builds |

> **`Module=` and `Class=` require `Name; File`. `Form=`, `UserControl=` and `PropertyPage=` require the
> file alone. Neither family tolerates the other's shape, and each rejects it in its own way.**

**The obvious guess is that one shape is right and VB6 is lenient about the other. Both halves of that are
wrong**, and the guess is the one HexIDE's serializer had made since it was written: it emitted
`Name; File` for all four, so every project it saved that contained a UserControl or a PropertyPage was
unopenable in VB6 (hexide-io/HexIDE#483). HexIDE's *reader* accepts both shapes, which is why the defect
survived every round-trip the project had — it round-tripped against itself.

Two things about the failures are worth keeping, because they are what a reader will meet before they meet
the rule:

- The `UserControl=Gauge; Gauge.ctl` failure names a file called `Gauge; Gauge.ctl`. VB6 is not parsing the
  value and rejecting it; it is taking the whole string as a filename. So the error is about a path, and
  says nothing about a shape.
- The `Module=Mod1.bas` failure says the **project file is corrupt** and names no line. A missing name
  prefix on one item line condemns the whole file, with nothing to point at. Anyone debugging that message
  will look for a truncated or mangled file long before they look for this.

Trailing whitespace after a path is trimmed, so the shape is the only thing under test here.

Related: [What a `.vbp` tolerates, and the one key for a non-code file
(2026-09-04)](what-a-vbp-tolerates-and-the-one-key-for-a-non-code-file.md#what-a-vbp-tolerates-and-the-one-key-for-a-non-code-file-2026-09-04), which establishes that
`RelatedDoc=` also takes a bare path and that an item line's target is opened at build time.

### What resisted explanation

Why the two families differ at all. The split is exactly designer-file (`Form`, `UserControl`,
`PropertyPage`) against code-file (`Module`, `Class`), which sounds like an explanation until you ask what
about a designer block makes the name redundant — and nothing does. All five kinds carry
`Attribute VB_Name` in the file, so the name is available either way, and all five are text. The grouping
is measured across every key, and no reason for it is offered here.

---

