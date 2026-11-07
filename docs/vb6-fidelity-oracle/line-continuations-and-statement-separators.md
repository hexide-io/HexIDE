## Line continuations and statement separators (2026-09-02)

319 clean-room cases compiled against real vb6.exe by `scripts/vb6-legality.ps1`. **243 legal, 76
illegal; 16 predictions wrong and 60 genuine unknowns resolved.** Every case was authored from the VB6
Language Reference and the grammar, under the clean-room rule in `vb6-grammar-fixes.md` — no GPLv3 VB6
grammar or test suite was consulted. That constraint is permanent, and the provenance record lives in
that document rather than here.

### An unterminated string literal auto-closes at end of line

| probe | measured |
|---|---|
| `Debug.Print "A` | **legal** — the string closes itself at the newline |
| `s = "A` | **legal** — same in an assignment |

Not a quirk of `Debug.Print`, and not what most people expect of a language with no multi-line strings.

### A trailing `_` inside a string continues the line — but only sometimes

| probe | measured |
|---|---|
| `Debug.Print "A _` then `B"` | **legal** |
| `Debug.Print "A _` alone | **illegal** — Syntax error |
| `s = "A _` then `B"` | **illegal** — Syntax error |
| `Debug.Print "AB_` (no space before `_`) | **illegal** — the whitespace-before rule holds inside a string too |

The first row only makes sense if the `_` joins the two physical lines, giving `Debug.Print "A B"` — and
the second row supports that, because alone it swallows the following `End Sub`. **But then the third row
should be legal, and it reproducibly is not**, with or without a later use of `s`.

Recorded as measured and unexplained rather than tidied into a rule. Both halves reproduce in isolation.
Something distinguishes an output list from an assignment here, and this document would rather say so than
invent the reason.

### Colons, and where they are refused

| probe | measured |
|---|---|
| `Debug.Print 1:` (trailing colon) | legal |
| a colon-only line at module level | legal |
| `Enum` members colon-joined | legal |
| `Type` header colon first member, and members colon-joined | legal |
| `Attribute` colon `Option` | legal |
| a statement colon `End If` | legal |
| a label named `Error` | legal — a statement keyword is still usable as a label |
| **two `Declare`s colon-joined at module level** | **illegal** — "Expected: statement or end of statement" |
| **a continuation inside an `Enum` member value** | **illegal** — "Invalid inside Enum" |

The last two are the only places in the whole sweep where colons and continuations are refused in a
context that otherwise accepts them. Continuations work almost everywhere — including the canonical
multi-line Win32 `Declare` — which makes the `Enum` exception worth remembering.

### The harness lesson, which cost more than the findings

The first run reported **35** wrong predictions. Nineteen of them were the harness's fault, in two ways:

1. It appended its own `Sub Main` to every module-scope case, including the many that define one. VB6
   answers *"Ambiguous name detected: Main"*, and the case is recorded illegal for a reason with nothing
   to do with what it tested — quietly converting a whole family of declaration probes into confident
   wrong facts. The canonical multi-line `Declare` was among them.
2. The guard added to fix that silently never fired, because a shell heredoc turned the `\b` in its regex
   into a literal **backspace byte** (0x08). The pattern `Sub\s+Main<BS>` cannot match anything, and the
   character is invisible in every editor, diff and terminal rendering — it was found only by hexdumping
   the line.

**A corpus is only as good as the harness, and a harness bug looks exactly like a language discovery.**
Both symptoms presented as surprising VB6 behaviour in the direction the author half-expected, which is
the most dangerous shape an error can take. Check that a suspicious cluster shares a compiler *message*
before believing it shares a *cause*.

### The single-line If: where a colon-joined tail belongs (2026-09-02)

The question the conformance corpus was built to ask, and the one a legality oracle cannot answer — the
line compiles either way, so only running it says what it means.

| probe | measured |
|---|---|
| `If False Then A : B` | **`[]`** — NEITHER runs. The whole joined tail is the Then branch. |
| `If True Then A : B` | `[AB]` — the whole tail runs, in order |
| `If False Then A : B Else C` | `[C]` — Else binds to the If, after the entire tail |
| `If False Then A : Else C` | `[C]` — a colon may sit immediately before Else |

