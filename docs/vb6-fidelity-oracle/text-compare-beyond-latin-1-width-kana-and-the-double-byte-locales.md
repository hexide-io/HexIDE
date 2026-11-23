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

### Every full-width letter (2026-11-23, #5253, SP6)

**The question.** The probe above took only `ＡＢＣａｂｃ` of the full-width letters. #5253 builds the fold, and the other 23
letters, in each case, were the open part.

**The probe.** One `Sub Main` program, built with `/make` on vb6.exe SP6 as native code and as p-code, under en-GB
(`GetUserDefaultLCID` 2057). The two builds logged identical lines. For each letter from A to Z, with `F` the full-width
capital (`ChrW(&HFF21 + i)`) and `f` the full-width small letter (`ChrW(&HFF41 + i)`), it logged:
- `StrComp(F, capital)`, `StrComp(f, small)`, `StrComp(F, f)` and `StrComp(F, small)`, all with `vbTextCompare`;
- `StrComp(F, the next ASCII capital)` and `StrComp(F, the previous one)`, with `vbTextCompare`;
- `StrComp(F & "1", capital & "2", vbTextCompare)`;
- `StrComp(F, capital, vbBinaryCompare)`.

**Every one of the 26 letters gave the same row:**

| Comparison | Result |
|---|---|
| `F` against its ASCII capital, `f` against its ASCII small letter, `F` against `f`, `F` against the ASCII small letter | 0 |
| `F` against the next ASCII capital | -1 |
| `F` against the previous ASCII capital | 1 |
| `F & "1"` against the capital `& "2"` | -1: the letters tie, and the next character decides |
| `F` against its ASCII capital, binary | 1 |

So **text compare folds every full-width letter to its ASCII letter, in either case,** as the probe above found for A to C,
and it sorts exactly where that letter does. Binary compare keeps them apart.

**Not measured:** full-width punctuation and symbols (U+FF01 to U+FF0F and the others), and any locale but en-GB.

### Each double-byte code page in full (2026-11-23, #5105, SP6)

**The question.** The first section sorted a 451-string sample. This one sorts every character of code pages 932, 936,
949 and 950, each under the locale that uses it and under en-GB. Its "Not measured" list began with that.

**The probe.**
- **Setup:** VB6.EXE 6.00.9782 (SP6) and MSVBVM60 6.00.9848, in a clone of the oracle guest on Windows 10 build
  19044.1288. One `Sub Main` program, made with `/make` as native code and as p-code, each exe run in session 0.
- **The characters:** printable ASCII but the space; 932's half-width katakana; and every two-byte sequence the code
  page decodes to one character outside the private use area. .NET Framework's `Encoding.GetEncoding` decoded them
  in the guest.
- **The sort:** a stable merge sort on `StrComp(a, b, vbTextCompare)`, with adjacent equals grouped.
- **The locale:** set by `Set-Culture` before each run, with `GetUserDefaultLCID` logged: 1041, 2052, 1042, 1028 and
  2057.
- **Native code and p-code wrote byte-identical files in all eight runs.** The files are `corpus/locale-sort/`, and
  its README gives the format.

| Code page | Characters | Under its own locale: groups, and characters in a group with another | Under en-GB |
|---|---|---|---|
| 932, ja-JP | 7,483 | 7,134; 576 | 7,104; 636 |
| 936, zh-CN | 21,885 | 21,581; 535 | 21,566; 565 |
| 949, ko-KR | 17,142 | 16,837; 539 | 16,571; 1,065 |
| 950, zh-TW | 13,587 | 13,434; 247 | 13,432; 251 |

**What the full sets confirm from the sample:**
- **Width folds** in every run, own locale and en-GB alike.
  - `A`, `Ａ`, `a` and `ａ` share a group, and so do `0` and `０`.
  - So does an ASCII punctuation mark and its full-width form: `!` and `！`, `(` and `（`.
- **Kana folds** in 932's runs, under both ja-JP and en-GB: `あ`, `ア` and `ｱ` share a group, and `゛` and `ﾞ`.
- **The 48 sample ideographs** sort in the same relative order here as in the sample, in all four locales. 46 of them
  are in 949's and 950's sets.

**Where the ideographs go, tested against each code page's own byte order and against code-point order.** These are
adjacent pairs in the measured order that ascend.

| Run | Ideographs | In the code page's byte order | In code-point order |
|---|---|---|---|
| 932 under ja-JP | 6,716 | **6,715 of 6,715** | 4,903 |
| 949 under ko-KR | 4,888 | **4,886 of 4,887**: one pair, 坍 (`D3BB`) before 撻 (`D3B5`) | 4,292 |
| 936 under zh-CN | 20,923 | 10,863 of 20,922 | 11,007 |
| 950 under zh-TW | 13,070 | 7,096 of 13,069 | **13,037 of 13,069** |
| 932, 936, 949, 950 under en-GB | as above | 3,595; 15,902; 2,446; 7,005 | 6,682 of 6,715; 20,901 of 20,922; 4,626 of 4,887; 13,067 of 13,069 |

The ideographs here are U+4E00 to U+9FFF and U+F900 to U+FAFF. The byte order is Python's `cp932`, `gbk`, `cp949` and
`cp950` encodings of each character, which encoded every one of them.

**Readings:**
- **Under ja-JP, the ideographs sort in Shift-JIS byte order,** every one of the 6,716, the NEC and IBM extension
  rows included.
- **Under ko-KR, they sort in CP949 order but for one pair,** which is measured and unexplained.
- **The 11,172 Hangul syllables sort in code-point order** under ko-KR and under en-GB alike, not in CP949's, which
  splits them between its KS X 1001 rows and its extension rows.
- **Under zh-TW, the order fits stroke count, with code-point order inside each count.** It starts 一 乙, then 丁 七
  乂 … 十 卜 厂 又, then 万 丈 三 上 下.
  - The 32 descents in 13,069 pairs fall where a count ends. That's read for the first 30 or so, and not checked
    against a stroke table.
  - It's not Big5's order, though Big5 orders by stroke count too: its second level starts again at one stroke.
- **Under zh-CN, the order fits Hanyu pinyin across all 20,923,** GB2312's two levels and GBK's extension
  interleaved. It starts 吖 阿 锕 錒 嗄 啊, then 哎 哀 埃 挨 唉. Not checked against a pinyin table.
- **Under en-GB, the unified ideographs (U+4E00 to U+9FFF) sort in code-point order,** with no descent among them in
  any of the four sets.
  - The descents fall among the compatibility ideographs (U+F900 to U+FAFF): 34 in 932's set, 21 in 936's, 268 in
    949's and 2 in 950's.
  - Where they go wasn't examined.

**HexIDE**, read from the code and not run: `VbTextCompare` holds weights for `Chr(0)` to `Chr(255)` only, and sorts
any other character after all of them by its invariant lower case (`VbTextCompare.cs:21-23`). So none of these orders
is followed in any locale. #5105's per-locale tables are the build that would.

**Not measured:**
- surrogate pairs, and characters outside these four code pages;
- the end-user-defined ranges;
- strings of more than one character, beyond the sample's;
- zh-HK, zh-SG, and the alternative sorts (Japanese Unicode, Bopomofo, Korean Unicode).
