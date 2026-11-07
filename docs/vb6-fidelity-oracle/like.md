## Like (2026-10-23, #1547)

Measured with `/make` on vb6.exe 6.00.8176 (the release build) with the MSVBVM60 6.00.9848 (SP6) runtime, under en-GB. These are run-time rows, and native and p-code agree on every one of them. There were five batches, about 195 rows in all.

Each row ran `r = (s Like p)` on Variant operands in two modules, one `Option Compare Binary` and one `Option Compare Text`, under `On Error Resume Next`, and logged `TypeName` and the value, or the error. A further 17 rows put typed operands inline, one procedure each. Characters outside ASCII were built with `Chr` and `ChrW`, because the probe files are written as ASCII. `LikeOperatorTests` is generated from these probes and their answers, so every row here is pinned.

**What the issue guessed, and what vb6.exe said:**
- **`[]` is not an error.** It matches nothing and takes no character: `"ax" Like "a[]x"`, `"a" Like "a[]"` and `"" Like "[]"` are all True.
- **An invalid list is error 93 only when it is reached with a character to match.** `"x" Like "y["`, `"" Like "*["` and `"a" Like "a[z-a]"` are False, with no error. `"abc" Like "a*["` is 93, because `*` gives `[` a character.
- **Ranges compare UTF-16 code units, not ANSI bytes.** `"€" Like "[" & Chr(127) & "-" & Chr(129) & "]"` is False, although `€` is byte 128. `"€" Like "[~-" & ChrW(&H2100) & "]"` is True.
- **Under Text, a range is not compared by collation.** `"é" Like "[a-z]"` is False under Text, as under Binary.

| s | pattern | Binary | Text |
|---|---|---|---|
| `"abc"` | `"abc"`, `"a?c"`, `"a*"`, `"*c"`, `"*"`, `"a**c"`, `"a*c*"`, `"*b*"` | True | True |
| `"aXbXc"`, `"abcbc"` | `"a*b*c"`, `"a*bc"` | True | True |
| `"abc"` | `"ABC"` | False | True |
| `"ac"` | `"a?c"` | False | False |
| `""` | `"*"`, `""` | True | True |
| `""` | `"?"`, `"*?"` | False | False |
| `"a"` | `""`, `"?*?"` | False | False |
| `"5"`, `"55"` | `"#"`, `"##"` | True | True |
| `"0"`, `"9"` | `"#"` | True | True |
| `"/"`, `":"` | `"#"` | False | False |
| `"a"`, `Chr(178)` ², `Chr(185)` ¹ | `"#"` | False | False |
| `ChrW(&HFF10)`, `&HFF15`, `&HFF19`: fullwidth ０, ５ and ９ | `"#"` | **True** | True |
| `ChrW(&HFF1A)`, `&HFF0F`, `&H660`, `&H6F5`, `&H966`: other "digits" | `"#"` | False | False |
| `ChrW(&HFF15)` | `"[0-9]"`, `"5"` | False | False |
| `"b"`, `"d"` | `"[abc]"` | True, False | True, False |
| `"d"`, `"b"` | `"[!abc]"` | True, False | True, False |
| `"M"`, `"Z"` | `"[a-z]"` | False | **True** |
| `"m"` | `"[A-Z]"` | False | True |
| `"d"`, `"f"` | `"[a-ce-g]"` | False, True | False, True |
| `"-"` | `"[a-]"`, `"[-a]"`, `"[-]"` | True | True |
| `"-"` | `"[!-]"` | False | False |
| `"d"`, `"-"`, `"e"` | `"[a-c-e]"` | False, True, True | the same |
| `"["`, `"*"`, `"?"`, `"#"` | `"[[]"`, `"[*]"`, `"[?]"`, `"[#]"` | True | True |
| `"x"` | `"[*x]"` | True | True |
| `"]"` | `"]"`, `"[]]"` | True | True |
| `"x"`, `"a]"` | `"[]x]"`, `"[]]"` | False: `[]` then `x]` or `]` | False |
| `"!"`, `"a"` | `"[!]"` | True, False: `[!]` is a `!` | True, False |
| `"!"` | `"[!!]"`, `"[a!]"` | False, True | False, True |
| `"x"`, `"!]"` | `"[!]]"` | False, True | False, True |
| `"a"`, `"ab"`, `"["` | `"[a"`, `"a[b"`, `"["` | **93** Invalid pattern string | 93 |
| `"a"`, `"b"` | `"[z-a]"` | 93 | 93 |
| `"b"` | `"[!z-a]"` | 93 | 93 |
| `"a"` | `"[az-a]"` | **True**: read item by item, and `a` matched first | True |
| `"a"` | `"[ab"` | 93: the `]` is needed first | 93 |
| `""` | `"[z-a]"`, `"[a"`, `"["`, `"a["`, `"[!]"` | False | False |
| `"a"` | `"*["`, `"*[z-a]"` | 93 | 93 |
| `"b"` | `"a[z-a]"`, `"[a][z-a]"` | False | False |
| `"a"` | `"[Z-a]"` | True | **93**: Text takes the bounds in lower case, `z-a` |
| `"b"`, `"Q"` | `"[a-Z]"` | **93** | True |
| `"_"`, `"^"` | `"[A-z]"` | True | **False** |
| `` "`" ``, `"B"` | `"[_-a]"` | True, False | True, False |
| `Chr(233)` é | `"[a-z]"`, `"e"` | False | False |
| `Chr(233)` é | `Chr(201)` É | False | True |
| `Chr(201)` É | `"[" & Chr(224) & "-" & Chr(255) & "]"` | False | True |
| `ChrW(&H178)` Ÿ | the same | False | True |
| `ChrW(&H212A)` Kelvin sign | `"k"`, `"[j-l]"` | False | **False** |
| `ChrW(&H130)` İ | `"i"`, `"[h-j]"` | False | **False** |
| `ChrW(&H131)` ı | `"i"` | False | False |
| `Chr(246)` ö, `Chr(241)` ñ | `"[o-p]"`, `"[a-z]"` | False | False |
| `Chr(223)` ß | `"ss"`, `"s*"` | False | **True** |
| `Chr(223)` ß | `"?"`, `"[" & Chr(223) & "]"` | True | True |
| `Chr(223)` ß | `"??"`, `"[s-t]"` | False | False |
| `"ss"` | `Chr(223)`, `"[" & Chr(223) & "]"` | False | **True** |
| `Chr(230)` æ, `Chr(230) & "x"` | `"ae"`, `"a*"`, `"ae*"` | False | True |
| `"ma" & Chr(223) & "e"`, `"mass"`, `"ssx"` | `"mass?"`, `"ma" & Chr(223)`, `"*" & Chr(223) & "x"` | False | True |
| `ChrW(&HFF41)` ａ | `"a"`, `"[a-z]"` | False | False |
| `ChrW(&HFF41)` | `"?"` | True | True |
| `ChrW(&H2100)` | `"?"` | True | True |
| `ChrW(&H2100)` | `"[?]"` | False: no ANSI conversion makes it `?` | False |
| a surrogate pair | `"?"`, `"??"` | False, True | False, True |
| `"a" & vbLf & "b"`, `"a" & Chr(0) & "b"` | `"a?b"` | True | True |
| `String(1000, "a")` | `"*a*a*a*b"` | False, at once | False |
| `"a"` | `String(5000, "*") & "a"` | True | True |
| `5`, `5.5`, `1E+20`, `True`, `#1/2/2003#`, `CCur(1.5)`, `CDec(1.5)`, `CSng(0.1)` | `"#"`, `"#.#"`, `"*E*"`, `"True"`, `"*2003*"`, `"1.5"`, `"1.5"`, `"0.1"` | True: the text `&` would make | True |
| `True` | `"-1"` | False | False |
| `"5"` | `5` | True | True |
| `Empty` | `""`, `"*"` | True | True |
| `"a"` | `Empty` | False | False |
| `Null` on either side, or both | | **Null** | Null |
| `CVErr(5)`, `Array(1)` | `"*"` | **13** Type mismatch | 13 |
| `Nothing` | `"*"` | **91** | 91 |
| `StrConv("ab", vbFromUnicode)`, a Byte array | `"*"` | True: taken as text | True |