**The intuitive reading is wrong, and wrong in the dangerous direction.** Treating the tail as
unconditional would silently execute code the program said not to — no error, nothing to debug from. It
is the same harm shape as the silent-failure class closed in #191, arrived at from a completely different
direction.

A consequence worth stating for implementers: because the branch is a run of statements rather than one,
control leaving it must stop the rest. `If i = 2 Then Exit For : s = s & "X"` must not append.

### A designer-file token was eating statement separators (2026-09-02)

Not a VB6 fact but a HexIDE one, recorded here because the corpus is what found it and because it is the
best example so far of why grouping failures by SYMPTOM misleads.

`FRX_OFFSET` matches a companion-binary offset in a designer file — `Picture = "Form1.frx":0000`. It was
written `COLON [0-9A-F]+` and, being an ordinary lexer rule, was live in ordinary code. A-F are hex
digits, so:

| source | lexed as |
|---|---|
| `Debug.Print "A":Debug.Print "B"` | `:D` became an OFFSET, swallowing the separator |
| `a = 1:b = 2` | `:b` likewise |
| `Skip:Debug.Print` | `:D` likewise |

Nine corpus cases, spread across three different areas and filed under three different apparent causes —
labels, separators, control flow — were all this one token. Narrowing it to the shape VB6 actually writes
(a leading digit and at least four hex digits, which zero-padding guarantees) fixed all nine at once.

**It was predicted before it was measured.** The agent auditing the corpus read the lexer and flagged
`FRX_OFFSET` as a hazard, noting that every existing tight-colon case happened to put a non-hex letter
after the colon and so missed it. It then wrote the cases that caught it.

### A continuation may fall inside a multi-word keyword (2026-09-02)

`End _ Sub` is legal, and so is every other multi-word keyword split the same way. Ten new corpus cases
(`corpus/conformance/keyword-splitting.json`), all compiled by `vb6.exe`:

| probe | verdict |
|---|---|
| `For _` ⏎ `Each c In Array(1, 2)` | legal |
| `On Error Resume _` ⏎ `Next` | legal |
| `Select _` ⏎ `Case n` | legal |
| `Declare Function … Lib _` ⏎ `"kernel32"` | legal |
| `Declare Function … Lib"kernel32"` — **no separator at all** | **legal** |
| `… Alias _` ⏎ `"GetTickCount"` | legal |
| `Option Base _` ⏎ `1` | legal |
| `End _` ⏎ `  _` ⏎ `Sub` — two continuations in a row | legal |
| `If i = 2 Then Exit _` ⏎ `For` — split keyword inside a single-line If | legal |
| `End _` ⏎ `'a comment` ⏎ `Sub` | **illegal** |

Ten predictions, zero disagreements — the first clean sweep this corpus has had, and worth noting only
because the two cases marked `unsure` were the ones that mattered.

**`Lib"kernel32"` compiles.** That was the genuinely open question, and the answer decided a grammar
change. HexIDE spells the multi-word keywords as single lexer tokens joined by a literal space, which is
right — `End` and `Sub` mean something else apart than together — but three of them (`Select Case`,
`For Each`, `Resume Next`) are assembled by the *parser* from two tokens with a mandatory `WS` between,
and a continuation is hidden from the parser by the time it looks. For those three, making the `WS`
optional is provably not a widening: `SelectCase` lexes as one `IDENTIFIER`, so the parser can only ever
see `SELECT` and `CASE` adjacent if something hidden separated them.

That argument does not reach `Lib "x"`, because a keyword and a string literal *can* abut with nothing
between them — which is exactly why it was measured instead of reasoned about. The measurement came back
better than the argument would have: VB6 accepts the abutting form outright, so `WS?` there is not a
tolerated widening but a faithful one.

**The illegal case is the useful one.** A comment is hidden from the parser on the same channel as a
continuation, so a grammar that admitted "anything hidden may separate the two words" would have admitted
`End _` ⏎ `'comment` ⏎ `Sub` as well. VB6 rejects it — a comment ends the statement, continuation or not.
The separator has to admit continuations and whitespace specifically, not hidden tokens generally.

**Incidental:** `Rem` followed by a **tab** is a comment. `COMMENT` shared the same single-space spelling
and was widened with the keywords.

Corpus false rejections: 21 → 14, clearing this group entirely. What remains is four causes —
`REM-FORM`, `STRING-CONTINUATION`, `LABEL`, `OTHER`.

