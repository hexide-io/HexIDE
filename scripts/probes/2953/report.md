# Recursion depth bisect (#2953, item 2)

Measured 2026-11-03 in WSL, Ubuntu 26.04.1 x64, .NET SDK 10.0.114,
Microsoft.NETCore.App 10.0.14. **Debug throughout. No product code changed.**
The latest [issue decision](https://github.com/hexide-io/HexIDE/issues/2953)
asks for this investigation; item 1 is #2955 and item 3 awaits direction.

## Result

The same probe completes **263** levels at `4dfa1624` (2026-09-03, near the
requested September 2 baseline) and **108** at `85d64b98` (the supplied
`upstream/main`). Both endpoints repeated identically in a second process:
four readings of 263 and four of 108. That is a **155-level loss (59%)** on this
fixed host and configuration. It is cumulative, not one regression.

The largest isolated step is **`3b67e5d3` (#1056), 220 → 187, a loss of 33**.
It adds asynchronous form loading to the shared variable-read/member paths.
The second largest is **`c59dbc98` (#1543), 154 → 145, a loss of 9**;
it wraps procedure/array calls to support indexing a call's result.
Several smaller steps and many differences at or below three levels make up
the rest. Those smaller differences are not individually significant here.

These are **HexIDE host measurements**, not new VB6 oracle measurements.
September's Windows 1 MB figures in `interpreter-gaps.md` did not state a build
configuration, and used other recursive shapes. This run neither reproduces
nor disproves those historical figures, nor establishes a Windows or macOS
regression ratio. It measures the same `Down` shape across revisions in Debug.

## Probe and reproduction

`Program.cs` runs this body through `BaseVBTestFixture.Run`:

```vb
On Error Resume Next
Debug.Print Down(n)
Debug.Print Err.Number
Function Down(n)
  If n <= 0 Then
    Down = 0
  Else
    Down = Down(n - 1) + 1
  End If
End Function
```

The top-level `n` is replaced by a literal on each attempt. Binary search brackets
0 (must succeed) and 1024 (must raise 28), finding the largest completing argument.
Each attempt creates a **fresh fixture on a fresh 4,194,304-byte thread**.
Success requires both the expected returned number and Err.Number 0; only 28 counts
as a depth failure. Unexpected errors fail the probe. Each process searches twice.
The probe is a console investigation, **not a test added to the Runtime suite**.
It calls the fixture directly rather than copying its interpreter setup.

From a clean branch in this checkout:

```sh
export PATH="$(printf '%s' "$PATH" | tr ':' '\n' | grep -v '^/mnt/' | paste -sd:)"
python3 scripts/probes/2953/measure.py 4dfa1624 85d64b98
python3 scripts/probes/2953/measure.py --steps 4dfa1624 upstream/main
python3 scripts/probes/2953/summarize-frames.py
```

`measure.py` checks out revisions **in the same clone**, restores the starting
branch in `finally`, builds Debug, and runs the built DLL in a separate process.
It directs ANTLR's runtime directory and NuGet's writable package/HTTP caches under
`artifacts/2953`, with the installed package cache as a read-only fallback. This
avoids the sandbox's read-only home-cache failures; no dependency pins are changed.
It captures .NET JIT output for `MoveNext` in the probe process only.
`--steps` resumes from the committed TSV cache; it does not rerun cached points.
To remeasure all recorded successful revisions, pass their SHAs explicitly:

```sh
python3 scripts/probes/2953/measure.py $(awk -F '\t' '$4 == 0 { print $1 }' scripts/probes/2953/measurements.tsv | sort -u)
```

The surviving evidence is `measurements.tsv` (full SHA, date, the two
`(last success, first failure)` pairs, exit status) and `frames.tsv`
(short SHA, source method, JIT prologue reservation in bytes). The initial two
failed historical restores remain visible as empty measurements with status 1;
the successful retries supersede them. Disassembly and verbose build logs are
scratch output under `artifacts/`; no conclusion depends on keeping them. The first
broad pass recorded depth only; frame captures cover the later narrowing points
and the explicit endpoint/boundary reruns, not every depth sample.

## Search scope and limits

The broad pass sampled 15 first-parent points from early September to main.
Intervals with a loss greater than three levels were recursively split until
adjacent commits, or until each remaining interval lost at most three levels.
The lower reading selects an interval when repeats differ; both readings remain
in the evidence and tables. The observed spread was at most three levels.
Small cumulative losses and compensating improvements can therefore remain
inside an unsplit interval: **this is not a measurement of every commit**.

The brief calls batches merge commits. In this clone they are **single-parent
squashes** (`git show -s --format='%h %p'`), including `85d64b98` and batch 87
`37e9dfd3`. There is no second-parent history to bisect within them. The largest
isolated steps below predate batching and have individually measurable commits.
The later batch intervals subdivide into losses of at most three levels at the
sampled boundaries; no individual batch above the threshold was isolated.
An internal PR-by-PR measurement of those squashes would require their original
commits, which are not reachable from this clone's refs. No synthetic rollback
was used as a substitute for measuring history.

## Why the biggest frames grew

The numbers below are **the stack reservation in each JIT prologue**, excluding
the saved frame pointer, return address, and intervening visitor/async machinery.
They are not the total stack bytes per VB call. An async state machine's hoisted
fields are on the heap in Debug; these figures measure the actual `MoveNext`
stack frame, including the JIT's spills and temporaries.

At #1056, comparing its direct parent `11fc7f54` with `3b67e5d3`:

| Hot `MoveNext` method | Before bytes | After bytes | Increase |
|---|---:|---:|---:|
| `StatementExecutor.VisitLetStmt` | 3936 | 5552 | 1616 |
| `ExpressionExecutor.ResolveImplicitCall` | 1680 | 2496 | 816 |
| `ExpressionExecutor.EvaluateProcedureOrArrayCall` | 1552 | 2256 | 704 |

The source replaces synchronous `TryGetVariable` calls with `TryReadNow` plus
`await ReadVariableAfter` and tuple deconstruction, adding `found`/`need`/value
locals. It also awaits `Loading` for form members, with tuple state and returned
`formWindow` pattern variables. `VisitLetStmt` adds these paths for the target,
leading member, and object field, plus an awaited member-chain read. These paths
need not execute to enlarge a Debug method's prologue reservation. The recursive
RHS is evaluated while the enlarged Let and call-resolution frames remain live.
The measured 33-level loss and the frame sizes are observations; attribution of
the increase to these added awaits/temporaries is from the adjacent source diff.
This is not a controlled experiment assigning a byte cost to each added local.

#1543 adds `EvaluateProcedureOrArrayCall` as an async wrapper around the renamed
`EvaluateCallOrElement`. The one-group path used by `Down(n - 1)` still awaits
that helper, so both frames stay live. Argument loops also gain `beforeLast`
callbacks and awaits for the evaluation order of later index groups. Comparing its direct parent `240eccf4` with `c59dbc98`:

| Hot `MoveNext` method | Before bytes | After bytes |
|---|---:|---:|
| `ExpressionExecutor.EvaluateProcedureOrArrayCall` | 3584 | 576 |
| `ExpressionExecutor.EvaluateCallOrElement` | absent | 3888 |
| `ExpressionExecutor.ResolveCallArgs` | 736 | 864 |
| `StatementExecutor.VisitLetStmt` | 6848 | 6688 |

The smaller wrapper does not mean the live call path shrank: wrapper and helper
now coexist, before counting the extra async call machinery. A 160-byte
reduction in Let does not offset that cost.

Across the complete measured range, `VisitLetStmt` grows from **3344 to 10624**
bytes, `ResolveImplicitCall` from **1520 to 4544**, and `RunProcedure` from
**1216 to 3408**. Procedure/array evaluation starts as a single **1104**-byte
method and ends as the **576**-byte wrapper plus a **5840**-byte helper.
These measurements explain why focusing only on `Vb6Value` size or VB local
allocation would miss much of the accumulated stack cost.

At the next largest step, #1005 (`2beb0268` → `c933fbdd`, 242 → 234),
`RunProcedure` grows **1472 → 1792** bytes. The diff adds the `returned` value
pattern, checks for typed/Enum/Variant returns, and value copies in the return
path. `VisitVsAdd` grows **352 → 480**, `VisitVsMinus` **288 → 416**, and
`EvaluateProcedureOrArrayCall` **1104 → 1200** as typed/Variant provenance is
carried through the expression and result paths. Again these are measured
whole-method reservations, not a per-local cost model.

The smaller steps show the same two mechanisms: new live wrappers (for example,
`EvaluateScalar` in #840), and larger shared methods even when the new branch is
not taken (for example, the refusal's interpolated string in #792 and Collection
branches in #1487). This report identifies candidates for item 3; it does not
choose a stack size, remove fidelity behaviour, or propose unmeasured VB6 results.

## Isolated steps greater than three levels

Every before/after pair here is adjacent on first-parent history. Depths show
both searches; the drop column uses their lower readings for consistency with
the search. #792 is borderline: its repeat ranges permit a drop as small as three.

| Before | Commit introducing step | Depth before | Depth after | Drop | Change |
|---|---|---:|---:|---:|---|
| `26c88e7b` | `4078f0e8` | 261, 261 | 255, 258 | 6 | #792: refusal of Let from an object/control |
| `9ed6e2b2` | `2047857e` | 260, 260 | 253, 253 | 7 | #840: scalar/default-member reading |
| `35d3bfc1` | `24195833` | 253, 253 | 248, 248 | 5 | #971: block error handling |
| `27a9fef2` | `e69077fa` | 248, 248 | 242, 242 | 6 | #1000: array-copy and reference-release paths |
| `2beb0268` | `c933fbdd` | 242, 242 | 234, 234 | 8 | #1005: typed/Variant arithmetic |
| `672bf7ca` | `2d605550` | 232, 232 | 225, 225 | 7 | #1027: bare form members |
| `a6ea38e0` | `ceb38ee6` | 225, 225 | 221, 221 | 4 | #1044: event-handler execution |
| `11fc7f54` | `3b67e5d3` | 220, 220 | 187, 187 | 33 | #1056: default form instances and loading |
| `e078c28c` | `8984e0e7` | 186, 186 | 182, 182 | 4 | #1078: form execution fixes |
| `972c9abe` | `10dab7c2` | 171, 171 | 164, 164 | 7 | #1487: Collection support |
| `c76c9ab2` | `85afcfa9` | 163, 163 | 159, 159 | 4 | #1496: As New |
| `240eccf4` | `c59dbc98` | 154, 154 | 145, 145 | 9 | #1543: indexing a call result |

## Every successfully measured revision

The raw TSV preserves failed attempts and endpoint reruns as well. Each entry
below gives the two largest successful arguments from the last process at that
revision. Both searches also checked that the next argument raises Error 28.
Dates are the commits' dates; all measurements were made on November 3.

| Revision | Commit date | Search 1 | Search 2 |
|---|---|---:|---:|
| `4dfa1624` | 2026-09-03 | 263 | 263 |
| `ee966883` | 2026-09-06 | 262 | 262 |
| `a5b87664` | 2026-09-13 | 262 | 262 |
| `9d767799` | 2026-09-22 | 262 | 262 |
| `4056698d` | 2026-09-22 | 262 | 262 |
| `87d2de25` | 2026-10-05 | 262 | 262 |
| `e1e7b89a` | 2026-10-12 | 261 | 261 |
| `26c88e7b` | 2026-10-12 | 261 | 261 |
| `4078f0e8` | 2026-10-12 | 255 | 258 |
| `ee0e5c0f` | 2026-10-12 | 255 | 258 |
| `3fe85c6f` | 2026-10-12 | 255 | 258 |
| `f08af9d8` | 2026-10-13 | 260 | 260 |
| `9ed6e2b2` | 2026-10-13 | 260 | 260 |
| `2047857e` | 2026-10-13 | 253 | 253 |
| `ab65d7f5` | 2026-10-13 | 252 | 252 |
| `c37d7231` | 2026-10-13 | 253 | 253 |
| `35d3bfc1` | 2026-10-13 | 253 | 253 |
| `24195833` | 2026-10-14 | 248 | 248 |
| `27a9fef2` | 2026-10-14 | 248 | 248 |
| `e69077fa` | 2026-10-14 | 242 | 242 |
| `2beb0268` | 2026-10-14 | 242 | 242 |
| `c933fbdd` | 2026-10-14 | 234 | 234 |
| `38fefc4e` | 2026-10-14 | 234 | 234 |
| `672bf7ca` | 2026-10-14 | 232 | 232 |
| `2d605550` | 2026-10-14 | 225 | 225 |
| `f1dd3e96` | 2026-10-14 | 225 | 225 |
| `e51f47ba` | 2026-10-14 | 225 | 225 |
| `a6ea38e0` | 2026-10-14 | 225 | 225 |
| `ceb38ee6` | 2026-10-14 | 221 | 221 |
| `fc011617` | 2026-10-14 | 221 | 221 |
| `11fc7f54` | 2026-10-14 | 220 | 220 |
| `3b67e5d3` | 2026-10-14 | 187 | 187 |
| `6431572c` | 2026-10-14 | 187 | 187 |
| `5c171e30` | 2026-10-14 | 187 | 187 |
| `f511be12` | 2026-10-15 | 187 | 187 |
| `e078c28c` | 2026-10-15 | 186 | 186 |
| `8984e0e7` | 2026-10-15 | 182 | 182 |
| `6ebd6be4` | 2026-10-15 | 180 | 180 |
| `e9dda25e` | 2026-10-19 | 177 | 177 |
| `c05c9c20` | 2026-10-21 | 176 | 176 |
| `0998a8d0` | 2026-10-22 | 174 | 174 |
| `9985c241` | 2026-10-22 | 171 | 171 |
| `972c9abe` | 2026-10-22 | 171 | 171 |
| `10dab7c2` | 2026-10-23 | 164 | 164 |
| `fed41e36` | 2026-10-23 | 164 | 164 |
| `c76c9ab2` | 2026-10-23 | 163 | 163 |
| `85afcfa9` | 2026-10-23 | 159 | 159 |
| `19e295cd` | 2026-10-23 | 157 | 157 |
| `c97b300d` | 2026-10-23 | 156 | 156 |
| `240eccf4` | 2026-10-23 | 154 | 154 |
| `c59dbc98` | 2026-10-23 | 145 | 145 |
| `2e63c120` | 2026-10-23 | 145 | 145 |
| `8cd7f1f8` | 2026-10-23 | 145 | 145 |
| `8bab9984` | 2026-10-23 | 145 | 145 |
| `598d8849` | 2026-10-23 | 145 | 145 |
| `95d5c746` | 2026-10-24 | 145 | 145 |
| `6e43592f` | 2026-10-24 | 143 | 143 |
| `0a78a01a` | 2026-10-25 | 140 | 140 |
| `01783e94` | 2026-10-26 | 137 | 137 |
| `c208b2d7` | 2026-10-26 | 136 | 136 |
| `420c212b` | 2026-10-27 | 133 | 133 |
| `fa2e94a0` | 2026-10-27 | 132 | 132 |
| `fea85ac1` | 2026-10-27 | 129 | 129 |
| `f47c3c88` | 2026-10-27 | 128 | 128 |
| `d9a4959a` | 2026-10-27 | 126 | 126 |
| `22df964a` | 2026-10-27 | 123 | 123 |
| `37e9dfd3` | 2026-10-27 | 120 | 120 |
| `f3e81429` | 2026-10-28 | 118 | 118 |
| `4ef7aaa3` | 2026-10-28 | 118 | 118 |
| `fc76e0e4` | 2026-10-28 | 115 | 115 |
| `c49f11aa` | 2026-10-28 | 113 | 113 |
| `af4c6015` | 2026-10-29 | 111 | 111 |
| `fce69645` | 2026-11-02 | 109 | 109 |
| `85d64b98` | 2026-11-03 | 108 | 108 |

**74 distinct revisions measured successfully.** No probe search skipped.

## Validation on the restored task branch

Windows PATH entries were removed before every build/test.

| Check | Passed | Skipped | Failed |
|---|---:|---:|---:|
| Full `dotnet test HexIDE.Runtime.Tests/` | 8014 | 13 | 0 |
| `MissingLanguageDocTests` | 6 | 0 | 0 |
| `ChangelogFragmentTests` | 3 | 0 | 0 |

`bash scripts/check-tree-hygiene.sh` passes, with nine existing nonblocking
warnings; `git diff --check` passes. MISSING_LANGUAGE's Function and
Function...End Function Detail notes link this investigation; no status or count
moved. No new VB6 measurement was used, so no oracle page was created or edited.

**New regression tests / teeth: 0 / not applicable.** This deliverable changes no
product behaviour and deliberately adds no probe as a test. Existing tests pass;
they are not claimed as new regression coverage. All 78 successful probe processes
completed both searches without skipping.

The 13 existing Runtime skips provide no coverage here. They require Windows
filesystem behaviour, a VB6 Template tree, or the opt-in VB6 round-trip oracle:

- `ClassHeaderPreservationTests.A_real_vb6_class_round_trips_byte_for_byte`
- `ClassHeaderPreservationTests.A_renamed_module_retargets_VB_Name_but_keeps_everything_else`
- `ClassHeaderPreservationTests.Instancing_and_data_binding_settings_survive`
- `ContainerHierarchySaveTests.RoundTrip_OfVb6sOwnNestedTemplates_ReproducesTheBeginStructure`
- `FileSystemTests.CurDir_uses_only_the_first_drive_character`
- `FileSystemTests.Windows_attribute_rows`
- `FileSystemTests.Windows_error_rows`
- `FileSystemTests.Windows_name_rows`
- `FileSystemTests.Windows_numeric_rows`
- `MenuHierarchySaveTests.RoundTrip_OfVb6sOwnMenuTemplates_ReproducesTheMenuShape`
- `MenuHierarchySaveTests.Vb6sOwnMenuTemplates_AreNoLongerHeldReadOnly`
- `Vb6OracleRoundTripTests.HexIDE_output_still_builds_in_vb6`
- `Vb6TextFileTests.The_one_corpus_file_with_high_bytes_round_trips`
