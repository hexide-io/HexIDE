<!-- SPDX-License-Identifier: MIT -->
# HexIDE.VbLspServer.Tests

Tests for the **MIT** VB6 language server (EmmyLua.LanguageServer.Framework shell + proleap /
grammars-v4 grammar). See `openspec/specs/language-server/spec.md`.

## Suites

| Suite | What it covers |
|---|---|
| `ServerShellSmokeTest` | Boots the server over an in-memory pipe pair (StreamJsonRpc); LSP lifecycle + the custom `vb/builtinSymbols` method. |
| `ComponentPortTests` | The three grammar-coupled components (diagnostics / scope / symbols) on the proleap tree. |
| `VbDiagnosticsProviderTests`, `VbScopeAnalyzerTests`, `VbFoldingProviderTests`, `VbFormatterTests` | Ported from the GPL server's suites — **our own inputs**, relicensed MIT. |
| `WireContractTests` | All 17 methods driven through **StreamJsonRpc** (the real IDE client's transport), asserting the exact response shapes + hard requirements (empty-publish on close, publish-on-change, `vb6://` URI round-trip) + protocol edge cases. |
| `SpawnRealServerTest` | Spawns the **real server process** and drives it over **real stdio** — the seam CI otherwise never exercises. |

## Excluded: `RubberduckGrammarTests`

The GPL server's `RubberduckGrammarTests.cs` is **not** ported here. Its VB6 code inputs are derived from
the Rubberduck project's GPLv3 test suite (`RubberduckTests/Grammar/VBAParserTests.cs`), so carrying them
into the MIT tree would reintroduce the very GPL dependency this swap removes. Clean-room grammar torture
coverage lives instead in the `vb6-corpus` (AI-generated from the VB6 Language Reference, validated against
a real `vb6.exe`).
