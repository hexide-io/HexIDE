## Format of a number beyond Decimal's range (2026-10-26, #2118)

Measured with vb6.exe SP6, `/make`, native and p-code identical on every row, under en-GB.

### The rule

A fixed or standard mask writes the number out in full. VB6 rounds it to **15 significant digits** and then pads
with zeros to the decimal point, so a `Double`'s 16th and 17th digits are dropped, and so are the exact digits of its
binary value. `2 ^ 100` is exactly 1267650600228229401496703205376, but prints as `1267650600228230000000000000000`.
Grouping, signs and section choice behave as they do for any other number. Scientific notation appears only where the
mask asks for it, as before ([Verified findings](verified-findings.md), "Out-of-decimal-range magnitudes").

### Rows

| Expression | vb6.exe |
|---|---|
| `Format(1.23456789012345E+30, "0.00")` | `1234567890123450000000000000000.00` |
| `Format(-1E+30, "0.00")` | `-1000000000000000000000000000000.00` |
| `Format(1.23456789012345E+30, "#,##0")` | `1,234,567,890,123,450,000,000,000,000,000` |
| `Format(-1E+30, "#,##0")` | `-1,000,000,000,000,000,000,000,000,000,000` |
| `Format(1E+30, "#,##0")` | `1,000,000,000,000,000,000,000,000,000,000` |
| `Format(1.26765060022823E+30, "0")` | `1267650600228230000000000000000` |
| `Format(2# ^ 100, "0")` | `1267650600228230000000000000000` |
| `Format(1.2345678901234567E+30, "0")` | `1234567890123460000000000000000`: rounded at the 15th digit |
| `Format(1E+29, "0.00")` | `100000000000000000000000000000.00` |
| `Format(7.9E+28, "0")` | `79000000000000000000000000000` |
| `Format(1.5E+300, "0")` | `15` and 299 zeros: 301 digits |
| `Format(1.23456789012345E+30, "0.00E+00")` | `1.23E+30` |
| `Format$(1.23456789012345E+30, "0.00")` | `1234567890123450000000000000000.00`, as `Format` |
| `Format(1.23456789012345E+30, "Fixed")` | `1234567890123450000000000000000.00` |
| `Format(1.23456789012345E+30, "Standard")` | `1,234,567,890,123,450,000,000,000,000,000.00` |
| `Format(-1.23456789012345E+30, "#,##0.00;(#,##0.00)")` | `(1,234,567,890,123,450,000,000,000,000,000.00)` |

### What HexIDE did

The numeric engine is built on `decimal`, which can't hold these values, so HexIDE fell back to scientific notation
for every mask: `Format(1E+30, "0.00")` printed `1E+30`. That was recorded as a divergence in Verified findings.
#2118 replaces it with the rule above.
