# Tasks

## 1. Prove the defect before changing anything

- [x] 1.1 Three tests against the unmodified code, observed red: a compiler clear erasing the server's
      diagnostics, a server's empty publish erasing the compiler's, and two servers on one document leaving
      only the second. Asserted through `AddinDiagnosticsService` — a real consumer's view — rather than by
      capturing the event, because what reached a consumer is the thing that was wrong
- [x] 1.2 Establish what the clear was *for* before removing it. It expires the previous build's errors, and
      that must survive: a scoping fix that stopped clearing stale compiler errors would trade one silent
      wrongness for another

## 2. The channel

- [x] 2.1 A ledger holding the last set per `(document, source)` and returning the union, shared by the
      single connection and the router — both are `ILspClient` and either can be what the toolchain holds
- [x] 2.2 Keyed with the URI comparer, not by raw string: two spellings of one document must merge, and
      that is the whole reason the comparer exists
- [x] 2.3 One spelling per document, fixed when first seen, so a merged set is never raised under two URIs
      and counted twice by a consumer keyed by raw string
- [x] 2.4 A source publishing empty drops its row rather than recording an empty one, so the ledger holds
      live marks rather than an entry per source per document forever
- [x] 2.5 Rows kept in first-seen order per document, so an unrelated source republishing does not shuffle
      a consumer's marks

## 3. The interface

- [x] 3.1 `InjectDiagnosticsAsync` takes an owner, **required rather than defaulted** — the defect is an
      unattributed publish, and omission must not be able to reintroduce it. Appended after the diagnostics
      array rather than placed first, so that a caller cannot silently transpose it with the URI
- [x] 3.2 `ClearDiagnosticsFromAsync(owner)` for expiring one source everywhere it published
- [x] 3.3 `DiagnosticOwner` for the keys, documented as distinct from `Diagnostic.Source` — that field is
      what a diagnostic says about itself for the reader, chosen freely by a server, and nothing derives one
      from the other

## 4. The router

- [x] 4.1 Each connection owns what it publishes, keyed by its registration id — unique across the registry
      and already what every log line about that connection names
- [x] 4.2 Resolved from the sender rather than by subscribing with a closure, because unsubscription
      happens in two places and a stored delegate per entry is a second thing to keep in step

## 5. The build

- [x] 5.1 Clear by ownership instead of walking the project's forms. Also reaches a form renamed since the
      last build, which the walk did not — the stale-marker half of #269
- [x] 5.2 The clear runs on the timeout path too. It sat after the bail-out, so a build that exceeded the
      limit was the one case that left the previous build's markers behind
- [x] 5.3 The compiler invocation separated from the handling around it, so the handling can be exercised
      without VB6 installed. Not a test-only seam by accident: a test that must start the real compiler is
      a test that never runs on CI, which is how this went uncaught

## 6. Tests

- [x] 6.1 The three red tests from 1.1, now green
- [x] 6.2 Ownership on a single connection as well as the router: two injecting sources, one withdrawing
- [x] 6.3 Withdrawing the last source still clears the document — the behaviour the erasing clear existed
      for, which the scoping must not lose
- [x] 6.4 Withdrawing a source that published nothing raises nothing. Every raise is a whole-document
      replacement at the far end, so a build on a clean project must not send one per document
- [x] 6.5 The build's effect on the channel, driven through the real `MakeWithVb6Async`: a successful build,
      a failed build whose errors reach no form, a failed build's errors sitting beside the server's on one
      form, the next build expiring the last one's, a timeout expiring them, and a renamed form still
      cleared
- [x] 6.6 Each verified to fail without its fix, by mutation — the clear ignoring its owner (3 tests), the
      clear moved back after the timeout bail-out (1), the router giving every connection one key (1), and
      the clear walking the project's forms (1)

## 7. Verification

- [x] 7.1 `HexIDE.Tests` 1119/1119 and `HexIDE.Integration.Tests` 288/288 on Windows, no skips
- [x] 7.2 Against the running IDE: a syntax error typed into a form reaches the automation server's
      diagnostics under `vb6://form/Form1` and renders, and correcting it empties them. That exercises the
      real out-of-process server through the ledger in both the connection and the router
- [x] 7.3 **Not verified end to end, and stated rather than implied:** no VB6 is installed on the machine
      this was written on, so `VB6.EXE /make` was never actually run. Everything from the exit code onward
      is real in the tests; producing the exit code is not

## 8. Documentation

- [x] 8.1 The client doc gains the ownership rule, and the note about `didClose` says what the empty
      publish now evicts
- [x] 8.2 The spec's clearing requirement amended — it said an empty set from "a source of diagnostics"
      clears the document, which is exactly the rule that produced this
