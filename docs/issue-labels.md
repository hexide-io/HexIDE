# Issue labels

How this backlog is labelled, and which label to reach for. The label set itself lives on GitHub; this
is the part GitHub cannot hold — what each label means, and which one wins when two look plausible.

**Filing an issue?** You need [Axis 1](#axis-1--type-exactly-one) and nothing else. A maintainer adds
the rest at triage; `area` in particular has a landing rule and five boundary rules behind it, and is
not something a filer should have to get right.

**Labelling one?** Read it all once. It is shorter than it looks and the rules are meant literally.

> **Where the reasoning lives.** [`archive/issue-labels-decided.md`](archive/issue-labels-decided.md)
> records how this set was arrived at — the passes, the counts, the labels that were proposed and died
> on the evidence. Kept rather than deleted because it holds the measurements, which is the part not
> recoverable from the commit that applied them. Section references below (§8.3 and the like) point
> into it.

**Every rule here was written to settle a specific disagreement between two independent raters over
184 issues.** They are meant to be applied literally. Where a rule reads oddly narrow, that is usually
the point — the wide reading is what two careful people already split on.

An issue takes **exactly one** `type`, **exactly one** `size`, **zero or more** `area` (prefer one, at
most three), **zero or more** `state` (usually none).

---

## Axis 1 — type (exactly one)

**`bug`** — Something isn't working.

- A gesture, default binding or menu item that diverges from VB6's own is `bug` even when the command
  it should reach is new or currently unbound.
- Behaviour VB6 has and HexIDE never built is `bug` when it crashes, corrupts or loses data.
- Where the same guard already exists elsewhere in the same component, it is `bug` — one guard applied
  inconsistently, not a new capability.

**`enhancement`** — New feature or request.

- Behaviour VB6 has and HexIDE never built is `enhancement` when its absence degrades gracefully: a
  disabled item, nothing drawn.
- A bad or misleading outcome the code produces **while correctly following its spec**, whose fix is a
  capability the spec never had, is `enhancement`. **"Its spec" means HexIDE's own spec for the
  component** — an `openspec/specs/` document — not an external protocol it implements.
- An open design decision whose answer is a change to HexIDE is `enhancement`, however undecided.
- A release artifact a user receives is `enhancement` even when the code lands only in packaging.

**`chore`** — Neither a defect nor a feature: a measurement, a decision written down, machinery moved.

- If you are reaching for `enhancement` only because nothing else fits, this is the label you wanted.
- Where the prose being written down **is** the decision, `chore` wins over `documentation`.

**`documentation`** — the prose is itself the defect: a doc or spec that is stale, wrong or absent
about something already decided. Not for a code fix that happens to need a doc update.

**Umbrellas:** an issue carrying `tracking` takes the type of the condition it was opened to close.

---

## Axis 2 — area (zero or more; prefer 1, at most 3)

**The landing rule:** an area applies when the fix will **land code in it**, including a prerequisite
the body names. A subsystem mentioned only as context does not qualify.

**Where these names come from.** `openspec/specs/` is the repo's own agreed capability decomposition, it
ships publicly, and `DesignRecordTests` fails the build when it drifts. It holds **39 capabilities**;
these are **22 areas**, so roughly 17 merges. Where an area's name matches a capability's, it means the
same thing — `lsp-client`, `language-server`, `protocol-inspector`, `grammar`, `form-designer`,
`object-browser`, `localization`, `ai-chat` all resolve to a spec directory. Two deliberate divergences:

- **`lsp-transport` is not a capability.** Transport sits inside `specs/lsp-client/`. The label splits it
  out because stdio, named pipe and WebSocket have their own test classes and their own failure modes,
  and one 40-issue label is not a filter. **When they disagree, the spec wins for design and the label
  wins for triage** — the capability decomposition is build-guarded and will outlive these labels.
- **`profiles` folds two capabilities**, `ide-personalities` and `user-data`. That is not an oversight:
  personalities, safe mode, profile directories and `--user-data-dir` are one axis, not three features,
  and personalities is being superseded and subsumed by profiles.

**Two areas is normal for a cross-cutting seam.** Export and redaction
(`HexIDE.Core/Redaction/`, `HexIDE.Core/Conversations/`) are consumed by the Protocol Inspector's UI
*and* by the `export_lsp_conversation` MCP tool, and a single change routinely touches both. Such an
issue takes `protocol-inspector` **and** `mcp`. Do not make yourself choose — the axis allows up to
three precisely so a real seam is not forced into one owner.

**The unknown-cause escape:** where the code at fault has not been localised, **leave area empty**
rather than guessing. Zero areas is legal. The failing suite's own area applies only when the body
names the test or harness itself as the fault.

**The three-area cap is a cap, not a target.** 151 of 184 issues take exactly one area and that is the
healthy shape; the cap exists so an issue cannot be labelled into every filter at once. Where a body
names more than three real owners, label the three that carry most of the work and say in a comment
which were dropped — a known-incomplete label is better than a silently incomplete one.

A third area has to be **earned by the landing rule**, not spent because it is available: the fix must
land code in it. "This touches everything" is not a third area.

| Area | Applies to |
|---|---|
| `lsp-transport` | Getting a server running and talking: launch, stdio/pipe/socket/WebSocket, message framing, `lsp-servers.json`, shutdown, teardown, stray processes. |
| `lsp-client` | What HexIDE says and understands once a server is talking: capability gating, document routing and the connection registry (including claim resolution), sync kinds, URIs, diagnostics plumbing. |
| `protocol-inspector` | The windows that explain a server to a human: the protocol inspector, the Language Servers window, the capture and its redaction. |
| `language-server` | The bundled VB6 server under `LspServer/`: what it advertises and what it answers. |
| `grammar` | The shared VB6 grammar, both `.g4` copies, judged by the conformance corpus. |
| `interpreter` | The tree-walking interpreter. |
| `debugger` | Breakpoints, break mode, stepping, watches, locals, call stack, Immediate window. |
| `compile` | Building a VB6 project, including driving real `vb6.exe` and parsing what it says. |
| `form-designer` | The visual designer and the controls it draws: toolbox, selection, geometry, properties grid. |
| `code-editor` | The code window: typing, indent, block completion, find/replace, bookmarks, squiggles, editor state. |
| `project-system` | A project **once it exists**: loading, members, naming, saving, the file watcher, crash recovery, workspace roots, the sidecar. |
| `object-browser` | The Object Browser and the type-library metadata behind it. |
| `serialization` | Reading and writing VB6's own files. |
| `profiles` | Surfaces that promise extension without a code change: theme packs, keymap packs, toolbars, personalities, profile bundles. |
| `localization` | Language packs, translations, access keys, RTL, the translation editor. |
| `ui` | **Residual** for IDE chrome with no more specific area: menus, dialogs, window layout, icons, project-creation surfaces. |
| `addins` | The add-in system end to end: manifests, packaging, loading, tool windows, trust. |
| `mcp` | The MCP server and its tool surface. |
| `ai-chat` | The bundled AI Chat panel: clients, settings, request lifecycle. |
| `tooling` | The repo's own machinery: test projects and frameworks, conformance and designer corpora and their fixtures, local guard scripts. |
| `ci` | GitHub Actions itself: workflow, matrix and runner configuration, and what CI does or does not run. |
| `packaging` | What gets shipped and signed: installers, Release artifacts, bundles. |

### Boundaries that were repeatedly contested

- **`serialization` ends once a record's bytes are correctly in memory.** Decoding a preserved blob for
  display belongs to the area that displays it. A caller that merely reuses an existing reader or writer
  takes its own area alone; `serialization` applies when the change alters *how* a VB6 file is read or
  written.
- **`ui` is a residual and must never shadow a more specific area.** The Language Servers window is
  `protocol-inspector`, not `ui`. Project *creation* surfaces — the New Project dialog, project templates,
  the project-type list — are `ui`; `project-system` covers a project once it exists.
- **`interpreter` applies only when the change lands inside the interpreter.** A debugger or UI surface
  that merely *consumes* interpreter semantics — resumability, `Err` state — stays in its own area.
- **A construct that is refused rather than mis-executed is `grammar`, not `interpreter`.**
- **`ci` is for `.github/` itself.** A test that fails intermittently but whose fault lies in product
  or fixture code takes `flaky` plus the area of the code at fault. An issue about what CI does or does
  not exercise is `ci` alone, even when the thing left unexercised is the Release configuration.

---

## Axis 3 — size (exactly one)

Size the **authoring** cost: designing, writing and testing the change once the answer is known.
T-shirt only — **never hours, never days.**

### Preamble — apply these before reaching for a rung

1. Size **only the work this issue asks for.** A prerequisite the body hands off to a separate issue is
   excluded.
2. Size it **as if it were picked up first and alone.** If a sibling landing first would make it
   cheaper, note that in the body. It is **not** a smaller size and it is **not** `blocked`.
3. Where the body offers **ranked options**, size the one it states as preferred, and record the
   alternative's size in a comment.
4. Where the body offers **a first slice and a full scope** rather than alternatives, size the first
   slice and say so in the issue.
5. **Mechanical breadth does not raise size** — the same key added to every language pack, a
   regenerated table — above the decisions the change actually requires.
6. Where the **cause is not yet identified**, size the fix you expect once it is diagnosed, and default
   to `small`. `trivial` requires the cause to be already understood.
7. A **bundle of independent fixes sizes as their sum**, not as its largest single fix. A bundle that
   changes a signed or persisted format in more than one place is at least `large`.
8. **The `needs-oracle` carve-out removes only the cost of taking the measurement**, not the design choices
   the measured answer then forces. "Once you know the answer" means the vb6.exe measurement is settled
   — *not* that the issue's design fork is settled.
9. Where the deliverable **is** a decision plus the prose recording it, size the prose, not the
   deliberation.

### The rungs

**`trivial`** — anchor **PR #31** (match the identity pattern case-insensitively; 1 file, +8/−1).

- One edit site, with the correct **form** of the edit already settled, plus a test that only pins it.
- The same obvious one-line edit repeated at a handful of adjacent sites is **still** `trivial`.
- What lifts it out: a second concept; a test harness that does not already exist; a new seam or
  structure; cleanup that a removal drags with it, such as persisted settings still naming the removed
  value; or an edit whose correct form still has to be chosen.

**`small`** — anchor **PR #240** (say where an out-of-cone member actually lives; 6 files, +177/−23).

- One clear seam, a handful of files, a few new tests, and **the mechanism already settled** — the
  remaining cost is applying it.
- A design decision already settled in a merged spec or an in-flight change's design doc leaves the
  issue here.

**`medium`** — anchor **PR #242** (advertise what the server can actually do; 7 files, +288/−14).

*Read this rung in three parts, in order.*

**(a) The gate.** `medium` is **both** halves, not either:

- a design decision **still open to the implementer**, *and*
- a coupled fix that **cannot be split out**.

The decision must change **which components exist** — not merely pick a value such as fail-versus-warn.
It may be latent in the code rather than stated in the issue: what a cancelled or half-finished
operation leaves behind is such a decision. An alternative the body records and reasons away still
counts as the decision half. A decision already settled does not.

**(b) Any one of these is independently sufficient**, gate or no gate:

- the change introduces a new node, item or state kind that **other components must then render or
  handle**;
- it must land on **both the producing and the consuming side** to have any effect at all;
- it introduces the **first mechanism of its kind in that area** — the first key handling in a view,
  the first cache, the first background queue;
- it cannot be asserted without **test infrastructure that does not yet exist** — a new test project
  plus its solution and CI wiring;
- the body **lists fix options without choosing between them**.

**(c) Before settling on `medium`, test `large`.** Do not stop at the first matching clause.

**`large`** — anchor **PR #243** (route documents to every server that claims their language; 10 files,
+842/−54).

