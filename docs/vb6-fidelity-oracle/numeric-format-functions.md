# FormatNumber, FormatCurrency and FormatPercent (#2515)

Provenance: [HexIDE #2515](https://github.com/hexide-io/HexIDE/issues/2515) records vb6.exe SP6 on an en-GB VM, built both native and p-code with identical output. Each successful call below returns String. Rounding goes away from zero at a midpoint. Optional decimal counts coerce as CInt; -1 selects two digits. The 256-digit results are observations, not evidence of a general wraparound rule. Named calls, other locales, missing required or surplus arguments, digit counts below -2 or above 99 other than 256, and tri-state inputs outside the listed values were not measured.

| Expression | Result | Type | Err |
|---|---|---|---|
| `FormatNumber(1234.5678)` | `1,234.57` | String | 0 |
| `FormatNumber(-1234.5678)` | `-1,234.57` | String | 0 |
| `FormatNumber(0.5)` | `0.50` | String | 0 |
| `FormatNumber(-0.5)` | `-0.50` | String | 0 |
| `FormatNumber(0)` | `0.00` | String | 0 |
| `FormatNumber(1E+20)` | `100,000,000,000,000,000,000.00` | String | 0 |
| `FormatNumber("123.4")` | `123.40` | String | 0 |
| `FormatNumber(Null)` |  | String | 0 |
| `FormatNumber(Empty)` |  | String | 0 |
| `FormatNumber(True)` | `-1.00` | String | 0 |
| `FormatNumber(CDec("1234.5"))` | `1,234.50` | String | 0 |
| `FormatNumber(0.005)` | `0.01` | String | 0 |
| `FormatNumber(0.015)` | `0.02` | String | 0 |
| `FormatNumber(0.025)` | `0.03` | String | 0 |
| `FormatNumber(2.5, 0)` | `3` | String | 0 |
| `FormatNumber(3.5, 0)` | `4` | String | 0 |
| `FormatNumber(1234567.891)` | `1,234,567.89` | String | 0 |
| `FormatNumber("abc")` |  |  | 13 |
| `FormatNumber(CCur(12.345))` | `12.35` | String | 0 |
| `FormatNumber(#2026-10-28#)` | `46,323.00` | String | 0 |
| `FormatNumber(1234.5678, 0)` | `1,235` | String | 0 |
| `FormatNumber(1234.5678, 3)` | `1,234.568` | String | 0 |
| `FormatNumber(1234.5678, -1)` | `1,234.57` | String | 0 |
| `FormatNumber(1234.5678, 2.6)` | `1,234.568` | String | 0 |
| `FormatNumber(1234.5678, 99)` | `1,234.567800000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000` | String | 0 |
| `FormatNumber(1234.5678, -2)` |  |  | 5 |
| `FormatNumber(1234.5678, 256)` | `1,235.` | String | 0 |
| `FormatNumber(1234.5678, Null)` |  |  | 94 |
| `FormatNumber(0.5, , vbTrue)` | `0.50` | String | 0 |
| `FormatNumber(0.5, , vbFalse)` | `.50` | String | 0 |
| `FormatNumber(0.5, , vbUseDefault)` | `0.50` | String | 0 |
| `FormatNumber(0.5, , 5)` |  |  | 5 |
| `FormatNumber(-1234.5, , , vbTrue)` | `(1,234.50)` | String | 0 |
| `FormatNumber(-1234.5, , , vbFalse)` | `-1,234.50` | String | 0 |
| `FormatNumber(1234.5, , , , vbFalse)` | `1234.50` | String | 0 |
| `FormatNumber(1234.5, , , , vbTrue)` | `1,234.50` | String | 0 |
| `FormatNumber(-0.5, 2, vbFalse, vbTrue, vbFalse)` | `(.50)` | String | 0 |
| `FormatCurrency(1234.5678)` | `£1,234.57` | String | 0 |
| `FormatCurrency(-1234.5678)` | `-£1,234.57` | String | 0 |
| `FormatCurrency(0.5)` | `£0.50` | String | 0 |
| `FormatCurrency(-0.5)` | `-£0.50` | String | 0 |
| `FormatCurrency(0)` | `£0.00` | String | 0 |
| `FormatCurrency(1E+20)` | `£100,000,000,000,000,000,000.00` | String | 0 |
| `FormatCurrency("123.4")` | `£123.40` | String | 0 |
| `FormatCurrency(Null)` |  | String | 0 |
| `FormatCurrency(Empty)` |  | String | 0 |
| `FormatCurrency(True)` | `-£1.00` | String | 0 |
| `FormatCurrency(CDec("1234.5"))` | `£1,234.50` | String | 0 |
| `FormatCurrency(0.005)` | `£0.01` | String | 0 |
| `FormatCurrency(0.015)` | `£0.02` | String | 0 |
| `FormatCurrency(0.025)` | `£0.03` | String | 0 |
| `FormatCurrency(2.5, 0)` | `£3` | String | 0 |
| `FormatCurrency(3.5, 0)` | `£4` | String | 0 |
| `FormatCurrency(1234567.891)` | `£1,234,567.89` | String | 0 |
| `FormatCurrency("abc")` |  |  | 13 |
| `FormatCurrency(CCur(12.345))` | `£12.35` | String | 0 |
| `FormatCurrency(#2026-10-28#)` | `£46,323.00` | String | 0 |
| `FormatCurrency(1234.5678, 0)` | `£1,235` | String | 0 |
| `FormatCurrency(1234.5678, 3)` | `£1,234.568` | String | 0 |
| `FormatCurrency(1234.5678, -1)` | `£1,234.57` | String | 0 |
| `FormatCurrency(1234.5678, 2.6)` | `£1,234.568` | String | 0 |
| `FormatCurrency(1234.5678, 99)` | `£1,234.567800000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000` | String | 0 |
| `FormatCurrency(1234.5678, -2)` |  |  | 5 |
| `FormatCurrency(1234.5678, 256)` |  |  | 5 |
| `FormatCurrency(1234.5678, Null)` |  |  | 94 |
| `FormatCurrency(0.5, , vbTrue)` | `£0.50` | String | 0 |
| `FormatCurrency(0.5, , vbFalse)` | `£.50` | String | 0 |
| `FormatCurrency(0.5, , vbUseDefault)` | `£0.50` | String | 0 |
| `FormatCurrency(0.5, , 5)` |  |  | 5 |
| `FormatCurrency(-1234.5, , , vbTrue)` | `(£1,234.50)` | String | 0 |
| `FormatCurrency(-1234.5, , , vbFalse)` | `-£1,234.50` | String | 0 |
| `FormatCurrency(1234.5, , , , vbFalse)` | `£1234.50` | String | 0 |
| `FormatCurrency(1234.5, , , , vbTrue)` | `£1,234.50` | String | 0 |
| `FormatCurrency(-0.5, 2, vbFalse, vbTrue, vbFalse)` | `(£.50)` | String | 0 |
| `FormatPercent(1234.5678)` | `123,456.78%` | String | 0 |
| `FormatPercent(-1234.5678)` | `-123,456.78%` | String | 0 |
| `FormatPercent(0.5)` | `50.00%` | String | 0 |
| `FormatPercent(-0.5)` | `-50.00%` | String | 0 |
| `FormatPercent(0)` | `0.00%` | String | 0 |
| `FormatPercent(1E+20)` | `10,000,000,000,000,000,000,000.00%` | String | 0 |
| `FormatPercent("123.4")` | `12,340.00%` | String | 0 |
| `FormatPercent(Null)` |  | String | 0 |
| `FormatPercent(Empty)` |  | String | 0 |
| `FormatPercent(True)` | `-100.00%` | String | 0 |
| `FormatPercent(CDec("1234.5"))` | `123,450.00%` | String | 0 |
| `FormatPercent(0.005)` | `0.50%` | String | 0 |
| `FormatPercent(0.015)` | `1.50%` | String | 0 |
| `FormatPercent(0.025)` | `2.50%` | String | 0 |
| `FormatPercent(2.5, 0)` | `250%` | String | 0 |
| `FormatPercent(3.5, 0)` | `350%` | String | 0 |
| `FormatPercent(1234567.891)` | `123,456,789.10%` | String | 0 |
| `FormatPercent("abc")` |  |  | 13 |
| `FormatPercent(CCur(12.345))` | `1,234.50%` | String | 0 |
| `FormatPercent(#2026-10-28#)` | `4,632,300.00%` | String | 0 |
| `FormatPercent(1234.5678, 0)` | `123,457%` | String | 0 |
| `FormatPercent(1234.5678, 3)` | `123,456.780%` | String | 0 |
| `FormatPercent(1234.5678, -1)` | `123,456.78%` | String | 0 |
| `FormatPercent(1234.5678, 2.6)` | `123,456.780%` | String | 0 |
| `FormatPercent(1234.5678, 99)` | `123,456.780000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000%` | String | 0 |
| `FormatPercent(1234.5678, -2)` |  |  | 5 |
| `FormatPercent(1234.5678, 256)` | `123,457.%` | String | 0 |
| `FormatPercent(1234.5678, Null)` |  |  | 94 |
| `FormatPercent(0.5, , vbTrue)` | `50.00%` | String | 0 |
| `FormatPercent(0.5, , vbFalse)` | `50.00%` | String | 0 |
| `FormatPercent(0.5, , vbUseDefault)` | `50.00%` | String | 0 |
| `FormatPercent(0.5, , 5)` |  |  | 5 |
| `FormatPercent(-1234.5, , , vbTrue)` | `(123,450.00%)` | String | 0 |
| `FormatPercent(-1234.5, , , vbFalse)` | `-123,450.00%` | String | 0 |
| `FormatPercent(1234.5, , , , vbFalse)` | `123450.00%` | String | 0 |
| `FormatPercent(1234.5, , , , vbTrue)` | `123,450.00%` | String | 0 |
| `FormatPercent(-0.5, 2, vbFalse, vbTrue, vbFalse)` | `(50.00%)` | String | 0 |
