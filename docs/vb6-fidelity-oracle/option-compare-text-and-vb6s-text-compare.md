## Option Compare Text, and VB6's text compare (2026-10-23, #1567)

Measured with `/make` on vb6.exe 6.00.8176 (the release build) with the MSVBVM60 6.00.9848 (SP6) runtime, under en-GB, in nine batches. Native and p-code agree on every row of every batch.

**The question.** What `Option Compare Text` changes, and what VB6's text compare actually is. The same compare serves:
- `StrComp` and the string functions given a compare of 1;
- `Option Compare Text`'s comparison operators and `Select Case`;
- a Collection's keys.

The obvious guess is "case-insensitive", and HexIDE used `OrdinalIgnoreCase` for it. The second guess is "the locale's collation", which on .NET means ICU. Both are wrong, and the measurements say by how much.

### The operators, Select Case and the module

Each expression was evaluated inline in a module saying `Option Compare Binary` and in one saying `Option Compare Text`. `sv` and `su` are Strings holding `"abc"` and `"ABC"`, and `vv` and `vu` are Variants holding the same.

| Row | Expression | Binary module | Text module |
|---|---|---|---|
| eq-case | `"abc" = "ABC"` | Boolean False | Boolean True |
| ne-case | `"abc" <> "ABC"` | Boolean True | Boolean False |
| lt-a-B | `"a" < "B"` | Boolean False | Boolean True |
| lt-B-a | `"B" < "a"` | Boolean True | Boolean False |
| le-a-A | `"a" <= "A"` | Boolean False | Boolean True |
| lt-a-A | `"a" < "A"` | Boolean False | Boolean False |
| gt-a-A | `"a" > "A"` | Boolean True | Boolean False |
| lt-us-a | `"_" < "a"` | Boolean True | Boolean True |
| lt-Z-us | `"Z" < "_"` | Boolean True | Boolean False |
| lt-br-a | `"[" < "a"` | Boolean True | Boolean True |
| lt-1-a | `"1" < "a"` | Boolean True | Boolean True |
| lt-sp-a | `" " < "a"` | Boolean True | Boolean True |
| eq-trailing | `"a" = "a "` | Boolean False | Boolean False |
| eq-e-acute-e | `Chr(233) = "e"` | Boolean False | Boolean False |
| lt-e-e-acute | `"e" < Chr(233)` | Boolean True | Boolean True |
| lt-e-acute-f | `Chr(233) < "f"` | Boolean False | Boolean True |
| lt-E-acute-f | `Chr(201) < "f"` | Boolean False | Boolean True |
| eq-sz-ss | `Chr(223) = "ss"` | Boolean False | Boolean True |
| eq-ae | `Chr(230) = "ae"` | Boolean False | Boolean True |
| eq-hyphen | `"coop" = "co-op"` | Boolean False | Boolean False |
| lt-hyphen | `"co-op" < "coop"` | Boolean True | Boolean False |
| eq-vars | `sv = su` | Boolean False | Boolean True |
| eq-variants | `vv = vu` | Boolean False | Boolean True |
| eq-var-str | `vv = su` | Boolean False | Boolean True |
| lt-vars | `su < sv` | Boolean True | Boolean False |
| eq-null | `"a" = Null` | Null | Null |
| str-num | `"10" < 9` | Boolean False | Boolean False |
| str-num-eq | `"5" = 5` | Boolean True | Boolean True |
| cross-module | `BinaryEquals("abc", "ABC")`, a function in the Binary module | Boolean False | Boolean False |
| instr | `InStr("ABC", "b")` | Long 0 | Long 2 |
| instr-start | `InStr(1, "ABC", "b")` | Long 0 | Long 2 |
| instr-explicit-0 | `InStr(1, "ABC", "b", 0)` | Long 0 | Long 0 |
| instrrev | `InStrRev("ABCabc", "B")` | Long 2 | Long 5 |
| replace | `Replace("ABCabc", "b", "x")` | String ABCaxc | String AxCaxc |
| split | `Split("aXbxc", "x")` | String() 2 | String() 3 |
| filter | `Filter(Array("A", "b", "a"), "a")` | String() 1 | String() 2 |
| strcomp | `StrComp("a", "A")` | Integer 1 | Integer 0 |
| like | `"ABC" Like "a*"` | Boolean False | Boolean True |
| collection-key | `c.Add 1, "abc"` then `c.Add 2, "ABC"` | err 457 This key is already associated with an element of this collection | err 457 This key is already associated with an element of this collection |
| select-case | the five Select Case tests below | else else is-lt else range-A-z | case-A range is-lt is-eq else |

