# FormatDateTime

Provenance: [#2516](https://github.com/hexide-io/HexIDE/issues/2516), measured on vb6.exe SP6
on an en-GB VM, compiled both native and p-code with identical output. These are the issue’s
measurements, transcribed here; no new oracle run was made for the implementation.

The function returns a String. General date (omitted or 0) uses CStr’s omission of a midnight
time or a zero date. Formats 1–3 use the locale’s long date, short date and long time;
4 writes 24-hour hours and minutes. The VM’s regional settings write long dates without a weekday.
Null as the date returns an empty String, Empty uses serial zero, and True uses serial -1.
The second argument converts as CInt does, including rounding 2.6 to 3 and reading numeric text.
Out-of-range formats raise 5, invalid text raises 13, and Null as the format raises 94.
The complete measured matrix follows (blank result with String and error 0 means an empty String).

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
