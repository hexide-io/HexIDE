# Enforce the completeness of the packs that ship

## Why

`CLAUDE.md` states the guarantee the whole localisation system rests on:

> the guarantee that makes this system worth anything is that **every shipped pack is 100% complete,
> enforced at build**

Nothing enforced it. `LocalizationCoverageTests` checks three things and all three point *at* the canonical
pack — that AXAML keys exist in it, that every VB6 property has a description in it, that region packs
introduce nothing absent from it. None of them asked whether the twenty-nine translations *of* it were
current. The only thing that did was `tools/TranslationCoverage`, a manual `dotnet run` that always exits
zero and that no workflow invokes.

So a missing key inherited English in silence, which is right for a user and is exactly what must not reach
a release. This is the fail-open shape the repository has paid for twice: the `HEXIDE_REQUIRE_FOREIGN_LSP`
guard that threw at discovery and turned a red suite green with fourteen tests simply absent, and the
coverage table that drifted into fiction until it was generated and checked. A guarantee nothing verifies
reads green either way.

It matters more than tidiness because that guarantee is the stated reason for refusing further languages.
Every pack is a permanent tax on every new key — adding two keys costs fifty-eight translations — and the
only thing that makes the tax worth paying is that the packs are actually complete. Enforcing it is what
makes the refusal honest. Reported as hexide-io/HexIDE#421.

## What Changes

- Three tests in `HexIDE.Tests`, so this rides the existing `build-ide` job and needs no CI change:
  every pack declared in the manifest carries every canonical key; every declared pack has a file; every
  full translation on disk is declared.
- `language-packs` gains a requirement that a **declared** pack is complete, and its existing "declare only
  what it translates" requirement is scoped to the packs that rule was written for.
- `CLAUDE.md` names the guard, so "enforced at build" is checkable rather than asserted.

### The spec said the opposite, and the contradiction is the interesting part

`language-packs` currently says a pack "SHALL NOT be required to enumerate the full key set", and its
scenario reads *"a pack defines values for some keys only → it is valid and ships"*. Read literally that
forbids this change.

Both rules are right about different packs, and the spec did not distinguish them because when it was
written there was nothing to distinguish:

- **A region pack** is a variant of a neutral and overrides only what differs. `en-GB` carries eighteen of
  seven hundred and twenty-seven keys, deliberately. Measuring it against the canonical pack would report a
  design decision as a defect.
- **A neutral pack** is not a variant of anything. Inheritance is its safety net, not its design: a missing
  key renders English rather than blank, which stops a gap becoming a broken control and is not a licence
  to leave the gap there.

The spec's own justification survives intact. It worried that completeness "would block a contribution
until it was finished" — and it still cannot, because a pack file may sit in `Packs/` half-done
indefinitely. Completeness is required to be **declared in the manifest**, which is the moment the project
starts telling users the language is available.

### What it costs, stated plainly

Adding one user-facing string means twenty-nine translations in the same change, or the build is red.
`CLAUDE.md` already instructs exactly that — *"Translate every new key into all shipped packs in the same
change — don't defer"* — so this makes an existing instruction enforceable rather than adding a new burden.
It does mean the code and the translations can no longer be split across two commits.

The duplication this appears to introduce is already the established practice, and is load-bearing. Every
pack already spells out between ten and fifty-three values identical to English: `id` has fifty-three
(`Edit`, `Debug`), `fr` and `nl` forty-five (`Menus`, `Options`), `ur` and `fa` ten (`Standard EXE`,
`ActiveX EXE`). Those are decisions — *this is a product term and stays*, *this word is already Indonesian*
— and recording them is the point. An explicit entry distinguishes **translated, and identical to English**
from **nobody has looked at this yet**. Permit omission and those two become the same thing, which is the
drift this change exists to stop.

### Also: "region" is being misread, and the spec should say what it means

The word invites reading as *dialect or minority language*, which would make Basque a region of Spanish.
It is not: a region here is a country or territory variant of a language already shipped — `fr-CA`,
`pt-BR`, `en-GB` — and Basque would be a neutral pack of its own, `eu`. This tripped the maintainer while
reviewing this change, which is good evidence it will trip a contributor, so the spec now says so.

## Impact

- `openspec/specs/language-packs/spec.md` — one requirement modified, one added.
- `IDE/HexIDE.Tests/ShippedPackParityTests.cs` — new.
- `CLAUDE.md` — the guarantee names its guard.
- `tools/TranslationCoverage/Program.cs` — its header said drift "never fails a build", which is no longer
  true; it points at the test instead. The tool stays, because a build failure says *that* a pack is stale
  and the tool says *how* across all of them at once, which is what a backfill pass needs.

No product behaviour changes. A pack that is already complete stays complete; all twenty-nine are at
parity today, so this locks in the current state rather than demanding work.
