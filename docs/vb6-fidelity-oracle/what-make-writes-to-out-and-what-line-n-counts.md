## What `/make` writes to `/out`, and what "Line N" counts (2026-09-19)

Measured because HexIDE parses that log to put compiler errors in the editor, and because
hexide-io/HexIDE#273 turns on whether a line number means the file or the code view. Both answers were
guessed in the code and both guesses were wrong. Real `vb6.exe` 6.00.8176 (the original release), `VB6.EXE /make <abs .vbp>
/out <abs log>`, every case reproduced in two separate runs (a4 in three).

**The format**, exact bytes, ANSI, CRLF, and the log always opens with a bare empty line:

```
<CRLF>Compile Error in File '<ABSOLUTE path>', Line <N> : <message><CRLF>Build of '<ExeName32>' failed.<CRLF>
```

Note the spaces either side of the colon. The path is **absolute** even where the `.vbp` named the file
relatively. Exit code 1, and no executable. Success is `<CRLF>Build of 'h0.exe' succeeded.<CRLF>`, exit 0.
For a group it names the group, not each executable.

**HexIDE expects `path(N) : error …` and so matches none of this** (`Vb6ToolchainService.ErrorRegex`), which
is filed separately.

**`N` is a 0-based index into the CODE VIEW, not into the file.** The code view is the file minus the
`VERSION` line and the designer or class `BEGIN…END` block, and minus **every** `Attribute` line —
module-level *and* procedure-level. Physical lines are counted; a `_` continuation is not collapsed.

| Probe | Hidden lines above | File line (1-based) | Code view (0-based) | VB6 printed |
|---|---|---|---|---|
| a1 `.bas`, `Variable not defined` | 1 | 8 | 6 | **Line 6** |
| a2 `.bas`, `Syntax error` | 1 | 8 | 6 | **Line 6** |
| a3 `.bas`, no `Option Explicit` | 1 | 4 | 2 | **Line 2** |
| a4 `.bas`, error on the FIRST code line | 1 | 2 | 0 | **Line 0** |
| a6 `.bas`, same error on the SECOND code line | 1 | 3 | 1 | **Line 1** |
| b1 `.cls`, canonical 13-line header | 13 | 21 | 7 | **Line 7** |
| c1 `.frm`, 25-line designer + attribute block | 25 | 34 | 8 | **Line 8** |
| d1 `.cls` with two **procedure-level** `Attribute Foo.VB_*` lines above the error | 13+2 | 21 | 5 | **Line 5** |
| e1 error after a 3-physical-line continued statement | 1 | 9 | 7 | **Line 7** |

a3 rules out the competing reading that fits a1–c2 ("1-based, counted from after `Option Explicit`"). a6
shows a4's `Line 0` is a real index rather than a "no line" sentinel.

**So the file line is `N + 1 + (hidden lines above it)`**, and the hidden count is not a per-kind constant:
it is the header *plus every `Attribute` line above the error inside the code* (d1). A consumer that adds a
fixed offset per file kind is right until the file has a procedure attribute in it.

**Where `N` points inside a continued statement**, one measurement of each kind:

| Probe | Shape | VB6 printed |
|---|---|---|
| f1 | `Variable not defined`, the undeclared name on the **3rd** physical line | the token's own physical line |
| f2 | the same, name on the **middle** physical line | the token's own physical line |
| f3 | `Syntax error` in a continued statement | the statement's **first** physical line |

A binding error carries the token's physical line; a syntax error carries the logical line's start. The
syntax half rests on f3 alone, so it is consistent rather than confirmed.

**The message wording follows the position, not the statement.** The same malformed text gives `Syntax
error` inside a procedure body (a2, a5) and `Expected: expression` in the declarations section (a4, a6, a7).
Recorded, not explained.

**Two projects in one group may NOT share a `Name=`.** A `.vbg` whose members have different filenames,
folders, modules and executables, but both `Name="Project1"`, is refused at load:

```
<CRLF>A project with the name 'Project1' is already loaded.<CRLF>A project with the name 'Project1' is already loaded.<CRLF>
```

Exit 1, and **nothing in the group builds**, not even a third member with a distinct name. The control with
distinct names builds both. The comparison is case-insensitive (`Project1` vs `PROJECT1` is refused), and
the name printed is the one loaded second. This is a group-load refusal, not a compile error: no file, no
line, no `Build of … failed.` trailer. It is also the first measurement that `/make` accepts a `.vbg`
directly; the 2026-09-07 probes compiled the two `.vbp` files separately.

**A form and a standard module in one project may NOT share a name either.** `Form=Thing.frm` plus
`Module=Thing` gives, whatever their order in the `.vbp`, and case-insensitively:

```
<CRLF>Name conflicts with existing module, project, or object library<CRLF>
```

Exit 1, no file, no line, and it does not say which of the two conflicted. That is the same message two
`.bas` files sharing a `VB_Name` produce (probe B, 2026-09-07), so forms and standard modules share **one**
per-project, case-insensitive component namespace.

**What resisted explanation:**

- **`Line 0` is ambiguous in the format itself.** a4 and a6 prove 0 is a real index, yet this document
  already records module-scoped errors with no source line that also print `Line 0`. A consumer cannot tell
  "on the first code line" from "no line applies".
- **The group refusal prints exactly twice** — twice with two members, still twice with three members of
  which one collides, twice with the case variant. Not once per member, and not once per collision.
- **Neither refusal carries the `Build of … failed.` trailer**, a file or a line, unlike a compile error.
- **Not measured:** what the interactive IDE does when *opening* such a group (PowerShell Direct cannot see
  the guest desktop); and whether a module-level `Attribute` sitting after code has started is skipped the
  same way. d1 covered only procedure-level attributes directly under their procedure header. The second
  half is answered under [Member attributes](member-attributes-where-vb6-attaches-them-and-what-its-ide-writes.md#member-attributes-where-vb6-attaches-them-and-what-its-ide-writes-2026-10-13-273):
  it is skipped the same way, and so is every physical line of a continued attribute line.

