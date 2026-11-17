# #4375 measurement — 2026-11-17

Measured in WSL (Ubuntu), starting at `upstream/main` commit `603771b09`, with this run's counter added.
Scope: the [supervisor's measurement-only decision](https://github.com/hexide-io/HexIDE/issues/4375).
Commands and the reporting hook are in [README.md](README.md). These are measurements of HexIDE, not new
measurements of VB6. No instance slots were freed and no language behaviour was changed.

| Run | Passed | Skipped | Failed | Non-forced terminated-instance accesses |
|---|---:|---:|---:|---:|
| Runtime, full (including `BattleshipChallenge.RunBattleship`) | 9043 | 20 | 0 | 0 |
| Runtime integration, full | 2988 | 2 | 0 | 0 |
| `BattleshipFormsDemoTests` | 1 | 0 | 0 | 0 |
| IDE, full (including the in-tree corpus) | 9148 | 68 | 0 | 0 |

The full IDE run completed all **9216** cases in **26m 04s**. It includes both `ProjectSmokeRunnerTests`
and `ProjectCorpusSmokeTests`, which performs the actual in-tree corpus iteration. All **36** corpus
projects passed the smoke harness: 21 ran to the time limit, ten refused and five ran to completion.
This is startup smoke coverage, not a claim that every project ran to completion. No extra corpus/template
roots were configured.

The IDE count aggregates **46** processes: the parent, 36 corpus workers and nine synthetic startup workers.
The parent supplied the only process-exit count. The harness kills still-running workers after observing
their result or time limit.
Their streamed hit records were included before aggregation. There were **no non-forced hit sites**.
A live replacement reached through a predeclared form name would not count as an access to the terminated
predecessor; no terminated predecessor was observed being accessed.

A preliminary full run exposed the missing provider-inventory row for the temporary probe. The runner now
maintains and restores that row; the corrected full rerun passed the inventory guard. Both source inventories
were restored byte-for-byte, and no temporary probe or backup files remain.

The Runtime report aggregates three test-host processes. Runtime integration and Battleship forms each
started and finished one host. All four final xUnit reports passed
`check-xunit-report.sh`, with no dropped test errors. Skipped tests are named in
[skipped-tests.txt](skipped-tests.txt); they are not coverage. The Runtime skips cover Windows-specific
behaviour, absent VB6 templates/oracle opt-in, and a child-process-only entry point. Runtime integration's
two skips require Windows GDI and the oracle's Windows Segoe UI font build. The IDE run skipped 68 cases;
the same list names each of them explicitly.

There are no non-forced hit sites to classify as a missed holder or VB6-permitted access.

## Diagnostic tests and teeth

All 14 `TerminatedMemberAccessTests` cases pass. Disabling the increment produced 13 failures out of 14;
disabling the hook exemption separately made `NormalRun_AndTerminationHookAccesses_AreNotCounted` fail.
Both mutations were restored. Coverage includes direct API and source method calls, field reads/writes,
Property Get/Let/Set, CallByName, implicit fields, class/form hook exemptions and the first-20 retention limit.
The exact implicit-field assertion also distinguishes fields from the shared Debug proxy.

The Runtime run records 39 intentionally forced diagnostic accesses, labelled `forced` and excluded from
program totals. [forced-sites.json](forced-sites.json) records every distinct forced site and its test.
These fixtures deliberately hold a stale object through the internal API; they are neither discovered
reference-count bugs nor claims about what VB6 allows.

## Other checks and limits

`MissingLanguageDocTests`: 20 passed, no skips or failures. No language-support row or count moved.
Tree hygiene passed (existing attribution/profile warnings only). No refusal was removed, and the test-project
search for #4375 found no pre-existing refusal pins to update. No changelog fragment or user-facing string
was added, as requested by the latest issue comment.

After verification, 48 verified untracked `bin`/`obj` directories inside this clone were cleared. `dotnet clean`
could not finish because Antlr's clean target tried to remove the shared JRE cache outside the clone;
that cache was left untouched, and only the verified clone-local outputs were removed.

The #2067 third-party sweep is **uncounted**: it requires a container or throwaway VM. Even a zero across
all in-tree runs does not satisfy that prerequisite for step 2. Slot freeing remains a later run.
