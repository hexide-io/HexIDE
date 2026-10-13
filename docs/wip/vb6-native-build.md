# Building with the real VB6: switches, compiler output and hooks

> **Work in progress.** Captured 2026-10-13. Nothing here is built. The measurements are real and are also
> recorded in [`vb6-fidelity-oracle.md`](../vb6-fidelity-oracle.md); everything under "Proposal" is an idea.

## Why

People who still build VB6 programs have spent twenty years learning to control its native build: console
programs, standard DLLs with real exports, statically linked C libraries, hardening flags, map files. Every one
of those techniques works by changing the user's VB6 installation, either by renaming `C2.EXE` or `LINK.EXE`
in Program Files and putting a wrapper in their place, or by running an add-in that patches `VBA6.DLL` in
memory. The VB6 IDE gives no other way in.

HexIDE launches `VB6.EXE` itself, so it can offer the same control, and show what the compiler and linker
print, without modifying the user's installation at all.

## How VB6 builds a program

- **The front end is `VBA6.DLL`,** the same language engine as Office VBA. It compiles each module and, for a
  p-code build, writes the object files itself and runs only the linker.
- **A native build runs `C2.EXE` once per module, then `LINK.EXE`.** `C2.EXE` is the code generator and
  optimiser from Visual C++ 6, without its C/C++ front end. Both ship in the `VB98` folder, alongside
  `CVPACK.EXE`. They run as separate console processes, started through `CreateProcessA`.
- **The result still needs the runtime.** A native executable calls `MSVBVM60.DLL` for strings, Variants, COM
  and errors; it just does not interpret p-code.
- **What the command lines look like.** *Advanced Microsoft Visual Basic 6.0* (2nd edition, chapter 7)
  captured them with a wrapper. A typical C2 run with no optimisation is
  `C2 -il <temp>\VB603389 -f Form1 -W3 -Gy -G5 -Gs4096 -dos -Zl -Fo<temp>\Form1.OBJ -QIfdiv -ML -basic`, where
  `-il`, `-dos` and `-basic` are undocumented. LINK receives the module objects, `VBAEXE6.LIB`,
  `/ENTRY:__vbaS`, `/OUT:`, `/BASE:0x400000`, `/SUBSYSTEM:WINDOWS,4.0`, `/VERSION:`, the `/DEBUG` options,
  `/INCREMENTAL:NO`, `/OPT:REF`, `/MERGE:` and `/IGNORE:4078`.

## What HexIDE does today

As of `upstream/main` at 7ae689d. The code is `IDE/HexIDE/IDE/Vb6ToolchainService.cs`.

- **Finding VB6:** the `VB6_EXE` environment variable, else the first of four standard install paths. There is
  no setting in the UI.
- **Running it:** always `/make "<project>.vbp" /out "<temp log>"`, with a 180-second limit after which the
  process tree is killed. It builds the files as last saved, not the editor's contents (#749).
