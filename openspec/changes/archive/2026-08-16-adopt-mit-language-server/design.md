# Design — replacing the language server

## Why the process boundary stayed

The obvious reading of "the server is no longer copyleft" is that it could now be linked into the IDE.
It was not, because licensing was never why the boundary existed.

A language server does unbounded work on hostile input: a parser generator's prediction can blow up on
pathological text, and a wedged parse in-process is a wedged IDE. Out of process, the worst case is that
language features stop answering while editing continues. The boundary is also what makes the backend
replaceable — anything that speaks the protocol over a byte stream can take over, including an
implementation that could never be linked in for licensing reasons. Removing the boundary would have
traded both properties for nothing.

## Why the grammar could be reused but the code could not

Licence provenance is per-file, not per-repository. The copyleft obligation came from one dependency, so
most of the server was original work that could be relicensed freely — but "most" is not "all", and the
distinction had to be checked rather than assumed.

Three things were treated as carrying the obligation and rewritten or dropped: the grammar itself, generated parser output
derived from it, and test inputs that had been taken from the replaced project's own suite. The test inputs
are the subtle one — they look like ordinary fixtures, and reusing them would have carried the obligation
across in a form nobody would notice until someone audited it.

Where the replacement grammar needed fixing to reach parity, the fixes were authored from the published
language reference rather than by consulting the grammar being replaced, so the result has clean
provenance rather than merely a different licence header.

## Why the wire contract is specified rather than left implicit

The client and server were written together, so a number of behaviours the client depends on were never
written down — they were just true. Several are not deducible from the protocol specification, because they
are choices the protocol permits rather than requires:

- Publishing an *empty* diagnostic set when a document closes, rather than publishing nothing. Consumers
  use that empty publish to evict cached state; a server that stays silent leaves stale markers behind.
- Publishing diagnostics on every change without debouncing or deduplicating, because a second consumer
  refreshes on the arrival of the publish rather than on its contents.
- Processing requests in order relative to document changes, so a request that follows an edit sees the
  edited text.
- Answering "nothing found" with an empty result rather than a protocol-level error, because errors are
  surfaced to the developer while empty results are not.

An implementation that got any of these wrong would pass a protocol conformance test and still break the
editor in ways that look like unrelated bugs. Writing them down is what makes the backend genuinely
replaceable rather than nominally replaceable.

## What was deliberately not built

The server stays syntactic. It does not build a bound semantic model, resolve names across files, or
perform project-wide analysis — those belong to a real language engine behind the replaceable seam, and
HexIDE's own tooling holds the line at the syntax tree. The one semantic-looking feature it does carry,
the undeclared-variable check, is off by default precisely because doing it properly needs the symbol
table this server does not have.

Also out: incremental synchronization, pull diagnostics, capability negotiation, and cross-file
navigation. Each is a real feature; none is needed to serve a single-document editing session, and each
would add a failure mode to a component whose main virtue is that it fails quietly.
