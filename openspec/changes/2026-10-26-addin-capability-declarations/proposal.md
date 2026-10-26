# Add-in capability declarations

## Why

A signed add-in manifest cannot say what the add-in intends to do (#459). Users therefore cannot
consent to those consequences or inspect them later. Compatibility validation (#310) now precedes
trust, making a versioned declaration possible without silently accepting it on older hosts.

This is a proposal for Jen's security review, as requested by the supervisor on 2026-10-26.
No vocabulary, migration policy or enforcement design is approved by this document. No code changes
are part of this delivery; the delta describes the proposed contract, not shipped behaviour.

## What Changes

- Propose five consequence-based capabilities and map them to current host entry points in `design.md`.
- Propose a top-level `capabilities` array in schema 2, signed and hashed as part of `addin.json`.
- Preserve consent keyed by manifest hash; declaration changes require a fresh decision.
- Present deny-by-default and disclosed legacy full-trust migration options for Jen to decide.
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

## Review decisions still required

Jen must approve the vocabulary and scope, schema transition, missing-declaration policy, treatment of
first-party and unsigned developer packages, and the host refusal/reporting contract before implementation.
This change stays unarchived. It does not close #459's implementation work.
