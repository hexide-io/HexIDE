## The native build's hidden switches, and where `VB6.EXE` finds C2 and LINK (2026-10-13)

Measured for [Building with the real VB6](../wip/vb6-native-build.md), which asks whether HexIDE can pass the
community's undocumented compiler and linker switches, and capture what `C2.EXE` and `LINK.EXE` print, without
touching the user's installation. `VB6.EXE /make <abs .vbp> /out <abs log>` on the oracle machine's VB6 install,
a one-module `Sub Main` project built as native code (`CompilationType=0`, `OptimizationType=0`). One run per
row.

| Probe | Exit | `/out` and result |
|---|---|---|
| No `[VBCompiler]` section (control) | 0 | `Build of 'probe.exe' succeeded.` No map file. |
| `[VBCompiler]` then `LinkSwitches=/MAP:<dir>\probe.map` | 0 | Succeeded, and the map file is written: its first line names the module, the next gives the link timestamp. |
| `[VBCompiler]` then `C2Switches=/Zzbogus` | 1 | `Unexpected error occurred in code generator or linker.` then `fatal error C1007: unrecognized flag '/Zzbogus' in 'p2'` then `Build of 'probe.exe' failed.` No executable. |
| `[VBCompiler]` then `LinkSwitches=/BOGUSSWITCH` | 0 | `Build of 'probe.exe' succeeded.` and nothing else. |
| The whole `VB98` folder copied to another directory, `/make` run with the copy's `VB6.EXE` | 0 | Succeeded. |
| The same copy with its `C2.EXE` renamed | 1 | `File not found: C2.EXE` then `Build of 'probe.exe' failed.` |

**`C2Switches` is honoured.** The community has used `LinkSwitches` for years. `C2Switches` was claimed in 2013,
exposed by one add-in, and a VBForums thread reports C2 rejecting `/Ob2` passed through it with the same C1007,
but no public project was found using it. This run confirms it from a clean `.vbp` under `/make`: a bogus flag
reaches C2, and the table above is exactly what `/out` says.

**A failing code generator does not block `/make`.** The interactive IDE shows "Unexpected error occurred in
code generator or linker" and offers to open the errors in Notepad; under `/make` the same text and C2's own
diagnostic go to `/out`, and the process exits 1.

**A successful link discards whatever LINK printed.** `/out` held only the success line. LINK is expected to
have warned about the unknown option (LNK4044), but that is inferred, not observed; a wrapper around LINK would
see it. Not measured: what `/out` holds when LINK fails.

**`VB6.EXE` runs the `C2.EXE` beside itself.** With the copy's `C2.EXE` gone, the build failed rather than
falling back to the original installation, which was still present. So a private copy of `VB98` runs whatever
code generator and linker are in the copy.

Not measured: where in the command lines VB6 inserts each value, whether `C2Switches` applies to every module,
and whether LINK 6 accepts `/DYNAMICBASE`, `/NXCOMPAT` or `/LARGEADDRESSAWARE`.


## The command lines VB6 gives C2 and LINK, and where the switches go (2026-11-21, #978, `/make`, SP6)

**The question.** The section above left four things open:
- where VB6 puts each `[VBCompiler]` value in the command lines it builds, and whether `C2Switches` reaches every
  module;
- whether LINK 6 accepts `/DYNAMICBASE`, `/NXCOMPAT` and `/LARGEADDRESSAWARE`;
- whether a quoted value reaches LINK with its quotes;
- what `/out` holds when LINK fails.

#978 also asked what a p-code build's link looks like.

**The rig.** VB6.EXE 6.00.9782 (SP6) in a clone of the oracle VM, run as `/make` over PowerShell Direct with a 90-second
limit. It ran from a private copy of the `VB98` folder, which works because VB6 runs the C2 and LINK beside it (above).
- **The wrappers.** In that copy, `C2.EXE` and `LINK.EXE` were renamed `C20.EXE` and `LINK0.EXE`, and replaced by one
  small logging program. It records its command line, runs the real tool with the same arguments, relays its output,
  and returns its exit code. The installation itself was untouched.
- **The project.** Each probe was a Standard EXE: `Form1.frm`, whose `Form_Load` calls a function in each of
  `Mod1.bas` and `Mod2.bas`, with `OptimizationType=0`.
- **The paths below:** `<dir>` is the project's folder, `<VB98>` the copy, and `<temp>` the user's temporary folder.
- **Reading the output:** the real linker printed its own name as `LINK0` because it had been renamed. An installed
  `LINK.EXE` prints `LINK`.

