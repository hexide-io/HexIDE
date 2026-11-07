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

