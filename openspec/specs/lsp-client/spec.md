# lsp-client Specification

## Purpose
Define how the IDE consumes language intelligence: through a single contract that hides the wire protocol,
the transport, and which server is answering, so the language backend can be replaced without touching the
editor.

VB6 had no language-server concept, so everything here is additive. The reason it is expressed as a narrow
contract rather than direct calls into a parser is that the backend is expected to change: HexIDE ships its
own syntactic server today, and the seam exists so an external backend with deeper analysis can take over
later without the editor noticing.

## Requirements

### Requirement: The IDE SHALL depend only on the language-service contract
Editor and view-model code SHALL obtain every language feature — diagnostics, hover, completion, document
symbols, folding, signature help, definition, document highlight, rename, formatting — through one
language-client interface, and SHALL NOT reference the transport, the RPC library, or any server assembly.

The point of the seam is that a replacement backend is a configuration concern rather than a refactor. That
only holds if nothing upstream of the interface knows what is behind it, so the constraint is on the
consumers as much as on the implementation.

#### Scenario: Adding a feature that needs language data
- **WHEN** a view model needs language information
- **THEN** it calls the language-client interface
- **AND** it does not observe which server, transport, or protocol produced the answer

#### Scenario: Replacing the backend
- **WHEN** the language backend is replaced with a different implementation
- **THEN** no editor or view-model code changes

### Requirement: Language features SHALL degrade rather than fail
Every language operation SHALL be best-effort: when no server is present, when the handshake failed, or when
a request errors, the operation SHALL return an empty or absent result and SHALL NOT throw to the caller or
tear down the connection.

Language intelligence is an enhancement, not a precondition for editing. A developer with no server
installed must still get a working editor, and a single malformed request must not take out the session.
This is what allows callers to invoke any operation at any time without first testing whether the service is
running.

#### Scenario: No language server is available
- **WHEN** the IDE starts and no language server can be located
- **THEN** the IDE runs normally with language features inert
- **AND** editing, saving and running are unaffected

#### Scenario: A request fails
- **WHEN** a language request fails or times out
- **THEN** the caller receives the empty or absent result for that request
- **AND** the connection remains usable for subsequent requests

#### Scenario: The server exits unexpectedly
- **WHEN** the language server process exits while the IDE is running
- **THEN** the client reports itself as not running and language features go inert
- **AND** the IDE does not restart it automatically within the session

### Requirement: Result shapes SHALL distinguish "empty" from "nothing to show"
Requests returning a collection SHALL return a non-null, possibly-empty collection. Requests returning a
single result or an optional collection SHALL return absent to mean "nothing to show".

Consumers rely on this split: they iterate collection results without a null check, and they treat absence
as a meaningful answer rather than an error. A reimplementation that returns absent for a collection would
break callers silently, so the convention is part of the contract rather than an implementation detail.

#### Scenario: Requesting symbols for a document with no declarations
- **WHEN** document symbols are requested for a document that declares nothing
- **THEN** an empty collection is returned rather than an absent result

#### Scenario: Requesting hover where nothing is defined
- **WHEN** hover is requested at a position with nothing to describe
- **THEN** an absent result is returned rather than an empty one

### Requirement: Documents SHALL be synchronized in full
The client SHALL send the entire document text on open and on every change, rather than a delta, and SHALL
notify the server when a document closes.

Full synchronization removes an entire class of desynchronization bug — a dropped or misapplied delta
leaving the server's copy silently diverged from the editor's — at a cost that is negligible for source
files of the size VB6 projects contain.

#### Scenario: Editing a document
- **WHEN** the developer edits an open document
- **THEN** the complete new text is sent with a version that increases

#### Scenario: Closing a document
- **WHEN** a document is closed
- **THEN** the server is notified, so it can release any state held for that document

### Requirement: Diagnostics SHALL be pushed through a single channel from more than one source
Diagnostics SHALL reach the editor by push rather than by polling, and the client SHALL expose one
diagnostics channel that carries both server-published diagnostics and diagnostics injected by the IDE. An
empty diagnostic set for a document SHALL clear that document's diagnostics.

The IDE has a second source of truth: compiling with the real VB6 toolchain produces errors the syntactic
server cannot know about. Feeding both through one channel means the marker pipeline, the editor, and any
future consumer handle them identically and cannot disagree about which errors are current.

#### Scenario: The server reports a syntax error
- **WHEN** the server publishes diagnostics for a document
- **THEN** they are raised on the diagnostics channel and rendered in the editor