- A new component with **more than one behaviour to decide inside it**, or one with no precedent in the
  repo to copy and a test suite of its own.
- A loading or discovery path that follows a precedent already in the repo and stays behind an
  unchanged interface is `medium`'s coupled fix, not `large`.
- Bundles: see preamble rule 7.

**There is no `medium-large`, and no "round up if in doubt".** Both existed in an earlier draft and
behaved as a one-way ratchet — the same rater was the higher one on 34 of 37 size splits. Removing them
brought that to 15 of 23.

**PR #265** (attach a language server without rebuilding; 78 files, +3551/−471 — configuration,
discovery, consent, docs, migration) is **not an anchor**. An issue that size should be split before it
is picked up. Say so in the issue rather than labelling it `large`.

---

## Axis 4 — state (zero or more; usually none)

Every definition below is deliberately **enumerated rather than thematic**. That is what made them
work: under thematic wording these four labels took 57, 31, 13 and 12 issues and two raters split on
63 of 184; under the enumerated wording they take 35, 14, 5 and 10, and disagreement fell to 18.

### `silently-wrong`

**TRUE only in one of these four shapes:**

| | Shape |
|---|---|
| (a) | a wrong value, wrong text or wrong file content is presented to the user **as correct** |
| (b) | a write the user asked for does not happen, or happens **somewhere the user did not choose** |
| (c) | an operation is accepted and then **not performed**, and nothing surfaces a failure |
| (d) | HexIDE writes a file that real `vb6.exe` **refuses to open, or compiles into a different program** |