- **On success:** nothing is shown. VB6's "Build of 'x.exe' succeeded." is discarded.
- **On failure:** the parser expects the Visual C++ shape `path(N) : error …`, but VB6 writes
  `Compile Error in File '<path>', Line <N> : <message>` (#477). So every failure falls through to a modal
  dialog showing the raw log. The fix is on the #273 branch, not yet on main. Errors outside a form never
  reach the editor at all (#376), and a diagnostic's message has nowhere to be read (#451).
- **C2 and LINK:** never referenced. Nothing they print is seen.
- **Project Properties:** the Make, Compile, Component and Debugging tabs are disabled (#48).
- **The `.vbp`:** the native-code keys (`CompilationType`, `OptimizationType` and the rest) are preserved
  verbatim in place but not modelled. Anything from the first `[Section]` line to the end of the file is kept as
  one verbatim tail, so a `[VBCompiler]` section survives a save but cannot be read or edited.

## What was measured (2026-10-13)

Real `vb6.exe`, `/make` with `/out`, on the oracle machine, building a one-module `Sub Main` project as native
code. One run per row.

| Probe | Exit | Result |
|---|---|---|
| No `[VBCompiler]` section (control) | 0 | Builds. No map file. |
| `[VBCompiler]` `LinkSwitches=/MAP:<dir>\probe.map` | 0 | Builds, and the map file is written. |
| `[VBCompiler]` `C2Switches=/Zzbogus` | 1 | No executable. `/out` holds "Unexpected error occurred in code generator or linker." then C2's own line, `fatal error C1007: unrecognized flag '/Zzbogus' in 'p2'`, then "Build of 'probe.exe' failed." |
| `[VBCompiler]` `LinkSwitches=/BOGUSSWITCH` | 0 | Builds. `/out` says only that it succeeded. |
| The whole `VB98` folder copied elsewhere, `VB6.EXE` run from the copy | 0 | Builds. |
| The same copy with its `C2.EXE` renamed | 1 | "File not found: C2.EXE". It does not fall back to the original installation. |

What follows from them:

1. **`LinkSwitches` and `C2Switches` both work,** read from a `[VBCompiler]` section of the `.vbp`. The
   research below found many projects using `LinkSwitches`, but none with a non-empty `C2Switches`, so this
   appears to be the first demonstration that VB6 honours it.
2. **A failing C2 reaches `/out`, and `/make` exits** rather than waiting on a dialog. That is what the IDE's
   "Unexpected error" message hides behind its offer to open the errors in Notepad.
3. **What LINK prints on a successful link is dropped.** LINK normally warns about an option it does not
   recognise (LNK4044), but nothing of it reached `/out`. Only a wrapper around LINK can show it.
4. **`VB6.EXE` runs the `C2.EXE` and `LINK.EXE` in its own folder.** A private copy of `VB98` therefore runs
   whatever is in the copy, and the user's installation is never involved.

Not yet measured: what `/out` holds when LINK *fails*, where VB6 inserts each switches value in the command line
it builds, and whether LINK 6 accepts the newer hardening switches some projects pass (`/DYNAMICBASE`,
`/NXCOMPAT`).

## What the community learned

Facts only, with sources. **No code from any of these tools may enter this tree.** Their licences are mostly
unstated (ThunderVB is LGPL-2.0), and HexIDE is MIT throughout; see "Code travels out, never back in" in
`CLAUDE.md`. Anything HexIDE needs is written here from the facts.

Much of this knowledge lives on VBForums, cyberforum.ru and vbstreets.ru, and in the READMEs of projects
by the people who still push VB6 hardest. The search behind this document could not reach Reddit or Stack
Exchange, so those are unsearched rather than empty.

### The `[VBCompiler]` section

- A vbstreets.ru member reported in 2013 having found "a pair of previously unknown keys", `C2Switches` and
  `LinkSwitches`, while reverse-engineering VB6 in 2011, and used them to build a console program on a clean
  VB6 with no add-ins.
- The section name is confirmed by several READMEs (wqweto's vbsqlite, The trick's VbPng).

`LinkSwitches` values found in public projects:

| Value | Purpose |
|---|---|
| `/SUBSYSTEM:CONSOLE` | A console program |
| `/LARGEADDRESSAWARE` | More than 2 GB of address space |
| `/DYNAMICBASE /NXCOMPAT /FIXED:NO` | Hardening (whether LINK 6 honours these is unmeasured) |
| `-DLL -ENTRY:DllMain -EXPORT:<name> …` | A standard DLL with real exports |
| `<lib>.obj <lib>.lib -ENTRY:mainCRTStartup` | Linking C objects into the program |
| `KERNEL32.LIB /OPT:NOREF` | A statically linked SQLite |
| `/ENTRY:DriverEntry /SUBSYSTEM:NATIVE` | A kernel driver (which also needed the runtime import patched out) |
| `/STACK:<bytes>` | A larger stack |

Caveats recorded alongside them: a replaced entry point skips some of VB's own initialisation; `/OPT:REF` can
drop the object that imports the runtime and produce a broken executable, hence `/OPT:NOREF`; and one project
swaps in a newer linker because LINK 6 has bugs with `/OPT:REF`.

### Wrappers and hooks

| Tool or method | How it works | What it was used for |
|---|---|---|
| OUTARGS (the *Advanced VB6* book) | Rename `C2.EXE` and `LINK.EXE`, put a logger in their place that records the arguments and runs the real tool | Seeing and editing the command lines |
| Compile Controller (John Chamberlain, 1999) | An add-in that hooks `VBA6.DLL`'s `CreateProcessA` and shows each command line for editing | Assembly listings, substituting a hand-assembled object, standard DLLs via a `.DEF` |
| LinkTool (Jim White, extended by David Zimmer) | Renames `LINK.EXE`, reads a per-project command file | Exports, extra objects, entry points, console programs |
| Ron Petrusha's method (2005) | A `LINK.EXE` wrapper that adds a `.DEF` and a `.RES` | Standard DLLs, Control Panel applets |
| vbAdvance (Peter Young, freeware) | Sits between compiler and linker; settings in a `[vbAdvance]` section of the `.vbp` | Console programs, exports, manifests, map and debug files, version resources |
| Trick Advanced Tools (The trick, 2016) | Patches `VBA6.DLL`'s compile entry points | Build events, and "Compiler options" and "Linker options" fields that write `[VBCompiler]` |
| ThunderVB (LGPL-2.0) | An add-in | Inline assembly and C, standard DLLs |

Two things stand out. Every wrapper renames files in the user's installation, which is why so many of these
tools come with warnings about reinstalling VB6. And a 2004 survey of C2's switches warns against changing
`-basic`, `-il` or the `-Z` options: without `-basic`, C2 hangs.

### The documented native-code keys

VB6 writes these to the `.vbp` from the Compile tab. Microsoft's format page lists the keys without values;
the values below come from a third-party parser and a forum post, and are not yet measured here.

| Key | Values | Compile tab option |
|---|---|---|
| `CompilationType` | 0 native, -1 p-code | Compile to P-Code / Native Code |
| `OptimizationType` | 0 fast, 1 small, 2 none | Optimize for Fast Code / Small Code / No Optimization |
| `FavorPentiumPro(tm)` | -1 on | Favor Pentium Pro (the "(tm)" is part of the key) |
| `CodeViewDebugInfo` | -1 on | Create Symbolic Debug Info |
| `NoAliasing` | -1 on | Assume No Aliasing |
| `BoundsCheck`, `OverflowCheck`, `FlPointCheck`, `FDIVCheck` | -1 means the check is **removed** | The four "Remove … Checks" options |
| `UnroundedFP` | -1 on | Allow Unrounded Floating Point Operations |
| `CondComp` | `"NAME = 1 : OTHER = 0"` | Conditional Compilation Arguments |

## Proposal

Four layers, each useful without the next.

### 1. The Compile tab, writing what VB6 itself reads

Enable the Compile tab (#48) with the documented options above, and add an **Advanced** area with two free-text
fields, "Code generator switches" and "Linker switches", stored as `C2Switches` and `LinkSwitches` in the
project's `[VBCompiler]` section. That is the place VB6 reads them from, so a project configured in HexIDE
builds identically in the real VB6 IDE, and a project configured with Trick Advanced Tools opens in HexIDE
with its settings intact.

Offer the common cases as named checkboxes that write the switch for the user: **Console application**
(`/SUBSYSTEM:CONSOLE`), **Large address aware**, and **Write a map file** (`/MAP`, measured). Hardening switches
only once LINK 6 is measured to honour them. Exports and entry points need more than a checkbox and come later.

This needs the `.vbp` reader to understand sections instead of keeping everything after the first one as an
opaque tail: edit `[VBCompiler]`, keep every other section verbatim and in its place.

A switch given for one build only, without saving it, can go in a temporary copy of the `.vbp`.

### 2. A private copy of the user's VB6

The first time a project is built with VB6, HexIDE copies the user's own `VB98` folder into its data folder and
builds with the copy. In the copy, `C2.EXE` and `LINK.EXE` are replaced by small programs written in this tree,
which run the real tools (kept in the copy under other names) and send their command line, output and exit code
back to HexIDE.

- **Nothing in the user's installation changes,** and no administrator rights are needed.
- **HexIDE ships no VB6 file.** It copies the user's own licensed installation, on the same machine, for the
  same user.
- **The registry is shared,** so references, controls and add-in settings resolve exactly as they do for the
  original.
- **Keep it current:** record the original files' hashes beside the copy, and refresh it when they change, for
  example after a service pack or a repair.
- **Keep it visible:** Options shows which `VB6.EXE` builds are using, the copy's location and size, and
  Rebuild and Remove buttons.
- **Size:** a full copy is the safe default. Trimming it to what `/make` needs means measuring which files
  that is.

### 3. A build output window

Show every build in an Output pane: the `/out` log, and for each C2 and LINK run its command line and what it
printed. Diagnostics in the Visual C++ format (`fatal error C1007`, `LNKnnnn`) go through a matcher into the
error list (#451), as VB6's own `Compile Error in File` lines do through #477's parser. Linker warnings that
VB6 discards on a successful build become visible, which is the case measured above.

### 4. Later, and optional: hooks for the real VB6 IDE

An **Install / Uninstall VB6 build hooks** command for people who still work in the VB6 IDE. It does what the
community does by hand: renames `C2.EXE` and `LINK.EXE` in the real `VB98` and puts the same wrappers in their
place, so builds from the VB6 IDE get the switches and captured output too.

It replaces executables in Program Files that every VB6 build runs, so it must:

- ask for elevation for this step only;
- refuse to install over files whose hashes are not those of a known Microsoft release;
- restore the originals exactly on uninstall;
- notice when a VB6 repair has put the originals back, and say so;
- be labelled and reviewed as `security`, as add-in trust is.

## Open questions

- Where VB6 inserts `C2Switches` and `LinkSwitches` in the command lines it builds, and whether `C2Switches`
  applies to every module. The wrapper in layer 2 answers this the first time it runs.
- What `/out` holds when LINK fails rather than warns.
- Whether LINK 6 accepts `/DYNAMICBASE`, `/NXCOMPAT` and `/LARGEADDRESSAWARE`, or ignores them.
- The values of the documented keys, which are taken from third parties.
- Which files `/make` needs, if the private copy is ever trimmed.
- How a p-code build's single LINK run looks through the wrapper.

## Related

- #399: compiling with `vb6.exe` is configuration, not a menu item. See
  [One Build command, many compilers](choosing-the-compiler.md).
- #48: the disabled Project Properties tabs.
- #477, #376, #968: parsing VB6's compile errors.
- #451: nowhere to read a diagnostic's message.
- #749: builds use the last saved files.
- #842: the oracle builds native code where the IDE runs p-code.

## Sources

- John Chamberlain, *Compile Controller Guide* (1999), mirrored at
  https://github.com/dzzie/addins/tree/master/CompilerControl99
- *Advanced Microsoft Visual Basic 6.0*, 2nd edition, chapter 7: https://flylib.com/books/en/2.716.1.64/1/
- The `C2Switches` / `LinkSwitches` discovery: http://bbs.vbstreets.ru/viewtopic.php?f=15&t=44359
- wqweto, vbsqlite: https://github.com/wqweto/vbsqlite
- The trick, VbPng: https://github.com/thetrik/VbPng
- The trick, Trick Advanced Tools: https://www.cyberforum.ru/blogs/354370/4409.html
- A survey of C2's switches (2004): https://sandsprite.com/vb-reversing/vb-decompiler/viewtopic1833.html
- On replacing the entry point: https://sandsprite.com/vb-reversing/vb-decompiler/viewtopic3d76.html
- `[VBCompiler]` usage notes: https://sandsprite.com/blogs/index.php?uid=11&pid=294
- LinkTool: https://github.com/dzzie/addins/tree/master/LinkTool
- Ron Petrusha's method, as reworked with attribution:
  https://www.elguille.info/vb/avanzado/crear_dll_windows_con_vb6_explicado.htm
- vbAdvance: https://classicvb.net/tools/vbAdvance
- ThunderVB: https://sourceforge.net/projects/thundervb/
- Karl Peterson, console programs: https://classicvb.net/samples/Console
- Microsoft, the `.vbp` format:
  https://learn.microsoft.com/en-us/previous-versions/visualstudio/visual-basic-6/aa716294(v=vs.60)
- Microsoft, "Unexpected error occurred in code generator or linker":
  https://learn.microsoft.com/en-us/office/vba/Language/reference/user-interface-help/unexpected-error-occurred-in-code-generator-or-linker
- KB Q193089, a stale PDB breaks the link: https://jeffpar.github.io/kbarchive/kb/193/Q193089/
- VBForums: c2switches, https://vbforums.com/showthread.php?887762-c2switches=
- VBForums: compiler switches, https://www.vbforums.com/showthread.php?880573-RESOLVED-Compiler-switches
- VBForums: replacing C2 with a newer compiler,
  https://www.vbforums.com/showthread.php?752777-replace-c2-exe-with-new(vc-)-cl-exe-vb6-compilation
- VBForums: a `.DEF` file for a VB6 DLL,
  https://vbforums.com/showthread.php?702697-RESOLVED-DLL-Injection-DLLMain-in-VB6-Help-with-DEF-File-HELP%21=