#### Scenario: The compiler reports an error the server cannot see
- **WHEN** the IDE compiles with the real VB6 toolchain and that compiler reports errors
- **THEN** those errors are injected into the same diagnostics channel and rendered the same way

#### Scenario: Errors are resolved
- **WHEN** a source of diagnostics reports an empty set for a document
- **THEN** that document's diagnostics are cleared

### Requirement: The language server SHALL run outside the IDE process
The client SHALL communicate with the server across a process boundary over a duplex byte stream, and SHALL
NOT load the server into the IDE process.

Two things follow from the boundary, and both are the reason for it. A server that crashes or hangs takes
nothing with it. And because nothing is linked, the licence of the backend is its own concern — an external
backend under any licence can sit behind the seam without affecting the IDE's own terms.

#### Scenario: The server crashes
- **WHEN** the language server process terminates abnormally
- **THEN** the IDE remains running and responsive

#### Scenario: Hosting a differently-licensed backend
- **WHEN** the backend behind the seam is an external implementation under a different licence
- **THEN** the IDE's own licensing is unaffected, because it links nothing across the boundary

### Requirement: The language client SHALL start and stop with the desktop session
The client SHALL be started when the desktop IDE starts and stopped when it shuts down, requesting an
orderly server shutdown before terminating the process.

An orphaned server process outliving the IDE would hold file handles and consume memory with nothing to
serve. Asking first and terminating second means a well-behaved server exits cleanly while a wedged one
still goes away.

#### Scenario: Shutting down the IDE
- **WHEN** the IDE shuts down
- **THEN** the server is asked to shut down and its process is ended
- **AND** no server process outlives the IDE

### Requirement: Message types SHALL be registered for ahead-of-time serialization
Every message type crossing the boundary SHALL be registered with the serializer's generated context, and
call sites SHALL NOT pass anonymously-typed payloads.

Under ahead-of-time compilation there is no reflection metadata to fall back on, so an unregistered type does
not raise an error — it serializes to nothing and the request fails silently at runtime. Registration is
therefore a correctness requirement, not an optimization.

#### Scenario: Adding a message type
- **WHEN** a new message type is added to the protocol surface
- **THEN** it is registered with the generated serialization context in the same change

#### Scenario: Sending a request with no parameters
- **WHEN** a request or notification carries no parameters
- **THEN** a declared empty-parameters type is sent rather than an anonymous object

### Requirement: The set of language servers SHALL be configuration, not code
The IDE SHALL build its set of language servers from a declarative configuration, and SHALL include the
server it ships as an ordinary entry in that set rather than as a special case beside it.

An entry SHALL carry a stable identifier, a display name, the file extensions it claims, the language
identifier it wants documents tagged with, how it is reached, and whether it is enabled. The identifier is
what everything else refers to the server by — a server identified only by its position in a list cannot be
named in a setting, quoted in a message, or remembered across restarts.

Shipping the bundled server as an entry is the requirement, not an implementation convenience. This
capability's Purpose states that the backend can be replaced without touching the editor; a backend
special-cased in code is abstracted but not replaceable, and the difference is only visible when someone
tries to replace it.

#### Scenario: A user attaches a server the IDE has never heard of
- **WHEN** a configuration entry names a server, the languages it claims, and how to reach it
- **THEN** documents of those languages are offered to it, with no rebuild and no code change

#### Scenario: The bundled server is replaced
- **WHEN** a user entry carries the same identifier as the bundled server
- **THEN** the user's entry is used in its place

#### Scenario: The bundled server is switched off
- **WHEN** an entry is marked as not enabled
- **THEN** no client is created for it, and it claims nothing

### Requirement: Configuration SHALL layer over compiled-in defaults
Default entries SHALL be contributed in code and user configuration SHALL be merged over them by identifier.
Removing the user configuration SHALL restore the defaults.

Layering rather than replacement is what makes the previous requirement safe. The bundled server can be a
row a user may break precisely because the row itself is not stored in the file they edit: an improvement to
a default reaches an existing user, and a user who renders their own configuration unusable recovers by
deleting a file rather than by reinstalling.

A default entry whose server cannot be located SHALL be omitted rather than contributed in a broken state.

#### Scenario: A user has no configuration
- **WHEN** no user configuration is present
- **THEN** the defaults apply and language features work as shipped

#### Scenario: A user removes their configuration
- **WHEN** the user configuration is deleted
- **THEN** the defaults apply again on the next start

