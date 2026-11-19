### Fixed

- **Class default members given arguments follow the measured early- and late-bound results** (#4747).
  Missing defaults raise 438; Sub and variable defaults execute or report the measured errors, and
  Get-only/Function assignments run the member before reporting 424 or 451, except that a Variant holding a class
  with a default Sub or Function raises 450 without running it. Early-bound writes through
  a Variant-array default check the index without changing the stored array. Object-returning assignments
  remain explicitly not implemented pending measurement.
