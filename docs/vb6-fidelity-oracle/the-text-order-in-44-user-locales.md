## The Text order in 44 user locales (2026-11-21, #5105, SP6)

**The question.** #5105 maps the Text order of each locale, so that text compare and sorted lists can follow the user's locale on every host. It asks for each locale to be spot-checked against vb6.exe with #1567's probe. This is that check, for the locales of the shipped language packs and a few more. #1573 measured the first five ("The Text order under other user locales" on `option-compare-text-and-vb6s-text-compare.md`).

**The probe.** It was #1573's, unchanged: the 321 strings of #1567's "The whole Text order", every `Chr(0)` to `Chr(255)`, the empty string and 64 longer strings. They were built with `ChrW` from the measured order in `TextCompareMeasurements.FullOrder`, and sorted by a stable insertion sort on `StrComp(a, b, vbTextCompare)` into groups of equals.

The guest's user locale was set with `Set-Culture` before each run, and the program read `GetUserDefaultLCID` back; that's the number beside each locale below. Built with `/make` on vb6.exe SP6 as native code and as p-code, the two orders were identical in every locale.

**The guest:** Windows 10 21H2, build 19044.1288. Its sort version (`GetNLSVersionEx`) read `0x60305` for en-GB, sv-SE and da-DK. Windows has changed its sort tables between versions, so these rows are this build's.

**23 locales gave en-GB's order exactly**, all 234 groups: ar-SA, bg-BG, ca-ES, de-DE, el-GR, en-AU, en-GB, en-US, eu-ES, fr-CA, fr-FR, gl-ES, he-IL, id-ID, it-IT, nl-NL, pt-BR, pt-PT, ru-RU, th-TH, uk-UA, zh-CN, zh-TW.

**21 differ.** For each one, the table lists the groups that left en-GB's order, found as those outside the longest run of groups still in en-GB's order. Each is shown with the group it now follows. A group shows its members joined by `=`, and a multi-character string is quoted. Where a locale also splits or joins equal strings, that's listed too.

