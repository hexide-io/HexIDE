## `Select Case` layout — colons, blank lines, and continuations (2026-09-03)

The behavioural gate's first run found a seven-case cluster where a `Select Case` that is colon-joined,
split by a line continuation, or preceded by a blank line **selects the wrong branch or none at all**,
silently. Those seven were simply the cases that happened to exist, which is no basis for designing a fix,
so the layout space around them was measured: 18 further cases, 17 legal, all executed and their output
recorded.

### A colon is an alternative line break, after every case form

| written | VB6 |
|---|---|
| `Case 1: Debug.Print "ONE"` | prints `ONE` |
| `Case 1:` then the body on the next line | prints `ONE` — **the colon needs no statement after it** |
| `Case Else: Debug.Print "OTHER"` | prints `OTHER` |
| `Case 1, 2, 3: Debug.Print "IN LIST"` | prints `IN LIST` |
| `Case Is > 1: Debug.Print "BIG"` | prints `BIG` |
| `Case 1 To 5: Debug.Print "IN RANGE"` | prints `IN RANGE` |
| `Select Case x:` then `Case 1` on the next line | prints `ONE` |
| `Debug.Print "ONE": End Select` | prints `ONE` |
| the whole construct on one line, colon-separated | prints `ONE` |

So the colon is accepted after the selector, after every one of the three case-condition forms, and before
the block terminator. There is no form of `Select Case` in which a colon may not stand where a line break
may.

### What may sit between the selector and the first `Case`

| between `Select Case x` and `Case 1` | VB6 |
|---|---|
| one blank line | legal |
| several blank lines | legal |
| a comment line | legal |
| **an executable statement** | **illegal** — the only illegal case of the 18 |

That last row is the boundary, and it is the useful one: whatever rule admits blank lines and comments must
still refuse a statement. A fix that simply lets anything appear there would be wrong in a way none of the
other 17 cases would catch.

### The case that catches a bad fix

**An empty matching arm does not fall through.**

```vb
x = 1
Select Case x
Case 1
Case 2
    Debug.Print "TWO"
End Select
Debug.Print "AFTER"
```

VB6 prints `AFTER` and nothing else. This is recorded deliberately as a trap: the obvious way to fix the
wrong-branch bug is to make a case body greedier, and a greedy body swallows the following `Case 2` clause
as though it were a statement in arm 1 — which would print `TWO` here. Nothing else in the corpus
distinguishes the correct fix from that one. Likewise `select-case-on-one-line-no-match` prints only
`AFTER`, separating "the construct was skipped entirely" from "the construct ran and chose correctly",
which a matching one-line case cannot.

### Continuations: each half of the keyword pair works alone

`Select _ ⏎ Case n` with `End Select` intact prints normally; `End _ ⏎ Select` with the opener intact prints
normally. Both halves are independently fine, so neither is uniquely responsible for the two corpus cases
that vary both at once.

### The layout was a red herring — and so was my reading of it

12 of the 18 diverged, all in the "prints nothing" direction, but not uniformly: `Case Is > 1:` and
`Case 1 To 5:` worked, while `Case 1:` and `Case 1, 2, 3:` did not. I recorded here, wrongly, that this
pointed at how far the condition expression extends before the colon. **It has nothing to do with the colon,
the blank line, the continuation, or any other layout.** The parse trees for all seven original cases and all
18 probes are correct; the grammar was never at fault.

The real cause is one line of the interpreter, and the asymmetry is its fingerprint. `Select Case` compared
the selector to each arm with `Vb6Value.Equals`, which is `Type == other.Type && Equals(Value, other.Value)`
— **type-identical**. Every case in the cluster declared `Dim n As Long` and wrote a bare literal `1`, which
is an **Integer**. `Long(1).Equals(Integer(1))` is false, so no arm ever matched, and that single fact
produces both symptom classes: with no `Case Else` nothing prints; with one, control falls through to it,
which merely *looks* like "the wrong branch was chosen".

The two forms that worked are the two that never used `Equals`: `Case Is <` / `<=` / `>` / `>=` and
`Case a To b` go through `Vb6Value.TryCompareTo`, whose cross-type numeric path was added — the comment says
so — for exactly this. Plain `Case v`, `Case Is =` and `Case Is <>` were the three that did not.

Why ~1300 tests never caught it: `StatementTests` selects on a bare literal so both sides are Integer,
`WideningTests` uses `To` ranges, and `SplitKeywordTests.AContinuationMaySplitSelectCase` is one of these
corpus cases character-for-character except that it writes `Dim n` — a Variant holding an Integer — instead
of `Dim n As Long`. One keyword away from the failure, and green.

**The lesson worth keeping:** every layout probe in this section was answered correctly by VB6 and every one
of them was measuring the wrong thing. A cluster of failures that share a visible surface feature is not
evidence that the feature is the cause — here the shared feature was in the *test corpus'* house style
(`As Long` selectors), not in the construct.

