# Security Policy

HexIDE is alpha software, an early developer preview. We take security seriously and appreciate responsible disclosure.

## Reporting a vulnerability

**Please do not open a public issue for security problems.**

Report privately via GitHub's
[private vulnerability reporting](https://github.com/hexide-io/HexIDE/security/advisories/new)
(**Security → Advisories → "Report a vulnerability"**). If private reporting is unavailable, email
**security@hexide.io** with details and, if possible, a proof of concept.

We aim to acknowledge reports within a few days. As an unfunded community project, fixes are
best-effort, but security issues are prioritised over feature work.

## Supported versions

Only the latest default branch is supported. Released binaries are built from it, so a fix reaches them in the
next [release](https://github.com/hexide-io/HexIDE/releases).

## Security-relevant surfaces

A few parts of HexIDE are worth calling out for reviewers:

- **Add-in system.** HexIDE can load add-ins, which are .NET assemblies that run **in-process with
  full trust**. Add-ins are verified against a signing trust chain (see [TRUST.md](TRUST.md))
  and first load requires explicit user consent. Only install add-ins from publishers you trust.
  The bundled *development* trust chain is **not** the release chain and is rotated to an offline
  root before any signed release.
- **MCP automation server.** A local HTTP server that can drive the IDE exists **only in Debug
  builds** (`#if DEBUG`), is off by default, and must be explicitly enabled with `--server-port`.
  It is **compiled out of Release builds entirely** — a distributed binary opens no port. It
  currently has no authentication, so never enable it on an untrusted network.
- **LSP server.** A separate subprocess communicating over stdio that parses VB6/VBA source. It
  does not open a network socket.

## Data handling

The core IDE does not phone home, collect telemetry, or transmit your projects anywhere you have not
configured. It makes a network call only to an address you give it: the add-in revocation list when
`RevocationListUrl` is set, and a language server you configure to connect over the network. Optional add-ins (for example, an AI chat assistant) may
contact external services **that you configure**; consult each add-in's documentation.
