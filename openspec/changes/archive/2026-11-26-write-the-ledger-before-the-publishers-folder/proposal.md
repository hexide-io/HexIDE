# Write the ledger before the publisher's folder (#5678)

## Why

`issue-publisher` wrote the publisher's `--out` folder, then the ledger entry. If the ledger write failed after `--out`
was written, a signed record could reach the publisher while the ledger didn't hold its id or key. A malformed revocation
signature could throw instead of refusing. Both were found in the security review of #5677
([#5678](https://github.com/hexide-io/HexIDE/issues/5678)).

## What changes

- **The ledger entry is written first,** then `--out`. If `--out` can't be written, the record is still held, and the
  error says where to copy it from.
- **An `--out` inside `--ledger` is refused** before anything is signed.
- **A revocation list that can't be checked refuses** with exit 2, as `verify-request`'s does.
- **The packer README says what to do after exit 3:** issue by hand with `sign-publisher`, and add the ledger entry and
  its stored request by hand. There is no bypass flag.
