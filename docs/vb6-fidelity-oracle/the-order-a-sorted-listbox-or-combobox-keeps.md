## The order a Sorted ListBox or ComboBox keeps (2026-11-20, #5059, SP6)

**The rig.**
- **Build:** vb6.exe SP6, `/make`, native code and p-code. They logged the same order in every list.
- **The guest:** an en-GB Windows 10, ANSI code page 1252.
- **The controls:** each probe loaded a form holding four controls saved `Sorted = True`: a plain ListBox, a `Style = 1` (checkbox) ListBox, a `Style = 2` ComboBox and a `Style = 0` ComboBox.
- **What was logged:** each list's items in order. Non-ASCII items went in through `Chr$`, so the source stayed ASCII.

**All four kinds of control keep the same order.** The first probe filled each of the four with the same 41 items and got the same order four times, so the later probes used one ListBox or ComboBox.

### What was overturned

Two first guesses were wrong.
- **#4209's round 2 sorted a checkbox list with .NET's current-culture, case-insensitive comparison,** inserting after any equal item. On this rig's .NET (ICU), that puts `-a` and `'a` among the symbols and `[a` among them too. It also orders `coop`, `co-op` and `Co-op` differently. VB6 does neither.
- **Equal items don't keep the order they were added in,** and don't simply put the newest first: see the model below.

### The orders

**41 items**, added in this order: `b B a A 10 9 1 2 a10 a2 a1 coop co-op "co op" Co-op _a ~a 'a -a " a" é e f E x x "" Z z [a {a 0 Æ ß ss ae ab a-b "a b" AB aB`. VB6 kept:

`""`, `" a"`, `_a`, `{a`, `~a`, `0`, `1`, `10`, `2`, `9`, `A`, `a`, `'a`, `-a`, `"a b"`, `a1`, `a10`, `a2`, `AB`, `aB`, `ab`, `a-b`, `ae`, `Æ`, `B`, `b`, `"co op"`, `coop`, `Co-op`, `co-op`, `E`, `e`, `é`, `f`, `ss`, `ß`, `x`, `x`, `z`, `Z`, `[a`

`NewIndex` read 19 after the last `AddItem`. That's `aB`'s position.

**Every printable ASCII and Latin-1 character, one per item,** added in code order (U+0020 to U+007E, then U+00A0 to U+00FF):

