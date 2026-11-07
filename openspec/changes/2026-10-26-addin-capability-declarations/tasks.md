## 1. Security review before implementation

- [x] 1.1 Jen approves or revises the vocabulary, scopes and host API mapping in design.md.
- [x] 1.2 Jen selects the missing/older-manifest policy, including historical consent migration.
- [x] 1.3 Jen decides first-party/developer treatment and consent granularity.
- [ ] 1.5 Settle the refusal shape (exception or result) in the implementation's security review.
  - Skipped for the 2026-10-29 run: the security review has not recorded an exception/result
    decision. Task 2.1's declaration format and packer do not depend on host-call refusal shape.
- [x] 1.4 Record those decisions and resolve the conditional migration contract before coding.

## 2. Signed declarations (only after review)

- [x] 2.1 Implement approved manifest schema, validation and packer output; migrate bundled manifests.
- [x] 2.2 Test signed/hash coverage, invalid declarations, compatibility and the approved legacy policy.
  - Signed-package tests cover declaration edits/reordering, raw-byte hashes, re-signing and invalid
    declarations even with a trusted publisher signature. Existing compatibility tests cover schema-2
    shapes and packer validation; legacy fixtures prove schemas 0/1 remain verifiable without a declaration.
  - Skipped portion for the 2026-10-29 run: proving legacy host denial depends on the unresolved
    refusal shape (1.5) and per-add-in host checks (3.1); refusal coverage remains in 3.2. Verification
    success alone is not evidence of a capability grant or denial.
  - Validation: PackageVerificationTests passed (41 passed, one Windows-only file-lock skip),
    including all 13 added cases. These pin existing behavior: zero before-change failures.
    Desktop Debug built with zero warnings; tree hygiene passed. Full HexIDE.Tests was invoked
    with foreign downloads disabled but stopped after ten minutes without a result summary;
    its full-suite check remains incomplete.
- [x] 2.3 Verify changed declarations cannot inherit consent from the same add-in id or earlier build.
  - Five signed-package cases cover adding, removing and reordering declarations and repacking
    schemas 0/1. With id, publisher and version unchanged, the persisted old Allow does not match
    the new verified hash; registry discovery waits for consent and prompts once. A new Block
    persists independently of the earlier Allow. All five pass against unchanged product code:
    zero before-change failures (coverage of existing manifest-hash keying, not a product fix).
  - Validation: HexIDE.Tests completed across two partitions with foreign downloads disabled.
    The first run's ten-minute timeout interrupted steady progress; its XML report and a continuation
    excluding completed classes (rerunning the interrupted class) cover every discovered test method:
    4,926 unique passes, 32 skips, zero failures/errors, above the 4,481 suite floor. Skips are 22
    Windows-only cases and 10 requiring VB6 templates (ClassModuleSaveFidelityTests and
    CompanionBinaryPreservationTests); none of the five new cases skip. Desktop Debug built with
    zero warnings; tree hygiene passed. Product code, feature status and language counts are unchanged.

## 3. Host capability checks (separate implementation phase)

- [ ] 3.1 Introduce per-add-in host checks for approved current API routes, including legacy overloads.
  - Skipped for this run: task 1.5 still leaves the exception/result refusal shape to security review;
    implementing privileged host routes would invent that decision.
- [ ] 3.2 Test refusals before reads/side effects, independent add-in authority and cleanup access.
  - Skipped for this run: depends on task 3.1 and its unresolved refusal contract.
- [x] 3.3 Expose verified declaration/legacy data for #460 and host-use attribution for #462 without implementing their UI here.
  - Completed independent declaration-data slice: successful verification creates an immutable snapshot
    of id, publisher id, manifest hash, legacy state and capabilities; registry metadata exposes it before
    consent. Failed verification exposes no verified declaration. Legacy stray fields grant nothing.
  - Skipped portion for this run: host-use attribution needs per-add-in checks (3.1) to distinguish
    success from refusal under the still-undecided refusal contract (1.5). No use telemetry or UI added.
    Tasks 3.1/3.2 and this skipped portion must be revisited after security review.
  - Teeth: seven new cases failed against unchanged product code. A temporary reflection accessor
    allowed testing the absent metadata API before implementation; final tests use typed access.
    Cases cover declarations/empty lists, schemas 0/1, untrusted/incompatible results and registry
    discovery before consent, with read-only-list assertions on every successful declaration case.
  - Validation: full HexIDE.Tests with foreign downloads disabled passed (4,944 passed, 32 skipped,
    zero failures); its xunit report records 4,976 tests and no errors. Skips are 22 Windows-only cases
    and 10 requiring VB6 templates; all seven new cases run on Linux. Focused PackageVerificationTests:
    53 passed, one Windows-only file-lock skip. Desktop Debug: zero warnings/errors. Tree hygiene passed.
    MISSING_FEATURES and the live addin-system spec describe this slice; MISSING_LANGUAGE counts do not move.
- [ ] 3.4 Prove regression-test teeth, run full affected suites, warning-free Desktop build and tree hygiene; update live specs and applicable feature documentation when implemented.
  - Skipped for this run: the implemented declaration-data slice already records its teeth, full-suite,
    build and hygiene results under 3.3. Completing this task for host enforcement requires 3.1/3.2
    and the refusal-shape decision in 1.5; those remain pending security review.

## 4. Follow-on integration and completion

- [ ] 4.1 Coordinate the single consent flow (#460) and disclosure of use (#462), including localization and live verification in their own scope.
  - Skipped for this run: verified declaration data is available from 3.3, but host-use attribution
    remains blocked by 1.5/3.1. The consumer contracts are recorded in design.md; their UI,
    localization and live verification belong to #460/#462, not this declaration-only slice.
- [ ] 4.2 Validate the completed contract strictly and archive only after approved implementation is complete.
  - Skipped for this run: host enforcement and attribution are incomplete, and the supervisor's
    2026-10-29 instruction explicitly forbids validation and archiving in these runs.
