### Fixed

- **String comparisons with Decimal and Variant String comparisons with Currency, Boolean or Date now run** (#211), following the 43 measured VB6 rows. Decimal compares as a number held in a Variant; a Variant String against typed Currency, Boolean or Date converts when possible and sorts above the number otherwise.
