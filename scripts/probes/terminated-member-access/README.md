# Terminated-instance access count (#4375)

This is the measurement step requested by the [latest issue decision](https://github.com/hexide-io/HexIDE/issues/4375).
It does not free slots or change a program's result. The interpreter counts accesses on the actual `VbObject`,
so a live replacement reached through a predeclared form name is not a hit on its terminated predecessor.
Its own termination hook, including helpers called by that hook, is exempt. Reference-count guards and
skipped event sinks are not member accesses. Debugger inspection is excluded.

Run from the repository root, with a fresh report directory for each invocation:

```sh
HEXIDE_TERMINATED_ACCESS_REPORT="$PWD/artifacts/4375/runtime" \
  bash scripts/probes/terminated-member-access/run.sh IDE/HexIDE.Runtime.Tests/
HEXIDE_TERMINATED_ACCESS_REPORT="$PWD/artifacts/4375/runtime-integration" \
  bash scripts/probes/terminated-member-access/run.sh IDE/HexIDE.Runtime.Integration.Tests/ \
    -- --report-xunit --report-xunit-filename 4375.xml
HEXIDE_TERMINATED_ACCESS_REPORT="$PWD/artifacts/4375/battleship-forms" \
  bash scripts/probes/terminated-member-access/run.sh IDE/HexIDE.Integration.Tests/ \
    -- --filter-class '*BattleshipFormsDemoTests' --report-xunit --report-xunit-filename 4375.xml
HEXIDE_TERMINATED_ACCESS_REPORT="$PWD/artifacts/4375/ide" \
  bash scripts/probes/terminated-member-access/run.sh IDE/HexIDE.Tests/
```

The last invocation runs the full IDE suite, including `ProjectSmokeRunnerTests` and the actual in-tree
corpus iteration in `ProjectCorpusSmokeTests`. Do not set `HEXIDE_ROUNDTRIP_CORPUS` or `VB6_TEMPLATES` to
third-party projects: the #2067 sweep requires the isolation specified on that issue and is not this probe.

The script temporarily copies `Probe.cs` into the selected test project and removes it on exit. Where the
project has a guarded `ProviderTestSort.md` source inventory, it temporarily adds the probe's `provider`
row (the label measured by `ProviderTestSortTests`) and restores the original file on exit. It always builds
the copied hook (`--no-build` is reported and ignored), so an old assembly without the hook cannot silently
report zero. It strips
the Windows PATH entries and exports `UseSharedCompilation=false`. The hook records each hit immediately
in a per-process JSON-lines file, with xUnit's test name and (for corpus workers) the saved project path.
This preserves hits even when the smoke harness kills a worker at its time limit. A start record with no
hits is zero through that worker's observation window; it does not assert a clean exit. Normal test-host
exit also prints the count. The script aggregates all worker hits and prints distinct sites at the end,
including when the suite fails. Deliberate accesses in `TerminatedMemberAccessTests` are labelled `forced`
and excluded from program totals. The interpreter itself retains only the first 20 sites per instance of
the interpreter, while counting all hits. A source line of zero denotes an internal API call with no VB
statement frame; source-executed accesses carry their current VB line.

Raw output under `artifacts/` is disposable. Keep the measured totals and any distinct sites in the result
posted to the issue and in `results.md` beside this file. The third-party sweep remains uncounted, so a zero
here alone does not authorise freeing slots.