| Locale (LCID) | Groups | What moves |
|---|---|---|
| cs-CZ (1029) | 234 | Ä = ä after ª<br>Ë = ë after E = e<br>"ch" after H = h<br>Ï = ï after I = i<br>Ö = ö after º<br>Š = š after "ss`Chr(32)`"<br>Ü = ü after U = u<br>ÿ = Ÿ after Y = y |
| da-DK (1030) | 235 | Ü = ü after ÿ = Ÿ<br>Æ = æ after Ž = ž<br>Ä = ä after Æ = æ<br>Ø = ø after Ä = ä<br>Ö = ö after Ø = ø<br>Å = å after Ö = ö<br>"aa" = "aa" after Å = å<br>no longer equal: "AE" / Æ; "AE" / æ; "Ae" / Æ; "Ae" / æ; "ae" / Æ; "ae" / æ |
| es-ES (3082) | 234 | "nj" after N = n |
| et-EE (1061) | 234 | Š = š after "ss`Chr(32)`"<br>Z = z after Š = š<br>Ž = ž after Z = z<br>Õ = õ after W = w<br>Ä = ä after Õ = õ<br>Ö = ö after Ä = ä<br>Ü = ü after Ö = ö |
| fi-FI (1035) | 234 | Ü = ü after ÿ = Ÿ<br>Å = å after Ž = ž<br>Ä = ä after Å = å<br>Ö = ö after Ä = ä<br>Ø = ø after Ö = ö |
| hr-HR (1050) | 234 | "ll" after L = l<br>Š = š after "ss`Chr(32)`" |
| hu-HU (1038) | 234 | Ö = ö after Œ = œ = "OE" = "Oe" = "oe"<br>"UE" = "Ue" = "ue" after Û = û |
| is-IS (1039) | 236 | Á = á after "ao"<br>Ð = ð after "dz"<br>É = é after Ë = ë<br>Í = í after "itss"<br>Ó = ó after Œ = œ = "OE" = "Oe" = "oe"<br>Ú = ú after "UE" = "Ue" = "ue"<br>ÿ = Ÿ after Y = y<br>Þ = þ after Ž = ž<br>Æ = æ after Þ = þ<br>Ö = ö after Æ = æ<br>Ø = ø after Ö = ö<br>no longer equal: "AE" / Æ; "AE" / æ; "Ae" / Æ; "Ae" / æ; "TH" / Þ; "TH" / þ; "Th" / Þ; "Th" / þ; "ae" / Æ; "ae" / æ; "th" / Þ; "th" / þ |
| ja-JP (1041) | 233 | \ = ¥ after ¤<br>newly equal: \ = ¥ |
| ko-KR (1042) | 234 | \ after ¥ |
| lt-LT (1063) | 234 | Y = y after Ï = ï<br>Š = š after "ss`Chr(32)`" |
| lv-LV (1062) | 234 | Y = y after "itss"<br>Š = š after "ss`Chr(32)`" |
| nb-NO (1044) | 235 | Ü = ü after ÿ = Ÿ<br>Æ = æ after Ž = ž<br>Ä = ä after Æ = æ<br>Ø = ø after Ä = ä<br>Ö = ö after Ø = ø<br>Å = å after Ö = ö<br>"aa" = "aa" after Å = å<br>no longer equal: "AE" / Æ; "AE" / æ; "Ae" / Æ; "Ae" / æ; "ae" / Æ; "ae" / æ |
| pl-PL (1045) | 234 | Ó = ó after Œ = œ = "OE" = "Oe" = "oe" |
| ro-RO (1048) | 234 | Â = â after "ao"<br>Î = î after "itss" |
| sk-SK (1051) | 234 | Ä = ä after "ao"<br>Ë = ë after E = e<br>"ch" after H = h<br>Ï = ï after I = i<br>Ö = ö after º<br>Ô = ô after Œ = œ = "OE" = "Oe" = "oe"<br>Š = š after "ss`Chr(32)`"<br>Ü = ü after U = u<br>ÿ = Ÿ after Y = y |
| sl-SI (1060) | 234 | Š = š after "ss`Chr(32)`" |
| sr-Latn-RS (9242) | 234 | "ll" after L = l<br>Š = š after "ss`Chr(32)`" |
| sv-SE (1053) | 234 | Ü = ü after ÿ = Ÿ<br>Å = å after Ž = ž<br>Ä = ä after Å = å<br>Ö = ö after Ä = ä<br>Ø = ø after Ö = ö |
| tr-TR (1055) | 234 | Ç = ç after "cop"<br>Ö = ö after Œ = œ = "OE" = "Oe" = "oe"<br>"UE" = "Ue" = "ue" after Û = û |
| vi-VN (1066) | 234 | À = à after ª<br>Ã = ã after À = à<br>Â = â after "ao"<br>"ch" after "cop"<br>È = è after E = e<br>Ë = ë after É = é<br>Ì = ì after I = i<br>Î = î after "itss"<br>Ò = ò after º<br>Õ = õ after Ò = ò<br>Ô = ô after Œ = œ = "OE" = "Oe" = "oe"<br>"TM" = "tm" after T = t<br>Ù = ù after U = u<br>Û = û after "UE" = "Ue" = "ue" |