### `Rem` takes no separator, and the documentation is wrong about it (2026-09-02)

The reference says "Rem followed by a space". It is not true. 21 new corpus cases
(`corpus/conformance/rem-forms.json`), compiled by `vb6.exe`, plus a behavioural round
through the value oracle for the cases where compiling proves nothing.

**The rule, as measured:** *`Rem`, standing as a whole word, begins a comment that runs to the end of the
physical line. No separator is required. A trailing `_` extends it onto the next line. It is reserved, and
is never an identifier anywhere.*

| probe | verdict |
|---|---|
| `Rem` alone on a line | legal |
| `Rem:` alone on a line | legal |
| `Rem=1` | legal |
| `Rem'not really nested` | legal |
| `Rem"text"` | legal |
| `Rem` + tab + EOL | legal |
| `Dim RemX As Long` / `RemX = 5` | legal — an ordinary identifier |
| `Dim Rem1 As Long` | legal |
| `Dim Rem As Long` | **illegal** — Syntax error |
| `Sub Rem()` | **illegal** — Expected: identifier |
| `Private Type TRec` / `Rem As Long` / `End Type` | **illegal** — *User-defined type without members* |
| `Private Enum EKind` / `Rem` / `End Enum` | **illegal** — *Enum without members* |
| `Private Sub Take(ByVal Rem As Long)` | **illegal** — Expected: identifier |
| `If True Then Rem a remark` | **illegal** — Syntax error |
| `If True Then s = "then" Else Rem nothing` | legal |
| `If True Then s = "then" Else` — bare trailing Else | legal |
| `Rem a remark _` above `End Sub` | **illegal** — *Expected End Sub* |

**The two error messages are the interesting rows.** *"User-defined type without members"* and *"Enum
without members"* do not say "Rem is reserved" — they say the `Rem As Long` line **was not there**. That is
the proof that `Rem` is a comment even in a declaration position, and it is stronger than the plain
syntax errors, which could have meant several things.

**It also caught a wrong reading of my own, immediately.** The first version of the enum probe listed `Rem`
*alongside* a second member and compiled — which I took as "Rem may name an enum member". Exactly
backwards: it compiled because the `Rem` line vanished and the *other* member carried the enum. Rewriting
it with `Rem` as the only member turned a false "legal" into a decisive "illegal". A case that appears to
prove something can prove its opposite, and the way to tell is to remove everything else from the probe.

**Behaviour, from the value oracle** — a legality verdict cannot answer these, because the line compiles
either way and the two readings differ in what runs. Each probe builds a string from the statements that
actually executed:

| probe | measured | meaning |
|---|---|---|
| `Rem: s = s & "B"` | `A` | B does **not** run — the colon is comment text |
| `s = "A": Rem a remark: s = s & "B"` | `A` | the remark eats the rest of the line, colons included |
| `Rem a remark _` ⏎ `s = s & "B"` ⏎ `s = s & "C"` | `AC` | the continuation **swallows the next line** |
| `GoTo 10` … `10 Rem arrived` ⏎ `s = s & "B"` | `AB` | a line number carrying only a remark is still a target |
| `If False Then s = s & "T" Else Rem nothing` | `AB` | an Else branch may be nothing but a comment |
| `Rem=1`, `Rem'q`, bare `Rem`, `Rem:` | all skipped | comments, every one |

`Rem: s = s & "B"` is the one with teeth. HexIDE lexed it as `REM`, `COLON`, and a live statement, so a
parser that accepted the line would have **executed a statement VB6 treats as a remark** — no error and
nothing to debug from. Same harm shape as the single-line-If tail, reached from a different direction.

**`Then Rem` is illegal and `Else Rem` is legal.** Measured, and I have no rule I would defend for it. A
comment is invisible to the parser in both positions, so whatever distinguishes them is not syntactic. A
bare trailing `Else` with nothing after it is also legal, which at least makes the Else half consistent
with itself. Recorded as measured-but-unexplained rather than tidied into a story.

**What this cost, and what it bought.** Fixing it needed three things in the lexer, each of which is a way
to get it wrong:

1. A guard on what follows `Rem`. Without it `RemX = 5` becomes a comment and the assignment is **deleted** —
   a wrong value, which the guardrail refuses outright.
