# Tasks

## 1. Establish grammar parity
- [x] 1.1 Bring the permissively-licensed grammar to parity with the corpus the server must handle.
- [x] 1.2 Author the outstanding fixes from the published language reference, not from the grammar being replaced, so provenance is clean.
- [x] 1.3 Measure robustness on partial and malformed input before committing to the swap, rather than assuming it.

## 2. Stand up the replacement server
- [x] 2.1 Build the server on a permissively-licensed server framework, keeping the process boundary and the byte-stream transport.
- [x] 2.2 Keep request handling ordered with respect to document changes.
- [x] 2.3 Keep an entry point that accepts a stream pair, so the protocol can be driven in-process by tests.

## 3. Port the language logic
- [x] 3.1 Reparse on change and collect every syntax error, rather than stopping at the first.
- [x] 3.2 Rewrite the declaration collector and the undeclared-variable check against the new tree.
- [x] 3.3 Rewrite the document-symbol provider against the new tree.
- [x] 3.4 Parse once per change and share the tree across diagnostics, symbols and completion.
- [x] 3.5 Carry across the grammar-independent handlers and tables only after confirming each was ours to relicense.

## 4. Meet the wire contract
- [x] 4.1 Serve every method the IDE calls, in the response shapes it expects.
- [x] 4.2 Publish an empty diagnostic set when a document closes.
- [x] 4.3 Publish diagnostics on every open and change, without debouncing or deduplicating.
- [x] 4.4 Answer "nothing found" with an empty result rather than a protocol error.
- [x] 4.5 Tolerate lifecycle messages arriving with empty parameters.

## 5. Test
- [x] 5.1 Port the server test suite, excluding inputs derived from the replaced project.
- [x] 5.2 Add an end-to-end test that spawns the real server, opens a document containing an error, and asserts a diagnostic arrives — converting silent integration failures into loud ones.

## 6. Integrate
- [x] 6.1 Publish the server self-contained and wire the desktop build to the publish output.
- [x] 6.2 Remove stale copies of the previous server from build outputs, and verify a release archive built from a clean clone contains only the replacement.

## 7. Complete the licence change
- [x] 7.1 Delete the copyleft licence file and the grammar that required it.
- [x] 7.2 Update the licence headers on the ported files.
- [x] 7.3 Update the notices and documentation that described the tree as mixed-licence.
- [x] 7.4 Confirm no copyleft licence file, grammar, or derived test input remains anywhere in the tree.
