# Text-compare orders of the double-byte code pages

Every character of Windows code pages 932, 936, 949 and 950, sorted the way VB6's text compare sorts them. Each code
page was sorted under the locale that uses it and under en-GB. #5105 maps VB6's text order locale by locale, so that
HexIDE can follow it on every host. These files are what was measured, not tables HexIDE loads: the format and the
loader are #5105's to decide.

The findings drawn from them are in
`docs/vb6-fidelity-oracle/text-compare-beyond-latin-1-width-kana-and-the-double-byte-locales.md`, "Each double-byte
code page in full".

## The files

| File | Code page | Sorted under | `GetUserDefaultLCID` | Characters | Groups |
|---|---|---|---|---|---|
| `ja-JP.cp932.txt` | 932 | ja-JP | 1041 | 7,483 | 7,134 |
| `en-GB.cp932.txt` | 932 | en-GB | 2057 | 7,483 | 7,104 |
| `zh-CN.cp936.txt` | 936 | zh-CN | 2052 | 21,885 | 21,581 |
| `en-GB.cp936.txt` | 936 | en-GB | 2057 | 21,885 | 21,566 |
| `ko-KR.cp949.txt` | 949 | ko-KR | 1042 | 17,142 | 16,837 |
| `en-GB.cp949.txt` | 949 | en-GB | 2057 | 17,142 | 16,571 |
| `zh-TW.cp950.txt` | 950 | zh-TW | 1028 | 13,587 | 13,434 |
| `en-GB.cp950.txt` | 950 | en-GB | 2057 | 13,587 | 13,432 |

**The format.** UTF-8, one line per group, in ascending order.
- **The first line** is the program's own header: the user locale it ran under and the number of characters.
- **A group** is the characters that compare equal under text compare. Its members are separated by tabs, in
  code-point order.
- **No character in any set** is a tab or a line break.

## Which characters

For each code page:
- printable ASCII but the space (`!` to `~`);
- for 932, the half-width katakana its single bytes `A1` to `DF` give;
- every two-byte sequence, lead byte `81` to `FE` and trail byte `40` to `FE` but `7F`, that the code page decodes to a
  single character.

Decoding was by .NET Framework's `Encoding.GetEncoding(codepage)` in the guest, with exception fallbacks, so an
undefined sequence was skipped.

**Left out:** characters in the private use area (U+E000 to U+F8FF), the code pages' end-user-defined ranges.
- 932: 1,880 sequences.
- 936: 2,149.
- 949: 188.
- 950: 6,217.

## How they were made

VB6.EXE 6.00.9782 (SP6) in a clone of the oracle guest, Windows 10 build 19044.1288 with MSVBVM60 6.00.9848,
2026-11-23.
- **Build:** one `Sub Main` program, made with `/make` as native code and as p-code.
- **Run:** each build ran in session 0, once per code page and locale, after `Set-Culture` set the user locale.
- **The two builds wrote the same file, byte for byte, in all eight runs.** These files are those outputs, with
  their CRLFs made LF and the UTF-16 made UTF-8.

The program reads the characters, sorts them with a stable merge sort on `StrComp(a, b, vbTextCompare)`, and writes
each group of equals on its own line:

```vb
If StrComp(a(j), a(i), vbTextCompare) < 0 Then
    t(k) = a(j): j = j + 1
Else
    t(k) = a(i): i = i + 1
End If
```

The input was in code-point order and the sort is stable, so a group's members stay in code-point order. Adjacent
characters were put in one group when `StrComp(a(i), a(i - 1), vbTextCompare)` was 0.
