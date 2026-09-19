# Generate a workspace artefact for a server that needs one

## Why

A large class of language server reads a project-descriptor file from the workspace root before it can
analyse anything: a compilation database, a solution or project file, a manifest, a tool configuration.
HexIDE can launch such a server and cannot make it useful. It holds the project model and has no way to
project it into the form the server reads.

The symptom is the worst available one. The server starts, completes `initialize`, and answers every
request with nothing — indistinguishable from a broken server, a misconfigured one, and one that genuinely
has nothing to say. That is the ambiguity the protocol inspector exists to destroy, reappearing a layer
below it.

This is not an accommodation for one backend. Of the third-party servers the test suite already drives,
two read such a file, and they were measured to disagree about whose job it is to notice a change: one
watches its own descriptor and asks the client for nothing, the other registers file watchers and depends
on the client entirely. Both strategies are live, in servers already in CI. The measurement is recorded on
hexide-io/HexIDE#455.

## What changes

- A server entry in `lsp-servers.json` may declare that it requires a **workspace artefact**: a path
  relative to the workspace root, and the name of a provider that produces its content.
- Before such a server starts, HexIDE asks the named provider for the content and writes it.
- A provider receives a read-only snapshot of the project — its name, its files with their kinds, and its
  references — and returns content. It never writes. Path validation, consent and the write itself stay in
  one place, so no provider can bypass them.
- Writing into a user's project directory is **consented per server**, remembered, and resettable.
- When the project model changes materially, the artefact is regenerated and the server is told through the
  standard watched-files notification.

## What this change does not do

- **No backend-specific knowledge enters HexIDE's core.** Core knows about artefacts, paths, consent and
  invalidation. A concrete projection is a provider.
- **No public add-in contract.** Third-party providers are hexide-io/HexIDE#457, deliberately after this,
  so the contract is extracted from a mechanism that exists rather than designed against an imagined
  consumer.
- **No shadow-directory generation.** Keeping generated state out of the source tree is attractive and does
  not generalise: at least one real backend derives the project's identity from the name of the folder its
  descriptor sits in, so a shadow directory silently renames the project. Root-writing is therefore the path
  that must work; a directory override remains possible for servers that accept one.

## Decisions, and what was considered

**The provider returns content; core writes it.** The alternative — handing the provider a path and letting
it write — puts consent and path validation on the honour system, and a provider is exactly the component
most likely to be third-party later.

**The artefact is materialised at server start, not at project load.** A server starts lazily, on the first
document of a language it claims, so "project loaded" and "this server is about to start" are different
moments and can occur in either order. A notification at project load is not a gate.

**Invalidation notifies rather than restarts.** Regenerating and sending the watched-files notification is
what the protocol specifies. It is knowingly a no-op for a server that reads its descriptor once at
startup and never re-reads; that is the server's gap to close, and watching one's own project file is an
ordinary expectation rather than a bespoke ask. The alternative considered was restarting the server on
every material change, which is correct by construction and pays a full re-parse for each added reference.

**A caveat recorded rather than resolved: the proving consumer's artefact may be empty.** The intention is
to prove the mechanism against a third-party server the suite already drives, rather than against the
backend that motivated it, so the capability cannot come out shaped like one consumer. The candidate's
descriptor, however, describes compilation units in a language a VB6 project does not contain — it has
content only where a project carries sources of that language, which is a real scenario but not the common
one. That same server was also measured to handle invalidation entirely internally, so it exercises the
generation half and not the other. What it proves is genuine and narrow: that HexIDE writes a descriptor a
foreign server then reads. Which consumer lands first is therefore settled when the mechanism exists, not
now.

## Phases

1. The seam — the project snapshot, the provider contract and its registry, the configuration and its
   validation. Nothing is written.
2. Consent and the write.
3. The first provider, proved end to end against a third-party server.
4. Invalidation, which depends on hexide-io/HexIDE#456.
