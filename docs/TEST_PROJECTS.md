# HexIDE Test Project Corpus

This document catalogues the real-world VB6 projects used to verify round-trip serialisation fidelity. The goal is to ensure that loading and saving any project in HexIDE does not corrupt project state — even in the presence of unknown or unmodelled serialisation items.

---

## How to use this corpus

Round-trip fidelity is verified by the `HexIDE.Runtime.Tests` xUnit harness (see `RoundTripTests.cs`). For each project, the harness asserts:

1. **Deserialise succeeds** — no exception, no error-sink entries for known-format files
2. **Re-serialise is parseable** — output of `Serialize()` can be fed back into `Deserialize()` without error
3. **No key loss** — every key present in the original appears in the round-tripped output
4. **No value mutation** — values for preserved keys are identical (modulo deliberate canonicalisation, e.g. path separator normalisation)
5. **`.frx` byte identity** — when no blob-backed properties were modified, the `.frx` output must be byte-for-byte identical to the original

The `.vbp` corpus comparison resolves each parsed `Reference=` path from its own project folder
using Windows path semantics (#2274). Absolute and relative spellings of the same library compare
equally; every other byte, including the rest of the reference line, remains significant. VB6 saves
a same-drive library relative, so a project moved from another drive need not retain its path spelling.
Line order is compared strictly for every project, the VB98 Template tree included: a VB6 save
with no edit keeps every line where it was read, and VB6's own saves of its templates showed it
(#2274). Only confirming the References dialog regroups a save, and the corpus round trip never
does.

To run the corpus tests locally:

```
cd IDE && dotnet test HexIDE.Runtime.Tests/ -- --filter-class "*SerializationCorpusTests"
```

There is no test category or trait to filter on — `[Trait]` is not used anywhere in `IDE/`, so filter by
type name.

The corpus is discovered rather than configured. `corpus/designer` and `corpus/member-attributes` are
required even with `HEXIDE_ROUNDTRIP_CORPUS` (a `;`-separated list of directories). That variable replaces
the default additional roots: the optional VB98 template tree (`VB6_TEMPLATES`, or the standard VB98
`Template` folder) and the required repository `demo/`. Missing required roots fail the lane (#1016).
The two whole-file grammar lanes, the header check and the IDE/server region-parity check always require
both `demo/` and `corpus/designer`, even with an explicit corpus.

Tests that read named Template files instead go through `Vb6Template` (#1440): `VB6_TEMPLATES` may name
the `Template` folder or the one above it. A set variable under which a test's file is not found fails the
test, naming what it tried; only an unset one on a machine without VB6 skips. CI has no VB6 install, but
still reads the repository corpora, including VB6-authored designer and member-attribute files.

The run writes a per-file report to `hexide-roundtrip-report.txt` beside the test assembly
(`AppContext.BaseDirectory`, normally `IDE/HexIDE.Runtime.Tests/bin/Debug/net10.0/`), listing
every `DIFF`, `BLOB-LOSS` and `BLOB-DIFF` with the first few differing lines. That is the full report — the
JSON report and the `--roundtrip` / `--corpus` / `--report` flags described here previously do not exist;
`HexIDE.Standalone` implements exactly one non-GUI mode, `--check <project.vbp>`, which loads a project and
prints per-module results.

The `.vbp` lane writes `hexide-vbp-report.txt` in that same assembly directory. The whole-file grammar
lanes likewise write `hexide-whole-file-grammar-interpreter.txt` beside the runtime test assembly and
`hexide-whole-file-grammar-lsp.txt` beside the LSP test assembly (normally
`LspServer/HexIDE.VbLspServer.Tests/bin/Debug/net10.0/`). Each checkout and each separate
`--artifacts-path` output keeps its own reports; look beside the assembly in the selected output directory
when overriding the build output.

---

## Project smoke checks (#2505, open/save lane)

`ProjectCorpusSmokeTests` in `HexIDE.Tests` opens projects through the real `ProjectService` and saves
scratch copies. It always includes `demo/`, `corpus/designer`, the member-attribute round-trip and
`saved*` evidence directories, and `corpus/inbox-controls/saved`. `HEXIDE_ROUNDTRIP_CORPUS` adds
semicolon-separated roots; a configured root that is missing fails the test. Project discovery is
case-insensitive for `.vbp` extensions, including on Linux.

Run from the repository root:

```sh
HEXIDE_FOREIGN_LSP_DOWNLOAD=0 pwsh scripts/test.ps1 IDE/HexIDE.Tests -- --filter-class 'HexIDE.Tests.Corpus.ProjectCorpusSmokeTests'
```

Each project gets its own scratch copy of its corpus root. The harness compares every copied file
byte for byte after normalising only the original project's reference paths, detecting added and deleted
files too. Same-drive reference paths become relative; other drives and unresolved paths stay as read
([measured in #2274](vb6-fidelity-oracle/a-references-path-and-the-line-order-a-save-writes.md)).
The harness uses the file's path, as HexIDE does today; it does not resolve registered libraries or
substitute library identities. `Retained=` is untouched (#2459). Every other byte must match.
File paths compare case-insensitively on Windows and macOS, ordinally on Linux. A reported save refusal is allowed, but never
excuses changed bytes. The original corpus is untouched. Members outside the copied project's own
directory are refused before saving; symbolic links are rejected. This conservative safety restriction
can refuse projects with shared parent-directory modules that the IDE itself would save.

The JSON report is `TestResults/hexide-project-smoke-report.json` beside the IDE test assembly, with
one result per project: opened, identical/different/refused save, differing relative file paths, expected reference-path `Normalisations`, and
any exception including its stack. Each result's `Evidence` path names a directory beside that report;
for each differing file, `original/` and `saved/` hold its exact bytes before and after saving. A missing
side means the file was added or deleted. Evidence from the preceding sweep is cleared before each run.
The normal `HexIDE.Tests` suite runs this lane, including in CI, whose failure upload includes
`TestResults`. Test diagnostics are developer-facing; no IDE strings change.

The first Linux sweep opened 29 projects without a crash but changed five demo forms. The
[line-by-line audit](project-smoke-save-audit-2505.md) records all 66 differing lines, original and saved:
one trailing ASCII space was added to each block opener. The hand-authored demos now use the recorded
VB6 block-opening whitespace, so the sweep requires byte identity apart from the measured reference-path normalisation above. The same differences
were reproduced with CRLF inputs, which `.gitattributes` already enforces on these files on Linux too.
Three evidence-writing regression cases cover changed, added and deleted files on Linux.

Remaining: bounded startup execution through `HexIDE.Standalone`, Make coverage, and their per-project
results. The open/save lane does not establish the full beta gate's run/make guarantee.

## Corpus coverage targets

The corpus should cover the following axes. Track gaps in the table below.

| Axis | Target | Current coverage |
|---|---|---|
| Project types | EXE, OleDll, OleExe, Control (.ocx) | EXE only |
| Module types | Form, Module, Class, UserControl, PropertyPage | Form, Module |
| References | Projects with many `Reference=` / `Object=` lines | Partial (PD) |
| Project groups | `.vbg` with 2–3 member projects | None |
| VB6 service pack versions | SP3, SP4, SP5, SP6 | Unknown |
| OCX-heavy projects | Multiple third-party OCX dependencies | Krool only |
| Large projects | 20+ forms, 50+ modules | PD only |
| Small/trivial projects | Single form, no references | None |
| Pure code projects | `.bas` / `.cls` only, no forms | None |

---

## Projects

### PhotoDemon
- **URL:** https://github.com/tannerhelland/PhotoDemon
- **Type:** Standard EXE
- **Modules:** Large (50+ forms, many modules)
- **References:** Several — GDI+, various Windows APIs
- **OCX:** Minimal — most UI built at runtime
- **Notes:** Primary test project. Poor designer test case (runtime-built UI) but excellent serialisation stress test due to size and complexity. VB6 SP6.
- **Licence:** BSD
- **Round-trip status:** 🔲 Not yet tested

### Krool Common Controls
- **URL:** https://github.com/Kr00l/VBCCR
- **Type:** ActiveX Control (.ocx)
- **Modules:** Medium
- **References:** Standard — ComCtl32, shell
- **OCX:** Self-referential (tests its own controls)
- **Notes:** Good for OleExe/Control project type coverage. OCX-heavy demo projects exercise the `Object=` deserialisation path.
- **Licence:** LGPL
- **Round-trip status:** 🔲 Not yet tested

---

## Wanted — coverage gaps to fill

The following project types are needed but not yet sourced. If you add a project, verify its licence permits use as a test fixture before committing.

| Gap | Notes |
|---|---|
| Standard EXE, small/trivial | Single form, few controls — good baseline |
| Standard EXE with `.vbg` group | Two or more related projects in a group file |
| ActiveX DLL | COM server, no forms, pure code modules |
| ActiveX EXE | Out-of-process COM server |
| UserControl project | `.ctl` files, OCX output |
| OCX-heavy consumer | Project that references many third-party OCXs (Sheridan, VSFlexGrid, etc.) |
| PropertyPage examples | Any project using `PropertyPage` modules |
| `.res` resource file | Project with an explicit `Resource=` line in `.vbp` |
| SP3/SP4/SP5 headers | Older service pack format variants (header line differences) |

**Good sources to check:**
- GitHub: search `language:"Visual Basic" extension:vbp` 
- SourceForge VB6 archive
- Planet Source Code (partially preserved on archive.org)
- VBForums CodeBank
- CodeProject VB6 articles with attached source

---

## Corpus management rules

1. **Licence first.** Verify licence permits use as a test fixture before adding. When in doubt, a CI fetch script pointing at a public URL is safer than committing source.
2. **Self-contained only.** All referenced files must be present (or the missing reference must be documented and handled gracefully by the harness).
3. **No binaries.** Do not commit compiled `.exe`, `.dll`, or `.ocx` outputs — source only.
4. **Version the results.** Corpus run logs are committed so regressions between runs are visible in git history.
5. **Document failures.** A project that currently fails round-trip is still useful — document the known failure reason so it becomes a tracked regression target.

---

## Known failure modes (to investigate)

| Failure mode | Affected projects | Status |
|---|---|---|
| Unknown `.vbp` keys silently dropped | All — pass-through buckets not yet implemented | 🔴 Blocking |
| `.frx` offset corruption on rewrite | Any project with blob-backed properties | 🔴 Blocking |
| Non-EXE project type serialisation | OleDll, OleExe, Control | 🟡 Partially fixed in Phase 22 |
| `Object=` (OCX) round-trip | Any project with third-party controls | 🔲 Not yet tested |
| `Reference=` MISSING state | Any project with unresolved references | 🔲 Not yet tested |
