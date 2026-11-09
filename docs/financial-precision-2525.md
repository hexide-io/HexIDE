# Cash-flow precision (#2525)

The measurement source is [issue #2525](https://github.com/hexide-io/HexIDE/issues/2525),
also recorded in the measuring author's [oracle page](vb6-fidelity-oracle/npv-irr-and-mirr.md).
Native and p-code agree. This note records calculations against those measurements, not new
VB6 measurements. No compiler binary was examined.

Run `python3 -B scripts/probes/financial-2525/probe.py` from the repository root.
It uses only Python's standard library and the existing #2523 rational/rounding helpers.
Inputs are binary64. Retained intermediates use 64- or 113-bit significands, nearest/even;
fractional powers use Decimal at 100 decimal digits before rounding. Expected bits are used
only for comparison, never to calculate a result.

## NPV

Three summation forms are tested: independently calculated powers, cumulative multiplication,
and reverse Horner evaluation. Each has six grouped spill sites (base, power, term, sum,
residual, iterate); the last two affect only IRR. At both precisions, cumulative multiplication
with only the power spilling to Double matches all three successful rows. Masks 2, 18, 34
and 50 describe that same NPV model. The base `1 + rate` stays extended; each cumulative
power rounds to Double; terms and summation stay extended until the returned Double.
Independent powers' nearest candidate produces `409C45757D31FA35` for the first row,
seven ULPs from `409C45757D31FA2E`. Horner's nearest candidate produces
`409C45757D31FA30` and `407E197C0E9E19C6`, versus measured
`409C45757D31FA2E` and `407E197C0E9E19C5`.

The implementation uses the shared `FinancialNumber` for retained intermediates and Double
spills for the cumulative powers. It matches the three exact Double results and both error-5
rows (rate -1 and an unallocated Double array). Matching these rows does not prove a unique
model for every input. Other array element types, scalar/Null/String/Date conversions, empty
or multidimensional arrays, named calls, argument-count/missing-argument errors and extreme
arithmetic domains remain explicitly unimplemented.

## IRR — refused

The probe tries the Rate family requested in the supervisor's comment: a secant pair starting
at Guess and Guess/2, stopping when the absolute residual is below 1e-7, up to 40 iterations.
Each of the three NPV residual forms runs with all 64 grouped spill masks at both precisions
(384 candidates). None matches any of the three successful rows. The closest aggregate
candidate is Horner, mask 9 (base and sum spills), at either precision:

| Call | Measured | Candidate |
|---|---|---|
| IRR(a) | `3FC875EFF170437B` | `3FC875EFF17028E0` |
| IRR(a, 0.2) | `3FC875EFF1704153` | `3FC875EFF170444A` |
| IRR(b) | `3FB62D72209FBD49` | `3FB62D7220A36D0D` |

For independently powered and cumulatively multiplied residuals, the closest aggregate mask
is 45 (base, term, sum, iterate spills), producing `3FC875EFF17028DC`,
`3FC875EFF170444E`, `3FB62D7220A36D0C` respectively at either precision.
No result or guessed error is shipped: IRR refuses with HexIDE's not-implemented error.
The all-positive error-5 row remains part of the unimplemented function.

## MIRR — refused

The probe calculates positive and negative NPV separately, compounds the positive NPV to
its terminal value, divides by the negative NPV times `1 + financeRate`, then takes the
`1/(count-1)` power and subtracts one. It varies the three NPV forms and 256 grouped spill
masks, at both precisions (1,536 candidates). Sites are base, cumulative/independent power,
term, sum, terminal power, denominator, ratio and root. None matches both successful rows.
The closest candidate at both precisions is Horner, mask 84 (term, terminal power and ratio
spills):

| Call | Measured | Candidate |
|---|---|---|
| MIRR(b, 0.1, 0.12) | `3FB94260ED237997` | `3FB94260ED23798E` |
| MIRR(a, 0.1, 0.12) | `3FC477233C8903B7` | `3FC477233C8903B7` |

The first result misses by nine ULPs. MIRR refuses with HexIDE's not-implemented error;
its measured error-11 and error-5 rows remain part of the unimplemented function.
Further arithmetic models or iteration choices are still needed; these searches are not
an exhaustive proof that no matching model exists.

## Verification

The final 20 new tests all fail against main's three runtime source files, then run against
the restored implementation. Five pin the NPV measurement rows (three exact Double patterns
and two error-5 results). Eight cover explicit refusal of every measured IRR/MIRR call;
seven cover unsupported NPV calls. Refusal tests establish no VB6 result or error number.

For the supervisor's live editor check, type `NPV(`, `IRR(` and `MIRR(` and confirm the
parameter lists and Double labels appear. IRR and MIRR remain unavailable at execution.
No live IDE verification was performed in this Linux run.

The full runtime run passed 8,462, skipped 14 and failed none (7m26s); the xUnit report
records zero hidden errors. All 20 new tests pass and none skips. Both the prebuilt and
rebuilt MissingLanguageDocTests runs pass all 18 cases after the guard's recount. Intrinsic
Dies/Partial moves 10/69 to 9/70; overall 80/479 to 79/480. The inventory remains 1,185 names.
Tree hygiene passes with nine existing nonblocking warnings. NuGet auditing was disabled
for these runs because its global HTTP cache is read-only in this sandbox; the temporary
HTTP cache is outside the source tree. Windows PATH entries were removed before every run.

The 14 skipped runtime cases provide no coverage for this change:

- Windows filesystem: `FileSystemTests.CurDir_uses_only_the_first_drive_character`,
  `Windows_name_rows`, `Windows_error_rows`, `Windows_numeric_rows`, `Windows_attribute_rows`.
- Missing VB6 templates: `Vb6TextFileTests.The_one_corpus_file_with_high_bytes_round_trips`;
  `ClassHeaderPreservationTests.Instancing_and_data_binding_settings_survive`,
  `A_real_vb6_class_round_trips_byte_for_byte`,
  `A_renamed_module_retargets_VB_Name_but_keeps_everything_else`;
  `ContainerHierarchySaveTests.RoundTrip_OfVb6sOwnNestedTemplates_ReproducesTheBeginStructure`;
  `MenuHierarchySaveTests.Vb6sOwnMenuTemplates_AreNoLongerHeldReadOnly`,
  `RoundTrip_OfVb6sOwnMenuTemplates_ReproducesTheMenuShape`.
- Opt-in oracle: `Vb6OracleRoundTripTests.HexIDE_output_still_builds_in_vb6`.
- Child-only probe: `UiThreadTests.Child_ReportsTheUiThreadsStack` (only runs through its parent test).
