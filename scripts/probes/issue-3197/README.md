# Editor redock timing (#3197)

Measured on Linux/WSL, 2026-11-06, from upstream/main c3b7d243.
This reproduces the reported failure shape; it does not establish the timing of
the original Windows CI failure, for which no timeline was recorded.

The editor uses the **300 ms** debounce in
`IDE/HexIDE/Forms/ViewModels/LspDocumentSession.cs`.
The issue's suggested 400 ms `ChangeCoalescer` is for filesystem notifications,
not editor synchronization.

## Forced failure

Temporary instrumentation records each edit and each server-received message on
one monotonic stopwatch. Stretching the first 50 ms delay to 650 ms sends an
early sync. That stretch alone passed all eight cases: their assertions could
run before the second sync. Adding a temporary 650 ms observation wait after
the edit loop lets both syncs arrive before the original exact assertion.

All **8/8** cases then failed at the first lens assertion, with
“Expected server.Count("textDocument/codeLens") to be 2, but found 3.”
For the issue's `codeLens, answersPending=False` case,
[forced-timeline.txt](forced-timeline.txt) records the following milliseconds,
relative to the first edit:

| Event | Time (ms) |
|---|---:|
| Edit 0 | 0.0 |
| didChange | 301.3 |
| diagnostic | 301.3 |
| codeLens | 301.4 |
| Edit 1 | 654.2 |
| Edit 2 | 705.5 |
| didChange | 1005.0 |
| diagnostic | 1005.0 |
| codeLens | 1005.0 |

There are two syncs, two diagnostic pulls, and two lens requests after the
baseline. This supports cause (a), rather than a duplicated lens for one sync.
The test change removes the yields between inserts. Both exact lens assertions
remain unchanged. Product code is unchanged.

## Reproduction

On a scratch checkout of c3b7d243, apply
[instrumentation.patch](instrumentation.patch) and build Integration.
The patch is temporary tracing, not part of the shipping test:

- `REDOCK_STRETCH=1`: first delay and final observation wait are 650 ms.
- `REDOCK_SINGLE_TURN=1`: no awaits between the three edits; with the stretch
  switch it still waits 650 ms **after** the burst.
- With neither switch, the original three 50 ms delays remain.

Run `python3 scripts/probes/issue-3197/repeat.py` against that instrumented
assembly. It copies the test output into four isolated directories under
`artifacts/issue-3197/`, runs twenty complete eight-case classes per condition,
and records xUnit XML, console output, timelines and exit codes. Four runners
use disjoint CPU pairs. Loaded runs add two Python CPU hogs on each runner's
pair; idle runs have no synthetic load. The script terminates its own hogs.
PATH entries under /mnt are removed. This is a Linux probe using CPU affinity,
not a cross-platform test runner.

The `after` condition selects the same no-yield edit loop as the final test
through the temporary environment switch. The full Integration run separately
checks the final test with all instrumentation removed.

## Separate failure found by repetitions

The unforced class also intermittently fails its **later** assertion,
“a suspended document must not request lenses through a detached view”,
with expected 3, found 4, in the first `codeLens, answersPending=False`
case. It occurs with and without the single-turn change.
[after-idle-timeline.txt](after-idle-timeline.txt) preserves one such trace:
the three edits yield **one** didChange, one diagnostic and one lens;
further lens requests occur in the subsequent explicit workspace refresh
and detach checks. The probe does not record the refresh/detach timestamps,
so it cannot establish the cause of that separate failure. This run does not
claim to fix it or to establish whether its fault is in the test or client.

With the single-turn switch, the forced probe under load passed **8/8** cases
(no skips), versus **0/8** before. In
[after-forced-loaded-timeline.txt](after-forced-loaded-timeline.txt), edits are
at 14979.4, 15041.8 and 15059.5 ms; didChange is at 15441.2 ms, diagnostic at
15442.5 ms, and codeLens at 15511.9 ms. The later lenses at 15738.7 and
16018.9 ms belong to the explicit refresh and reattachment checks.
The before trace under the same load is
[forced-loaded-timeline.txt](forced-loaded-timeline.txt): edits at 17868.1,
18631.3 and 18698.1 ms produce didChange/diagnostic/lens groups at
18282.5/18282.9/18325.4 and 19006.2/19006.3/19007.0 ms.
These eight existing assertions have teeth against the induced split burst;
this is not a claim that eight newly added regression tests fail on main.

