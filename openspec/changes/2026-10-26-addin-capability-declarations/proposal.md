# Add-in capability declarations

## Why

A signed add-in manifest cannot say what the add-in intends to do (#459). Users therefore cannot
consent to those consequences or inspect them later. Compatibility validation (#310) now precedes
trust, making a versioned declaration possible without silently accepting it on older hosts.

Jen decided its policy questions on 2026-10-26 (#459, recorded under **Decisions** below). No code
changes are part of this delivery; the delta describes the agreed contract, not shipped behaviour.

## What Changes

- Propose five consequence-based capabilities and map them to current host entry points in `design.md`.
- Propose a top-level `capabilities` array in schema 2, signed and hashed as part of `addin.json`.
- Preserve consent keyed by manifest hash; declaration changes require a fresh decision.
- Give a manifest that declares nothing no host capabilities, until it is repacked with a declaration.
- Define the declaration data that #460's single consent flow and #462's disclosure of use consume.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `addin-system`: signed capability declarations and checks at the curated host surface. Existing
  `addin-trust` signature, revocation and manifest-hash consent contracts remain authoritative.
  Implementation must also update its schema-compatibility requirement from 1 to the approved version.

## Impact

Future implementation touches the manifest, verifier, packer, per-add-in host facade, consent model,
first-party packages and add-in tests. #460 owns consent presentation; #462 owns disclosure of actual
host-mediated use. Proposed dialog descriptions are review copy only; implementation must localize
approved copy in every shipped pack. No dependency, UI, runtime or language behaviour changes here.
MISSING_FEATURES statuses and MISSING_LANGUAGE counts remain unchanged.

## Decisions

Made by Jen on 2026-10-26, on #459:

- **An add-in that declares nothing gets no host capabilities** (option A in `design.md`). It still loads
  after the existing trust and consent checks, and every host call that needs a capability is refused
  until its publisher repacks it with a declaration. Legacy full trust (B) is rejected.
- **The vocabulary is the five capabilities** in `design.md`, and **template creation also needs
  `project.write`**.
- **First-party packages declare their own capabilities**, and **unsigned developer packages are
  enforced the same way**.
- **One consent per build**, covering its whole declaration, not per-capability toggles.

Still open, and settled in the implementation's own security review: the concrete shape of a refusal
(an exception or a result), provided an existing API can never report success for a refused call.
This change stays unarchived until the implementation lands. It does not close #459's implementation work.