The five Select Case tests, in order:
1. `"a"` against `Case "A"`;
2. `"B"` against `Case "a" To "c"`;
3. `"A"` against `Case Is < "b"`;
4. `"abc"` against `Case Is = "ABC"`;
5. `"_"` against `Case "A" To "z"`.

What the table says:
- **Option Compare belongs to the module the comparison is written in.** `BinaryEquals`, a function in the Binary module, compares as Binary when the Text module calls it.
- **A string function given no compare takes the module's mode:** InStr, InStrRev, Replace, Split, Filter and StrComp all answer as text in the Text module, and an explicit 0 overrides that. HexIDE does not do that yet (#1572).
- **A Collection's keys are text in both modules.**
- **A String against a number compares as numbers in both modes.** HexIDE raises 13 for it in either mode (#211).
- **A row was dropped.** The probe had a row `lt-apos`, but a quoting slip in its generator compared `"its"` with itself. The order below has `"its"` < `"it's"`.

### The whole Text order

321 strings were sorted by a stable insertion sort using `StrComp(a, b, vbTextCompare)`, and logged as groups of strings it found equal. The strings were:
- every single character `Chr(0)` to `Chr(255)`;
- the empty string;
- 64 longer strings: candidate expansions, word-sort cases and near neighbours.

The same sort by `<` in a Text module gave the identical sequence.

In the list below, characters are shown by their AscW code where they would not print, and multi-character strings are in quotes.

<details><summary>The 234 groups, in order</summary>

```text
""
<0> = <173>
<1>
<2>
<3>
<4>
<5>
<6>
<7>
<8>
<14>
<15>
<16>
<17>
<18>
<19>
<20>
<21>
<22>
<23>
<24>
<25>
<26>
<27>
<28>
<29>
<30>
<31>
<127>
' = '
"''"
- = -
"--"
–
—
<32>
<160>
<9>
<10>
<11>
<12>
<13>
!
"
#
$
%
"%0"
"%o"
&
(
)
*
,
.
"..."
/
:
;
?
@
[
\
]
^ = ˆ
_
`
{
|
}
~
¡
¦
¨
¯
´
¸
¿
˜
‘
’
‚
“
”
„
‹
›
¢
£
¤
¥
€
+
<
=
>
±
«
»
×
÷
§
©
¬
®
°
µ
¶
·
…
†
‡
•
‰
<129>
<141>
<143>
<144>
<157>
0
¼
½
¾
1 = ¹
"1/2"
"1/4"
2 = ²
3 = ³
"3/4"
4
5
6
7
8
9
A = a = a
"a'"
"a-"
"'a"
"-a"
ª
Á = á
À = à
Â = â
Ä = ä
Ã = ã
Å = å
"a<32>b"
"aa" = "aa"
"ab"
"a'b"
"a-b"
Æ = æ = "ae" = "AE" = "Ae"
"ae<32>"
"ao"
B = b = b
C = c
Ç = ç
"ch"
"coo"
"coop"
"co-op"
"cop"
D = d
Ð = ð
"dh" = "DH"
"dz"
E = e
É = é
È = è
Ê = ê
Ë = ë
F = f
ƒ
"ff"
"ffi"
"fi"
"fl"
G = g
H = h
I = i
Í = í
Ì = ì
Î = î
Ï = ï
"ij" = "IJ"
"it"
"its"
"it's"
"itss"
J = j
K = k
L = l
"lj"
"ll"
M = m
N = n
Ñ = ñ
"nj"
O = o
º
Ó = ó
Ò = ò
Ô = ô
Ö = ö
Õ = õ
Ø = ø
Œ = œ = "oe" = "OE" = "Oe"
P = p
Q = q
R = r
"rr"
S = s
Š = š
ß = "ss" = "SS" = "Ss"
"ss<32>"
T = t
Þ = þ = "th" = "TH" = "Th"
"TM" = "tm"
™
U = u
Ú = ú
Ù = ù
Û = û
Ü = ü
"ue" = "Ue" = "UE"
V = v
W = w
X = x
Y = y
Ý = ý
Ÿ = ÿ
Z = z
Ž = ž
```

</details>

### Its structure

Two probes took the order apart:
- **Level.** For each pair of neighbouring single characters `c1 < c2`, the probe compared `StrComp(c1 & "b", c2 & "a", 1)`. -1 means the two differ at the first level. +1 means they differ only below it.
- **Kind.** For each character `c`, the probe compared `StrComp("a" & c & "b", "ab", 1)` and `StrComp("a" & c & "b", "aba", 1)`. 0 means ignorable; -1 means a primary weight below b; +1 then -1 means ignored at the first level but not at a lower one; +1 then +1 means a primary weight above b.

What they found:
- **121 primary classes.** Within a class, case is ignored and the accented forms follow the plain letter in a fixed order. For a, that is a, then ª, á, à, â, ä, ã, å.
- **Accents compare left to right, after the whole first level.** `"aà"` < `"áA"`, and `"àa"` < `"àà"`.
- **31 word-sort characters,** ignored at the first two levels and compared after them: controls 1 to 8, 14 to 31 and 127, then `'`, `-`, the en dash and the em dash, in that order.
  - Their **position** counts the letters before them, an expansion counting as two and an ignorable as none.
  - **A later position sorts first.** So `"abc'"` < `"ab'c"` < `"a'bc"` < `"'abc"`, and `"coop"` < `"coop-"` < `"coo-p"` < `"co-op"` < `"c-oop"`.
- **2 ignorables, Chr(0) and the soft hyphen.** `"a" & Chr(0) & "b"` = `"ab"`. Yet `""` < `Chr(0)`: an empty string is below any other, even one with nothing to compare. That is consistent with VarBstrCmp handling an empty string before it compares anything.
- **7 expansions:** ß = ss; æ and Æ = ae; œ and Œ = oe; þ and Þ = th.
  - A sweep compared every `Chr(128)` to `Chr(255)` with all 26 one-letter and 676 two-letter lower-case strings. It found exactly these seven, and no others.
  - ¹, ² and ³ are ties with 1, 2 and 3, not expansions. `^` and `ˆ` tie too.
  - ½, ™, … and ƒ are none of these: they are characters of their own.
- **Where the rest fall.**
  - Tab, LF, VT, FF and CR are symbols after the space and the no-break space.
  - µ is a symbol, not a letter.
  - The five codes cp1252 leaves undefined (129, 141, 143, 144, 157) sort together just before the digits.

The position rule was settled by a third probe. Here B is ß, Z is æ, N is Chr(0) and T the soft hyphen:

| Pair | StrComp |
|---|---|
| `B-` against `s-s` | -1 |
| `B-` against `ss-` | 0 |
| `s-s` against `ss-` | 1 |
| `Z-` against `a-e` | -1 |
| `Z-` against `ae-` | 0 |
| `-B` against `-ss` | 0 |
| `xB-y` against `xs-sy` | -1 |
| `xB-y` against `xss-y` | 0 |
| `aN-b` against `a-b` | 0 |
| `Na-b` against `a-b` | 0 |
| `a-Nb` against `a-b` | 0 |
| `aT-b` against `a-b` | 0 |
| `Ta-b` against `a-b` | 0 |
| `a-Tb` against `a-b` | 0 |
| `aT-b` against `a-Tb` | 0 |
| `aTb-` against `ab-` | 0 |
| `aTb-` against `ab-T` | 0 |
| `aN` against `a` | 0 |
| `N` against `T` | 0 |
| `N` against `TT` | 0 |
| `NT-` against `-` | 0 |
| `xTe-y` against `xe-y` | 0 |
| `xTT-y` against `x-y` | 0 |
| `aB'` against `ass'` | 0 |
| `a'B` against `a's's` | -1 |

### The model, and a held-out check

A comparer built from those findings goes level by level:
1. the primary weights;
2. then the accents;
3. then the word-sort marks, each a position and a rank, a later position first.

It reproduces all 51,360 pairs of the order. That is the data it was built from, so a **held-out** set was measured too.

The held-out set was 569 strings:
- 110 random words, and four variants of each;
- 18 strings that differ only in word-sort characters;
- the empty string.

Each variant changed its word in one to three ways:
- a change of case;
- an accent swapped in;
- a word-sort character inserted;
- an ignorable inserted;
- a symbol inserted;
- an expansion swapped either way;
- a letter dropped.

The comparer then got 1 of its 161,596 pairs wrong: it had counted a soft hyphen in a word-sort position. With the position counting only letters, it gets 0 of 161,596 wrong.

The same pairs against the alternatives, on Windows (where .NET uses ICU) and under Linux's ICU 78, which agree to the pair:

| Comparer | Wrong, of the order's 51,360 | Wrong, of the held-out 161,596 |
|---|---|---|
| `OrdinalIgnoreCase` | 14,856 | 28,364 |
| ICU, en-GB, ignoring case, kana type and width | 2,699 | 4,455 |
| the same, with the 7 expansions first | 2,614 | 3,732 |
| lower case, then ICU with no options | 2,693 | 4,455 |
| the same, with the 7 expansions first | 2,605 | 3,732 |
| **the measured rows** | **0** | **0** |

ICU orders the punctuation and symbols differently from vb6.exe. It also keeps ß from ss, and merges ª with a and the no-break space with the space, each of which vb6.exe does the other way round.

**Our own wrong expectation, recorded:** before this was measured, this session told the supervisor that ICU "gets every row right except the hyphen word sort". ß = ss was in the rows it had, and ICU gets it wrong.

### Searching is another rule

InStr, InStrRev, Replace, Split and Filter with a compare of 1 do not search by the comparer:
- **A search matches character by character, case ignored, with the 7 expansions read as their letters, and nothing else merges.**
  - Chr(0), the soft hyphen, ¹ and the word-sort characters match only themselves.
  - So `InStr(1, "ab", "a" & Chr(0) & "b", 1)` is 0 and `InStr(1, "x" & Chr(185), "1", 1)` is 0, although each pair compares equal.
- **InStr, InStrRev and Filter take a match that starts or ends inside an expansion,** and report the character it starts in.
  - `InStr(1, "ß", "s", 1)` is 1, `InStr(1, "xæy", "ey", 1)` is 2, and `Filter(Array("ß"), "s", True, 1)` keeps the element.
- **Replace and Split take only whole characters.**
  - `Replace("straße", "s", "X", 1, -1, 1)` replaces only the first s, `"ss"` replaces the ß, and `Split("aßb", "s", -1, 1)` does not split.

<details><summary>The 79 search rows</summary>

| Row | Call | vb6.exe |
|---|---|---|
| instr-half-sz | `InStr(1, Chr(223), "s", 1)` | 1 |
| instr-sz-ss | `InStr(1, "stra" & Chr(223) & "e", "ss", 1)` | 5 |
| instr-sz-es | `InStr(1, "stra" & Chr(223) & "e", "es", 1)` | 0 |
| instr-sz-sse | `InStr(1, "stra" & Chr(223) & "e", "sse", 1)` | 5 |
| instr-sz-as | `InStr(1, "stra" & Chr(223) & "e", "as", 1)` | 4 |
| instr-sz-ass | `InStr(1, "stra" & Chr(223) & "e", "ass", 1)` | 4 |
| instr-ss-sz | `InStr(1, "strasse", Chr(223), 1)` | 5 |
| instr-ss-sze | `InStr(1, "strasse", Chr(223) & "e", 1)` | 5 |
| instr-sss-sz | `InStr(1, "asssb", Chr(223), 1)` | 2 |
| instr-sz-from-2 | `InStr(2, "stra" & Chr(223) & "e", "ss", 1)` | 5 |
| instr-sz-from-5 | `InStr(5, "stra" & Chr(223) & "e", "s", 1)` | 5 |
| instr-ae-half | `InStr(1, "x" & Chr(230) & "y", "a", 1)` | 2 |
| instr-ae-e | `InStr(1, "x" & Chr(230) & "y", "ey", 1)` | 2 |
| instr-ae-whole | `InStr(1, "aeb", Chr(230), 1)` | 1 |
| instr-nul-skip | `InStr(1, "a" & Chr(0) & "bc", "bc", 1)` | 3 |
| instr-nul-lead-needle | `InStr(1, "a" & Chr(0) & "bc", Chr(0) & "bc", 1)` | 2 |
| instr-nul-in-needle | `InStr(1, "abc", "a" & Chr(0) & "b", 1)` | 0 |
| instr-nul-spans | `InStr(1, "a" & Chr(0) & "bc", "ab", 1)` | 0 |
| instr-shy-spans | `InStr(1, "a" & Chr(173) & "bc", "ab", 1)` | 0 |
| instr-shy-lead | `InStr(1, Chr(173) & "abc", "ab", 1)` | 2 |
| instr-nul-needle | `InStr(1, "abc", Chr(0), 1)` | 0 |
| instr-nul-needle-has | `InStr(1, "a" & Chr(0), Chr(0), 1)` | 2 |
| instr-empty-needle | `InStr(1, "xyz", "", 1)` | 1 |
| instr-empty-needle-3 | `InStr(3, "xyz", "", 1)` | 3 |
| instr-hy-coop | `InStr(1, "co-op", "coop", 1)` | 0 |
| instr-coop-hy | `InStr(1, "coop", "co-op", 1)` | 0 |
| instr-hy-op | `InStr(1, "co-op", "op", 1)` | 4 |
| instr-hy-dash-op | `InStr(1, "co-op", "-op", 1)` | 3 |
| instr-hy-o-o | `InStr(1, "co-op", "o-o", 1)` | 2 |
| instr-hy-alone | `InStr(1, "co-op", "-", 1)` | 3 |
| instr-hy-in-empty | `InStr(1, "coop", "-", 1)` | 0 |
| instr-ctl-spans | `InStr(1, "x" & Chr(1) & "y", "xy", 1)` | 0 |
| instr-ctl-needle | `InStr(1, "xy", "x" & Chr(1) & "y", 1)` | 0 |
| instr-a-dash | `InStr(1, "a-", "a", 1)` | 1 |
| instr-dash-a | `InStr(1, "-a", "a", 1)` | 2 |
| instr-accent | `InStr(1, Chr(201), "e", 1)` | 0 |
| instr-case | `InStr(1, "AbC", "bc", 1)` | 2 |
| instrrev-half-sz | `InStrRev("stra" & Chr(223) & "e", "s", -1, 1)` | 5 |
| instrrev-sz-ss | `InStrRev("stra" & Chr(223) & "e", "ss", -1, 1)` | 5 |
| instrrev-nul | `InStrRev("a" & Chr(0) & "b", "b", -1, 1)` | 3 |
| instrrev-hy | `InStrRev("co-op", "coop", -1, 1)` | 0 |
| replace-sz-s | `Codes(Replace("stra" & Chr(223) & "e", "s", "X", 1, -1, 1))` | [88.116.114.97.223.101] |
| replace-sz-ss | `Codes(Replace("stra" & Chr(223) & "e", "ss", "X", 1, -1, 1))` | [115.116.114.97.88.101] |
| replace-ss-sz | `Codes(Replace("strasse", Chr(223), "X", 1, -1, 1))` | [115.116.114.97.88.101] |
| replace-nul-ab | `Codes(Replace("a" & Chr(0) & "bc", "ab", "X", 1, -1, 1))` | [97.0.98.99] |
| replace-nul-b | `Codes(Replace("a" & Chr(0) & "bc", "b", "X", 1, -1, 1))` | [97.0.88.99] |
| replace-hy-coop | `Codes(Replace("co-op", "coop", "X", 1, -1, 1))` | [99.111.45.111.112] |
| replace-hy-op | `Codes(Replace("co-op", "op", "X", 1, -1, 1))` | [99.111.45.88] |
| split-sz-s | `Parts(Split("a" & Chr(223) & "b", "s", -1, 1))` | 1 [97.223.98] |
| split-nul | `Parts(Split("a" & Chr(0) & "b" & Chr(0) & "c", Chr(0), -1, 1))` | 3 [97][98][99] |
| split-hy | `Parts(Split("co-op", "coop", -1, 1))` | 1 [99.111.45.111.112] |
| filter-hy | `UBound(Filter(Array("co-op"), "coop", True, 1))` | -1 |
| filter-nul | `UBound(Filter(Array("a" & Chr(0) & "b"), "ab", True, 1))` | -1 |
| filter-half-sz | `UBound(Filter(Array(Chr(223)), "s", True, 1))` | 0 |
| filter-ae-ey | `UBound(Filter(Array("x" & Chr(230) & "y"), "ey", True, 1))` | 0 |
| instr-shy-vs-nul | `InStr(1, "a" & Chr(173) & "b", "a" & Chr(0) & "b", 1)` | 0 |
| instr-sup1-1 | `InStr(1, "x" & Chr(185), "1", 1)` | 0 |
| instr-1-sup1 | `InStr(1, "x1", Chr(185), 1)` | 0 |
| instr-en-em | `InStr(1, "a" & Chr(150) & "b", "a" & Chr(151) & "b", 1)` | 0 |
| instr-apos-rsquo | `InStr(1, "a'b", "a" & Chr(146) & "b", 1)` | 0 |
| replace-ae-ey | `Codes(Replace("x" & Chr(230) & "y", "ey", "Z", 1, -1, 1))` | [120.230.121] |
| replace-ae-a | `Codes(Replace("x" & Chr(230) & "y", "a", "Z", 1, -1, 1))` | [120.230.121] |
| split-ae-ey | `Parts(Split("x" & Chr(230) & "y", "ey", -1, 1))` | 1 [120.230.121] |
| instr-szs-sss | `InStr(1, Chr(223) & "s", "sss", 1)` | 1 |
| instr-ssz-sss | `InStr(1, "s" & Chr(223), "sss", 1)` | 1 |
| replace-ssz-sss | `Codes(Replace("s" & Chr(223), "sss", "Z", 1, -1, 1))` | [90] |
| replace-szs-ss | `Codes(Replace(Chr(223) & "s", "ss", "Z", 1, -1, 1))` | [90.115] |
| replace-ssz-ss | `Codes(Replace("s" & Chr(223), "ss", "Z", 1, -1, 1))` | [115.90] |
| instr-ssz-ss | `InStr(1, "s" & Chr(223), "ss", 1)` | 1 |
| instrrev-szs-ss | `InStrRev(Chr(223) & "s", "ss", -1, 1)` | 1 |
| instrrev-ssz-ss | `InStrRev("s" & Chr(223), "ss", -1, 1)` | 2 |
| instr-AE-E | `InStr(1, Chr(198), "E", 1)` | 1 |
| filter-nul | `UBound(Filter(Array("a" & Chr(0)), Chr(0), True, 1))` | 0 |
| instr-sz-sz | `InStr(1, "x" & Chr(223), Chr(223), 1)` | 2 |
| instr-sz-half-needle | `InStr(1, "xs", Chr(223), 1)` | 0 |
| replace-count-sz | `Codes(Replace(Chr(223) & Chr(223), "s", "Z", 1, -1, 1))` | [223.223] |
| instr-accent-in-expansion | `InStr(1, Chr(230), "a" & Chr(233), 1)` | 0 |
| instr-empty-past | `InStr(5, "abc", "", 1)` | 5 |
| instr-empty-end | `InStr(4, "abc", "", 1)` | 4 |

</details>

**Measured, and no rule I would defend:** why a search refuses the merges the compare makes (¹ and 1, Chr(0) and nothing) while keeping the expansions. The rows are consistent with a search that folds case and expands ß, æ, œ and þ, and does nothing else. That is a description of the answers, not a reason for them.

### A Variant selector, and a search with a start

A ninth batch measured what the first eight left out. It was prompted by mutants that survived the tests built from them.

**Select Case with a Variant on either side.** Each of these was run in the Binary module and the Text module:
- a Variant selector against a literal, against `To`, against `Is <` and against `Is =`;
- a Variant against a Variant;
- a Variant function's result on either side, and on both;
- an expression selector, `v & ""`;
- `Is >` and `Is <` against a Variant.

Every one compares as text in the Text module: `var-lit var-to var-is-lt var-is-eq var-var var-fn str-fn fn-lit fn-fn expr-lit var-is-gt-var var-us-lt-var`. In the Binary module only `"A" < "b"` holds: `- - var-is-lt - - - - - - - - -`.

**Split and InStrRev as text.**

| Row | Call | vb6.exe |
|---|---|---|
| split-case | `Parts(Split("aXbxc", "x", -1, 1))` | 3 [97][98][99] |
| split-case-bin | `Parts(Split("aXbxc", "x", -1, 0))` | 2 [97.88.98][99] |
| split-ss-sz | `Parts(Split("a" & Chr(223) & "b", "ss", -1, 1))` | 2 [97][98] |
| split-sz-ss | `Parts(Split("assb", Chr(223), -1, 1))` | 2 [97][98] |
| split-limit | `Parts(Split("aXbxc", "x", 2, 1))` | 2 [97][98.120.99] |
| instrrev-start | `InStrRev("abcABC", "B", 4, 1)` | 2 |
| instrrev-start-bin | `InStrRev("abcABC", "B", 4, 0)` | 0 |
| instrrev-start-sz | `InStrRev("ss" & Chr(223), "ss", 2, 1)` | 1 |
| instrrev-start-mid-sz | `InStrRev("a" & Chr(223) & "b", "s", 2, 1)` | 2 |
| instrrev-start-before-sz | `InStrRev("a" & Chr(223) & "b", "s", 1, 1)` | 0 |
| instrrev-start-end-sz | `InStrRev("s" & Chr(223), "ss", 1, 1)` | 0 |

As text, `"ss"` splits `"aßb"` at its one ß, and `"ß"` splits `"assb"` at its two s. InStrRev's start bounds where a
match may end: with a start of 2, `"s"` is found in the ß at 2, and `"ss"` is not found in `"sß"` with a start of 1, since
the only match ends inside the ß.

### A compare of 2 or more: StrComp and InStr with a locale ID (2026-11-16, #4151)

**The question.** `IsTextCompare` refused a compare from 3 up. #881 had measured that VB6 accepts some such values and
raises 5 for others, consistently with Windows locale IDs, but not whether a valid one compares in that locale or just
as text.

**The probe.** vb6.exe SP6, `/make`, native and p-code agreeing in every cell, under en-GB. Each call ran under
`On Error Resume Next`, so a cell is the value, or `E5` for error 5. The strings were built with `Chr()`, cp1252.

#### StrComp(a, b, compare)

| a, b | 0 | 1 | 2 | 3 | 4 | 5 | 100 | 1024 | 1033 | 1031 | 1053 (sv) | 1029 (cs) | 2057 | 1041 | 999999 | -2 | 65535 |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| "a", "B" | 1 | -1 | E5 | -1 | -1 | -1 | -1 | -1 | -1 | -1 | -1 | -1 | -1 | -1 | E5 | E5 | E5 |
| "a", "A" | 1 | 0 | E5 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | E5 | E5 | E5 |
| "ä", "b" | 1 | -1 | E5 | -1 | -1 | -1 | -1 | -1 | -1 | -1 | **1** | -1 | -1 | -1 | E5 | E5 | E5 |
| "ö", "z" | 1 | -1 | E5 | -1 | -1 | -1 | -1 | -1 | -1 | -1 | **1** | -1 | -1 | -1 | E5 | E5 | E5 |
| "ß", "ss" | 1 | 0 | E5 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | E5 | E5 | E5 |
| "Æ", "AE" | 1 | 0 | E5 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | E5 | E5 | E5 |
| "co-op", "coop" | -1 | 1 | E5 | 1 | 1 | 1 | 1 | 1 | 1 | 1 | 1 | 1 | 1 | 1 | E5 | E5 | E5 |
| "ch", "d" | -1 | -1 | E5 | -1 | -1 | **1** | -1 | -1 | -1 | -1 | -1 | **1** | -1 | -1 | E5 | E5 | E5 |
| "", "a" | -1 | -1 | E5 | -1 | -1 | -1 | -1 | -1 | -1 | -1 | -1 | -1 | -1 | -1 | **-1** | **-1** | **-1** |

**The readings:**
- **0 is binary, 1 is text, and 2 raises 5**, even with an empty argument.
- **Any other value is a Windows locale ID, and the compare is that locale's text order.**
  - Swedish (1053) sorts ä and ö after z.
  - Czech (1029) sorts "ch" after "d", and so does 5, which is Czech's bare language ID.
  - Bare IDs (3, 4, 5, 100) and 1024, the user default, are accepted.
  - The rows from #881 already had 3, 4, 5, 1033, 2057 and 4096 accepted, and 3000, 4095, 32767 and 65536 refused with 5.
- **An invalid ID raises 5:** 999999, -2 and 65535.
  - **The exception:** with an empty argument StrComp returns -1 and raises nothing. 2 still raises 5 there.

#### InStr(1, s, find, compare)

| s, find | 0 | 1 | 2 | 3 | 1033 | 1031 | 999999 | -2 |
|---|---|---|---|---|---|---|---|---|
| "xxaBb", "b" | 5 | 4 | E5 | 4 | 4 | 4 | E5 | E5 |
| "Straße", "ss" | 0 | 5 | E5 | **0** | 5 | 5 | E5 | E5 |
| "xÆy", "ae" | 0 | 2 | E5 | **0** | 2 | 2 | E5 | E5 |
| "co-op", "coop" | 0 | 0 | E5 | 0 | 0 | 0 | E5 | E5 |
| "", "a" | 0 | 0 | **0** | 0 | 0 | 0 | **0** | **0** |

**The expansions (ß as ss, Æ as ae) depend on the locale ID,** and I can't find the rule. Here's a wider row for the same three searches:

| Search | 1 | 3 | 1027 (ca) | 4 | 2052 (zh) | 5 | 1029 (cs) | 7 | 1031 (de) | 9 | 1033 | 2057 | 100 | 1024 | 2048 |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| "Straße", "ss" | 5 | 0 | 0 | 0 | 0 | 0 | 5 | 0 | 5 | 0 | 5 | 5 | 0 | 0 | 0 |
| "xÆy", "ae" | 2 | 0 | 0 | 0 | 0 | 0 | **0** | 0 | 2 | 0 | 2 | 2 | 0 | 0 | 0 |
| "ß", "s" | 1 | 0 | 0 | 0 | 0 | 0 | 1 | 0 | 1 | 0 | 1 | 1 | 0 | 0 | 0 |

- **Bare language IDs aren't the rule.** Catalan's full ID 1027 doesn't expand either.
- **"Ask the default locale" isn't the rule either.** 1024 (user default) and 2048 (system default) don't expand, though 1 does.
- **Czech expands ß but not Æ.**

**StrComp, by contrast,**, treats ß and "ss" as equal under every ID in the wider row. A case-only difference is found under every valid ID.

**What resisted explanation.** Which IDs make InStr read ß as ss and Æ as ae. Two candidate rules are refuted:
- **"Only full locale IDs":** Catalan's 1027 doesn't expand, while 1029, 1031, 1033 and 2057 do.
- **"The default locale":** 1024 and 2048 don't expand, though a compare of 1 does.

Czech expands ß but not Æ. I'd not generalise past these rows.

### Scope

- **Measured under en-GB only, on the characters cp1252 holds.** HexIDE applies the same rows in every locale, which is a wrong-value risk wherever Windows tailors the sort, such as Swedish å, ä and ö, or Turkish i. #1573 owes that measurement.
- **A character outside cp1252 is unmeasured.** HexIDE sorts it after all of them, by its invariant lower case.
- **Like is a different operation.** Its `Option Compare Text` is a per-character case fold (#1547), and nothing here measured Like against the compare.

### In HexIDE (#1567)

- `VbTextCompare` holds the measured rows, generated from this order in `VbTextCompare.Table.cs`, with the seven expansions. It also has the search rule.
- `TextCompareTests` replays every pair of the order and of the held-out set, and the position pairs. `OptionCompareTextTests` runs the operator and search probes as programs.
- The rule is used by:
  - the comparison operators and `Select Case` in an `Option Compare Text` module, the module being the one the comparison is written in;
  - a Collection's keys, hashed on what the comparer compares;
  - InStr, InStrRev, Replace, Split and Filter with a compare of 1.
- There is no ICU anywhere in it, so every host gives the same answers.

### The Text order under other user locales (2026-11-21, #1573, SP6)

**The question.** "The whole Text order" above was measured under en-GB, and HexIDE applies it in every locale. Does vb6.exe's text compare follow the user's locale?

**The probe.** It used the same 321 strings, with every single character `Chr(0)` to `Chr(255)`, the empty string and the 64 longer ones. They were taken from the measured order in the test suite (`TextCompareMeasurements.FullOrder`) and built with `ChrW`, so the source's code page didn't matter. They were sorted the same way, by a stable insertion sort with `StrComp(a, b, vbTextCompare)`, and logged as groups of equals.

The guest's user locale was set with `Set-Culture` before each run, and the program read `GetUserDefaultLCID` back. Built with `/make` on vb6.exe SP6 as native code and as p-code, the two orders were identical in every locale.

| User locale | Against the en-GB order above |
|---|---|
| en-GB (2057) | **identical**, all 234 groups: the order above, measured again |
| de-DE (1031) | **identical** |
| tr-TR (1055) | Ç sorts after every `c` string, before D. Ö sorts after the `OE` group, before P. Ü sorts after the `UE` group, before V. Each case pair stays equal |
| sv-SE (1053) | Ü (and ü) sorts with Ÿ, before Z. Å, Ä, Ö and Ø move to the end, after Ž, in that order |
| da-DK (1030) | Ü sorts with Ÿ, before Z. Æ, Ä, Ø, Ö, Å and `"aa"` move to the end, after Ž, in that order. Æ no longer equals `"ae"`, which makes 235 groups |

**What it shows.**
- **vb6.exe's text compare follows the user's locale.** Swedish, Danish and Turkish tailor it as Windows sorts those languages: `"aa"` sorts as Å in Danish. The table above is en-GB's, and de-DE shares it.
- **Not all of the tailoring is applied.** Under tr-TR, `i` and `I` stay equal ("The case of a character under Turkish and three other user locales", #797).

**HexIDE,** per #1573's own account: it applies the en-GB table in every locale. So under sv-SE, da-DK or tr-TR, `Option Compare Text`, `Select Case`, Collection keys and `StrComp(..., 1)` give en-GB's answers where these differ. Mapping the locales is #5105.

**Not measured:**
- the held-out set and the expansion sweep under these locales;
- any locale beyond these five;
- characters outside code page 1252.
