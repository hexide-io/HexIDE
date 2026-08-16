# VB6 LSP Server (HexIDE)

An out-of-process C# [Language Server Protocol](https://microsoft.github.io/language-server-protocol/)
server for VB6/VBA, built as part of the [HexIDE](../README.md) project. **MIT-licensed.**

## Attribution

The server parses VB6/VBA with the **[proleap / grammars-v4 VB6 grammar](https://github.com/antlr/grammars-v4/tree/master/vb6)**
(MIT) — carrying HexIDE's clean-room fixes — and is built on
**[EmmyLua.LanguageServer.Framework](https://github.com/CppCXY/LanguageServer.Framework)** (MIT).

It runs as a **separate process** from the IDE for crash isolation and to keep the language backend
replaceable — not for any licensing reason (the whole tree is MIT).

## Features

- Parses VB6/VBA using the proleap / grammars-v4 VB6 grammar (two-stage SLL→LL prediction with a
  wall-clock backstop for keystroke-time robustness)
- Syntax diagnostics (squiggle underlines) via `textDocument/publishDiagnostics`
- `Option Explicit` / undeclared-variable warnings via scope analysis
- Document symbols, code folding, hover, completion, signature help, go-to-definition, document
  highlight, rename, and formatting
- The non-spec `vb/builtinSymbols` method the Object Browser depends on
- Communicates over **stdio** with standard LSP `Content-Length` framing

## Building

```sh
# No Java required — Antlr4BuildTasks bundles the ANTLR tool
dotnet build HexIDE.VbLspServer/

# Run tests
dotnet test HexIDE.VbLspServer.Tests/
```

## Usage

The server is normally spawned automatically by the IDE. To run manually:

```sh
dotnet run --project HexIDE.VbLspServer/
```

Communicate via stdin/stdout using standard LSP JSON-RPC with `Content-Length` headers.

## License

**MIT** — see [LICENSE](LICENSE) and [THIRD-PARTY-NOTICES.md](../THIRD-PARTY-NOTICES.md).
