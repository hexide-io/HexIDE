### Fixed

- **A control carried as text takes its TabIndex at form load by the same rule as every other control** (#4884). A repeated or out-of-range number is renumbered with the rest of the form, nested carried controls included, where the form used to keep all its saved numbers. Only the number in each carried `TabIndex` line changes; the rest of the block is kept.
