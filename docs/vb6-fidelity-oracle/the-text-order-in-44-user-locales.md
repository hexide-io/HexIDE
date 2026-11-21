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

**Not measured:**
- characters outside code page 1252, which is every locale's own script beyond Latin;
- the held-out set and the expansion sweep;
- sorted ListBoxes and ComboBoxes, which compare as `lstrcmpi` does (#5059);
- any Windows build other than this one.