**FALSE, explicitly, for:**

- a resource left behind — a leaked process, an unreclaimed slot, a stray handle — that produces no
  wrong user-visible result;
- a log line that is unhelpful, misattributed or missing, **where the product behaviour is correct**;
- a feature that is visibly absent, disabled, greyed or unimplemented;
- an error that **is** raised, however badly worded, wrongly numbered or unhelpful;
- a defect that would be silent only in a hypothetical future server or caller that does not exist.

**The test is the enumerated shapes, not the phrase "the failure does not announce itself."** That
phrase is what produced 57 issues — 52% of all bugs — and a label that matches half the bugs filters
nothing.

### `needs-decision`

TRUE when a fork **the maintainer** must answer stands between the issue and any authoring, and the
body names that fork.

FALSE when:

- the fork is one **the implementer can settle** while doing the work — that cost is priced on Axis 3
  instead, and **the same fork never counts twice**;
- the body **states its own recommendation** or preferred option — a recommended option is not an open
  fork;
- the issue merely has more than one possible implementation, as most issues do.

### `blocked`

TRUE only for a **hard dependency**: the work cannot **start** until something else lands in this
repository.

FALSE when a named sibling would merely make the work cheaper, tidier or better-ordered — that is a
note in the body. An upstream report or external filing the issue also asks for does not make it
unblocked; judge only the in-repo change.

