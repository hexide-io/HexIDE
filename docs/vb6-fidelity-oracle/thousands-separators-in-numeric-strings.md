## Thousands separators in numeric Strings (2026-10-24, #1446)

Measured with vb6.exe **SP6** (VB6.EXE 6.00.9782, MSVBVM60 6.00.9848), `/make`, on an **en-GB**
guest: decimal point `.`, thousands separator `,`. Native and p-code produced identical results.
The probe called `CDbl(s)`, `CLng(s)`, `Val(s)`, `CCur(s)`, `IsNumeric(s)` and `s + 0` for each
String below under `On Error Resume Next`, logging `TypeName` and value. A failing call skipped its
log line; the following log supplied the still-set `Err`. For `CDbl(",1.5")` the next call overwrote
that number, so only the fact that it raised is known. Types are stated only where the table recorded them.

| String | `CDbl` | `CLng` | `Val` | `CCur` | `IsNumeric` | `s + 0` |
|---|---|---|---|---|---|---|
| `"1.5,2003"` | 1.52003 | 2 | 1.5 | 1.52 | True | Double 1.52003 |
| `"1.5,"` | 1.5 | 2 | 1.5 | 1.5 | True | 1.5 |
| `"1,.5"` | 1.5 | 2 | 1 | 1.5 | True | 1.5 |
| `"1.5,2,3"` | 1.523 | 2 | 1.5 | 1.523 | True | 1.523 |
| `"1.,5"` | 1.5 | 2 | 1 | 1.5 | True | 1.5 |
| `"1,5.5"` | 15.5 | 16 | 1 | 15.5 | True | 15.5 |
| `"12,34.5,6"` | 1234.56 | 1235 | 12 | 1234.56 | True | 1234.56 |
| `",1.5"` | raises; its number was overwritten by the next call's | 13 | 0 | 13 | False | 13 |

The measured findings:

- Once a digit has appeared, commas disappear before parsing, including after the decimal point,
  at the end, and in repeated groups. The original failure, `"1.5,2003"`, therefore reads as 1.52003.
- A comma at the start is refused. The measured error is 13 except for CDbl, whose number was lost.
- `Val` follows a separate prefix rule: its first comma ends the number, giving zero for a leading comma.
- Currency rounds to four decimal places (1.52003 becomes 1.5200, printed as 1.52), while Long
  rounds halves to even (1.5 becomes 2 and 15.5 becomes 16).

HexIDE pins each of the 48 cells in `ThousandsSeparatorConversionTests`. Its locale number readers
remove the program locale's thousands separator after the first digit; `Val` keeps its invariant scanner.

