# Serialization outcomes — the acceptance framework

Decided 2026-08-11. The axis for judging any round-trip defect: **what does it do to the user's file?**
This replaces ad-hoc severity labels, which kept conflating "ugly diff" with "destroyed project".

> **The framework stands; the numbers in it are the 2026-08-11 position.** Where this file says "0/22",
> read it as the state that prompted the decision, not the state today — that is now **21 of 22**, with
> the 22nd refused rather than saved. The prediction below was borne out: pass-through landed, and it
> moved the number without the text writer changing. The live figure is `KnownVb6Failures` in
> `IDE/HexIDE.Runtime.Tests/SerializationCorpusTests.cs`.

## The five outcomes

| # | Outcome | Verdict |
|---|---|---|
| **0** | **Won't round-trip at all** — HexIDE refuses to open, or refuses to save | **Acceptable.** Honest and safe. |
| **1** | **Byte-identical round-trip** | Ideal. Required for text formats. |
| **2** | **Semantically-identical round-trip** — bytes differ, meaning does not | Acceptable for companion binaries. Interim only for text. |
| **3** | **Works in HexIDE, fails in VB6** | **Worst outcome in the set.** Never acceptable. |
| **4** | **Outright broken** — the file is corrupt or unloadable anywhere | Never acceptable. |

### Why 0 is its own category, not a flavour of 4

Refusing to open a file is *safe*: nothing is damaged, the original is intact, and the fix is entirely on
our side. Saving a file that then cannot be loaded is *destructive*. Both look like "it's broken" from the
outside; the blast radius is opposite. Any defect we cannot fix in time should be moved **into** category 0
deliberately — refuse the operation — rather than left to produce a 3 or a 4.

### Why 3 is worse than 4

Outright breakage announces itself immediately: you try to open the file and it fails, while you still have
the original and remember what you did. Category 3 is silent. You work in HexIDE for a week, committing as
you go, and discover the problem when you try to return to VB6 — by which point the last good version may
be gone from your working tree.

**Any finding in category 3 is a launch blocker regardless of how narrow it looks.** Issue #22 (menu
shortcuts) is the canonical example: the form still opens perfectly in HexIDE.

## Target per format

| Format | Target | Why |
|---|---|---|
| `.vbp` | **1** | Small key=value file; humans read its diffs |
| `.frm` / `.ctl` designer block | **1** | See "diffs are the real requirement" below. 2 is an interim state, not a resting place |
| `.cls` / `.bas` headers | **1** | These carry Instancing semantics; preserving what was read is both easier and safer than regenerating |
| `.frx` / `.ctx` / `.pgx` | **2** | Byte-identity is *not* required — see below |

### Companion binaries: 2 is the bar, and 1 arrives anyway

A control persisting through the COM interfaces (`IPersistStream`, `IPersistPropertyBag`) has no obligation
to emit identical bytes twice, and in practice does not. VB6 developers under source control already treat
`.frx` churn as noise. So byte-identity is not a fidelity requirement here and **must not be asserted**.

But for an *opaque* blob, byte-preservation is the only practical route to semantic identity — HexIDE cannot
ask an OCX whether the bytes it wrote are good. So pass-through lands at outcome 1 in practice. That is a
*means*, not a goal: we keep the bytes because it is the only way to guarantee meaning, not because the
bytes must match.

### The two targets are coupled — text byte-identity depends on the blob strategy

The offsets live **in the text**:

```
Picture         =   "Form1.frx":0442
```

So renumbering blobs rewrites the `.frm` as well. **Byte-identical text is unachievable if the companion is
regenerated**, no matter how good the text writer is — every citation churns.

Pass-through resolves it: preserve the companion bytes at their original offsets and the citations are
unchanged too. That makes pass-through a **precondition for the text target**, not merely the safe choice
for blobs. The two decisions are one decision.

The exception is legitimate: if the user actually edits a blob-backed property, the citation *should*
change, and that diff is meaningful. What we are eliminating is churn with **no semantic change** — the
`.frx` renumbering itself and dragging thirty citation lines with it.

**Consequence for the current harness numbers:** part of today's 0/22 text failures are offset churn, not
text-writer defects — `FormSerializer` calls `FrxSerializer.Write`, which assigns fresh offsets, and those
propagate into every citation line. The text writer is bad, but the measurement currently overstates by an
unknown amount. Landing pass-through will move that number without touching the text writer at all, and
only then is the residual an honest measure of the writer.

### Text: diffs are the real requirement