```text
U+00AD ' - U+0020 U+00A0 ! " # $ % & ( ) * , . / : ; ? @ \ ] ^ _ ` { | } ~ ¡ ¦ ¨ ¯ ´ ¸ ¿ ¢ £ ¤ ¥ + < = > ± « » × ÷
§ © ¬ ® ° µ ¶ · 0 ¼ ½ ¾ 1 ¹ 2 ² 3 ³ 4 5 6 7 8 9 a A ª á Á à À â Â ä Ä ã Ã å Å æ Æ b B c C ç Ç d D ð Ð e E é É è È ê
Ê ë Ë f F g G h H i I í Í ì Ì î Î ï Ï j J k K l L m M n N ñ Ñ o O º ó Ó ò Ò ô Ô ö Ö õ Õ ø Ø p P q Q r R s S ß t T þ Þ
u U ú Ú ù Ù û Û ü Ü v V w W x X y Y ý Ý ÿ z Z [
```

**56 strings of accents, ligatures and hyphens,** added in the order `u ü v U Ü n ñ o ö oe œ ae æ Æ a å Å b c ç d ð th þ t ss ß st y ÿ z é e f è ê ë E É resume résumé Resume resumé coop co-op coöp a-b ab "a b" a'b a_b a.b ab- -ab a--b a'-b`:

`a`, `Å`, `å`, `"a b"`, `a.b`, `a_b`, `ab`, `ab-`, `a'b`, `a'-b`, `a-b`, `a--b`, `-ab`, `Æ`, `æ`, `ae`, `b`, `c`, `ç`, `coop`, `co-op`, `coöp`, `d`, `ð`, `E`, `e`, `É`, `é`, `è`, `ê`, `ë`, `f`, `n`, `ñ`, `o`, `ö`, `œ`, `oe`, `Resume`, `resume`, `resumé`, `résumé`, `ß`, `ss`, `st`, `t`, `þ`, `th`, `U`, `u`, `Ü`, `ü`, `v`, `y`, `ÿ`, `z`

**A leading `[`.** Items added `a z m [b [A [ [-a-] [-b-] a[b ab ac z[ za ]a` were kept as:
`]a`, `a`, `a[b`, `ab`, `ac`, `m`, `z`, `z[`, `za`, `[`, `[A`, `[-a-]`, `[b`, `[-b-]`
- **Only a leading `[` goes last.** `a[b` and `z[` sort where any symbol would, and `]a` sorts first.
- **The `[` items order normally among themselves.**
- **A list started with `[a`** still ends with it: `[a z a m _a 0 Z` was kept as `_a 0 a m Z z [a`.

**Symbols,** added in the first order below, were kept in the second:

```text
added: m [a ]a ^a `a @a a z !a #a (a )a *a /a :a ;a <a =a >a ?a |a }a $a %a &a +a ,a .a "a Za za
kept:  !a "a #a $a %a &a (a )a *a ,a .a /a :a ;a ?a @a ]a ^a `a |a }a +a <a =a >a a m z za Za [a
```

An item meant to be `\a` reached the list as `Chr(7)`, by way of the probe harness, so it's left out here.

**Ligatures and their expansions,** each pair in a fresh list:

| Added | Kept |
|---|---|
| `ß`, `ss` | `ss`, `ß` |
| `ss`, `ß` | `ß`, `ss` |
| `æ`, `ae` | `ae`, `æ` |
| `œ`, `oe` | `oe`, `œ` |
| `þ`, `th` | `th`, `þ` |
| `Æ`, `æ`, `ae` | `ae`, `æ`, `Æ` |
| `ae`, `AE`, `Æ` | `Æ`, `AE`, `ae` |

**The comparisons themselves.** The same probe called `lstrcmpi` (kernel32's `lstrcmpiA`, through `Declare`) and `StrComp(x, y, vbTextCompare)`. The two agreed on every pair:

| Pair | Result |
|---|---|
| `[a` and `z`, `[a` and `a`, `a[b` and `ab`, `a[b` and `ac` | -1 |
| `ß` and `ss`, `Æ` and `ae`, `Æ` and `æ`, `æ` and `ae`, `œ` and `oe`, `þ` and `th`, `a` and `A`, `b` and `B` | 0 |
| `co-op` and `coop`, `-a` and `'a` | 1 |

So a ligature compares equal to its expansion, and `[a` compares below `z`. A list's special place for a leading `[` is the list's own, not the comparison's.

### A model that fits every list

This model reproduces the order of all 12 lists checked against it, 359 items, character for character. It's a fit, not a rule read out of VB6. It was checked with Windows' own NLS comparison on another Windows machine, through .NET's NLS mode.
- **Compare:** Windows' case-insensitive comparison in the user's locale, the comparison `lstrcmpi` makes. Any string beginning with `[` sorts after every string that doesn't. Two strings that both begin with `[` compare normally.
- **Insert:** a binary search over the list so far. Each probe looks at index `(min + max - 1) \ 2`.
  - An exact tie (the comparison gives 0) inserts there, before the item probed.
  - Otherwise the search narrows, and the item goes in where it ends.
  - So a new item lands before an equal one it happens to probe. That's how `AB`, `aB`, `ab` and the `Æ`/`æ`/`ae` lists came out.
- **Rejected:** probing at `(min + max) \ 2` fits ten of the twelve lists, but not the 41-item list or the `ae`, `AE`, `Æ` one.

### Not measured

- **Any other user locale.** The comparison follows it (reasoned, not measured), so a Swedish machine, where `å` sorts after `z`, would order these lists differently.
- **Characters outside code page 1252.**
- **`AddItem` with an index on a sorted list,** which [#821's page](a-lists-members-listcount-list.md) measured: the index wins.
