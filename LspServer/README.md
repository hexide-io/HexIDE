# VB6 LSP Server (HexIDE)

An out-of-process C# [Language Server Protocol](https://microsoft.github.io/language-server-protocol/) server for VB6/VBA, built as part of the [HexIDE](../README.md) project.

## Attribution

This server uses the **[Rubberduck VBA ANTLR4 grammar](https://github.com/rubberduck-vba/Rubberduck)** by the Rubberduck contributors, which is licensed under GPLv3. This project is therefore also GPLv3.

The companion IDE project (`IDE/`) is MIT-licensed. The subprocess boundary (separate process, stdio communication) keeps the GPL from propagating to the IDE.

## Features

- Parses VB6/VBA source using the [Rubberduck VBA ANTLR4 grammar](https://github.com/rubberduck-vba/Rubberduck)
- Publishes syntax diagnostics (squiggle underlines) via `textDocument/publishDiagnostics`
- `Option Explicit` / undeclared variable warnings via scope analysis
- Document symbols for procedure navigation via `textDocument/documentSymbol`
- Code folding ranges via `textDocument/foldingRange`
- Hover tooltips via `textDocument/hover`
- Communicates over **stdio** with standard LSP `Content-Length` framing
- No external runtime dependencies — pure .NET 10

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
# or after build:
./bin/HexIDE.VbLspServer
```

Communicate via stdin/stdout using standard LSP JSON-RPC with `Content-Length` headers.

## License

**GPLv3** — this project embeds the [Rubberduck VBA ANTLR4 grammar](https://github.com/rubberduck-vba/Rubberduck), which is licensed under GPLv3. See [LICENSE](LICENSE).
