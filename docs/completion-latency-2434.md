# Bundled completion latency (#2434)

Measured on WSL Ubuntu, 2026-10-27, with Release builds and the probe from
[issue #2434](https://github.com/hexide-io/HexIDE/issues/2434). This changes scheduling and cache reuse,
not VB6 language behaviour or the grammar.

The probe's `--make` output is unchanged: `Big.bas` has 2,107 lines, 300 functions and 72,300 bytes,
alongside `Consts.bas` and `Check.vbp`. Both runs use `WAIT_MODES=alone` and
`HEXIDE_LOG_LEVEL=Debug`. The original scheduling was measured over seven trials (six warm rounds);
the finished change over 21 (20 warm rounds), with the full test/build checks also running.

The probe adaptations are replacing Windows `taskkill` cleanup with `os.kill(pid, 15)` on Linux and,
for the final run, printing the first response's item count after recording its latency. Document text,
request sequence, position and timing are unchanged.

| Measurement (ms) | Original scheduling, with timing logs | Finished change |
|---|---:|---:|
| Probe median, excluding first round | 2,014 | 14 |
| Probe nearest-rank p90 | 2,019 | 24 |
| Probe warm range | 2,013–2,019 | 3–41 |
| Completion queue wait, median (all trials) | 2,008.69 | 0.011 |
| Completion handler, median (all trials) | 0.251 | 1.089 |
| Diagnostics/analysis wall time, range | 2,002–2,056 | 2,000–2,026 |
| Parse thread-pool queue wait, range | 0.066–4.498 | 0.074–8.807 |
| Finished SLL stages, range | 10.9–500.4 | 51.8–376.5 |

Original probe raw completion times: `[2031, 2019, 2014, 2014, 2015, 2013, 2014]`.
Finished change: `[29, 8, 8, 4, 3, 14, 10, 7, 23, 19, 14, 31, 15, 12, 41, 6, 24, 24, 23, 8, 17]`.
Probe output rounds individual times; its median is computed before rounding.

The finished server returns **129 completion items** even while its first analysis is pending: the
existing keywords and built-ins. The original server's expired analyses left no declaration snapshot,
so its responses were empty. An intermediate dispatch-only experiment also returned empty lists;
the final measurement above includes the populated response, not just an empty envelope.

The diagnostics duration includes the budgeted parse and cache refresh, not an additional cost to add
to SLL/LL. The parse budget expires. Stage-start and document-version logging records SLL starting
after that budget has already expired, and no LL starts: substantial time is spent before SLL, not in
a completed LL fallback. These logs do not separate preprocessing from lexing. Expired parses still
run in the background and discard their results, as before this change.

Completion's measured delay was the single worker waiting for analysis, so completion now bypasses
that worker and reads only the last fully built declaration snapshot through a concurrent per-URI
cache. The cache starts with no declarations, making static keywords and built-ins available during
the first parse. Other requests retain their ordering against document changes; diagnostics still
analyse and publish every revision, without debounce or coalescing.

This fixes response latency, not parse throughput. A document with an older snapshot keeps those
names until analysis succeeds. The now-fast probe submits changes faster than sequential diagnostics
can analyse them. Protocol regressions check a cached declaration while analysis is held, static
completion during the first analysis, refreshed names afterwards, and a non-completion request still
waiting. Four new cases fail against the original scheduling/logging; the first-analysis case also
fails independently without keyword availability.

Local raw output, debug logs, tooth runs and check logs are under the ignored `artifacts/2434/` folder.
The supervisor can post this breakdown to the issue; this run does not write to GitHub.

For a live IDE check, open the generated project with the bundled server, type in the middle of
`Big.bas`, and inspect `textDocument/completion` latencies in the Protocol Inspector. Check that replies
stay well under 100 ms median and offer keywords while diagnostics continue arriving. With a smaller
document that has already parsed, add a declaration and check that completion initially uses the old
snapshot, then includes the new name after diagnostics arrives. Formatting must still use the latest
edited text.