**The command lines, native code, no `[VBCompiler]` section.** C2 ran once per module, in project order, and LINK
once:

```
C2:   -il "<temp>\VB546137" -f "<dir>\Form1.frm" -W 3 -Gy -G5 -Gs4096 -dos -Zl -Fo"<dir>\Form1.OBJ" -QIfdiv -ML -basic
C2:   -il "<temp>\VB546138" -f "<dir>\Mod1.bas" ... -Fo"<dir>\Mod1.OBJ" ...
C2:   -il "<temp>\VB546139" -f "<dir>\Mod2.bas" ... -Fo"<dir>\Mod2.OBJ" ...
LINK: "<dir>\Form1.OBJ" "<dir>\Mod1.OBJ" "<dir>\Mod2.OBJ" "<dir>\p0.OBJ" "<VB98>\VBAEXE6.LIB" /ENTRY:__vbaS
      /OUT:"<dir>\p0.exe" /BASE:0x400000 /SUBSYSTEM:WINDOWS,4.0 /VERSION:1.0   /INCREMENTAL:NO /OPT:REF
      /MERGE:.rdata=.text /IGNORE:4078 
```

LINK's line is one line, wrapped here. It has three spaces after `/VERSION:1.0`, and ends with a space.

**Where the switches go:**

| `[VBCompiler]` line | Exit | What the logs show |
|---|---|---|
| `C2Switches=-Gy` | 0 | every C2 line, all three modules, starts `-Gy  -il ...`: the value first, then two spaces |
| `C2Switches=/Gy` | 1 | C2's line starts `/Gy  -il ...`. C2 refused it on the first module, `fatal error C1007: unrecognized flag '/Gy' in 'p2'`, so the other two never ran |
| `LinkSwitches=/DYNAMICBASE /NXCOMPAT /LARGEADDRESSAWARE` | 0 | appended after LINK's trailing space; LINK warned `LNK4044: unrecognized option "DYNAMICBASE"; ignored` and the same for `NXCOMPAT`, and nothing for `/LARGEADDRESSAWARE` |
| `LinkSwitches="/DYNAMICBASE /NXCOMPAT /FIXED:NO"` | 0 | appended **with its quotes**, so LINK saw one option: `LNK4044: unrecognized option "DYNAMICBASE /NXCOMPAT /FIXED:NO"; ignored` |

`-Gy` is already in VB6's own line, and C2 accepted it a second time. `/Gy` was refused where `-Gy` was accepted;
other slash-form flags weren't tried.

**What the executables hold,** read from their PE headers:

| Build | `IMAGE_FILE_LARGE_ADDRESS_AWARE` | DllCharacteristics | Relocations |
|---|---|---|---|
| no switches | off | `0x0000` | none |
| `/DYNAMICBASE /NXCOMPAT /LARGEADDRESSAWARE` | **on** | `0x0000` | none |
| the quoted value | off | `0x0000` | none |

So LINK 6.00.8447 applies `/LARGEADDRESSAWARE` and ignores the other two. The quoted form applies nothing at all,
`/FIXED:NO` included, and the build still reports success.

**A successful link discards LINK's warnings.** Both LNK4044 builds wrote only `Build of '<name>.exe' succeeded.` to
`/out`. The section above inferred this; the wrapper saw the warnings LINK printed.

**When LINK fails.** `LinkSwitches=/DEF:<missing file>`:
- **The exit:** 1, within the limit, with no dialog.
- **`/out`** held VB6's line, LINK's banner and both of its errors, then VB6's verdict:

```
Unexpected error occurred in code generator or linker.
Microsoft (R) Incremental Linker Version 6.00.8447
Copyright (C) Microsoft Corp 1992-1998. All rights reserved.

LINK0 : fatal error LNK1104: cannot open file "<missing file>"
LINK0 : fatal error LNK1141: failure during build of exports file

Build of 'p4.exe' failed.
```

No executable was written.

**A p-code build** (`CompilationType=-1`):
- **No C2 at all.**
- **LINK links one object:** `"<dir>\p5.OBJ"    /ENTRY:__vbaS ...`, with four spaces where the module objects and
  `VBAEXE6.LIB` would be, and the same switches after.
- **`LinkSwitches` reaches a p-code link too:** `/MAP:<dir>\p6.map` was appended the same way, and the map file
  was written.

### Not measured

- **A form-less project, a class, or an ActiveX DLL's link.**
- **`C2Switches` in a p-code build,** which runs no C2.
- **The release build's lines.**
