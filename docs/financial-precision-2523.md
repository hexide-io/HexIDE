# Financial precision investigation (#2523)

The [issue and its supervisor comment](https://github.com/hexide-io/HexIDE/issues/2523) own the
VB6 measurements: SP6, both native and p-code, identical output. This document records calculations
against those measurements, not measurements of VB6 made here. No compiler binary was examined.

## Reproduce

Save the complete issue as Markdown, including its measured table, then run these dependency-free
Python 3 probes from the repository root, passing that file as the last argument:

```sh
python3 -B scripts/probes/financial-2523/probe.py issue-2523.md
python3 -B scripts/probes/financial-2523/composed.py issue-2523.md
python3 -B scripts/probes/financial-2523/rate.py issue-2523.md
python3 -B scripts/probes/financial-2523/selected-rate.py issue-2523.md
```

The probes parse all 41 measured rows. Inputs are binary64, including the division at the call site.
Integer powers use exact rational arithmetic with arbitrary-size integers. Fractional powers and
logarithms use Python Decimal at 100 decimal digits. A second pass rounds intermediates to a
64-bit binary significand, nearest/even; Rate also runs at 113 bits. Selected spills round to
binary64. Every comparison uses the final binary64 bits, never a tolerance.

The first probe enumerates independent spill sites for Pmt/FV/PV/NPer. The composed probe groups
sites by operation, plus payment, balance and interest return boundaries; it does not exhaustively
enumerate every independent operation in IPmt/PPmt. The Rate probe searches 256 spill combinations
for each precision and each of four choices of the second secant iterate. These are candidate
models, not a reconstruction of the runtime's internals.

## Results

| Function | Rows | Matching candidate |
|---|---:|---|
| Pmt | 12 | Round `1+r` to binary64; retain all remaining intermediates until the result. |
| FV | 4 | Same base spill, exact remaining intermediates. |
| PV | 3 | Same base spill, exact remaining intermediates. |
| NPer | 5 | Keep `1+r` extended; spill the denominator `payment*timing + pv*r` before the ratio and logarithms. |
| IPmt | 7 | Spill base and payment; use the beginning-period recurrence described below. |
| PPmt | 4 | Spill base and each division in the composed Pmt-minus-IPmt calculation. |
| Rate | 6 | Secant from guess and guess/2; spill base, residual and iterate; stop at absolute residual below 1e-7. |

These variants are not unique. Pmt, FV, PV and NPer have respectively 8, 128, 26 and 144 matching
spill masks in the exact/high-precision pass, with the same counts in the 64-bit pass. PPmt has
48 matching grouped masks in either pass. No measured row distinguishes retaining 64 bits from
retaining the higher precision used here. The non-integer Pmt row and the logarithmic NPer rows
still require more than binary64 for the selected models; their agreement at both precisions
is not evidence that either reproduces every unmeasured case.

For IPmt, the composed future-value/division model's best candidate matches 6/7 rows: period 2,
beginning timing gives `C050222191C94997`, against measured `C050222191C94996`.
The algebraically equivalent beginning-period recurrence
`(FV(r, per-2, roundedPayment, pv, fv, beginning) - roundedPayment) * r`
(with period 1 special-cased to positive zero) matches all seven. End timing uses
`FV(r, per-1, roundedPayment, pv, fv, end) * r`. Payment and each base spill to binary64.
The zero-rate interest's negative zero and period validation are explicit measured cases.

For Rate, start the secant pair at `guess` and `guess/2`. With base, residual and iterate spills
(mask 193), both 64- and 113-bit runs contain the three measured targets:

| Call | Target | Iteration |
|---|---|---:|
| Rate(48, -200, 8000) | `3F7F8B94477C8BB2` | 10 |
| Rate(48, -200, 8000, 0, 1) | `3F807E14EFFCE5F5` | 10 |
| Rate(48, -200, 8000, 0, 0, 0.5) | `3F7F8B94477C6585` | 23 |

The final supervisor comment supplies the stopping rule: absolute residual below 1e-7.
`selected-rate.py` applies that rule without using expected results in the calculation and matches
all six rows at both 64 and 113 bits. The three successful traces stop at iterations 10, 10 and 23.
The two measured failing nonzero-period calls exhaust 40 iterations; zero periods is rejected.
The implementation keeps that 40-iteration cap, but the measurements do not pin the cap beyond
requiring at least 23 iterations. The threshold and starting pair are fitted to these rows, not
independently measured internal details of VB6.

## Implemented slice and remaining work

The first slice registers Pmt/FV/PV. `FinancialNumber` retains exact rational intermediates for
integer powers, and uses 160-bit roots for fractional powers. The measured 10.5-period payment
uses an exact integer power and a high-precision square root. The Double result is rounded
nearest/even from the rational quotient. All 19 measured rows are interpreter tests. Ten additional cases check explicit refusal of
unmeasured edges. The editor also carries positional signature help; its displayed names do
not enable named runtime calls.

The continuation registers NPer, IPmt, PPmt and Rate with the per-function models above.
NPer uses range-reduced logarithms with 160-bit series intermediates. IPmt retains the rounded
payment boundary and the beginning-period recurrence; PPmt rounds each division in its composed
payment-minus-interest calculation. Rate spills base, residual and each iterate, retaining exact
rational arithmetic elsewhere. Each model was selected because it matches every measured row;
several spill masks match, so unmeasured inputs are not proven. None of these measurements
distinguishes a 64-bit significand from the higher precision used here. All 41 issue rows are
interpreter tests with exact Double bits and measured errors. No expected value was guessed or relaxed.

Named calls, argument-count/missing-required errors, Date and nonnumeric conversions (except the measured
Pmt rate String), Null outside Pmt's rate, singular powers, nonpositive bases and periods with
magnitude above 10,000 refuse as unimplemented. The bound limits exact-power work; it is not a VB6
limit. Overflow and other extreme-domain error rules remain unmeasured. The ordinary numeric
calculation follows the fitted formula; agreement beyond the measured rows is not claimed.

## Verification on 2026-10-28 (Linux)

All 29 new cases failed with financial registration disabled and passed after registration:
17 exact Double patterns, two measured errors, and ten unmeasured-edge refusals. The measured
rows preserve the return subtype in a Variant and run under the oracle's en-GB program locale,
without changing the thread culture.

The full MTP runtime run passed 6,784 cases, skipped 13, failed none, and reported zero xUnit
errors. It ran against stable binaries with the CI floor of 6,270 and took 18 minutes 52 seconds.
The final en-GB test scope was also checked separately: 29 passed, none skipped. The two
MissingLanguageDocTests cases (LF and CRLF) and three existing signature-help wire tests passed.
The coverage inventory moves three functions from Dies to Partial: intrinsic counts 30/56,
overall counts 124/455; the inventory size is unchanged.

The skipped cases supply no coverage for this change:

- Windows filesystem: `FileSystemTests.CurDir_uses_only_the_first_drive_character`,
  `Windows_name_rows`, `Windows_error_rows`, `Windows_numeric_rows`, `Windows_attribute_rows`.
- Missing VB6 Template tree: `Vb6TextFileTests.The_one_corpus_file_with_high_bytes_round_trips`;
  `ClassHeaderPreservationTests.Instancing_and_data_binding_settings_survive`,
  `A_real_vb6_class_round_trips_byte_for_byte`,
  `A_renamed_module_retargets_VB_Name_but_keeps_everything_else`;
  `ContainerHierarchySaveTests.RoundTrip_OfVb6sOwnNestedTemplates_ReproducesTheBeginStructure`;
  `MenuHierarchySaveTests.Vb6sOwnMenuTemplates_AreNoLongerHeldReadOnly`,
  `RoundTrip_OfVb6sOwnMenuTemplates_ReproducesTheMenuShape`.
- Opt-in VB6 oracle disabled: `Vb6OracleRoundTripTests.HexIDE_output_still_builds_in_vb6`.

A supervisor's live editor check should type `Pmt(`, `FV(` and `PV(` and confirm their parameter
lists and Double return labels appear in signature help. No live IDE check was performed here.

## Continuation verification on 2026-10-28 (Linux)

All 26 added tests fail with NPer/IPmt/PPmt/Rate registration disabled: 22 measured rows and
four unmeasured-edge refusals. All 41 measured issue rows then pass, including signed zero,
Double subtypes and all measured errors. The selected Rate probe matches all six rows at
both 64 and 113 bits. The existing signature guard exposed missing tooltips for the newly
registered functions; after adding them, all three parity tests pass. Named runtime calls
remain explicitly unimplemented; the tooltip names describe the positional surface only.

Both LF and CRLF MissingLanguageDocTests pass after changing exactly the counts they named:
intrinsic Dies/Partial 30/56 becomes 26/60, overall 124/455 becomes 120/459. The inventory size
stays 1,184. Tree hygiene passes with nine existing nonblocking warnings.

For the supervisor's live editor check, type `NPer(`, `IPmt(`, `PPmt(` and `Rate(` and confirm
the parameter lists and Double return labels appear. No live IDE check was performed here.

The full runtime suite completed in 14 minutes 21 seconds: 6,809 passed, 13 skipped, one failed,
zero xUnit errors. The failure was the signature tooltip guard, before the tooltip entries were
added during that run; its subsequent focused rerun passes all three tests. The full suite was
not repeated after that source-table correction. No runtime implementation changed during the
full run. The 13 skipped cases are exactly the Windows-filesystem, missing-template and opt-in
oracle cases listed for the first slice above; none supplies coverage for this continuation.