2. **No leading `WS?`.** With one, the rule starts a character early, at the space in `Dim RemX`, and
   matches `" Rem"` — four characters, which beats the one-character `WS` token and produces the same
   deleted assignment from the other side. The guard cannot help: it governs how far a match runs, not
   where it begins.
3. **Position ahead of every keyword.** ANTLR settles an equal-length match by declaration order, and a
   bare `Rem` is matched at exactly three characters by the comment rule, the `REM` keyword and
   `IDENTIFIER` alike.

Two of those were found by the corpus rather than by reasoning, and both would have shipped silently.

### The other direction was never gated (2026-09-02)

Not a VB6 fact but a process one, recorded because it is the second time this corpus has revealed a gate
that was quietly guarding less than it appeared to.

`CorpusConformanceTests.Compare()` has always returned **both** lists — code VB6 accepts that HexIDE
rejects, and code VB6 rejects that HexIDE accepts. Only the first was ever asserted. The second was
computed, returned, and consumed by nothing.

That is tolerable while every change narrows the grammar, and dangerous the moment one widens it — which
the `Rem` work does, and whose central hazard is precisely over-reach. Gated now: **43 false acceptances**,
grouped by cause into six defects. `UNDERSCORE-IDENTIFIER` is 16 of them, all one lexer character:
`LETTER` includes `_`, so a lone `_` is an identifier here and never is in VB6, and `x = 1 +_` completes as
arithmetic against a variable named `_` instead of failing.

That fix was tried in this change and **reverted**. `_` alone on a line is legal VB6, and `NEWLINE`
(`WS? '\r'? '\n' WS?`) has already eaten the space that `LINE_CONTINUATION` (`[ \t]+ '_' …`) needs to
recognise it — so removing the underscore from the identifier start refuses two legal cases to fix one
illegal. One false acceptance bought for two false rejections is the wrong direction, and the honest
record of it is worth more than a fix that traded badly.

### Two asymmetries that were sitting in the corpus unread (2026-09-02)

Both of these were measured months ago and recorded as pass/fail verdicts. Neither had ever been turned
into a *rule*, which is the difference between a corpus and an oracle — and both were found only when the
false-acceptance list was grouped by cause and someone had to say what each group actually was.

**A continuation line of nothing but an underscore is legal if and only if it is indented.**

| probe | verdict |
|---|---|
| `x = 1 + _` ⏎ `_` ⏎ `2` — the lone underscore in column 1 | **illegal** |
| `x = 1 + _` ⏎ `  _` ⏎ `2` — the same file, indented two spaces | **legal** |

Two files differing by two space characters. So the whitespace before the underscore is *literally
required*, not merely how the documentation phrases it — and a continuation is genuinely
whitespace-then-underscore-then-end-of-line, with no exemption at the start of a line.

This is why the obvious repair for HexIDE's biggest remaining divergence does not work. `LETTER` includes
`_`, so a lone `_` lexes as an identifier and `x = 1 +_` completes as arithmetic instead of failing —
16 of the 43 false acceptances, one character. But removing `_` from the identifier start alone breaks the
indented form, because `NEWLINE` (`WS? '\r'? '\n' WS?`) has already eaten the space that
`LINE_CONTINUATION` (`[ \t]+ '_' …`) needs to recognise it. The two rules are arguing over the same
whitespace, and until that is settled the fix trades one false acceptance for two false rejections.

**A continuation is honoured in every declaration block except an Enum body.**

| probe | verdict |
|---|---|
| `Private Type TPoint` / `X _` ⏎ `As Long` / `End Type` | legal |
| `Private Declare Function GetTickCount _` ⏎ `Lib "kernel32" _` ⏎ `() As Long` | legal |
| `Attribute VB_Name = _` ⏎ `"Mod1"` | legal |
| `#Const DBG = _` ⏎ `1` | legal |
| `Private Enum EColor` / `Red = _` ⏎ `3` / `End Enum` | **illegal** — *Invalid inside Enum* |

The obvious guess is that continuations are lexical and therefore work everywhere. They do not, in exactly
one declaration block, and the error message is specific enough (*"Invalid inside Enum"*, anchored on the
member line) that it is clearly a rule rather than an accident. The inverse holds too: an Enum body accepts
colon separators, and a Type body does not.