**Check the blocker's state, do not trust the prose.** Every candidate that survived verification did so
because its named blocker was still open, and every one that failed did so because the blocker had since
closed. A body that says "blocked on #N" is a claim about the past.

Three shapes will fool a text scan, and all three occur in this backlog:

- **the negation** — "Blocked on nothing", or a sentence saying the *other* issue is not blocked by this one;
- **the already-landed blocker** — the citation is real and the issue closed months ago;
- **the hypothetical** — "splitting this would produce two issues blocked on a third".

**A `## Prerequisite` heading is not sufficient.** Two issues carry one and are startable: in one the
prerequisite is work the contributor does anyway as part of this issue, in the other it is step 1 of the
issue's own stated order. Ask what the contributor would be *stuck* on, not what ought to land first.

The residual risk is a dependency stated in **neither** prose nor a citation — a feature that would be
inert until a sibling lands, even though nothing says so. That is a judgement call; make it explicitly
and record it in the issue.

### `needs-oracle` *(was `fidelity`; the wording is the original, only the name changed)*

> **"Fidelity" means three different things in this project, and this label is the narrowest of them.**
>
> 1. **The Fidelity Principle** — `CLAUDE.md:489`: *"Fidelity means reproducing VB6's intended
>    behaviour, not its bugs."* A live, load-bearing principle about what correct means here.
> 2. **Tier 1 — Fidelity** — a feature-tier classification of *what gets built*, tested by "would a
>    VB6 developer from 1998 recognise this?". It is used in maintainers' planning and is historical;
>    you are not missing a document you should have.
>
>    *That phrasing is deliberate.* An earlier draft of this very section cited the maintainers'
>    roadmap by path and line. Pointing a public reader at a file they do not have is not
>    carelessness — it is what happens when someone writes with the private tree open, which is
>    exactly why the rule exists and why it is easy to break while documenting it.
> 3. **This label** — a **measurement you still owe the oracle VM**, and nothing else.
>
> An issue can be Tier 1 work, governed by the Principle, and carry no label here. The three share a
> word and no meaning, which was the argument for the rename: only the third sense is a state an issue
> can be in, and only the third is something a filter should return.
>
> **Renamed `fidelity` → `needs-oracle` on 2026-09-20**, description untouched. All 25 assignments
> carried over. Note for anyone verifying: `gh issue list --label` reads a search index that lags a
> rename by a minute or so and will report zero — check the issues' own labels instead.