**What it shows.**
- **Where a locale tailors these characters, vb6.exe's text compare follows it.** Examples:
  - the Nordic letters after Z;
  - Czech and Slovak `ch` after H;
  - Spanish `nj`, Croatian and Serbian `ll`, and Vietnamese and Estonian reorderings;
  - Japanese making `\` equal to `¥`, and Korean putting `\` after `¥`.
- **Most locales share en-GB's order** across code page 1252's characters, Russian, Greek and Chinese among them. Their own scripts' letters weren't in these strings.
- **An alternate sort wasn't reached.** `Set-Culture de-DE_phoneb` and `es-ES_tradnl` set the base cultures, LCIDs 1031 and 3082, with no sort identifier. So German phone book and traditional Spanish order are still unmeasured.

  **Since reached (2026-11-21, #5105).** Writing the sort LCID straight into the guest account's `HKCU\Control Panel\International` reached both: `Locale` `00010407` with `LocaleName` `de-DE_phoneb`, then `0000040A` with `es-ES_tradnl`. The program read `GetUserDefaultLCID` back as 66567 (0x10407) and 1034 (0x40A). #1573's probe, unchanged, was built native and as p-code under each, and the two orders were identical:

  | Sort (LCID) | Groups | What moves, against en-GB |
  |---|---|---|
  | German phone book (66567) | 231 | `Ä = ä = Æ = æ = "ae" = "AE" = "Ae"` after "a-b"<br>`Ö = ö = Œ = œ = "oe" = "OE" = "Oe"` after `Ø = ø`<br>newly equal: `Ä` and `ä` with `Æ`, `æ`, `"ae"`, `"AE"` and `"Ae"`; `Ö` and `ö` with `Œ`, `œ`, `"oe"`, `"OE"` and `"Oe"`; `Ü` and `ü` with `"ue"`, `"Ue"` and `"UE"` |
  | traditional Spanish (1034) | 234 | `"ch"` after "cop"<br>`"nj"` after `N = n`, as in modern Spanish (3082) |

  - **The phone book sorts each umlaut as its two-letter spelling:** `ä` as "ae", `ö` as "oe", `ü` as "ue". The plain de-DE sort (1031) keeps en-GB's order.
  - **Traditional Spanish sorts "ch" as a letter of its own, after "c".** These strings put nothing else between "c" and "d" to show exactly where.

**Not measured:**
- characters outside code page 1252, which is every locale's own script beyond Latin;
- the held-out set and the expansion sweep;
- sorted ListBoxes and ComboBoxes, which compare as `lstrcmpi` does (#5059);
- any Windows build other than this one.

### Each locale's own script (2026-11-21, #5105, SP6)

**The question.** The table above covered only code page 1252's characters, so a locale whose letters lie outside it was checked on Latin-1 alone. This run sorts the characters of the other Windows ANSI code pages under the locales that use them.

**The probe.** 660 strings:
- the empty string;
- `0` to `9`, `a` to `z` and `A` to `Z`;
- every character of the upper halves of code pages 1250, 1251, 1253, 1254, 1255, 1256, 1257, 1258 and 874, decoded to Unicode, without the C1 controls and without repeats;
- 15 digraphs: `ch`, `cs`, `dz`, `dzs`, `gy`, `ly`, `ny`, `sz`, `ty`, `zs`, `lj`, `nj`, `dž`, `dd`, `ij`.

They were built with `ChrW` and sorted as above, by a stable insertion sort on `StrComp(a, b, vbTextCompare)` into groups of equals. VB6 strings are Unicode, so the guest's ANSI code page, 1252, doesn't limit what can be compared.

**The runs:**
- The user locale was set with `Set-Culture`, and `GetUserDefaultLCID` read back the LCID shown beside each locale.
- Built with `/make` on vb6.exe SP6, as native code and as p-code.
- **The two gave the same order in all 21 locales.**

**en-GB** sorts the 660 strings into 468 groups. **4 locales keep that order exactly:** ar-SA, el-GR, he-IL, th-TH. So Arabic, Greek, Hebrew and Thai text sorts the same under those locales as under en-GB, as far as these characters go.

**The rest differ.** As in the table above, a locale's moves are the groups outside the longest run still in en-GB's order, each shown with the group it now follows.

| Locale (LCID) | Groups | What moves |
|---|---|---|
| bg-BG (1026) | 468 | Й = й after Ї = ї |
| cs-CZ (1029) | 467 | Ä = ä after ª<br>Č = č after "cs"<br>"dzs" after "dz"<br>Ė = ė after e = E<br>Ë = ë after Ė = ė<br>"ch" after h = H<br>Ï = ï after İ = ı<br>Ö = ö after º<br>Ŗ = ŗ after Ŕ = ŕ<br>Š = š after "sz"<br>Ü = ü after u = U<br>Ÿ = ÿ after y = Y<br>Ż = ż after z = Z<br>"zs" after Ź = ź<br>newly equal: İ / ı |
| et-EE (1061) | 468 | "dzs" after "dz"<br>Š = š after "sz"<br>z = Z after Š = š<br>Ź = ź after z = Z<br>Ż = ż after Ź = ź<br>"zs" after Ż = ż<br>Ž = ž after "zs"<br>Õ = õ after w = W<br>Ä = ä after Õ = õ<br>Ö = ö after Ä = ä<br>Ü = ü after Ö = ö |
| hr-HR (1050) | 468 | Ă = ă after ª<br>Č = č after "cs"<br>Ć = ć after Č = č<br>"dzs" after "dz"<br>Đ = đ after "dž"<br>Ě = ě after e = E<br>Ľ = ľ after l = L<br>"ly" after Ł = ł<br>Ň = ň after n = N<br>"nj" after №<br>Ř = ř after r = R<br>Š = š after "sz"<br>"zs" after Ż = ż |
| hu-HU (1038) | 468 | "dž" after "dd"<br>№ after "nj"<br>Ö = ö after Œ = œ<br>Ő = ő after Ö = ö<br>™ after Ţ = ţ<br>Ư = ư after Ų = ų<br>Ü = ü after Ư = ư |
| lt-LT (1063) | 468 | Č = č after "cs"<br>"dzs" after "dz"<br>Ė = ė after Ę = ę<br>y = Y after Į = į<br>"ly" after Ł = ł<br>"ny" after Ņ = ņ<br>Š = š after "sz"<br>"zs" after Ż = ż |
| lv-LV (1062) | 468 | Ā = ā after Æ = æ<br>Č = č after "cs"<br>"dzs" after "dz"<br>Ę = ę after Ě = ě<br>"gy" after Ğ = ğ<br>y = Y after "ij"<br>Ī = ī after y = Y<br>"ly" after Ł = ł<br>Ļ = ļ after "lj"<br>"ny" after Ñ = ñ<br>Ņ = ņ after №<br>Ō = ō after Œ = œ<br>Š = š after "sz"<br>Ū = ū after Ư = ư<br>"zs" after Ż = ż |
| pl-PL (1045) | 468 | Æ = æ after Å = å<br>Ć = ć after "cs"<br>Ł = ł after "ly"<br>Ń = ń after №<br>Ó = ó after Œ = œ<br>Ś = ś after "sz"<br>Ž = ž after z = Z<br>"zs" after Ž = ž |
| ro-RO (1048) | 468 | Ă = ă after Æ = æ<br>Â = â after Ă = ă<br>Î = î after "ij"<br>Ş = ş after "sz"<br>Ţ = ţ after ™ |
| ru-RU (1049) | 468 | Й = й after Ї = ї |
| sk-SK (1051) | 467 | Ä = ä after Æ = æ<br>Č = č after "cs"<br>"dzs" after "dz"<br>Ė = ė after e = E<br>Ë = ë after Ė = ė<br>"ch" after h = H<br>Ï = ï after İ = ı<br>Ö = ö after º<br>Ô = ô after Œ = œ<br>Ŗ = ŗ after Ŕ = ŕ<br>Š = š after "sz"<br>Ü = ü after u = U<br>Ÿ = ÿ after y = Y<br>Ż = ż after z = Z<br>"zs" after Ź = ź<br>newly equal: İ / ı |
| sl-SI (1060) | 468 | Č = č after "cs"<br>Ć = ć after Č = č<br>"dzs" after "dz"<br>Đ = đ after "dž"<br>Š = š after "sz"<br>Ś = ś after Š = š<br>"zs" after Ż = ż<br>Ź = ź after Ž = ž |
| sr-Latn-RS (9242) | 468 | Ă = ă after ª<br>Č = č after "cs"<br>Ć = ć after Č = č<br>"dzs" after "dz"<br>Đ = đ after "dž"<br>Ě = ě after e = E<br>Ľ = ľ after l = L<br>"ly" after Ł = ł<br>Ň = ň after n = N<br>"nj" after №<br>Ř = ř after r = R<br>Š = š after "sz"<br>"zs" after Ż = ż |
| tr-TR (1055) | 468 | Ç = ç after "cs"<br>Ğ = ğ after "gy"<br>Ö = ö after Œ = œ<br>Ş = ş after "sz"<br>Ü = ü after Ư = ư |
| uk-UA (1058) | 468 | Й = й after Ї = ї<br>Ь = ь after Щ = щ |
| vi-VN (1066) | 465 | À = à after ª<br>Ã = ã after À = à<br>Ă = ă after Æ = æ<br>Â = â after Ă = ă<br>"cs" after Ç = ç<br>Đ = đ after "dzs"<br>È = è after e = E<br>Ê = ê after Ę = ę<br>Ì = ì after ı<br>Î = î after "ij"<br>Ò = ò after º<br>Õ = õ after Ò = ò<br>Œ = œ after Ø = ø<br>Ô = ô after Œ = œ<br>r = R after Ŗ = ŗ<br>Ù = ù after u = U<br>Û = û after Ư = ư<br>newly equal: U+0309 / U+0E49; U+0303 / U+0E4A; U+0323 / U+0E4D |

**What it shows:**
- **The Cyrillic locales differ from en-GB only around `Й`,** plus `Ь` in uk-UA. Their alphabets otherwise sort as en-GB sorts them.
- **The Central European locales place their letters with diacritics as separate letters.** Under cs-CZ, for instance, `Č` follows `"cs"`, and `Š` follows `"sz"`. They also treat some digraphs as letters: `"ch"` follows `H` in cs-CZ and sk-SK.
- **vi-VN moves the most:** {len(r['vi-VN']['moves'])} groups. It also makes three combining marks Vietnamese uses for tones (U+0309, U+0303 and U+0323) equal to three Thai marks (U+0E49, U+0E4A and U+0E4D). It's the one locale here that newly equates characters from different scripts.

**Not measured:**
- the double-byte code pages (932, 936, 949 and 950) and their locales;
- German phone book and traditional Spanish, which `Set-Culture` doesn't select (above);
- strings longer than one character beyond the digraphs listed.

### One string set under every locale (2026-11-23, #5105, SP6)

**The question.** The sections above measured these locales in pieces:
- #1567's 321 strings under 44 locales;
- the other code pages' upper halves under the 20 or so locales that use them;
- the two alternative sorts on their own.

Each piece was recorded as what moved against en-GB. #5105's loader needs every locale's whole order in one shape, so
this run sorts one set that holds all of those pieces, under all 46 locales.

**The probe.**
- **The set:** 784 strings. It's every string of #1567's measured order but `""`, NUL, Tab, LF and CR; every
  character of the upper halves of code pages 1250, 1251, 1253 to 1258 and 874 not already among them, without the C1
  controls; and the 15 digraphs of "Each locale's own script".
- **The sort:** a stable merge sort on `StrComp(a, b, vbTextCompare)`, with adjacent equals grouped.
- **Setup:** one `Sub Main` program on VB6.EXE 6.00.9782 (SP6) and MSVBVM60 6.00.9848, in a clone of the oracle guest
  on Windows 10 build 19044.1288. Built with `/make` as native code and as p-code, each exe run in session 0.
- **The locale:** set by `Set-Culture`. For German phone book and traditional Spanish, the sort LCID was written into
  `HKCU\Control Panel\International`, as above. `GetUserDefaultLCID` read back the expected LCID in every run, 66567
  and 1034 for those two.
- **Native code and p-code wrote byte-identical files in all 46 locales.** The orders are
  `corpus/locale-sort/<locale>.sbcs.txt`, and that folder's README gives the format and each locale's group count.
  The files leave out five C1 controls, which the repository refuses in a tracked file. Each was a group of its own in
  every locale.

**What it shows:**
- **19 locales besides en-GB sort the whole set exactly as en-GB does:** ar-SA, ca-ES, de-DE, el-GR, en-AU, en-US, eu-ES, fr-CA,
  fr-FR, gl-ES, he-IL, id-ID, it-IT, nl-NL, pt-BR, pt-PT, th-TH, zh-CN and zh-TW. Above, 22 besides en-GB did so on
  #1567's strings alone. bg-BG, ru-RU and uk-UA leave en-GB's order once their own Cyrillic is in the set, as "Each locale's own script"
  found around Й.
- **en-GB's order agrees with #1567's** on all 310 strings the two sets share, with no pair ordered differently.
- **The tailorings recorded above all hold in the full set.** These were checked:
  - sv-SE: Å after Ž, and Ä after Å;
  - da-DK: `"aa"` after Å;
  - tr-TR: Ç after `"cop"` and before D;
  - cs-CZ: `"ch"` after H and before I;
  - German phone book: `ä` equal to `"ae"`, which plain de-DE keeps apart;
  - traditional Spanish: `"ch"` after `"cop"` and before D;
  - ja-JP: `\` equal to `¥`.
- **Group counts run from 569 (vi-VN, German phone book) to 574 (is-IS)** against en-GB's 572. They differ where a
  locale joins strings en-GB keeps apart, or splits ones it joins.

**Not measured:** characters outside these code pages; strings beyond this set's 69 longer ones; any locale or Windows
build but these.
