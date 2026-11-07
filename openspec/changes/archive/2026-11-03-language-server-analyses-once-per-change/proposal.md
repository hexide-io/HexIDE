## Why

The language-server spec required the server to "parse a changed document once". The server analyses a
changed document once and shares the result, which is what the requirement is for. Inside that analysis it
parses more than once on purpose: SLL falls back to LL, an unchosen conditional block is retried with its last
branch, and an error in a protected line is retried with those lines blanked. Later requirements in the same
spec need that recovery. So the requirement, read literally, forbids behaviour the spec asks for elsewhere.
The #2900 docs audit found the contradiction (`scripts/probes/docs-drift/report.md`).

## What Changes

- Rename "The server SHALL parse a document once per change" to "The server SHALL analyse a document once
  per change".
- State that one analysis may parse more than once to recover, and must keep one tree for every feature.
- Give the three recovery parses, and an analysis abandoned at the parse budget, a scenario each.
- Bring `DocumentStore.UpdateDocumentAsync`'s doc comment, which said "Parse the document ONCE", into line.

## Impact

- Affected spec: `language-server`.
- No behaviour changes; the spec now describes what ships.
