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

To run the corpus tests locally:

```
cd IDE && dotnet test HexIDE.Runtime.Tests --filter "FullyQualifiedName~SerializationCorpusTests"
```

There is no test category or trait to filter on — `[Trait]` is not used anywhere in `IDE/`, so filter by
type name.

The corpus is discovered rather than configured. `HEXIDE_ROUNDTRIP_CORPUS` (a `;`-separated list of
directories) replaces it outright; otherwise the roots are the VB98 template tree — `VB6_TEMPLATES`, or
`C:\Program Files (x86)\Microsoft Visual Studio\VB98\Template` — plus the repository's own `demo/`. CI is
Linux and has no VB6, so there the corpus is `demo/` alone: HexIDE's own output, which proves nothing about
fidelity. Read the report, not the pass.

The run writes a per-file report to `hexide-roundtrip-report.txt` in the system temp directory, listing
every `DIFF`, `BLOB-LOSS` and `BLOB-DIFF` with the first few differing lines. That is the full report — the
JSON report and the `--roundtrip` / `--corpus` / `--report` flags described here previously do not exist;
`HexIDE.Standalone` implements exactly one non-GUI mode, `--check <project.vbp>`, which loads a project and
prints per-module results.

---

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
