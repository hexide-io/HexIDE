# The label set — how it was decided

> **Archived record.** The live document is [`../issue-labels.md`](../issue-labels.md), which holds the
> rules you apply. This is kept rather than deleted because it records *measurements*: what was tried,
> what the numbers were, and which readings turned out to be wrong — the part not recoverable from the
> commit that applied them. Nothing here is a live instruction.

> **APPLIED 2026-09-20.** 33 labels created and 617 applications made across all 184 open issues.
> **Zero issues are now unlabelled**, down from 101. Verified against the live repo afterwards: every
> issue carries exactly one `type` and one `size`, and the labels on GitHub match this document on
> every axis with no disagreements. Two sets were held for a decision and then resolved — [§11](#11-what-was-held-and-how-it-was-resolved).
>
> This document remains the argument for the set; [§9](#9-how-to-apply-it) records how it was applied.

Scope: the 184 open issues on `hexide-io/HexIDE` as at 2026-09-20.

The rules this produced are in [`../issue-labels.md`](../issue-labels.md).

---

## 1. The short version

| | |
|---|---|
| Open issues | **184** |
| Carrying no label at all | **101** (55%) before; **0** after |
| Labels before this | 11 — nine GitHub defaults plus `fidelity` and `grammar` |
| Labels proposed | **44** total: 11 kept unchanged, 33 new |
| Axes | four — **type**, **area**, **size**, **state** — each answering one question |

The backlog lives in GitHub Issues by deliberate choice (`CLAUDE.md`: *"The backlog lives in GitHub
Issues, not in a file. `docs/TODO.md` was retired on 2026-08-17 … so a contributor can find work without
reading the repository"*). That decision makes labels the only navigation the backlog has, and right now
more than half of it has none.

The five suggested flavours all survive contact with the evidence, three of them changed:

- `lsp-client` — survives, but **split three ways**. As one label it held 47 issues, a quarter of the
  whole backlog, which is not a filter.
- `language-server` — survives (renamed from `lsp-server` to its spec name), and is **tiny**: 1
  issue. Kept deliberately at [§8.2](#8-decisions-taken).
- `interpreter` — survives, 21 issues, the largest single area after the split.
- `ui` — survives as an explicitly labelled **residual**, 8 issues, with a rule against shadowing.
- `ux` — does **not** survive as an area. Both raters reached for it where the other declined on 7 of
  its 8 uses. It ships on the state axis, renamed **`misleading`** so the name states its gate (§8.3).
- `mcp` — survives, 13 issues.

---

## 2. What was there before

```
bug · documentation · duplicate · enhancement · good first issue
help wanted · invalid · question · wontfix          (GitHub defaults, untouched)

fidelity   The answer comes from real vb6.exe. Measure it, then record it in docs/vb6-fidelity-oracle.md.
grammar    The shared VB6 grammar: both .g4 copies, judged by the conformance corpus
```

Usage across the 184 open issues **before this change**: `bug` 46, `enhancement` 25, `fidelity` 11,
`good first issue` 6, `documentation` 4, `grammar` 3. `question`, `help wanted`, `duplicate`, `invalid` and `wontfix` are
used **zero** times.

Three things in those two hand-made labels set the house style, and this proposal follows all three:

1. **A description says what to do, not just what the label means.** `fidelity` does not stop at
   "concerns VB6 behaviour" — it tells you to measure it and where to record the answer.
2. **Descriptions run long and are written as prose.** `fidelity` is 94 characters. GitHub's hard cap is
   100. Every description proposed here has been measured against that cap; none exceeds it.
3. **Names are flat and lower-case, with no `area:` or `type:` prefix.** This proposal keeps that, and
   carries the axis in **colour** instead — see [§4.3](#43-colour-carries-the-axis-so-the-name-doesnt-have-to).

The two hand-made labels kept their colour and their wording exactly. `grammar` kept its name too;
`fidelity` was renamed to **`needs-oracle`** at §8.18 — references to `fidelity` in §1 and §2 are the
name as it was before that.

---

## 3. How this was arrived at

The taxonomy was not designed and then asserted. It was drafted, **applied to all 184 issues twice by
two independent raters**, measured where the two disagreed, rewritten to kill the specific ambiguities
that caused those disagreements, and then applied to all 184 again.

| | draft v0 | revised v1 |
|---|---|---|
| Issues classified | 184 | 184 |
| Rater pairs disagreeing | 64 (34.8%) | 63 (34.2%) |
| …because a **definition was ambiguous or an anchor did not discriminate** | **64 of 64** | **7 of 63** |
| …because a rater **missed a rule that was already written** | — | 56 of 63 |
| Issues where a rater reported the vocabulary had no word for it | 25 | 0 forced values |
| Largest single area label | 47 (26% of backlog) | 21 (11%) |
| Type values never used | 1 (`question`) | 0 |
| Out-of-vocabulary values returned | yes (`medium-large` leaked) | **0** |

**The headline number is flat and it is the wrong number.** What changed is the *composition*. In v0
every disagreement traced to the taxonomy — 37 ambiguous definitions and 25 cases where the size anchors
did not discriminate. In v1, 56 of 63 were adjudicated as "the rule exists and a rater missed it", which
is a calibration problem, not a vocabulary problem. Only **7** cases were ones where v1 genuinely had
nothing to say, and all seven are named and fixed in [§8](#8-decisions-taken).

Two sub-results worth stating because they were the goal rather than a side effect:

- **The size ratchet is gone.** In v0, where the two raters disagreed on size, the same rater was the
  higher one 34 times out of 37 — a systematic offset, not noise, caused by a "round up if in doubt"
  clause. In v1 that fell to 15 of 23 (p≈0.21): the raters now err in both directions, which is what an
  unbiased scale looks like.
- **Tightening `needs-oracle` shrank it honestly**, from 27 issues to 19, by ruling out issues that merely
  concern VB6 or quote the fidelity principle.

**A third pass then re-screened all 184 issues against the four state labels that came back too big**,
after pinning each to an enumerated test rather than a theme. That pass is the subject of
[§7](#7-what-the-evidence-says-about-the-four-contested-state-labels), and it produced the clearest
result of the three: disagreement fell to **18 of 184 (9.8%)**, against 63 on the classification pass.

Everything asserted in this document with a number behind it came out of those runs — three passes,
110 agents, 184 issues rated or screened six times each. Where a judgement is mine and unmeasured, it
says so. Two proposals of mine died on contact with the evidence and are recorded as such: `walled`
(§5.4) and the loose reading of `silently-wrong` (§7).

---

## 4. The design decisions

### 4.1 Four axes, because one list cannot answer four questions

An issue gets **exactly one** `type`, **exactly one** `size`, **zero or more** `area` (prefer one, at
most three), and **zero or more** `state` (usually none). The axes are independent by construction: you can
filter on any one without the others interfering.

### 4.2 No severity ramp — the repo has already ruled on that

There is no `priority:high` here, deliberately. `docs/serialization-outcomes.md` opens with:

> The axis for judging any round-trip defect: **what does it do to the user's file?** This replaces
> ad-hoc severity labels, **which kept conflating "ugly diff" with "destroyed project"**.

That decision is already made and a generic severity ramp would reverse it. What *is* proposed is the
specific named outcome the project ranks worst, in two separate documents that arrived at it
independently:

- `docs/MISSING_LANGUAGE.md` ranks **Silently wrong** *above* **Won't load**: *"Nothing announces it, so
  the user debugs their own correct code."*
- `docs/serialization-outcomes.md` ranks **works in HexIDE, fails in VB6** worst of five and a launch
  blocker: *"Category 3 is silent. You work in HexIDE for a week, committing as you go…"*

Those are the same harm shape, and `MISSING_LANGUAGE.md` says so in as many words. Hence one label,
`silently-wrong`, rather than a severity scale. Its size is the subject of
[§8.5](#8-decisions-taken).

### 4.3 Colour carries the axis, so the name doesn't have to

Prefixes (`area:lsp-client`) are the usual way to group labels, but they cost horizontal space on every
issue row, and GitHub label search does not support wildcards, so a prefix buys sorting and nothing else.
The existing two hand-made labels are unprefixed, and so is every name in the suggested list.

So: **one colour per axis**, and the text distinguishes within it.

| Axis | Colour | Anchored on |
|---|---|---|
| type | the GitHub defaults' own colours | unchanged |
| area | `#0e8a16` green | `grammar`, which is already this green |
| size | a grey ramp, light→dark | new — the ramp itself encodes the ordering |
| state | `#fbca04` amber | `needs-oracle` (was `fidelity`), which is already this amber |
| state — the two that mean *worse than it looks* | `#d93f0b` | the one deliberate exception |

The grey ramp is the part worth keeping: `trivial` `#ededed` → `small` `#c9ccd1` → `medium` `#8b949e` →
`large` `#57606a`. The label gets darker as the work gets bigger, so a scan of an issue list reads the
sizes without reading the words.

### 4.4 T-shirt sizes, and what "size" is measured on

Sizes are `trivial` / `small` / `medium` / `large`. **Never hours, never days** — the format is chosen
because it cannot express false precision.

The anchor table you had agreed covered `small` (#240), `medium` (#242) and `medium–large` (#243), with
a note that the 49-pair numeric oracle sweep sizes *large* for measurement and *medium* for writing.
That note is the interesting one, and this proposal resolves it:

> **Size is the authoring cost. The `needs-oracle` label is what says there is a measurement cost on top.**

No new label is needed for it, because `needs-oracle` already exists and already says *"Measure it, then
record it"*. The two costs stay separately legible: size answers "how big is the change", `needs-oracle`
answers "and you have to go and find out the answer first".

That rule needed one sharpening, from a case where two raters split: the carve-out removes the cost of
**taking the measurement**, not the design choices the measured answer then forces. Those still count.

The gaps in your anchor table are filled from the repo's own merged history:

| Size | Anchor | Shape | Status |
|---|---|---|---|
| `trivial` | **PR #31** — match the identity pattern case-insensitively (1 file, +8/−1) | Cause already understood, one edit site, the form of the edit settled, a test that pins it. | **proposed, not agreed** |
| `small` | PR #240 — say where an out-of-cone member actually lives (6 files, +177/−23) | One seam, a handful of files, mechanism already settled — the remaining cost is applying it. | agreed |
| `medium` | PR #242 — advertise what the server can actually do (7 files, +288/−14) | An open design decision **and** a coupled fix that cannot be split out. | agreed |
| `large` | PR #243 — route documents to every server that claims their language (10 files, +842/−54) | A new component, several behaviours to decide inside it. | **promoted from medium–large** |

**`medium-large` is deleted as a selectable value.** It was chosen once in 368 rater-calls, and its
"round to `large` if in doubt" parenthetical was the one-way ratchet described in §3. #243 keeps its
place as the anchor — for `large`.

**PR #265** (attach a language server without rebuilding — 78 files, +3551/−471: configuration,
discovery, consent, docs, and a migration of what was hardcoded) is deliberately **not** an anchor. An
issue that size is one that should be split before it is picked up. Say that in the issue rather than
labelling it `large`.

One finding that makes all of this necessary: **diff size is a bad proxy in this repo.** #129 is one
file and one line — on top of a full corpus run against a real `VB98\Template` tree. #444 is one `await`
— on top of diagnosing a race that had been passing by luck across 44 tests. Size the work, not the patch.

---

## 5. The proposed label set

### 5.1 type — exactly one

| Label | Colour | Description | n |
|---|---|---|---|
| `bug` | `#d73a4a` | Something isn't working | 109 |
| `enhancement` | `#a2eeef` | New feature or request | 55 |
| `chore` | `#c5def5` | Neither a defect nor a feature: a measurement, a decision written down, machinery moved. | **13** |
| `documentation` | `#0075ca` | Improvements or additions to documentation | 7 |

`chore` is the only new type, and it earns its place: in the v0 pass, three separate raters reported
choosing `enhancement` *only because nothing else fit* — #160 ("builds no feature and fixes no defect"),
#389, #308. It also absorbed the work `question` was never used for.

**`question` is dropped from this axis** — zero of 184 issues took it, and the one rater who reached for
it was overruled. It stays as a GitHub default for issues that arrive needing information from outside
the repo.

Tiebreaks, each written to settle a real split:

- A gesture, binding or menu item that diverges from VB6's own is `bug`, even when the command it should
  reach is new or unbound.
- Behaviour VB6 has and HexIDE never built is `enhancement` when its absence degrades gracefully (a
  disabled item, nothing drawn) and `bug` when it crashes, corrupts or loses data.
- "Its spec" means HexIDE's own spec for the component, **not** an external protocol it implements.
  Where the same guard already exists elsewhere in the same component, it is `bug` — one guard applied
  inconsistently, not a new capability.
- An umbrella carrying `tracking` takes the type of the condition it was opened to close.

### 5.2 area — zero or more, prefer one, at most three

All `#0e8a16`. An area applies when **the fix will land code in it**, including a prerequisite the body
names; a subsystem mentioned only as context does not qualify.

| Label | Description | n |
|---|---|---|
| `interpreter` | The tree-walking interpreter. Only when the change lands inside it, not merely uses it. | 21 |
| `lsp-client` | What HexIDE says once a server is talking: capability gating, routing, URIs, diagnostics. | 20 |
| `lsp-transport` | Getting a server running and talking: launch, stdio/pipe/socket, framing, teardown. | 20 |
| `project-system` | A project once it exists: loading, members, naming, saving, watching, recovery. | 19 |
| `code-editor` | The code window: typing, indent, find, bookmarks, squiggles, editor state. | 16 |
| `tooling` | The machinery, not the product: test projects, corpora and fixtures, local guards. | 15 |
| `serialization` | Reading and writing VB6's own files. Ends once a record's bytes are correct in memory. | 13 |
| `mcp` | The MCP server and its tool surface. It ships to agents, so judge it as a UI. | 13 |
| `addins` | The add-in system end to end: manifests, packaging, loading, tool windows, trust. | 12 |
| `form-designer` | The visual designer and the controls it draws: toolbox, selection, geometry, properties. | 10 |
| `ui` | Residual for IDE chrome with no more specific area. Never use it to shadow one. | 8 |
| `profiles` | Surfaces that promise extension without a code change: theme and keymap packs, profiles. | 7 |
| `protocol-inspector` | The windows that explain a server to a human: the inspector, the capture, its redaction. | 7 |
| `debugger` | Breakpoints, break mode, stepping, watches, locals, call stack, Immediate window. | 5 |
| `ci` | GitHub Actions itself: workflow, matrix, runners, and what CI does or does not run. | 5 |
| `localization` | Language packs, translations, access keys, RTL, the translation editor. | 4 |
| `grammar` | *(existing, unchanged)* The shared VB6 grammar: both .g4 copies, judged by the conformance corpus | 4 |
| `ai-chat` | The bundled AI Chat panel: its clients, settings and request lifecycle. | 3 |
| `packaging` | What gets shipped and signed: installers, Release artifacts, bundles. | 3 |
| `compile` | Building a VB6 project, including driving real vb6.exe and parsing what it says. | 3 |
| `object-browser` | The Object Browser and the type-library metadata behind it. | 2 |
| `language-server` | The bundled VB6 server under LspServer/: what it advertises and what it answers. | 1 |

Names follow `openspec/specs/` wherever one exists (`grammar`, `form-designer`, `object-browser`,
`localization`, `serialization`, `ai-chat`). Four names are new coinages and two are open questions —
see [§8](#8-decisions-taken).

**Where the area vocabulary came from.** Six of these did not exist in the first draft and were added
because raters reported, unprompted, that they had no word for a cluster:

- `tooling` (15) — the repo's own machinery. Seven separate gap reports said `ci` was standing in for
  the conformance corpus, test-project migration, and the pre-`git add` hygiene guard. This also gives
  the proposed `trivial` anchor (PR #31, a change to `scripts/check-tree-hygiene.sh`) somewhere to live.
- `compile` (3) — driving real `vb6.exe` and parsing what it says. #399 notes the menu items are
  "Build, Rebuild and Clean — not Make".
- `ai-chat` (3) — `openspec/specs/ai-chat/` exists; the label did not.
- `profiles` (7) — the data-driven extension surfaces. `docs/profile-bundles.md` is cited by exactly the
  contiguous block #413–#423. Draining these out of `ui` is most of why `ui` fell from 23 to 8.
- `lsp-transport` and `protocol-inspector` — the split of the 47-issue label.

Boundary rules that settled repeated splits:

- `serialization` **ends once a record's bytes are correctly in memory**; decoding a preserved blob for
  display belongs to the area that displays it. A caller that merely reuses an existing reader takes its
  own area alone.
- `ui` is a **residual** and must never be applied to a window that belongs to a more specific area —
  the Language Servers window is `protocol-inspector`, not `ui`. Project *creation* surfaces are `ui`;
  `project-system` covers a project once it exists.
- `interpreter` applies only when the change lands **inside** it; a debugger or UI surface that merely
  consumes interpreter semantics stays in its own area.
- A construct that is **refused** rather than mis-executed is `grammar`, not `interpreter`.
- Where the code at fault has not been localised, **leave area empty** rather than guessing.

### 5.3 size — exactly one

| Label | Colour | Description | n |
|---|---|---|---|
| `trivial` | `#ededed` | T-shirt size, never time. One settled edit site. Anchor: PR #31. | 19 |
| `small` | `#c9ccd1` | T-shirt size, never time. One seam, mechanism already settled. Anchor: PR #240. | 69 |
| `medium` | `#8b949e` | T-shirt size, never time. An open design decision AND a coupled fix. Anchor: PR #242. | 72 |
| `large` | `#57606a` | T-shirt size, never time. A new component, several behaviours to decide. Anchor: PR #243. | 24 |

Preamble rules, each of which decided a real disagreement:

- Size **only the work this issue asks for**. A prerequisite the body hands off to a separate issue is
  excluded.
- Size it **as if it were picked up first and alone**. If a sibling landing first would make it cheaper,
  that is a note in the body — not a smaller size, and not `blocked`.
- Where the body offers ranked options, size the one it states as **preferred**. Where it offers a first
  slice and a full scope rather than alternatives, size the **first slice** and say so in the issue.
- Mechanical breadth — the same key added to every language pack, a regenerated table — does not by
  itself raise the size above the decisions the change actually requires.
- Where the cause is not yet identified, size the fix you expect **once it is diagnosed**, and default
  to `small`. `trivial` requires the cause to be already understood.
- A bundle of independent fixes sizes as their **sum**.

Note the distribution honestly: 141 of 184 sit on `small` or `medium` (77%). That is not obviously
wrong — the tails carry 19 and 24, about 12% each — but it did not improve on v0's 73%, and the metric
was probably the wrong target. Chasing spread is what reintroduces a ratchet.

### 5.4 state — zero or more, usually none

All `#fbca04` except the two marked, which are `#d93f0b`.

| Label | Description | n |
|---|---|---|
| `silently-wrong` **`#d93f0b`** | Wrong output shown as right, a dropped or misplaced write, an accepted no-op, a file VB6 refuses. | **35** |
| `needs-oracle` | *(was `fidelity`; wording unchanged)* The answer comes from real vb6.exe. Measure it, then record it in docs/vb6-fidelity-oracle.md. | 19 |
| `security` **`#d93f0b`** | Signing, revocation, package verification, consent, redaction, or an escape. | 14 |
| `needs-decision` | A fork only the maintainer can settle, named in the body. An implementer's fork is size, not this. | **12** |
| `misleading` | A surface that works and tells the user or a calling agent something untrue. Additive. | **10** |
| `flaky` | Fails intermittently. Takes the area of the code at fault, not ci. | 6 |
| `blocked` | Cannot start until something else lands. Name the blocker in the body. | **6** |
| `tracking` | An umbrella. Only when the work lives in other, separately filed issues. | 2 |

Four of these counts (marked in bold) are the **pinned** figures — each label was first defined loosely,
came back too large to filter on, and was re-measured against an enumerated test. [§7](#7-what-the-evidence-says-about-the-four-contested-state-labels)
has the before-and-after and the wording that did it. `blocked` was then verified issue by issue (§7.1).

`needs-oracle` keeps the exact wording it had as `fidelity` — only the name changed (§8.18). What else
changed is the rules around it, all from real splits:
apply it only when the issue cannot be closed without running vb6.exe and recording the result; if the
answer is already in the checked-in VB6-authored corpus, leave it off; byte-level round-trip correctness
is carried by `serialization`; it is not a topic tag for issues that merely concern VB6; apply it if
**any** part of the stated scope needs vb6.exe, and such an issue is never a `good first issue`; and
drop it once the answer is recorded in the oracle.

**`walled` was proposed and is withdrawn.** It was meant for work recorded deliberately and not
scheduled — `interpreter-gaps.md`'s own "Walled off (by design)". Applied to the backlog it took exactly
**one** issue (#352), alongside `blocked` and `security`, and changed no decision those two did not
already make. A label nobody uses is the failure mode this exercise exists to avoid, so it is not
proposed.

### 5.5 Off-axis, unchanged

`duplicate`, `invalid`, `wontfix`, `question` — closing verdicts and triage hygiene, not classifications.
Zero uses on a curated open backlog is the correct count. Leave them alone.

One recorded limitation rather than a new label: `duplicate` is all-or-nothing and cannot say "this is
one third of #367", which is #365's actual relationship. That belongs in the body as a link.

### 5.6 `good first issue` — derive it, don't judge it

The rule: size is `trivial` or `small`, **exactly one** area, no `needs-oracle`, no `blocked`, no
`needs-decision`, and the issue does not leave a choice between two materially different resolutions to
the implementer.

**This should be computed from the other axes, not judged per issue.** Asked to judge it directly, a
model returned 52 of 184 — and certified #423 despite it carrying two areas, which the rule it had just
been given forbids. Computing the gate removes that class of error entirely.

**But do not mistake the derived list for an answer.** Computed against the labels in the mapping file
it returns **67 of 184 — 36% of the backlog**, which is *more* than the model's 52. Verifying `blocked`
issue by issue (§7.1) grew this list rather than shrinking it, which settles the question of whether the
gate was ever the problem here: it was not.

So the honest position is: derive the candidate list mechanically, then treat the survivors as a
**queue to pick from, not a label to apply** — see
[§8.4](#8-decisions-taken). A third of a backlog cannot all be good first issues,
and the label's entire value is a newcomer trusting it.

`help wanted` is a maintainer's judgement about wanting outside help. It cannot be derived and is left
alone.

---

## 6. What this does not do

- **It does not duplicate the catalogues.** `MISSING_FEATURES.md`, `MISSING_LANGUAGE.md`,
  `interpreter-gaps.md`, `debugger-vb6-divergences.md` and `vb6-fidelity-oracle.md` already track
  coverage and divergence in far more detail than a label can. No label here restates a row in any of
  them; `needs-oracle` *points at* the oracle rather than summarising it.
- **It does not add a status axis.** OpenSpec's position-is-status rule (`specs/` = built, `changes/` =
  in flight, `archive/` = done) is a deliberate design decision, and a `status:` label family would be a
  second, drifting copy of it.
- **It adds no milestone or release scheme.** There are no milestones on the repo today and this
  proposal does not introduce one.
- **It does not label new issues for you**, though §8.17 adds a template that captures `type` at filing
  time. Area stays a triage judgement: it has a landing rule and five boundary rules behind it, and a
  filer who picks `ui` because they saw a dialog has done exactly what §5.2 forbids.

---

## 7. What the evidence says about the four contested state labels

Four state labels were defined loosely at first and came back too big to be useful. Each was then pinned
to an enumerated test and **all 184 issues were re-screened against the pinned wording**, by two
independent screeners with a third adjudicating. The question was simple: does tightening a label
actually shrink it, or was the label never discriminating?

| Label | Loose reading | Pinned reading | Change | Final, after §8 |
|---|---|---|---|---|
| `silently-wrong` | 57 (31% of backlog) | **35** (19%) | −39% | 35 |
| `needs-decision` | 31 | **14** | −55% | 12 — 8.11 dropped #418, #419 |
| `blocked` | 13 | **5** | −62% | 6 — §7.1 verified 4, then 8.11 and 8.13 added two |
| `ux` → `misleading` | 12 | **10** | −17% | 10 |

**Rater disagreement collapsed with them: 18 of 184 (9.8%), against 63 for the classification pass.**
Narrow, enumerated definitions are decisive in a way prose definitions are not — which is the single
most useful methodological finding here, and the reason every label description in §5 tries to state a
test rather than a theme.

**`silently-wrong` survives, and it is a descriptor, not a crisis.** 35 issues, by shape:

| Shape | n |
|---|---|
| (a) a wrong value, text or file content presented as correct | 19 |
| (b) a write the user asked for does not happen, or happens somewhere they did not choose | 6 |
| (c) an operation accepted, then not performed, with nothing surfacing a failure | 6 |
| (d) HexIDE writes a file real vb6.exe refuses, or compiles into a different program | 4 |

The exclusions did most of the work: a leaked process or unreclaimed slot that produces no wrong result
is not `silently-wrong`; neither is an unhelpful log line where the product behaviour is correct, a
visibly absent feature, an error that *is* raised however badly worded, or a defect that would be silent
only in a future caller that does not exist. Under the loose reading — "the failure does not announce
itself" — 52% of all bugs qualified, which is a label that cannot filter anything.

### 7.1 `blocked`, verified issue by issue

An earlier draft of this section claimed `blocked` under-detects, on the strength of a grep for
dependency language that returned 11 candidates against the screen's 5. **That claim was wrong**, and
the check that disproved it is worth recording, because it is the only label here whose value depends
on being *complete* — §5.6 gates `good first issue` on it.

All 13 candidates (the screen's 5 plus the 8 the grep added) were read individually, with the state of
every named blocker checked live rather than taken from the prose. **Four survived on the evidence**,
and §8 then added two more by decision:

| Issue | Waits on | Blocker state | Source |
|---|---|---|---|
| #49 | #43 — the `Object=`/`Reference=` modelling | open | verified |
| #160 | #60 | open | verified |
| #459 | #310 | open | verified |
| #460 | #459 | open | verified |
| #169 | #171 | open | decision 8.13 — see below |
| #418 | the profiles supersession | **unfiled** | decision 8.11 — see §8.14 |

**Nine of the thirteen were noise, in three recurring shapes** — worth knowing because they are what a
scan will always produce:

- **The negation.** #263 says *"Blocked on nothing"*; #500 says *"Blocked on nothing."*; #260 says the
  *other* issue is not blocked by this one. A grep for "blocked on" cannot read a negative.
- **The already-landed blocker.** #169 cites #124, #221 cites #219, #500 cites #489 — all closed.
  Checking state rather than trusting prose changed the outcome in every one.
- **The hypothetical.** #164's *"Splitting them would produce two issues blocked on a third"* is an
  argument for **not** splitting the issue.

**A `## Prerequisite` heading is not reliable on its own.** #159 and #174 both carry one and both are
startable. #159's prerequisite (#156) is an ordering-and-shared-work argument — *"the fix is the same
peeling this feature needs"*, so the contributor writes it as part of the Export work. #174's is
literally step 1 of its own suggested order.

So the pinned definition is both precise **and** adequate on recall: one false positive (#169, below)
and no confirmed false negatives across every candidate either method could find. The residual risk is
narrower than "the label under-detects" — it is a dependency stated in *neither* prose nor a citation,
which is exactly #169's shape.

**#169 is the interesting one, and it was decided rather than measured.** It names only #124, which is
closed, so on the text it is not blocked. But its own worked example is `x = 5` with no `Dim`, and #171
— *"There is no implicit variable declaration — x = 5 raises Err 424"* — is open. DefType is a
letter-range-to-type table consulted when an undeclared variable is created; until #171 lands there are
no undeclared variables for it to type. The table could be written and unit-tested; the feature would be
inert. Strict "cannot start" says not blocked; "cannot be finished or demonstrated" says blocked, and
that is the reading taken (§8.13) because the costs are asymmetric — a hidden workable issue is a minor
loss, a newcomer shipping inert code is the failure the gate exists to prevent.

This is the residual risk class named above: **a dependency stated in neither prose nor a citation.** It
is the one shape no scan and no screen will find, and the only defence is someone who knows the system
reading the issue.

Note which way the verification cut: removing #169 from the *evidence-based* blocked set did not shrink
the derived `good first issue` list, it grew it. Repairing this label was never going to solve §8.4.

---

## 8. Decisions taken

All thirteen open questions were put to the maintainer and settled on 2026-09-20. They are recorded
with their reasoning rather than just their verdict, because a label set is only as good as the record
of why it is shaped the way it is.

| § | Question | Decision |
|---|---|---|
| 8.1 | What does `blocked` mean? | **Hard dependency only, and it is a gate** |
| 8.2 | Three client-side LSP labels, or two? | **Keep three** |
| 8.3 | Does `ux` belong, and is it named right? | **Keep on the state axis, renamed `misleading`** |
| 8.4 | How tight should `good first issue` be? | **Five-part gate produces a queue; hand-pick 10–15** |
| 8.5 | Is `silently-wrong` a driver or a descriptor? | **Descriptor, 35 issues** |
| 8.6 | Keep `chore`, and under what name? | **Keep it, call it `chore`, with two tiebreaks** |
| 8.7 | `profiles` or `personalities`? | **`profiles`** |
| 8.8 | LSP naming: spec-aligned, or the `lsp-*` family? | **Spec-aligned** |
| 8.9 | Colour of the two alarm states | **Alarm red on both** |
| 8.10 | The area cap | **Raised from two to three** |
| 8.11 | `needs-decision` on #418 / #419 | **Dropped from both; #418 becomes `blocked`** |
| 8.12 | Who applies this, and when? | **Documents read and amended first, then applied** |
| 8.13 | #169 — cannot start, or cannot be demonstrated? | **`blocked` on #171** |
| 8.18 | Is `fidelity` the right name for the label? | **No — renamed `needs-oracle`** |

### 8.1–8.8, in brief

**`blocked` is a gate** (8.1), which is why §7.1 spends a verification pass on it rather than trusting a
screen. A gate that is wrong hands a newcomer unstartable work.

**The LSP split stands at three** (8.2) and takes the spec names (8.8): `lsp-client`, `lsp-transport`,
`protocol-inspector`, `language-server`. Only `lsp-transport` is a coinage, and it is honest about being
a carve-out of `specs/lsp-client/`, which already owns transport. The cost accepted: all area labels
share one green, so alphabetical adjacency was the only grouping cue available, and spec-aligned names
scatter the LSP cluster across the list. Searchability won — a contributor who sees `protocol-inspector`
and greps the tree finds the spec.

**`ux` ships as `misleading`** (8.3), so the name states the gate: the surface must work *and* lie. The
cost is accepted and should be understood — this drops the other half of the concept, *correct behaviour
that cannot be discovered*, which is exactly #263's shape. The bar `CLAUDE.md` sets for the automation
surface stays a review standard rather than a filter; `docs/mcp-server-gaps.md` records it better than a
label could.

**`good first issue` is a queue, not a label** (8.4). The five-part gate returns 67 of 184 — see §7.1 for
why verifying `blocked` *grew* that number — so the gate produces candidates and a human picks 10–15.
The standing cost is real: hand-picked lists rot as issues close, and a stale list of three is worse
than an honest list of 67.

**`silently-wrong` is a descriptor at 35** (8.5), not a launch-blocker filter at 23. A VB6
reimplementation genuinely produces silent divergence at scale, and 19% may simply be the true number.

**`chore` keeps its name** (8.6) despite having no precedent in this repo — of 213 merged PRs only 31
carry a conventional-commit prefix (`fix:` 16, `docs:` 9, `feat:` 4, `ci:` 1, `demo:` 1) and `chore`
appears **not once**. It wins on connoting "not the product"; `task` was rejected as too vague to resist
becoming the next residual. Its two tiebreaks: prose in a doc or spec is `documentation` even when the
missing prose *is* the decision; a release artifact a user receives is `enhancement` even when the code
lands only in packaging.

**`profiles`, not `personalities`** (8.7), and on firmer ground than the naming argument alone.
Personalities was an earlier, simpler attempt at letting the IDE impersonate various VBIDEs, and it will
be **superseded and subsumed by** profiles — a subset that goes away, not a parallel feature to keep in
step. The label is named for the destination.

### Two decisions went against my recommendation

**8.9 — alarm red on both `silently-wrong` and `security`.** I argued for demoting `silently-wrong` to
amber, on the grounds that a descriptor covering 19% of the backlog should not wear the loudest colour
in the set. Overruled, and the reasoning holds: the project's own documents rank silent harm as the
worst outcome in two separate places, and colouring it quieter than `security` would say the opposite.
The consequence to accept is that 49 of 184 issues — 27% — carry an alarm colour.

**8.10 — the area cap rises to three.** I argued for a hard two with an exemption for `documentation`.
Overruled in favour of the simpler rule, and the evidence supports it: 151 of 184 issues take exactly
one area and only 30 sit at the old cap, so the room to drift is small, and it fixes both known losses
(#478's five owners, #399's dropped `localization`) without a type-conditional rule.

### 8.14–8.17, a second round

Four questions fell out of the first thirteen and were settled the same way.

| § | Question | Decision |
|---|---|---|
| 8.14 | #418's blocker is not an issue number | **File the profiles issue, then point #418 at it** |
| 8.15 | Do areas take colour shades, to restore the grouping spec-aligned names lost? | **No — one green** |
| 8.16 | Do the 30 issues at the old two-area cap want a third? | **Re-screen all 30** |
| 8.17 | Issue templates | **Yes — a type dropdown only, no area** |

**8.14 — the profiles issue gets filed.** `blocked` on #418 pointed at the profiles supersession, and
no open issue tracked that work: #412 is a merged PR, and no open issue has "profile" in its title. A
blocker that exists only in `docs/profile-bundles.md` and in the maintainer's head contradicts both this
rulebook ("name the blocker in the body") and the repo's own position that the backlog lives in GitHub
Issues rather than in a file. Note that this is the one action in the whole exercise that **creates**
something rather than labelling it, so it is gated on its own approval.

**8.15 — one green for all 22 areas.** Spec-aligned naming (8.8) scattered the LSP cluster
alphabetically, and shading it back together was considered and rejected: it would buy grouping only for
the labels just decided should be found by *name*, and shading by cluster more broadly is a second
taxonomy encoded in colour that nobody would maintain. Colour continues to mean axis, with the single
documented exception at 8.9.

**8.16 — the 30 capped issues get re-screened.** The mapping was produced under a cap of two, so any
issue wanting a third area silently lost it; #478 (five real owners) and #399 (a dropped `localization`
the body calls required) are the two known losses. Thirty issues is the entire set that *can* have been
truncated — an issue with one area was never up against the cap — so re-screening 184 would be measuring
the wrong thing.

### 8.18 — `fidelity` is renamed `needs-oracle`

Applied 2026-09-20. The description is untouched; all 25 assignments carried over.

The case rests on a finding that only emerged when a second session misread the label: **"fidelity"
means three different things in this project**, and this label was the narrowest of them.

| Sense | Where | What it governs |
|---|---|---|
| The Fidelity **Principle** | `CLAUDE.md:489` — *"reproducing VB6's intended behaviour, not its bugs"* | what *correct* means |
| **Tier 1 — Fidelity** | the maintainers' roadmap (not in a public clone) — *"would a VB6 developer from 1998 recognise this?"* | what gets *built* |
| This **label** | the backlog | a measurement still *owed to the oracle VM* |

Only the third is a state an issue can be in, and only the third is something a filter should usefully
return. An issue can be Tier 1 work, governed by the Principle, and carry no label here.

Two supporting arguments, one measured and one by precedent:

- **It was the most misapplied label in the set**, contested on 9 of its 27 applications, and §5.4
  needed *five separate rules* saying "not a topic tag, not for issues that merely concern VB6". A name
  that says `needs-oracle` retires those rules structurally rather than by repetition.
- **It is the same fix as `ux` → `misleading`** (§8.3): rename so the name states the gate.

It also completes a trio on the state axis — `needs-decision` (a maintainer must answer),
`needs-oracle` (vb6.exe must answer), `blocked` (another issue must land) — which are exactly the three
`good first issue` disqualifiers, so that rule now reads as one idea rather than three exceptions.

**8.17 — templates carry type, not area.** Type is four values a filer assesses correctly; area is
twenty-two behind a landing rule and five boundary rules, and a filer picking `ui` because they saw a
dialog is exactly the residual-shadowing §5.2 forbids. The template captures what is cheap and leaves
the judgement where the rulebook is. This supersedes §6's note that templates are out of scope.

## 9. How it was applied

These are the commands that were run, in this order. `gh label create` was run once per label; the
per-issue application grouped issues by label (one `gh issue edit` per label, in batches of 20) rather
than one call per issue, which turned 600 applications into 38 label groups.

```sh
# Colour families: area #0e8a16 · state #fbca04 (#d93f0b for the two alarms)
# size ramp #ededed → #c9ccd1 → #8b949e → #57606a · type keeps GitHub's own colours.
# Every description below is <= GitHub's 100-character cap.

R=hexide-io/HexIDE   # origin is a fork; --repo is required

# --- type -------------------------------------------------------------------
gh label create chore --repo $R --color c5def5 \
  --description "Neither a defect nor a feature: a measurement, a decision written down, machinery moved."

# --- area -------------------------------------------------------------------
gh label create lsp-transport  --repo $R --color 0e8a16 --description "Getting a server running and talking: launch, stdio/pipe/socket, framing, teardown."
gh label create lsp-client     --repo $R --color 0e8a16 --description "What HexIDE says once a server is talking: capability gating, routing, URIs, diagnostics."
gh label create protocol-inspector  --repo $R --color 0e8a16 --description "The windows that explain a server to a human: the inspector, the capture, its redaction."
gh label create language-server     --repo $R --color 0e8a16 --description "The bundled VB6 server under LspServer/: what it advertises and what it answers."
gh label create interpreter    --repo $R --color 0e8a16 --description "The tree-walking interpreter. Only when the change lands inside it, not merely uses it."
gh label create debugger       --repo $R --color 0e8a16 --description "Breakpoints, break mode, stepping, watches, locals, call stack, Immediate window."
gh label create compile        --repo $R --color 0e8a16 --description "Building a VB6 project, including driving real vb6.exe and parsing what it says."
gh label create form-designer  --repo $R --color 0e8a16 --description "The visual designer and the controls it draws: toolbox, selection, geometry, properties."
gh label create code-editor    --repo $R --color 0e8a16 --description "The code window: typing, indent, find, bookmarks, squiggles, editor state."
gh label create project-system --repo $R --color 0e8a16 --description "A project once it exists: loading, members, naming, saving, watching, recovery."
gh label create object-browser --repo $R --color 0e8a16 --description "The Object Browser and the type-library metadata behind it."
gh label create serialization  --repo $R --color 0e8a16 --description "Reading and writing VB6's own files. Ends once a record's bytes are correct in memory."
gh label create profiles       --repo $R --color 0e8a16 --description "Surfaces that promise extension without a code change: theme and keymap packs, profiles."
gh label create localization   --repo $R --color 0e8a16 --description "Language packs, translations, access keys, RTL, the translation editor."
gh label create ui             --repo $R --color 0e8a16 --description "Residual for IDE chrome with no more specific area. Never use it to shadow one."
gh label create addins         --repo $R --color 0e8a16 --description "The add-in system end to end: manifests, packaging, loading, tool windows, trust."
gh label create mcp            --repo $R --color 0e8a16 --description "The MCP server and its tool surface. It ships to agents, so judge it as a UI."
gh label create ai-chat        --repo $R --color 0e8a16 --description "The bundled AI Chat panel: its clients, settings and request lifecycle."
gh label create tooling        --repo $R --color 0e8a16 --description "The machinery, not the product: test projects, corpora and fixtures, local guards."
gh label create ci             --repo $R --color 0e8a16 --description "GitHub Actions itself: workflow, matrix, runners, and what CI does or does not run."
gh label create packaging      --repo $R --color 0e8a16 --description "What gets shipped and signed: installers, Release artifacts, bundles."

# grammar already exists and is already #0e8a16 — recolour nothing, reword nothing.

# --- the one rename (§8.18) -------------------------------------------------
# Renames in place: assignments are preserved, the description is untouched.
gh label edit fidelity --repo $R --name needs-oracle

# --- size -------------------------------------------------------------------
gh label create trivial --repo $R --color ededed --description "T-shirt size, never time. One settled edit site. Anchor: PR #31."
gh label create small   --repo $R --color c9ccd1 --description "T-shirt size, never time. One seam, mechanism already settled. Anchor: PR #240."
gh label create medium  --repo $R --color 8b949e --description "T-shirt size, never time. An open design decision AND a coupled fix. Anchor: PR #242."
gh label create large   --repo $R --color 57606a --description "T-shirt size, never time. A new component, several behaviours to decide. Anchor: PR #243."

# --- state ------------------------------------------------------------------
gh label create silently-wrong --repo $R --color d93f0b --description "Wrong output shown as right, a dropped or misplaced write, an accepted no-op, a file VB6 refuses."
gh label create security       --repo $R --color d93f0b --description "Signing, revocation, package verification, consent, redaction, or an escape."
gh label create needs-decision --repo $R --color fbca04 --description "A fork only the maintainer can settle, named in the body. An implementer's fork is size, not this."
gh label create blocked        --repo $R --color fbca04 --description "Cannot start until something else lands. Name the blocker in the body."
gh label create flaky          --repo $R --color fbca04 --description "Fails intermittently. Takes the area of the code at fault, not ci."
gh label create tracking       --repo $R --color fbca04 --description "An umbrella. Only when the work lives in other, separately filed issues."
gh label create misleading     --repo $R --color fbca04 --description "A surface that works and tells the user or a calling agent something untrue. Additive."
# fidelity already exists, is already #fbca04, and its wording is unchanged.
```

Order, because it is reversible at every step and §8.12 puts a read of these documents before any of it:

0. **Read this document and the rulebook.** §8.12 gates everything below on that, because the rulebook
   is what decides 184 calls and it has not been read yet.
1. **Create the labels.** Nothing is labelled; nothing is disturbed. Fully reversible.
2. **Apply `area`**, from the mapping file. It is the axis with the least ambiguity left and the most
   navigational value on a 55%-unlabelled backlog.
3. **Apply `size`.** Expect to overrule it here and there — the mapping marks which issues were
   contested between raters, so those are the ones to look at first.
4. **Apply `state`.** All four contested state labels are now pinned and their counts measured (§7).
5. **Hand-pick `good first issue`** from the 67 derived candidates — 10–15, per §8.4.

Two steps sit outside that sequence because they create rather than label, and each needs its own yes:

- **File the profiles issue** (§8.14), then point #418's `blocked` at its number. Until that lands,
  #418 carries a blocker that is not an issue, which is the one place this set knowingly breaks its own
  rule.
- **Add `.github/ISSUE_TEMPLATE/`** with a `type` dropdown (§8.17). It changes what future filers see,
  so it is a repo change rather than a labelling one.

One caveat on all of it: another session is working this repo, so new issues filed after 2026-09-20 are
not in the mapping. Re-deriving is cheap; noticing the gap late is not.

---

## 10. Why there is no issue-by-issue mapping here

The labelling was driven from a 184-row table of issue-to-labels. It is **deliberately not committed.**
`CLAUDE.md` retired `docs/TODO.md` on 2026-08-17 on the grounds that *"the backlog lives in GitHub
Issues, not in a file"*, and a snapshot of issue state is backlog state in a file however it is dressed.
It would also be wrong within days: the labels on GitHub are the source of truth, and `gh issue list
--json number,labels` reproduces the table at any time.

What the table recorded and GitHub does not is **which calls were contested** between raters. Those are
named throughout §7 and §8 where they mattered.

## 11. What was held, and how it was resolved

Two sets were not applied in the first pass, because applying them would have overwritten labelling the
maintainer had already done. Both were then ruled on, and **the backlog now matches this document on
every axis for all 184 issues** — verified by diffing the live labels against the mapping.

### 11.1 Eleven issues where an existing `type` contradicted the rules — **rules applied**

Applying the mapped type would have left these carrying two type labels, which the axis forbids, so the
first pass skipped the type and left the existing label standing. Each is a case the rules were written
to decide, against a label applied before those rules existed.

| Issue | Was | Now | The rule that decided it |
|---|---|---|---|
| #3, #5, #6 | `enhancement` | `bug` | a gesture diverging from VB6's own is a `bug` even when the command is new |
| #9, #11, #50 | `bug` | `enhancement` | never built, and its absence degrades gracefully |
| #21 | `enhancement` | `bug` | an umbrella takes the type of the condition it was opened to close |
| #60 | `enhancement` | `bug` | it loses data |
| #69 | `enhancement` | `bug` | a VB6 gesture that is permanently disabled |
| #16, #485 | `enhancement` | `chore` | machinery and a corpus upgrade, not features |

The `bug` → `enhancement` direction was the one worth checking, since it reads as a demotion. #11 — the
Object Browser showing text glyphs instead of member-kind icons — was put to the maintainer as the test
case: if a wrong-looking UI is a `bug` regardless, the rule is too narrow and should have changed
instead. It was confirmed as `enhancement`, so the rule stands as written.

### 11.2 Six issues carrying `needs-oracle` that the tightened rule no longer qualified — **removed**

#178, #180, #222, #223, #224, #225 (they carried `fidelity`, before §8.18 renamed it). Under §5.4 the
label marks a measurement *still owed* to vb6.exe; for these the answer is already in the checked-in
VB6-authored corpus, and byte-level round-trip correctness is carried by `serialization`. Removing a
label a maintainer applied is a different kind of act from adding one, which is why it waited for a
decision rather than being swept up in the first pass.
