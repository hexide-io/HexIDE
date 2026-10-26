## Why

HexIDE can read an archive project and can talk to that project's compiler, but the compiler adapter sits in the
IDE's own tree, and only for expedience. The decision of 2026-10-19 (#1178) moves everything compiler-facing into a
bridge process. The bridge speaks LSP, and later DAP, to HexIDE, and the compiler's own protocol to the compiler.
HexIDE then holds nothing about that compiler beyond reading the archive format, and the bridge can serve other
editors as well. Nothing yet records how the bridge receives documents, how it runs, where it lives, or what stays
behind in core (#1690). This change records those decisions before any bridge code is written.

## What Changes

- **A bridge executable**, separate from the IDE. It serves LSP over standard I/O, owns every compiler process it
  starts and every staging directory those processes use, and relays DAP for a debug adapter process, so one
  project never runs two compilers.
- **How the bridge gets documents.** An archive entry reaches a language server under an archive entry URI, which
  names the container and the entry. The bridge resolves that URI against the container it reads. The managed
  source folder stays a projection for version control and outside tools, and is not the bridge's input.
- **What changes in core:** archive documents may be offered to a language server while staying read-only. That
  changes the archive-browsing requirement that forbade it.
- **The boundary.** The IDE shell references the archive reader and nothing compiler-facing. The compiler adapter
  moves into an assembly that only the bridge references.
- **Where it lives.** The bridge starts in this tree, as its own executable beside the archive and compiler
  libraries, and the shell does not reference it. It moves out with the compiler adapter once the package format
  (#1210) gives suites somewhere to install from. The maintainer decided against a repository of its own for now.

- **The first LSP mapping.** The compiler's language channel turns out to be JSON-RPC that is already LSP by most
  names. design.md maps each observed message to LSP 3.17, and lists where the channel departs from JSON-RPC.
  The observation was made with the compiler's author's permission.

**Out of scope:** building the bridge, and the package format (#1210).

## Impact

- **Specs:** a new `compiler-bridge` capability, and a change to `archive-project-browsing`.
- **Code, when built:**
  - `LspDocumentUri` gains a third kind of document name.
  - Archive tabs get a document session and markers, staying read-only.
  - `ILspWorkspace` gains archive containers as a source of workspace folders.
  - `ConversationRedactor` pseudonymises the new scheme.
  - `EditorService.NavigateTo` resolves it.
  - The compiler adapter's files move to their own assembly.
- **Packaging:** the bridge ships beside the IDE, as the bundled language server does, until #1210 replaces that.
