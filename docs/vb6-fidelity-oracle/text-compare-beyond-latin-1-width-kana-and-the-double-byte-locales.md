## Text compare beyond Latin-1: width, kana and the double-byte locales (2026-11-21, #5105, SP6)

**The question.** #5105 maps each locale's Text order. "The Text order in 44 user locales" covered code page 1252's characters, and oracle PR #5251 the single-byte code pages, each under the locales that use it. This page takes a sample of the double-byte code pages' characters, under the four locales that use them, against en-GB.

**The probe.** 451 strings:
- the empty string, `0` to `9`, `abc`, `ABC`, `xyz` and `XYZ`;
- every hiragana (U+3041 to U+3096) and katakana (U+30A1 to U+30FA), the prolonged sound mark and the two iteration marks (U+30FC to U+30FE);
- every half-width katakana form (U+FF66 to U+FF9F);
- the full-width digits and `ＡＢＣａｂｃ`;
- every Hangul compatibility jamo (U+3131 to U+318E), and 24 Hangul syllables from 가 to 힣;
- 48 common ideographs, the same for every locale;
- 9 two-kana strings in hiragana, katakana and half-width, voiced and not.

They were built with `ChrW` and sorted by a stable insertion sort on `StrComp(a, b, vbTextCompare)` into groups of equals, as on the 44-locales page. The guest's user locale was set with `Set-Culture`, and `GetUserDefaultLCID` read back 2057, 1041, 1042, 2052 and 1028. Built with `/make` on vb6.exe SP6 as native code and as p-code, **the two gave the same order in all five locales.**

**In every one of the five locales, text compare ignores width and kana type,** and the 451 strings fall into the same 280 groups:

| Strings | Equal under text compare |
|---|---|
| ASCII and full-width | `0 = ０`, `a = A = Ａ = ａ` |
| hiragana, katakana and half-width katakana | `あ = ア = ｱ`, `ぁ = ァ = ｧ`, `ん = ン = ﾝ` |
| two kana, in each form | `"あい" = "アイ" = "ｱｲ"`, `"かき" = "カキ"` |
| a voiced kana and its katakana | `が = ガ`, `ば = バ`, `ぱ = パ`. Each is a group of its own, apart from `は = ハ = ﾊ` |
| the prolonged sound mark and the iteration marks | `ー = ヽ = ヾ = ｰ`, sorting last of all |

The half-width voiced marks `ﾞ` and `ﾟ` sort straight after the empty string, before the digits. A small kana sorts just before its full-size one.

**Where the locales differ is the ideographs:**

| Locale | The 48 ideographs, in the order measured | Fits |
|---|---|---|
| en-GB | 一三上下中二人先北十南口右国土大天女子字学小山川左年心愛手日書月木東水漢火王生田目耳西足道金長門 | code-point order |
| ja-JP | 愛一右王下火学漢金月口国左三山子字耳手十書女小上心人水生西先川足大中長天田土東道南二日年北木目門 | Japanese readings, あい, いち, う, おう, か… |
| ko-KR | 口国金南女年大道東木目門北山三上生書西先小手水心十愛王右月二耳人一日子字長田足左中天川土下学漢火 | Korean readings, 구, 국, 금, 남… |
| zh-CN | 愛北長川大道東耳二国漢火金口門木目南年女人日三山上生十手書水天田土王西下先小心学一右月中子字足左 | Hanyu pinyin, ài, běi, cháng, chuān… |
| zh-TW | 一二人十三上下口土大女子小山川中天心手日月木水火王北右左生田目先字年耳西足国学東金長門南書愛道漢 | stroke count, 1, 2, 2, 2, 3… |

The "Fits" column is read by hand against each reading or stroke count, for the first several of each list and spot checks after. It's not checked against a reference table.

- **Under en-GB the ideographs sort after the Hangul.**
- **Under the four double-byte locales they're interleaved with the Hangul syllables and jamo.** Under ko-KR that fits their Korean readings (reasoned, not checked). Under the other three no rule was found.
- **The 24 Hangul syllables sort in the same order in all five locales,** with the jamo among them.

**HexIDE,** read from the code and not run: `VbTextCompare` holds vb6.exe's weights for `Chr(0)` to `Chr(255)`, and sorts any other character after all of them by its invariant lower case (`VbTextCompare.cs:21-23`). So in HexIDE, `StrComp("Ａ", "a", vbTextCompare)` and `StrComp("あ", "ア", vbTextCompare)` are not 0, where VB6 gives 0 under every locale measured here.

**Not measured:**
- the double-byte code pages in full: this is a sample;
- ideographs beyond the 48;
- the half-width forms of Hangul;
- the other East Asian locales (zh-HK, zh-SG, ja-JP's alternative sorts).
