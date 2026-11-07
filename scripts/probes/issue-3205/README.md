# #3205: a cancelled folding continuation resumes a detached session

Measured in WSL on 2026-11-06, source baseline `c39ec903` (`upstream/main`). No VB6 measurements.
The final attempt to refresh that ref could not run: this clone has no configured `upstream` remote.
The supplied ref stayed at `c39ec903`; a subsequent #3206 merge could not be checked from this clone.

## Result

This is a client fault, case **(b)** in the issue. A folding timer can finish while its UI continuation
is still queued. Detach cancels the token, but awaiting an already completed delay does not check that
cancellation again. The continuation constructs `CodeLensDisplay`, whose constructor resumes the session.
The second workspace refresh then sends a new lens request, or the resumed session itself does so if the
refresh has already invalidated the answer. Waiting for the first refresh to complete cannot fix that.

In `main-loaded-suspension-timeline.txt`, the first case records:

| Time (ms) | Event |
| --- | --- |
| 9580.056 | First workspace refresh sent |
| 9585.259 | Its client lens request starts |
| 9591.373 | Server completes the request |
| 9592.298 | Client completes the request |
| 9611.989 | Detach begins |
| 9717.104 | Detach ends |
| 9717.126 | Baseline count read: 3 |
| 9717.129 | Second workspace refresh sent |
| 9720.127 | A new client lens request starts |
| 9734.561 | Assertion count read: 4 |

The extra identity/stack probe (`diagnosis-main-loaded-pass-timeline.txt`) identifies the caller. In its
first case, the session is suspended at 9420.420 ms and detach finishes at 9428.511 ms. Refresh sees
`suspended=True` at 9430.358 ms. At 9434.228 ms, `CodeEditorView.FoldAfterDelayAsync` calls the lens display
constructor and resumes that same session. Its new request reaches the server at 9438.147 ms. This run
happens to pass because the assertion read 3 at 9434.884 ms, before the server received that request.
Thus a passing count alone can miss the client fault. Stack excerpts retain the first four frames per
stack and replace the local checkout path with `<repo>`.

The fix rechecks cancellation immediately after the folding delay, before any session/display access.
`CodeLensTests.LensInCodeWindow("cancelled-fold")` holds precisely that posted continuation, detaches,
invalidates lenses with a refresh, cancels, then releases the continuation on the UI thread. Before the
fix it failed: expected 1 lens request, found 2. The other 12 cases passed; none skipped. The test restores
the original synchronization context before waiting for or running the held continuation.

## Repetitions

Each row is 20 runs of the complete eight-case `EditorRedockTests` class, four workers with disjoint
pairs of CPUs. Loaded runs add one busy Python process per CPU, with the same affinity as their worker.
`single` means #3206's edit-loop change (no awaits between the three inserts), without importing its files.
`results.tsv` records every run, including failures unrelated to the suspension assertion.

| Variant | Load | Suspension failures (class runs / cases) | Other failed cases |
| --- | --- | --- | --- |
| Before, current edit loop | None | 0/20 / 0 | 0 |
| Before, current edit loop | CPU hogs | 10/20 / 10 | 0 |
| Before, single-turn edit | None | 0/20 / 0 | 0 |
| Before, single-turn edit | CPU hogs | 6/20 / 6 | 0 |
| After, current edit loop | None | 0/20 / 0 | 0 |
| After, current edit loop | CPU hogs | 0/20 / 0 | 0 |
| After, single-turn edit | None | 0/20 / 0 | 0 |
| After, single-turn edit | CPU hogs | 0/20 / 0 | 0 |

These counts do **not** reproduce the suggested increase under load from the issue's earlier 19-versus-10
sample. They do not establish that either edit loop changes the probability: the sample is small and
scheduler-dependent. The additional four loaded identity/stack runs all passed, despite recording the
faulty continuation; their much heavier instrumentation is not part of the frequency comparison.

The baseline probe used the clone's cached IDE/Desktop assemblies: a complete rebuild remained in C#
compilation. The test project was rebuilt with only the unrelated `Automation/RunningCodeEditTests.cs`
excluded, because its new `DebugStateResult.Note` assertion needs an updated Desktop assembly. No
`EditorRedockTests` case was excluded. That probe build is **not** a full Integration validation. A brief
8-second Core-only instrumentation build overlapped the middle of the single-turn loaded cohort; this
is another reason not to infer a frequency difference from these counts.

