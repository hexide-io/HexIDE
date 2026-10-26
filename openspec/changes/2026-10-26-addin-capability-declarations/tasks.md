## 1. Security review before implementation

- [ ] 1.1 Jen approves or revises the vocabulary, scopes and host API mapping in design.md.
- [ ] 1.2 Jen selects the missing/older-manifest policy, including historical consent migration.
- [ ] 1.3 Jen decides first-party/developer treatment, consent granularity and refusal contract.
- [ ] 1.4 Record those decisions and resolve the conditional migration contract before coding.

## 2. Signed declarations (only after review)

- [ ] 2.1 Implement approved manifest schema, validation and packer output; migrate bundled manifests.
- [ ] 2.2 Test signed/hash coverage, invalid declarations, compatibility and the approved legacy policy.
- [ ] 2.3 Verify changed declarations cannot inherit consent from the same add-in id or earlier build.

## 3. Host capability checks (separate implementation phase)

- [ ] 3.1 Introduce per-add-in host checks for approved current API routes, including legacy overloads.
- [ ] 3.2 Test refusals before reads/side effects, independent add-in authority and cleanup access.
- [ ] 3.3 Expose verified declaration/legacy data for #460 and host-use attribution for #462 without implementing their UI here.
- [ ] 3.4 Prove regression-test teeth, run full affected suites, warning-free Desktop build and tree hygiene; update live specs and applicable feature documentation when implemented.

## 4. Follow-on integration and completion

- [ ] 4.1 Coordinate the single consent flow (#460) and disclosure of use (#462), including localization and live verification in their own scope.
- [ ] 4.2 Validate the completed contract strictly and archive only after approved implementation is complete.
