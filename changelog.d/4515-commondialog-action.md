### Fixed

- **CommonDialog Action assignments convert as a VB6 Integer** (#4515), preserving overflow, Null and type-mismatch errors before rejecting values outside 1–6 with error 380. Integer/Long values 1–6 still open their dialogs; other types landing on those actions remain refused pending measurements.