### Requirement: A document SHALL be routed by extension, and each server told the identifier it declared
Routing SHALL key on the document's extension. Where a document is offered to more than one server, each
server SHALL be told the language identifier **that server declared**, rather than a single identifier
shared between them.

Two servers may legitimately disagree about what an extension means. A document carries exactly one language
identifier when it is opened, so a single shared table forces a winner and makes the loser wrong about every
file it sees. Each server has its own connection, and the protocol does not require two connections be told
the same thing — so telling each what it asked for dissolves the disagreement instead of adjudicating it.

An extension no entry claims SHALL route nowhere, and the document SHALL open with language features absent.
That is a normal outcome and SHALL NOT be reported as an error.

A URI scheme that names a language SHALL continue to take precedence over the extension, because the IDE's
own documents carry no extension at all.

#### Scenario: Two servers claim one extension under different identifiers
- **WHEN** a document of that extension is opened
- **THEN** both servers receive it, each told the identifier it declared

#### Scenario: An extension nothing claims
- **WHEN** a document is opened whose extension no entry claims
- **THEN** it opens with language features absent and no error is reported

### Requirement: Where one server must be chosen, a user's entry SHALL outrank a default
For the features that cannot combine two answers, entries SHALL be ordered by an explicit priority, and the
entries contributed as defaults SHALL rank below the value an entry takes when it does not state one.

Ordering must not fall to registration order, which is deterministic but accidental — it varies with
discovery, and discovery changes whenever a server is added or removed. A user who attaches a server for a
language the IDE already serves is expressing a preference, and should not have to discover a priority field
to have it honoured.

#### Scenario: A user attaches a second server for a language the IDE already serves
- **WHEN** a feature requires exactly one server
- **THEN** the user's entry is chosen over the default

### Requirement: A server SHALL be given the project's working directory
Each server SHALL be started with the current project's working directory as its working directory, and told
that directory as its workspace root, unless its entry states otherwise.

Servers routinely resolve their own configuration relative to where they are run. A server started somewhere
arbitrary reads none of the user's settings for it and reports subtly different results with no indication
why — a wrong answer rather than a missing one.

Where a project's working directory changes during a session, running servers SHALL be restarted so that
none continues against a root that no longer describes the project.

#### Scenario: A server that reads its own configuration from the workspace
- **WHEN** a server is started for a project
- **THEN** its working directory and workspace root are the project's working directory

### Requirement: A malformed entry SHALL fail alone, and visibly
An entry that cannot be used SHALL NOT prevent other entries from being used, and SHALL NOT prevent the IDE
from starting. An entry missing information required to reach a server SHALL be rejected. An entry carrying
information the IDE does not recognise SHALL be kept and reported, not silently ignored.

The distinction is between *"you left out something I need"* and *"you wrote something I do not understand"*,
and it matters because the common failure is a misspelled field name. Silently ignoring it produces an entry
that fails for no visible reason.

Failures SHALL be reported somewhere a user can see, not only to a log. This is the case where the usual
log-and-continue is insufficient: a language server that is absent, and one that is attached with nothing to
say, present identically to the user, and there is already a defect of exactly that shape on record.

Changes to the configuration SHALL take effect when the IDE next starts. A language server is a process
rather than data, and partial live application — new entries taking effect while changed ones do not — would
be indistinguishable from a defect.

#### Scenario: One entry is malformed
- **WHEN** the configuration contains an entry that cannot be used
- **THEN** every other entry still applies, and the failure is reported rather than only logged

#### Scenario: A field name is misspelled
- **WHEN** an entry carries a field the IDE does not recognise
- **THEN** the entry is kept and the unrecognised field is reported

### Requirement: A newly named command SHALL be shown before it is run
Where configuration names an executable to launch, and that command has not previously been seen for that
entry, the IDE SHALL make the command visible to the user before acting on it.

Typing a path into one's own configuration is consent; a file appearing with a path in it is not. The
configuration is an ordinary file that any process running as the user may write, and an entry naming an
executable is launched on every start thereafter — so without this, writing that file is a durable way to
have the IDE run something on a user's behalf indefinitely and silently.

This SHALL NOT require signing, a consent store, or revocation. Those are proportionate to loading code into
the IDE's own process; a language server is a separate process the user already installed. What is required
is only that the launch is not silent the first time.

#### Scenario: An entry's command changes
- **WHEN** an entry names a command not previously seen for that entry
- **THEN** the command is surfaced to the user before the server is started
