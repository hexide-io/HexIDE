# Fixed number masks beyond Decimal range

Provenance: the supervisor’s measurement comment on hexide-io/HexIDE#2118, supplied in the issue
brief for this change (also reports PR #2125). Measured with vb6.exe SP6 under en-GB;
native and p-code produced identical text. These measurements were not rerun in this Linux clone.

Fixed masks round to 15 significant digits, then fill the remaining integer places with zeros.
This differs from expanding the exact binary Double. Grouping and a negative section still apply.

| Expression | vb6.exe |
|---|---|
| `Format(1.23456789012345E+30, "0.00")` | `1234567890123450000000000000000.00` |
| `Format(-1E+30, "0.00")` | `-1000000000000000000000000000000.00` |
| `Format(1.23456789012345E+30, "#,##0")` | `1,234,567,890,123,450,000,000,000,000,000` |
| `Format(-1E+30, "#,##0")` | `-1,000,000,000,000,000,000,000,000,000,000` |
| `Format(1.2345678901234567E+30, "0")` | `1234567890123460000000000000000`: rounded at the 15th digit |
| `Format(1.5E+300, "0")` | `15` and 299 zeros |
| `Format(1.23456789012345E+30, "Standard")` | `1,234,567,890,123,450,000,000,000,000,000.00` |
| `Format(-1.23456789012345E+30, "#,##0.00;(#,##0.00)")` | `(1,234,567,890,123,450,000,000,000,000,000.00)` |
| `Format(1.23456789012345E+30, "0.00E+00")` | `1.23E+30`, unchanged |

The issue body additionally records `Format(1E+30, "0.00")` as
`1000000000000000000000000000000.00`, measured with SP6 in native and p-code.
General Number, no-mask and named Scientific retain their previous paths. Scaling masks and the
other named numeric formats beyond Decimal range have no new measurements in this brief.