**Typed operands inline** gave the same answers: `"a" Like "[a"` is 93 and `5 Like "#"` True. `Like` binds more tightly than `Not`, `&` binds more tightly than `Like`, and `=` is not tighter than `Like`:
- `"a" Like "a" = True` is True;
- `Not "a" Like "b"` is True;
- `"a" & "b" Like "ab"` is True.

**What resisted explanation, and is recorded as measured, not as a rule:**
- **The Text case fold.** The rows fit one model: a character, and each bound of a range, is replaced by its lower case where the two form a pair that maps back. Ÿ and ÿ do; the Kelvin sign's lower case is `k`, whose upper case is `K`, so it stays as it is, and so does İ. Nothing here shows that this is how Windows decides, only that no row contradicts it.
- **Text treats a ligature or a sharp s as the letters it stands for.** It does so mid-pattern too, so `"ß" Like "s*"` and `"ss" Like "[ß]"` are True, while `?` still takes ß as one character. No character-at-a-time rule produces those rows, and no rule is offered for them.

### In HexIDE (#1547)

`VbLike.Matches` is a greedy matcher that backtracks to the last `*`. It reads a list only when a character is there to match it, as vb6.exe does.
- `Option Compare` is now recorded for each module (`PrePass.OptionCompareText`). Like is the only thing that reads it so far: the comparison operators still compare strings as Binary (docs/interpreter-gaps.md).
- The case fold is the model above.
- The ligature and sharp-s rows are False under Text here, the same limit `Split` and `Filter` have (#1479). `LikeOperatorTests` pins each of them beside vb6.exe's answer.
- Two rows are not reproduced yet: `CVErr` is unimplemented, and so is `StrConv` (#1560).