## Unforced reproduction and repetition counts

Crucially, CPU contention also reproduced the issue **without stretching any
delay**: `before-loaded-08` failed the first lens assertion in the exact
`codeLens, answersPending=False` case. Its
[unforced-loaded-timeline.txt](unforced-loaded-timeline.txt) shows:

| Event | Stopwatch (ms) |
|---|---:|
| Edit 0 | 14251.4 |
| Edit 1 | 14488.6 |
| didChange | 14885.8 |
| diagnostic | 14894.2 |
| Edit 2 | 15135.1 |
| didChange | 15138.0 |
| diagnostic | 15153.2 |
| codeLens | 15223.5 |
| codeLens | 15282.8 |

The second nominal 50 ms delay took 646.5 ms. A second loaded baseline
(`before-loaded-06`, semanticTokens/full, answersPending=False) reached the
later folding assertion with three syncs and three new lenses, expected 2,
found 4; its [timeline](unforced-folding-timeline.txt) is also retained.
No unforced single-turn case failed either edit-burst assertion.

Each repetition runs all eight cases. All repetitions had zero skips and zero
xUnit errors. [results.tsv](results.tsv) records every run.

| Variant | Class runs failed / run | Cases passed | Cases failed | First lens assertion failures | Folding assertion failures | Suspension assertion failures |
|---|---:|---:|---:|---:|---:|---:|
| Before, no synthetic load | 5 / 20 | 155 | 5 | 0 | 0 | 5 |
| Before, CPU hogs | 11 / 20 | 148 | 12 | 1 | 1 | 10 |
| Single turn, no synthetic load | 7 / 20 | 153 | 7 | 0 | 0 | 7 |
| Single turn, CPU hogs | 14 / 20 | 141 | 19 | 0 | 0 | 19 |

These numbers establish the targeted split-burst fix, **not** a green class:
the later suspension assertion remains intermittent and needs separate
measurement of refresh, detach and request completion. The run leaves that
assertion intact.

## Final-source checks

- Full Integration, with tracing removed: **5229 passed, 2 skipped, 0 failed,
  total 5231**, in 9m 37s. `check-xunit-report.sh` confirmed 5231 tests and no
  xUnit errors. All eight EditorRedockTests cases passed in that full run.
- Integration's two skips are `ToolbarCaptionTests.Windows_caption_width_follows_installed_fonts_Gdi_extent`
  (requires Windows GDI) and
  `ToolbarCaptionTests.Segoe_UI_nine_point_matches_console_measurements`
  (requires the oracle's Windows font build). Neither is coverage in this run.
- Desktop Debug build: **0 warnings, 0 errors**.
- Tree hygiene: passed, with nine existing non-blocking attribution warnings.
- PATH entries under /mnt were removed before tests. A clone-local NuGet cache
  was needed because the sandbox cannot write the default package cache.
  Integration's first HangDump startup failed on the read-only default runtime
  directory before any tests ran; setting `XDG_RUNTIME_DIR` inside the clone
  allowed the full run with its 10-minute quiet-time hang timeout.
- This is a test-only change: per the latest issue comment, no changelog
  fragment. LSP contracts and MISSING_LANGUAGE counts do not change.
- Full IDE suite (`HEXIDE_FOREIGN_LSP_DOWNLOAD=0`): **6455 passed, 35 skipped,
  1 failed, total 6491**, in 25m 31s. The sole failure was
  `RealServerShutdownTests.CppClosesAndExits`: clangd was killed after its exit
  wait (137 instead of 0). Its isolated recheck passed **1/1**, no skips.
  The full-suite failure remains a failure; no client/shutdown code was changed.
- The IDE suite's [35 skipped definitions](skipped-definitions.tsv) are
  recorded from discovery (29) and the six Windows-only `SkipUnless`
  declarations. They require Windows filesystem/process/type-library behaviour,
  a Windows made-program launch, or VB6 templates. They are not coverage here.