**Recorded, and deliberately not implemented.** Matching it means a lexer mode pushed on `Enum` and popped
on `End Enum` — context-sensitive lexing driven by parser state, for one measured shape. Guessing the
rule's scope wide would turn a missing error into a false rejection of a whole module, which is the trade
this project refuses. What is unmeasured is the scope: whether the restriction covers the whole Enum body
or only a member's value expression, and whether the header line is affected. Two probes would settle it.

### VB6 has no bracket-escaped identifiers (2026-09-02)

| probe | verdict |
|---|---|
| `Dim [q] As Long` / `[q] = 5` | **illegal** — Syntax error, line 1 |
| `Dim [Print] As Long` | **illegal** — Syntax error, line 1 |
| `Dim [Rem] As Long` | **illegal** — Syntax error, line 1 |

`[name]` is a **VBA / VB.NET** feature. VB6 predates it and does not have it, so there is no escape hatch
for a name that collides with a keyword — the name is simply unavailable.

**How this was found is the point, and it is a caution about confident review.** The `Rem` change made
`Dim [Rem] As Long` stop parsing: the comment rule fires inside the brackets and eats the closing one.
Two independent reviewers, working separately, reported that as a **regression to fix before shipping** —
one calling `[…]` *"the documented escape hatch for a name that collides with a keyword"*, and both
correctly observing that HexIDE had accepted it the week before. The reasoning was sound and the
conclusion was wrong, because the premise was a VBA fact wearing VB6 clothes.

Measuring it inverted the finding entirely. `[Rem]` no longer parsing is not a regression: it is one case
that accidentally moved *towards* VB6. The grammar's bracket alternative in `ambiguousIdentifier` is an
over-acceptance in every other case, and dropping it retires three corpus rows rather than costing any.

This is the same failure mode the oracle file was created for, and it is worth naming because it recurs:
**a rule remembered from a neighbouring product, asserted confidently, and wrong here.** It has now
happened with the VBA documentation (`Rem` requires a space — it does not), with a reviewer's mental model
(`[…]` escapes keywords — not in VB6), and, in this same change, with my own reading of an enum probe.
The defence is not better reviewers. It is that nothing counts until `vb6.exe` has been asked.

### Line labels: a property of the line, not a statement (2026-09-03)

Ten new corpus cases, and the answer to two of them decided the grammar's shape.

| probe | verdict |
|---|---|
| `10 Skip: Debug.Print "A"` — a line number AND a named label | **legal** |
| `Skip: 10 Debug.Print "A"` — the same pair, other order | **illegal** — Syntax error |
| `10 Skip:` ⏎ `stmt` — number and name with **nothing after** the colon *(2026-09-27)* | **legal**; `GoTo 10` and `GoTo Skip` each reach `stmt`, and falling into the line from above just continues |
| `10 Skip: ' a remark` ⏎ `stmt` *(2026-09-27)* | legal; `GoTo 10` reaches `stmt` |
| `GoTo Skip` … `10 Skip:` ⏎ `End Function` — the pair as the procedure's last line *(2026-09-27)* | legal; the jump skips the assignment between and the function returns normally |
| `Skip<tab>: Debug.Print "A"` | legal |
| `First:` ⏎ `Second:` ⏎ `stmt` — two labels on consecutive lines | legal, and BOTH reach `stmt` |
| `Cont: Next i` — a label before a loop terminator | legal |
| `Main:` inside `Sub Main` — a label named for its own procedure | legal |
| `Chk: If True Then` … `End If` | legal |
| `z = 1:: Here: z = 2` | **illegal** — *Sub or Function not defined* |
| `Orphan:` at module level between two procedures | **illegal** — *Only comments may appear after End Sub…* |

So the head of a line is an **ordered sequence**, number then name, not a set. And a label is
procedure-scoped: outside one there is nowhere to jump from and VB6 says so.

**The most useful row is the illegal one that is not a syntax error.** `z = 1:: Here: z = 2` comes back
*"Sub or Function not defined"* — VB6 is not refusing the shape, it is **reading `Here` as a call**, which
is exactly what HexIDE's grammar does with it. The two parses agree and only the timing of the complaint
differs. That single message settled a design question that reasoning had not: a line head reachable after
a colon would not merely over-accept, it would invent a jump target the program does not have. Six corpus
rows previously filed as a label defect are really this, and they are permanent — the residue is name
binding.

