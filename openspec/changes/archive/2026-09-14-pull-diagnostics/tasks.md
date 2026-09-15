# Tasks

## 1. The fifth foreign server

- [x] 1.1 Give `ForeignAsset` a per-asset subdirectory. Ruff's tarballs each extract into their own stem
      (`ruff-x86_64-unknown-linux-musl/ruff`) while the Windows zip is flat, which the server-wide
      `ExecutableSubdirectory` cannot express — it takes the version, not the asset.
- [x] 1.2 Add `ForeignServerAcquisition.Python`, pinned to 0.16.7 with publisher-attested digests taken
      from the `.sha256` files published beside each asset.
- [x] 1.3 Add `ForeignServer.Python` to the fixture — `HEXIDE_PYTHON_LSP`, `ruff` on PATH, `server`,
      language id `python`, extension `.py` — and to `ForeignServerFactAttribute`'s switch.
- [x] 1.4 Add it to `RequiredServersTests`, so `HEXIDE_REQUIRE_FOREIGN_LSP=1` covers it like the rest.

## 2. The gap, proved before it is closed

- [x] 2.1 A test that attaches ruff and asserts it advertises `diagnosticProvider` in the **object** form —
      the half of the `boolean | Options` contract that #238 rejected. Passed before any client work, which
      is what says the fixture reaches the real server.
- [x] 2.2 Establish what the gap actually is, by measurement rather than assumption. It is **not** "a
      pull-only server says nothing": ruff publishes happily to a client that declares no pull support, and
      falls silent only once told the client can pull. Recorded in `proposal.md`, because the first guess
      was the opposite and it is the guess the next reader will also make.

## 3. The client asks

- [x] 3.1 `diagnosticProvider` on `ServerCapabilities`, as `JsonElement?` like every other capability, with
      readers for the identifier and for `workspaceDiagnostics`.
- [x] 3.2 `DiagnosticClientCapabilities`, declared under `textDocument.diagnostic`. Empty, for the reason
      in `CodeLensClientCapabilities` — `dynamicRegistration` must not appear, and `relatedDocumentSupport`
      stays absent rather than `false`.
- [x] 3.3 `DocumentDiagnosticParams` and the report records; register every one of them in
      `LspJsonContext` (step 4 of the five-step recipe, which is not optional).
- [x] 3.4 Request on open and after change. Echo the server's `identifier`; carry `previousResultId`
      forward per document; discard a result that a newer change has already overtaken.
- [x] 3.5 Route the answer through `RaisePublishDiagnostics` under `DiagnosticOwner.LanguageServer` — the
      same call push takes, so the marker pipeline cannot tell them apart.
- [x] 3.6 An `unchanged` report keeps what was last published; it must not clear it. `didClose` clears this
      server's set explicitly, because nothing else would — a server that only answers when asked cannot
      say anything about a document we have stopped asking about.
- [x] 3.7 Add `diagnosticProvider` to `ConsumedCapabilities` via a gate that does **not** warn when the
      capability is absent, and teach `ConsumedCapabilitiesTests` about it.

## 4. Proof

- [x] 4.1 Flip 2.1: the `F401` ruff reports reaches the channel, with a well-formed range.
- [x] 4.2 A scripted in-process server that answers `unchanged`, proving the previous report survives and
      the `previousResultId` went back out. Ruff cannot prove this — it never sends a `resultId`.
- [x] 4.3 A server that did not advertise the capability is neither asked nor complained about. Scripted
      rather than foreign, on a measured surprise: rumdl **publishes and also advertises**
      `diagnosticProvider`, so asking it is correct and it cannot serve as the negative case.

## 5. Documents

- [x] 5.1 `docs/foreign-language-servers.md` — a fifth row, and the bar it cleared. Say plainly that it
      duplicates texlab's crate and is here for the protocol shape.
- [x] 5.2 `docs/lsp-client.md` — **regenerate**, never hand-edit; `ProtocolCoverageDocTests` guards it.
- [x] 5.3 `CLAUDE.md` — the server table goes four to five, and the env-var list gains `HEXIDE_PYTHON_LSP`.

## 6. Found on the way, and fixed because it blocked this

- [x] 6.1 A frame that cannot be decoded no longer costs the **connection**. Declaring the client
      capability makes rumdl send `workspace/diagnostic/refresh` with `"params": null` — invalid JSON-RPC —
      which killed the whole connection rather than that one message. Repaired at the formatter, narrowly:
      a null `params` is dropped, because it is the one value the grammar forbids outright and so has a
      single safe reading. Anything else still fails loudly.
- [x] 6.2 `workspace/diagnostic/refresh` is honoured rather than refused — the only way a server that
      speaks when spoken to can report a change that is not a document.
- [x] 6.3 A pull-advertising server's publications are ignored, with a note in the protocol inspector
      saying so. rumdl does both, and taking both makes the marks depend on arrival order.
- [x] 6.4 The `ForeignServerIntegrationTests` timeout now reports the conversation instead of a bare
      `TimeoutException` (#390 asked for this; 6.1 is what it found first).
