## InStr and InStrRev at the ends (2026-10-23, #1567)

Measured with `/make` on vb6.exe 6.00.8176 (the release build) with the MSVBVM60 6.00.9848 (SP6) runtime, native and p-code identical. InStr was measured with no compare, with 0 and with 1, and all three agree on every row. HexIDE's answers before the fix are in the last column.

| Call | vb6.exe | HexIDE before |
|---|---|---|
| `InStr(1, "abc", "")`, `InStr(3, "abc", "")` | 1, 3 | 1, 3 |
| `InStr(4, "abc", "")` | **4** | 0 |
| `InStr(5, "abc", "")`, `InStr(100, "abc", "")` | **5, 100**: an empty string2 is found at start, however far past the end | 0 |
| `InStr(1, "", "")`, `InStr(2, "", "")` | 0: an empty string1 holds nothing, an empty string2 included | 0 |
| `InStr(4, "abc", "a")`, `InStr(5, "abc", "c")` | 0 | 0 |
| `InStr("abc", "")`, `InStr("", "")` | 1, 0 | 1, 0 |
| `InStrRev("abc", "")`, `InStrRev("abc", "", 2)` | 3, 2 | **4, 3** |
| `InStrRev("abc", "", 5)`, `InStrRev("abc", "", 5, 1)` | 0 | 4 |
| `InStrRev("abc", "c", 4)` | **0**: a start past the end is 0, even where the text is there to find | 3 |
| `InStrRev("", "")` | 0 | 0 |

`InStrRev("abc", "")` was 4 because .NET's `LastIndexOf` of an empty string answers one past the index it is given; that is also why every empty-string row of InStrRev was one too high.