After-fix cohorts used newly rebuilt assemblies and `after-instrumentation.patch`, which instruments only
`EditorRedockTests`: server request start/completion and the client's applied-answer event. No temporary
Core API is needed for those runs. All 80 after-fix runs passed all eight cases (640 passed, none skipped).
The full unit checks ran on CPUs 8–15 while the after-fix workers used 0–7; no unit worker shared a probe
CPU. As with the before cohorts, the frequency counts are observations of these schedules, not an estimate
of a portable failure probability. Requests after the final assertion are from the test's explicit
reattachment immediately afterward.

## Validation

- New deterministic regression: failed before (1 expected request, 2 observed), passed after in the full
  Integration suite. Teeth count: **1**.
- Full Integration: **5,238 passed, 2 skipped, 0 failed**; xUnit report confirms all 5,240 cases and no errors.
  The two skips require Windows GDI and the oracle's Segoe UI font build; neither is claimed as coverage.
- Full `HexIDE.Tests` via `dotnet test`: **6,455 passed, 35 skipped, 1 failed**, 6,491 total
  in 22m43.439s. The xUnit report contains no framework errors and exceeds the 5,955-test CI floor.
  All skips and their reasons are in `unit-skips.tsv`; none is claimed as coverage.
  The only failure was a 30-second timeout in the untouched
  `TheRecordSeesTheProcessTests.A_server_that_goes_away_while_this_client_stops_it_is_not_a_fault`:
  `no disconnection reached the record for 'vb6'`. Its isolated `dotnet test` rerun passed (1 passed,
  no skips, 2.020 seconds). This is not a green full-unit result and no conversation/client changes were
  made to address it. The earlier progress-free unit attempt and the diagnostic console copy were
  cancelled and are not counted as passes. The diagnostic copy was stopped after discovering the two
  runners shared the corpus smoke evidence directory; the final standard-run report covers 35 corpus
  projects with zero failures.
- Desktop Debug: **0 warnings, 0 errors**.
- `check-tree-hygiene.sh`: passed with nine existing warnings; identity scan not configured.
- No VB6 semantics or language coverage status changed. `MISSING_LANGUAGE.md` counts are unchanged.
- Live supervisor check: detach a code window with a folding timer due, send a code-lens refresh while
  detached, and confirm there is no new lens request until reattachment. Re-docking a tool should retain
  the existing labels; editing should still refresh them once after document sync.

Sandbox setup for the full checks: missing already-pinned NuGet dependencies were restored into
`artifacts/nuget-packages` with the read-only user package cache as a fallback, and the NuGet HTTP cache
was also redirected under `artifacts/`. No package references or versions changed. The hang-dump extension
needs `XDG_RUNTIME_DIR` set to a writable, mode-700 directory in the clone; otherwise its named mutex fails
under read-only `/run/user`. The full Integration project was built against the successfully rebuilt
Desktop dependencies (`BuildProjectReferences=false` to avoid concurrent rebuilds), then run with
`--no-build --no-restore`, `--report-xunit` and the ten-minute hang-dump timeout. It included the previously
excluded `RunningCodeEditTests` class. Initial cancelled build/check attempts are not counted as passes.

## Reusing the probe

`instrumentation.patch` is temporary instrumentation against the source baseline, including the optional
single-turn edit loop. It records a monotonic per-case clock, client lens start/completion, server lens
start/completion, refresh calls/replies, detach boundaries, counts, suspension and resumption. Identity
and resumption stacks were added for the four diagnosis runs, after the frequency cohorts.

Do not ship the instrumented product or test file. Save local changes first, apply the patch to the
matching baseline, build Integration, then run the harness. To reuse #3197's harness without retaining a
second copy (as requested by the supervisor):

```sh
git show fix/3197-codex:scripts/probes/issue-3197/repeat.py > scripts/probes/issue-3205/repeat.py
git apply scripts/probes/issue-3205/repeat.patch
python3 scripts/probes/issue-3205/repeat.py main single
python3 scripts/probes/issue-3205/summarize.py
```

The runner strips `/mnt/` PATH entries. `REDOCK_RUNS` overrides 20, and `REDOCK_LOADED_ONLY=1` selects just
the loaded condition. Labels ending in `single` select the single-turn edit. Keep the resulting summary
and representative timelines here; raw logs, XML and private worker binaries go to disposable
`artifacts/issue-3205/`. Remove the reconstructed `repeat.py` afterward. No #3197 probe file is duplicated
in this change.