> The answer comes from real vb6.exe. Measure it, then record it in docs/vb6-fidelity-oracle.md.

- Apply **only** when the issue cannot be closed without running real `vb6.exe` and recording the
  result. If the answer is already visible in the checked-in VB6-authored corpus, leave it off.
- Byte-level round-trip correctness of VB6's own formats is carried by `serialization`; this label is
  for questions the round-trip corpus cannot answer.
- **Not a topic tag.** An issue that merely concerns VB6 behaviour, or quotes the fidelity principle,
  does not qualify. Neither does a feature whose scope and presentation are ours to choose.
- Apply it if **any** part of the stated scope needs `vb6.exe`, even where the main fix can land
  without that answer — and such an issue is never a `good first issue`.
- It marks a measurement **still owed**. Drop it once the answer is in the oracle, even where the body
  still carries the questions it was filed with.

### `misleading`

A surface that **works** and tells the user or a calling agent something **untrue**. **Additive** —
apply it alongside the area that owns the code.

Both halves are required, and the name is chosen to say so. This label was `ux` in draft and was the
single most contested label in the whole exercise — 7 of its 8 uses disputed, in both directions —
because "ux" invites the reading "this is a usability problem", which is true of far more issues than
the gate admits.

- It requires a surface that is **present and working**. A construct that fails, crashes or is
  unimplemented belongs to its area alone, however bad the message.
- A tool that **advertises a capability nothing implements** does qualify: that surface works and lies.
  A field that is simply wrong for its own name does not — that is an ordinary defect in its own area.
- It names a cross-cutting quality rather than a place in the code, so **it never counts as an extra
  area** in the `good first issue` test.

> **What this label deliberately does not cover:** correct behaviour that cannot be *discovered*. #263
> — a Windows path in `lsp-servers.json` needing doubled backslashes — is correct and undiscoverable,
> and takes no state here. That bar is a review standard (`CLAUDE.md` on the automation surface,
> `docs/mcp-server-gaps.md` as the worked example), not a filter. Decided at §8.3 of the decision record.

