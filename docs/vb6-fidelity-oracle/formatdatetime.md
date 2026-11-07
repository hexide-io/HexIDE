## FormatDateTime (2026-10-28, #2516)

Measured on vb6.exe SP6, every row built native and as p-code, which logged the same lines. The oracle VM is an en-GB machine: `Format(1234.5, "Currency")` is `£1,234.50` and a date reads `28/10/2026`.

**What the rows say:**
- The result is a String. Null gives `""` with no error, and Empty is the zero date. `"abc"` is 13.
- `NamedFormat` 0, vbGeneralDate, drops the time when it is midnight and the date when it is the zero date, as `CStr` does.
  1 is the long date, 2 the short date, 3 the long time and 4 `hh:mm`, 24-hour, with no seconds.
- `NamedFormat` is converted as CInt converts (2.6 is 3), and a numeric String is taken. 5 and -1 are 5, `"x"` is 13,
  and Null is 94.
- A number is a date serial (45000.5 is `15/03/2023 12:00:00`), and so is True (-1, `29/12/1899`).

| Expression | Result | Type | Err |
|---|---|---|---|
| `locale` | CStr(#10/28/2026 14:05:09#): [28/10/2026 14:05:09] | String | 0 |
| `FormatDateTime(#2026-10-28 14:05:09#)` | `28/10/2026 14:05:09` | String | 0 |
| `FormatDateTime(#2026-10-28 14:05:09#, 0)` | `28/10/2026 14:05:09` | String | 0 |
| `FormatDateTime(#2026-10-28 14:05:09#, 1)` | `28 October 2026` | String | 0 |
| `FormatDateTime(#2026-10-28 14:05:09#, 2)` | `28/10/2026` | String | 0 |
| `FormatDateTime(#2026-10-28 14:05:09#, 3)` | `14:05:09` | String | 0 |
| `FormatDateTime(#2026-10-28 14:05:09#, 4)` | `14:05` | String | 0 |
| `FormatDateTime(#2026-10-28#)` | `28/10/2026` | String | 0 |
| `FormatDateTime(#2026-10-28#, 0)` | `28/10/2026` | String | 0 |
| `FormatDateTime(#2026-10-28#, 1)` | `28 October 2026` | String | 0 |
| `FormatDateTime(#2026-10-28#, 2)` | `28/10/2026` | String | 0 |
| `FormatDateTime(#2026-10-28#, 3)` | `00:00:00` | String | 0 |
| `FormatDateTime(#2026-10-28#, 4)` | `00:00` | String | 0 |
| `FormatDateTime(#14:05:09#)` | `14:05:09` | String | 0 |
| `FormatDateTime(#14:05:09#, 0)` | `14:05:09` | String | 0 |
| `FormatDateTime(#14:05:09#, 1)` | `30 December 1899` | String | 0 |
| `FormatDateTime(#14:05:09#, 2)` | `30/12/1899` | String | 0 |
| `FormatDateTime(#14:05:09#, 3)` | `14:05:09` | String | 0 |
| `FormatDateTime(#14:05:09#, 4)` | `14:05` | String | 0 |
| `FormatDateTime(0)` | `00:00:00` | String | 0 |
| `FormatDateTime(0, 0)` | `00:00:00` | String | 0 |
| `FormatDateTime(0, 1)` | `30 December 1899` | String | 0 |
| `FormatDateTime(0, 2)` | `30/12/1899` | String | 0 |
| `FormatDateTime(0, 3)` | `00:00:00` | String | 0 |
| `FormatDateTime(0, 4)` | `00:00` | String | 0 |
| `FormatDateTime(45000.5)` | `15/03/2023 12:00:00` | String | 0 |
| `FormatDateTime(45000.5, 0)` | `15/03/2023 12:00:00` | String | 0 |
| `FormatDateTime(45000.5, 1)` | `15 March 2023` | String | 0 |
| `FormatDateTime(45000.5, 2)` | `15/03/2023` | String | 0 |
| `FormatDateTime(45000.5, 3)` | `12:00:00` | String | 0 |
| `FormatDateTime(45000.5, 4)` | `12:00` | String | 0 |
| `FormatDateTime(-1.25)` | `29/12/1899 06:00:00` | String | 0 |
| `FormatDateTime(-1.25, 0)` | `29/12/1899 06:00:00` | String | 0 |
| `FormatDateTime(-1.25, 1)` | `29 December 1899` | String | 0 |
| `FormatDateTime(-1.25, 2)` | `29/12/1899` | String | 0 |
| `FormatDateTime(-1.25, 3)` | `06:00:00` | String | 0 |
| `FormatDateTime(-1.25, 4)` | `06:00` | String | 0 |
| `FormatDateTime("2026-10-28")` | `28/10/2026` | String | 0 |
| `FormatDateTime("2026-10-28", 0)` | `28/10/2026` | String | 0 |
| `FormatDateTime("2026-10-28", 1)` | `28 October 2026` | String | 0 |
| `FormatDateTime("2026-10-28", 2)` | `28/10/2026` | String | 0 |
| `FormatDateTime("2026-10-28", 3)` | `00:00:00` | String | 0 |
| `FormatDateTime("2026-10-28", 4)` | `00:00` | String | 0 |
| `FormatDateTime("abc")` |  |  | 13 |
| `FormatDateTime("abc", 0)` |  |  | 13 |
| `FormatDateTime("abc", 1)` |  |  | 13 |
| `FormatDateTime("abc", 2)` |  |  | 13 |
| `FormatDateTime("abc", 3)` |  |  | 13 |
| `FormatDateTime("abc", 4)` |  |  | 13 |
| `FormatDateTime(Null)` |  | String | 0 |
| `FormatDateTime(Null, 0)` |  | String | 0 |
| `FormatDateTime(Null, 1)` |  | String | 0 |
| `FormatDateTime(Null, 2)` |  | String | 0 |
| `FormatDateTime(Null, 3)` |  | String | 0 |
| `FormatDateTime(Null, 4)` |  | String | 0 |
| `FormatDateTime(Empty)` | `00:00:00` | String | 0 |
| `FormatDateTime(Empty, 0)` | `00:00:00` | String | 0 |
| `FormatDateTime(Empty, 1)` | `30 December 1899` | String | 0 |
| `FormatDateTime(Empty, 2)` | `30/12/1899` | String | 0 |
| `FormatDateTime(Empty, 3)` | `00:00:00` | String | 0 |
| `FormatDateTime(Empty, 4)` | `00:00` | String | 0 |
| `FormatDateTime(True)` | `29/12/1899` | String | 0 |
| `FormatDateTime(True, 0)` | `29/12/1899` | String | 0 |
| `FormatDateTime(True, 1)` | `29 December 1899` | String | 0 |
| `FormatDateTime(True, 2)` | `29/12/1899` | String | 0 |
| `FormatDateTime(True, 3)` | `00:00:00` | String | 0 |
| `FormatDateTime(True, 4)` | `00:00` | String | 0 |
| `FormatDateTime(#1899-12-30 00:00:01#)` | `00:00:01` | String | 0 |
| `FormatDateTime(#1899-12-30 00:00:01#, 0)` | `00:00:01` | String | 0 |
| `FormatDateTime(#1899-12-30 00:00:01#, 1)` | `30 December 1899` | String | 0 |
| `FormatDateTime(#1899-12-30 00:00:01#, 2)` | `30/12/1899` | String | 0 |
| `FormatDateTime(#1899-12-30 00:00:01#, 3)` | `00:00:01` | String | 0 |
| `FormatDateTime(#1899-12-30 00:00:01#, 4)` | `00:00` | String | 0 |
| `FormatDateTime(#2026-10-28 14:05:09#, 5)` |  |  | 5 |
| `FormatDateTime(#2026-10-28 14:05:09#, -1)` |  |  | 5 |
| `FormatDateTime(#2026-10-28 14:05:09#, 2.6)` | `14:05:09` | String | 0 |
| `FormatDateTime(#2026-10-28 14:05:09#, Null)` |  |  | 94 |
| `FormatDateTime(#2026-10-28 14:05:09#, "1")` | `28 October 2026` | String | 0 |
| `FormatDateTime(#2026-10-28 14:05:09#, "x")` |  |  | 13 |