**What HexIDE was getting wrong, and why no gate saw it.** `lineLabel` was an alternative of `blockStmt`
while the numeric line number was already a prefix on the block. Because the block's separator admits a
colon, taking the label alternative would have consumed the colon the block needed next — so ANTLR never
took it, and `Skip: Debug.Print "x"` parsed as a bare call to `Skip` followed by a separator.

**Ten of fourteen measured-legal label forms were dead.** Only a label standing ALONE on its line worked,
because only that one has nothing to compete with:

| form | before |
|---|---|
| `Skip: Debug.Print "x"` | label lost |
| `Later : stmt` (space before the colon) | label lost |
| `Skip:: stmt` | label lost |
| `Skip:Debug.Print` (no space after) | label lost |
| an indented label sharing its line | label lost |
| `Retry: i = i + 1 : If i < 2 Then GoTo Retry` | label lost |
| `Handler: Debug.Print "handled"` on an On Error target | label lost |
| a label whose name collides with a variable | label lost |
| `Skip _` ⏎ `: stmt` | label lost |
| `Skip: _` ⏎ `stmt` | label lost |
| `Skip:` alone on its line | worked |
| `Fin:` alone before `End Sub` | worked |

**None of this was visible to the conformance corpus**, and that is the lesson worth keeping. A lost label
PARSES — the name simply becomes a call — so a gate that asks "does this module load" reports green on
every row above. The failure surfaces later, somewhere else in the procedure, as `Label not defined` from
a `GoTo` that looks correct. An entire class of defect sat under a passing gate because the gate was
asking the only question the corpus could answer.

The fix is a line head reachable only after a separator containing a real line break. Two false rejections
fell out of it for free: `Error:` and `Name:` as label names, which the statement rules for `Error` and
`Name` had been shadowing.

### The underscore, and two rules arguing over one space (2026-09-03)

Not a new measurement — the facts were already recorded. This is the entry for what it took to *act* on
them, because the obvious repair was wrong and the reason is worth keeping.

The two facts, from earlier rounds:

| probe | verdict |
|---|---|
| `x = 1 + _` ⏎ `_` ⏎ `2` — a lone underscore in column one | **illegal** |
| `x = 1 + _` ⏎ `  _` ⏎ `2` — the same file, indented two spaces | **legal** |

So the whitespace before a continuation's underscore is *literally* required, and `_` is never a name.
HexIDE's `fragment LETTER` contained the underscore, making a lone `_` an identifier — which is what let
`x = 1 +_` complete as an addition against a variable named `_` instead of failing. Thirteen corpus rows.

**Why the one-character fix could not be taken alone.** `NEWLINE` was `WS? '\r'? '\n' WS?`, and that
trailing `WS?` eats the *next* line's indentation. So by the time the lexer reaches a line whose whole
content is ` _`, the space is already gone and `LINE_CONTINUATION` (`[ \t]+ '_' …`) cannot match. With the
underscore still an identifier that line lexed anyway, wrongly but harmlessly; take the underscore away
and it stops lexing at all. Two rules arguing over the same space, and each fix regressing the other.

**The obvious repair is wrong, and expensively so.** Giving `NEWLINE` its trailing whitespace back —
recommended by the analysis, and the first thing anyone would try — produced **151 test failures in one
run**. Every rule written since assumes a newline swallows the following indentation, so the whitespace
reappears as a token in a hundred places that never expected one. Worth recording as a measurement of the
grammar rather than of VB6: a lexer rule this old has a blast radius, and *"just make it stop doing that"*
is not available.

**What works is to ask what the line means.** A continuation joins its line to the next; joining an *empty*
line to the next yields the next line. So a line whose entire content is ` _` is not a line at all — it is
part of the boundary. Absorbing it into `NEWLINE` costs nothing elsewhere, because the token that comes out
is the one every existing rule already expects:

```
NEWLINE : WS? '\r'? '\n' (WS '_' [ \t]* ('\r'? '\n')?)* WS? ;
```

The mandatory `WS` inside the group is the whole discriminator — it is what keeps a column-one `_`
unmatched, and therefore refused, exactly as VB6 refuses it. Two space characters, load-bearing.

Thirteen false acceptances retired, no regressions. The general lesson is the one this file keeps
recording from a new direction: the fix that follows from the diagnosis is not always the fix that fits the
code, and the cheapest way to find out is to try it and count.