### `security`

Signing, revocation, package verification, consent, redaction, or a process/filesystem escape.

### `flaky`

Fails intermittently. Takes the **area of the code at fault**. `ci` applies only if the fix lands in
`.github/` — see the area boundaries above.

### `tracking`

An umbrella. Apply **only** when the work itself lives in other, separately filed issues. An issue that
enumerates sub-tasks it will close **under its own number** is not `tracking`, however explicitly it
says it exists to track them.

---

## Off-axis

`duplicate`, `invalid`, `wontfix`, `question` — closing verdicts and triage hygiene, not
classifications. Leave them alone.

### `good first issue` — derive it, do not judge it

All five must hold:

1. size is `trivial` or `small`;
2. **exactly one** area (the `misleading` state does not count as one);
3. no `needs-oracle`;
4. no `blocked`;
5. no `needs-decision` — an issue that leaves a choice between two materially different resolutions to
   the implementer is never a good first issue.

Compute this from the other axes rather than judging it per issue: asked to judge it directly, a model
certified an issue carrying two areas, which rule 2 forbids.

**The derived list is a queue to pick from, not a label to apply.** It returns 67 of 184, plainly too
many to advertise. Verifying `blocked` issue by issue grew that number rather than shrinking it, so the
gate was never what stood between this and a usable list.

### Then pick by hand, and these are the disqualifiers

The five gates are **necessary and nowhere near sufficient**. They are all mechanical, which is what
makes them reliable, and also what makes them blind to everything below. None of this can be derived,
so it is a checklist rather than a rule, applied when choosing which of the 67 to actually advertise.

Disqualify an issue, however well it scores on the gates, when:

- **A plain fork cannot build or run it.** Check this first, because it is invisible from the issue.
  The bundled AI Chat add-in is only packaged when a first-party signing key is present, and no fork
  has one — `CONTRIBUTING.md` says so and CI prints it as expected. So the panel does not exist in a
  contributor's build, there is no Tools menu entry for it, and they cannot produce the screenshot
  `CONTRIBUTING.md` asks for on a UI change. That is intended behaviour rather than a defect, and it
  still makes every `ai-chat` issue a poor first issue until someone writes down how to run bundled
  add-ins from a keyless clone.
- **A build guard pins it, and the way past the guard is a maintainer-only workflow.** Renaming a
  `### Requirement:` heading in `openspec/specs/` looks like editing Markdown, but
  `DesignRecordTests.EveryArchivedRequirementReachedTheSpecItTargeted` builds its expected set from the
  deltas under `changes/archive/`, so the heading cannot move by editing `specs/` alone. The clean route
  is a `## REMOVED Requirements` change archived through the openspec CLI — tooling `CONTRIBUTING.md`
  calls optional, in a workflow `CLAUDE.md` says must never be done by hand, on files `CONTRIBUTING.md`
  says are the maintainer's job. The contributor meets a red test with no obvious escape.
- **The setup cost dwarfs the change.** Anything needing a foreign language server running locally, the
  `vb6.exe` oracle VM, or a hand-built fixture tree. A newcomer's first hour should not be spent on
  prerequisites, and they will not tell you they gave up.
- **The result is invisible.** If the contributor cannot *see* the change work — plumbing, protocol
  framing, an MCP tool description, a guard script — they finish without ever feeling they finished.
  This is the one that rules out most of the backlog's genuinely small issues, and it is deliberate.
- **Being wrong is silent.** VB6 semantics, coercion rules, arithmetic edges. A newcomer cannot
  self-check a divergence they have no oracle for, and `needs-oracle` only catches the cases where we
  already knew a measurement was owed.
- **The work is diagnosis, not a fix.** An intermittent failure, or a cause that is not yet identified.
  Sizing it as `small` prices the eventual fix, not the hunt, and the hunt is miserable without context.
