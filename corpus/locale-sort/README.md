# VB6's text-compare order, locale by locale

What VB6's text compare (`StrComp(a, b, vbTextCompare)`, `Option Compare Text`) does with a set of strings, measured
under one user locale at a time. #5105 maps VB6's text order locale by locale, so that HexIDE can follow it on every
host. These files are what was measured, not tables HexIDE loads. The format and the loader are #5105's to decide,
with the interpreter's move to HexIDE.Basic (see #5105).

The findings drawn from them are in two pages:
- `docs/vb6-fidelity-oracle/the-text-order-in-44-user-locales.md`, "One string set under every locale", for the
  `.sbcs.txt` files;
- `docs/vb6-fidelity-oracle/text-compare-beyond-latin-1-width-kana-and-the-double-byte-locales.md`, "Each double-byte
  code page in full", for the `.cp*.txt` files.

## The format

Every file has the same shape: UTF-8, one line per group, in ascending order.
- **The first line** is the program's own header: the user locale it ran under, as `GetUserDefaultLCID` read it, and
  the number of strings.
- **A group** is the strings that compare equal under text compare. Its members are separated by tabs, in code-point
  order.
- **No string in any file** holds a tab, a line break, a NUL or a C1 control.

## The single-byte set, under 46 locales: `<locale>.sbcs.txt`

One set of 784 strings, sorted under each locale:
- every string of #1567's measured order (`TextCompareMeasurements.FullOrder`): every `Chr(0)` to `Chr(255)` under
  code page 1252, and 64 longer strings such as `"ae"`, `"ch"`, `"co-op"` and `"ss "`. That's 311, once `""`, NUL,
  Tab, LF and CR are left out; #1567's order places those five;
- every character of the upper halves of code pages 1250, 1251, 1253, 1254, 1255, 1256, 1257, 1258 and 874 that the
  first part doesn't already hold, without the C1 controls: 463;
- the digraphs `cs`, `dzs`, `gy`, `ly`, `ny`, `sz`, `ty`, `zs`, `dž` and `dd`. These, with the `ch`, `dz`, `lj`, `nj`
  and `ij` already present, are the 15 that oracle PR #5251 added.

The locales are the 44 of "The Text order in 44 user locales", plus German phone book and traditional Spanish.

**Five strings were sorted but aren't in the files:** the C1 controls U+0081, U+008D, U+008F, U+0090 and U+009D, which
code page 1252 leaves undefined. The repository's hygiene check refuses C1 characters in a tracked file, so they
were removed afterwards. Each was a group of its own in every locale, so each file holds 779 strings and 5 fewer
groups than were measured. #1567's order places all five, under en-GB.

| Locales | LCID read back | Groups in the file (measured: 5 more) |
|---|---|---|
| ar-SA, ca-ES, de-DE, el-GR, en-AU, en-GB, en-US, eu-ES, fr-CA, fr-FR, gl-ES, he-IL, id-ID, it-IT, nl-NL, pt-BR, pt-PT, th-TH, zh-CN, zh-TW | 1025, 1027, 1031, 1032, 3081, 2057, 1033, 1069, 3084, 1036, 1110, 1037, 1057, 1040, 1043, 1046, 2070, 1054, 2052, 1028 | 567, every one in en-GB's order |
| bg-BG, ru-RU, uk-UA | 1026, 1049, 1058 | 567 |
| es-ES, et-EE, fi-FI, hr-HR, hu-HU, ko-KR, lt-LT, lv-LV, pl-PL, ro-RO, sl-SI, sr-Latn-RS, sv-SE, tr-TR | 3082, 1061, 1035, 1050, 1038, 1042, 1063, 1062, 1045, 1048, 1060, 9242, 1053, 1055 | 567 |
| cs-CZ, sk-SK, ja-JP | 1029, 1051, 1041 | 566 |
| da-DK, nb-NO | 1030, 1044 | 568 |
| is-IS | 1039 | 569 |
| vi-VN | 1066 | 564 |
| German phone book (`de-DE_phoneb`) | 66567 | 564 |
| traditional Spanish (`es-ES_tradnl`) | 1034 | 567 |

**The locale:** set by `Set-Culture`. German phone book and traditional Spanish aren't reachable that way, so for
those two the sort LCID was written into the account's `HKCU\Control Panel\International`: `Locale` `00010407` with
`LocaleName` `de-DE_phoneb`, and `0000040A` with `es-ES_tradnl`. That's the route "The Text order in 44 user locales"
records.

**The members of a group** came out in the order of the probe's input, which wasn't code-point order. They were sorted
into code-point order here. The measurement is which strings are equal and the order of the groups.

## The double-byte code pages, in full: `<locale>.cp<codepage>.txt`

Every character of code pages 932, 936, 949 and 950, each sorted under the locale that uses it and under en-GB.

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

**Which characters,** for each code page:
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
- **Run:** each build ran in session 0, once per string set and locale, after the user locale was set.
- **The two builds wrote the same file, byte for byte, in every run:** 8 double-byte and 46 single-byte. These files
  are those outputs, with their CRLFs made LF and the UTF-16 made UTF-8.

The program reads the strings, sorts them with a stable merge sort on `StrComp(a, b, vbTextCompare)`, and writes each
group of equals on its own line:

```vb
If StrComp(a(j), a(i), vbTextCompare) < 0 Then
    t(k) = a(j): j = j + 1
Else
    t(k) = a(i): i = i + 1
End If
```

Adjacent strings were put in one group when `StrComp(a(i), a(i - 1), vbTextCompare)` was 0.

**Checked against earlier measurements:**
- **en-GB's single-byte order** agrees with #1567's `FullOrder` on all 310 strings both hold, with no pair ordered
  differently.
- **The 48 ideographs of the double-byte sample** keep that sample's order.