Byte-identity for text formats is not polish, and the argument is not fidelity. If a save reorders thirty
properties, every commit becomes unreviewable, code review is impossible, and `git blame` is destroyed —
even though the file is perfectly valid. That is a usability failure in exactly the source-controlled
workflow this tool exists to serve.

Tolerating churn in binaries is forced; nobody extends it to text.

## Comprehension is a feature enabler, not only a fidelity cost

Preservation (keep the bytes) and comprehension (know what they mean) are separate problems, and only
preservation is needed for the outcomes above. But comprehension is not therefore worthless-until-someone-
edits-an-image — it unlocks product surface that does not exist today:

- **A resource editor.** VB6 shipped one as an add-in; a modern equivalent is a straightforward
  Evolution-tier win once the records can be decoded.
- **Resource nodes in the Project Explorer** — the images and data a form or UserControl actually carries,
  shown as children of it rather than hidden inside an opaque sibling file. That is a VS-shaped affordance
  on a VB6-shaped model: recognisable, and better.

**Two formats, not one.** They are often spoken of together and are genuinely different:

| File | Format | Scope |
|---|---|---|
| `.frx` / `.ctx` / `.pgx` | VB's own length-prefixed blob container (`06 03 00 00 6C 74 00 00 …`) | Per form / UserControl / PropertyPage |
| `.res` | Standard **Win32 RES** (`00 00 00 00 20 00 00 00 FF FF …`) | Project-level; what `LoadResString` / `LoadResPicture` read |

A resource editor would want both. The `.res` side is a documented Win32 format with no VB-specific
mystery; the `.frx` side is the one needing the record layouts.

So the sequencing stands — preservation first, because it is what stops damage — but comprehension should be
scheduled as **feature work with a fidelity dividend**, not as fidelity debt nobody wants to fund.

## How each outcome is detected

| Outcome | Detection | Where |
|---|---|---|
| 1 | Byte comparison after load+save | `SerializationCorpusTests.Forms_and_controls_round_trip_byte_for_byte` |
| 2 | Every offset the form cites resolves to a record, and that record is unchanged | `SerializationCorpusTests.Every_companion_offset_cited_by_a_form_resolves_to_a_blob` |
| 4 | Load failure on our own output | Implicit — a failed re-read |
| 0 | Deliberate refusal | Not yet implemented as a gate |
| **3** | **Only the oracle can see this** | `Vb6OracleRoundTripTests` — round-trip through HexIDE, then `vb6.exe /make` |

**Category 3 is the only outcome we cannot detect by reasoning about our own code.** Every other check is
HexIDE marking its own homework. That is why the oracle lane exists and why it is worth its runtime: it is
the only gate that can fail for a reason we did not anticipate.

## Running the oracle lane

Slow (one `vb6.exe` process per corpus form) and Windows-only, so it is opt-in:

```sh
HEXIDE_ORACLE=1 dotnet test HexIDE.Runtime.Tests/ --filter "FullyQualifiedName~Vb6OracleRoundTrip"
```

It is self-calibrating: each form is compiled **before** the round-trip as a control. A form VB6 cannot
build in the first place — a missing OCX, an unregistered dependency — is skipped rather than blamed on
HexIDE. Only "VB6 built the original but not our output" counts as a failure, which is category 3 exactly.

---

## Status: the gate is in place (2026-08-11)

Forms HexIDE cannot reproduce are now **refused on save** rather than written. Detection is the deepest
`Begin` nesting seen at load: depth 1 is the root, 2 is the root plus direct children, and 3+ means a
container holding controls or a menu holding items — exactly the structure `FormDefinition.Components`
cannot represent, because it is flat and `ComponentInstance` has no parent link.

**This catches 12 of the 22 VB6-authored corpus forms**, which is a lot, and it is the honest number: those
are precisely the forms a save would flatten. The developer is told once per save which forms were skipped
and why; silence would be the worst outcome, protecting the file while letting them believe the edit landed.

The effect on the gates:

| | Before | After |
|---|--:|--:|
| Category-3 regressions (`Vb6OracleRoundTripTests`) | 5 | **0** |

That is a real resolution rather than a suppression. The failure this lane exists to catch is a file the
developer cannot reopen in VB6, and there is now no way to produce one — the defect moved from outcome 3
to outcome 0, which is the move this document recommends.

**What is still owed.** The refusal is enforced at the save path only; the designer and code editor will
still let a developer edit a form whose changes cannot be persisted, and they find out at save time. Gating
the *editing* surface is the follow-up, and it is the difference between "HexIDE protected my file" and
"HexIDE wasted my afternoon". When the parenting model lands and the refusal is lifted, the category-3
baseline must stay at zero on its own merits.
