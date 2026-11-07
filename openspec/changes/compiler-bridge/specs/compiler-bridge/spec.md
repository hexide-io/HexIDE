## Purpose

Run an archive project's compiler outside the IDE, behind a bridge process that HexIDE reaches only through LSP and
DAP, so core holds nothing compiler-specific and the same bridge can serve other editors. Tracks #1690.

## ADDED Requirements

### Requirement: The shell SHALL NOT depend on the compiler adapter
The IDE shell SHALL reference the archive reader and SHALL NOT reference any assembly that starts, initialises or
exchanges messages with the compiler. Everything compiler-facing SHALL live in an assembly that only the bridge
references, and a test SHALL fail the build when the shell's assembly references it.

#### Scenario: A shell type uses a compiler session type
- **WHEN** code under the IDE shell names a compiler session type
- **THEN** the build fails, because the shell does not reference that assembly

### Requirement: Archive entries SHALL reach a server under an archive entry URI
The IDE SHALL name an archive entry to a language server by an archive entry URI. The URI's path SHALL be the
entry's ordinal segments, each percent-encoded, and its `container` query SHALL be the container's `file:` URI. Two
such URIs SHALL name the same entry when their segments match exactly and their containers match under the host's
file-name rules. A server SHALL be chosen for the entry by the extension of its last segment, as for any document.

#### Scenario: An entry whose name contains a slash
- **WHEN** an archive entry named `a/b.ext` is offered to a server
- **THEN** its URI's last segment is `a%2Fb.ext`, and the server is chosen by `.ext`

#### Scenario: The server echoes the container in another case
- **WHEN** on Windows a server publishes diagnostics for an entry URI whose container differs only in case
- **THEN** the diagnostics are shown on that entry

### Requirement: The bridge SHALL own the compilers it starts
The bridge SHALL start a compiler for a container when the first entry of that container is opened, and SHALL end
it after the container's last entry closes and when the bridge shuts down. A compiler SHALL NOT outlive the bridge,
and its staging directory SHALL be the bridge's own, never beside the project, and SHALL be removed when that
compiler exits.

#### Scenario: The last entry closes
- **WHEN** the last open entry of a container is closed
- **THEN** that container's compiler ends after the grace period, and its staging directory is removed

#### Scenario: The bridge ends unexpectedly
- **WHEN** the bridge process is ended without a shutdown
- **THEN** every compiler it started ends too

### Requirement: Debugging SHALL share the language session's compiler
A debug adapter started for an archive project SHALL relay to the bridge that holds the project, and SHALL NOT start
a second compiler for it. With no bridge holding the project, the adapter SHALL fail with a message that says so.

#### Scenario: Debugging an open archive project
- **WHEN** a debug session starts for an archive project whose entries are open
- **THEN** the debug adapter uses the compiler the bridge already runs for it

### Requirement: The bridge SHALL NOT advertise edits it cannot apply
While archive entries are read-only, the bridge SHALL NOT advertise formatting, rename or code actions, which answer
with edits.

#### Scenario: Asked for its capabilities
- **WHEN** HexIDE initialises the bridge
- **THEN** the capabilities include none of formatting, rename or code actions