- **It has a prerequisite**, even an informal one. `blocked` catches the hard dependencies; a "do this
  after that lands, or you will do the work twice" is just as expensive to walk into.
- **It is entangled with a live design question.** If a decision in flight could invalidate the fix,
  the contributor pays for our indecision.

And one positive selector worth applying on purpose: **an issue that needs a language rather than C#**.
The language-pack issues are JSON edits that need a speaker of the language, which is the widest door
this repo has and reaches people the rest of the backlog never will.

**The bar is not "is this easy".** It is *"can someone with no context finish this, and know that they
finished?"* Most small issues fail the second half.

### The standing target is twenty

A **maximum**, not a quota. Twenty is enough that an arriving contributor finds something in their area
and does not meet an empty shelf, and few enough that each one can be checked properly and reviewed
promptly when the PR lands. If fewer than twenty survive the two passes below, advertise fewer — an
issue that disappoints someone is more expensive than an issue they never saw. Promote from
`help wanted` as advertised ones are taken, rather than topping up with whatever is left.

### One gate: write the scaffolding comment

Before the label goes on, **someone other than the person who chose it opens the code and writes the
comment a newcomer would need.** That single act is both the review and the deliverable: it either
produces a usable comment, or it produces the reason the issue is unsuitable.

There is deliberately no separate approval step in front of it. On the first run of this process every
rejection came out of the attempt to write the comment — a reviewer asked "is this a reasonable first
issue?" says yes, because from the issue body it always is.

**The comment says:** where the change goes, with files and symbols; the exact command to run; how they
will know it worked; and the one gotcha specific to this issue. The newcomer's blocker is rarely finding
the file — it is not knowing whether they have finished.

**Write it against the tree, and check it claim by claim.** Every path, symbol and test command gets
opened, not inferred. On the first run **nine of fourteen first drafts carried factual errors**: the
wrong method on one of two paths through a feature, a test rationale that was backwards, an NSubstitute
auto-value assumed to be `null` when it is `string.Empty`, a list of key namespaces that omitted the
very settings page the comment then told the reader to open.

Inaccurate scaffolding is worse than none. It sends someone confidently to the wrong place, and when it
does not work they assume the fault is theirs. If a claim cannot be confirmed, leave it out.

**Three of twenty picks failed this gate**, each for something invisible from the issue text:

- a feature whose add-in is only packaged when a first-party signing key is present, so **no fork can
  run it** at all;
- an issue naming a `.vbp` key as undecided, where **no checked-in corpus file carries it**, so the
  answer needed the oracle;
- a Markdown rename **pinned by a build guard**, whose only clean escape is a maintainer-only workflow.

All three read as clean small issues. None was findable without opening files. **If the comment cannot
be written honestly, that is the answer** — do not apply the label, and say in the issue what would make
it eligible.

### `help wanted`

A maintainer's judgement about wanting outside help. It cannot be derived. **Every `good first issue`
carries it too, and not the other way round**: a `help wanted` issue can be too large, or not yet
scaffolded, to offer a newcomer, and those are where `good first issue` is promoted from as advertised ones
are taken. It is advertised in its own right — GitHub features it beside `good first issue` — so it is not
a place to hold issues out of sight.

### Claiming

Comment on an issue to claim it. If there is no further activity for **seven days** it is unclaimed
again and anyone may pick it up. This exists so two people do not spend the same weekend on the same
issue, which is the most expensive thing that can happen to a first contribution.

---

## A note on keeping this true

If a labelling call is hard enough to argue about, the argument belongs here as a sentence, not in a
comment thread. Every rule above started as two people reading the same words and reaching opposite
conclusions. The document is useful exactly to the degree it keeps absorbing those.

The counter-pressure is real, though, and worth stating: the dominant residual failure is **rules that
exist and were not found** — 56 of 63 disagreements in the last full pass. Every sentence added raises
that rate. Prefer deleting an ambiguous sentence to adding a clarifying one.
