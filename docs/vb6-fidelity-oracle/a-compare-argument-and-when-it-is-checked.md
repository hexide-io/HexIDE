## A compare argument, and when it is checked (2026-10-27, #881)

**The question.** #881 measured `InStr`, `StrComp`, `Split` and `Replace` with a compare of 0, 1, 2, -1, 1033 and 3000:
- 2 is 5 in InStr and StrComp, and a text compare in Split and Replace;
- -1 and 3000 are 5 in all four;
- 1033 is a text compare.

Left open were `InStrRev`, and whether each function checks the compare on a path that needs no comparison, such as an
empty string. A helper shared by the functions has to know that timing.

**The probe.** Two programs, each call under `On Error Resume Next`, logging the result's type and value, the number of
elements for `Split`, or the error. Native and p-code agreed on every row.

**InStrRev reads compare as Split and Replace do:**

| `InStrRev("abc", "B", -1, c)` | 0 | 1 | 2 | -1 | 3 | 4 | 1033 | 3000 |
|---|---|---|---|---|---|---|---|---|
| vb6.exe | 0 | 2 | **2: text** | 5 | 2 | 2 | 2 | 5 |

**3 and 4** compare as text in all five functions: `InStr(1, "abc", "B", c)` is 2, `StrComp("a", "A", c)` 0, `Split`
gives 2 elements and `Replace` "a-b".

**When the compare is checked.** Each row was run with c = 2, -1 and 3000. Where a row's results differ by c, the cell
says so. "5" means error 5.

| Call | vb6.exe |
|---|---|
| `InStr(1, "", "", c)`, `InStr(1, "", "x", c)` | 0, no error |
| `InStr(1, "abc", "", c)`; `InStr(5, "abc", "", c)` | 1; 5, no error |
| `InStr(1, Null, "x", c)` | Null, no error |
| `InStr(4, "abc", "x", c)`, `InStr(5, "abc", "x", c)`, `InStr(2, "abc", "x", c)`, `InStr(1, "abc", "abcd", c)`, `InStr(1, "a", "a", c)` | **5**, every c. With c = 0 or 1, these give 0, 0, 0, 0 and 1. |
| `StrComp("", "", c)`, `StrComp("", "a", c)`, `StrComp("a", "", c)` | c = 2: **5**. c = -1 or 3000: 0, -1 and 1, no error |
| `StrComp("a", "a", c)` | 5, every c |
| `StrComp(Null, "a", c)` | Null, every c |
| `InStrRev("", "x", -1, c)`, `InStrRev("abc", "", -1, c)`, `InStrRev("abc", "x", 5, c)` | c = 2: 0, 3 and 0. c = -1 or 3000: **5** |
| `Split("", ",", -1, c)`, `Split("abc", ",", 0, c)`, `Split("abc", "", -1, c)` | c = 2: 0, 0 and 1 elements. c = -1 or 3000: **5** |
| `Replace` of `""`, of an empty find, with count 0, and from start 5 | c = 2: "", "abc", "abc" and "". c = -1 or 3000: **5** |

So the four functions check at different points:
- **InStrRev, Split and Replace** check the compare before anything else, on every path. A value they refuse is 5 even
  for an empty string or a count of 0.
- **InStr** returns early without checking it when string1 is Null or empty, or string2 is empty. On any other path it
  is checked, even where no comparison could match, as with a start past the end or a find longer than the string.
- **StrComp** refuses 2 at once. It checks a negative compare or an invalid LCID only when both strings are non-empty.

**What a compare that is not a whole number reads as.** These were run in InStr, Split and StrComp:
- 0.4 and -0.4 are binary;
- 0.6 is text;
- 1.5 and 2.5 are read as 2: 5 in InStr and StrComp, text in Split. That fits banker's rounding. Rounding half up
  would make 2.5 into 3, a text compare in InStr, which is not what came back.
- The String "1" is text, and Null is **94** in all three.
