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

Folds the editor makes itself to present a file's structure — the header, and attribute lines that describe
a member — SHALL NOT be obtained through that interface, and are not a language feature in this sense. They
describe the layout of the file format, which is the same whichever server is attached and present when none
is, and a fold that disappeared with the server would expose the text it exists to keep out of the way.

#### Scenario: Adding a feature that needs language data
- **WHEN** a view model needs language information
- **THEN** it calls the language-client interface
- **AND** it does not observe which server, transport, or protocol produced the answer

#### Scenario: Replacing the backend
- **WHEN** the language backend is replaced with a different implementation
- **THEN** no editor or view-model code changes

#### Scenario: No server is attached
- **WHEN** a form or class is opened with no language server running
- **THEN** its header is still folded, and the fold does not depend on any server's answer

### Requirement: Language features SHALL degrade rather than fail
Every language operation SHALL be best-effort: when no server is present, when the handshake failed, or when
a request errors, the operation SHALL return an empty or absent result and SHALL NOT throw to the caller or
tear down the connection. Rename error replies other than cancellations SHALL instead return the server's
message as a refusal, which the code window SHALL show. `-32803 RequestFailed` SHALL be logged at debug
without consuming the method's failure warning; other errors retain that warning.

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
- **THEN** the caller receives the empty or absent result, or a refusal message for a rename error
- **AND** the connection remains usable for subsequent requests

#### Scenario: The server never answers one request
- **WHEN** a language request reaches its 30-second local deadline
- **THEN** the client SHALL stop waiting locally, send cancellation best effort, and record `TimedOut`
- **AND** a language operation SHALL return its empty or absent result and warn once per method per connection
- **AND** subsequent requests on the same connection SHALL remain usable
- **AND** a shorter caller cancellation SHALL stop waiting sooner without spending the failure warning

#### Scenario: The server cancels a superseded request
- **WHEN** a language request receives error `-32801` (ContentModified) or `-32800` (RequestCancelled)
- **THEN** the client SHALL log it at debug as superseded, without warning that the feature failed
- **AND** a later genuine failure SHALL still receive the method's once-per-connection warning
- **AND** a superseded rename SHALL show no refusal dialog either

#### Scenario: A server refuses a rename
- **WHEN** a server returns `-32803 RequestFailed` to a rename
- **THEN** the code window displays the server's message and applies no edits
- **AND** a later unexpected failure of rename can still produce its first warning

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

### Requirement: Hover content unions

The client SHALL accept hover contents as `MarkupContent`, a `MarkedString`, or `MarkedString[]`.
It SHALL preserve markup content's kind and value, normalize marked strings to Markdown (language-tagged
values as fenced code blocks), and join array parts with blank lines. Any supplied hover range SHALL
be preserved.

#### Scenario: A server replies with a legacy Markdown string
- **WHEN** a hover reply contains `{"contents":"answered"}`
- **THEN** the client returns Markdown content with value `answered` for the editor's tooltip

#### Scenario: A server replies with mixed marked strings
- **WHEN** hover contents is an array of Markdown strings and language-tagged values
- **THEN** the client returns their Markdown text and fenced code blocks separated by blank lines

### Requirement: Diagnostic hover messages

The editor SHALL show the messages of all diagnostic markers covering the hovered offset, including
sources when supplied, combined with any quick info for that position. Diagnostic tips SHALL be available
in every personality, including Classic, even when Auto Quick Info is disabled. Marker ranges SHALL
include their start and exclude their end, including ranges widened for zero-width diagnostics.

#### Scenario: Overlapping diagnostics and quick info
- **WHEN** the pointer dwells over overlapping diagnostic markers and quick info is available
- **THEN** the tooltip shows every matching message, its source when supplied, and the quick info

#### Scenario: Diagnostic without quick info
- **WHEN** the pointer dwells over a diagnostic with no quick info or with Auto Quick Info disabled
- **THEN** the tooltip still shows the diagnostic message

### Requirement: Documents SHALL be synchronized in full
The client SHALL send the entire document text on open and on every change, rather than a delta, and SHALL
notify the server when a document closes. Each change SHALL carry a version later than the last message about
that document, the open included.

Full synchronization removes an entire class of desynchronization bug — a dropped or misapplied delta
leaving the server's copy silently diverged from the editor's — at a cost that is negligible for source
files of the size VB6 projects contain.

This is deliberate for servers declaring either Full or Incremental synchronization: each change SHALL
contain one event with the whole new text and no range. A server declaring None SHALL receive no changes.
LSP permits full-document changes whatever sync kind the server advertises. Incremental sync is deferred
until a server needs it to justify the desynchronization risk (hexide-io/HexIDE#282).

The version is the server's only way to order what it receives, and a server may discard a change that is not
later than what it holds. The open counts: a first change that repeated the open's version was lost to such a
server (hexide-io/HexIDE#470).

A change made while a server is still starting SHALL NOT be lost. That server SHALL be opened on the document once,
before any change to it, with the text as it stands when the open is sent. Starting a server takes long enough to
type in, and an edit dropped in that window left the server analysing the text from before it until the next
edit (hexide-io/HexIDE#719).

A document SHALL be one document to a server however many editors hold its name. Its first holder SHALL open
it, every holder SHALL draw its versions from one sequence, and its last holder SHALL close it. Two windows over one
file kept two version counts, so a server discarded whichever was behind, and either window's close closed the
document for both (hexide-io/HexIDE#721).

#### Scenario: Editing a document
- **WHEN** the developer edits an open document
- **THEN** the complete new text is sent with a version that increases

#### Scenario: The first change after an open
- **WHEN** a document has been opened and is then changed for the first time, by an edit or by a save that
  sends its text
- **THEN** the change carries a later version than the open did

#### Scenario: An edit while a server is starting
- **WHEN** a document is edited, or saved, while a server it is opened to is still starting
- **THEN** that server is opened on the document once, before any change to it, with the text as edited
- **AND** every later message about the document carries a later version than the one before

#### Scenario: Two editors holding one name
- **WHEN** a second editor opens a document whose name another editor already holds, and either then edits it
- **THEN** the server has been opened on the document once, and every change carries a later version than the one
  before, whichever editor it came from
- **AND** closing one editor leaves the document open for the other, and closing the last closes it

#### Scenario: A debounced change overtaken by a flush
- **WHEN** a change arrives with a version no later than the one the client already tracks
- **THEN** it is not sent to the server
- **AND** the newer tracked text and version are kept for reconnect replay

#### Scenario: Closing a document
- **WHEN** a document is closed
- **THEN** the server is notified, so it can release any state held for that document

### Requirement: Diagnostics SHALL be pushed through a single channel from more than one source
The client SHALL expose one diagnostics channel that carries both server-published diagnostics and
diagnostics injected by the IDE, and SHALL raise on it rather than requiring a consumer to poll.

How the client OBTAINS diagnostics from a server is a separate question from how it DELIVERS them onward,
and the two SHALL NOT be conflated. A server may publish unbidden or may require the client to ask; either
way what reaches the channel SHALL be indistinguishable, so that no consumer of the channel — the marker
pipeline, the editor, or anything later — can tell which model the server on the other end uses.

Every diagnostic SHALL carry the identity of the source that published it, and a source's publication for a
document SHALL replace only what that source last published for it. What the channel raises SHALL remain a
single whole-document set — everything every source currently claims about that document — so that no
consumer has to reason about who published what. An empty set from a source SHALL clear that source's
diagnostics for the document, and SHALL NOT clear another source's.

A source SHALL additionally be able to withdraw everything it has published, across every document. That
withdrawal SHALL be scoped by what the source actually published rather than by a list of documents the
caller supplies, because the two disagree exactly when it matters: a document renamed since that source
last spoke is absent from such a list and still carries its marks.

The IDE has a second source of truth: compiling with the real VB6 toolchain produces errors the syntactic
server cannot know about, and more than one language server may claim one document. Feeding all of them
through one channel means the marker pipeline, the editor, and any future consumer handle them identically
and cannot disagree about which errors are current. Ownership is what keeps that from also meaning the last
publisher wins — without it, a build expiring its own previous errors deletes what a server published for
the same document, and two servers on one document erase each other.

#### Scenario: The server reports a syntax error
- **WHEN** the server publishes diagnostics for a document
- **THEN** they are raised on the diagnostics channel and rendered in the editor

#### Scenario: The compiler reports an error the server cannot see
- **WHEN** the IDE compiles with the real VB6 toolchain and that compiler reports errors
- **THEN** those errors are injected into the same diagnostics channel and rendered the same way
- **AND** they appear alongside anything a server has published for the same document

#### Scenario: Errors are resolved
- **WHEN** a source of diagnostics reports an empty set for a document
- **THEN** that source's diagnostics for the document are cleared
- **AND** what other sources have published for it remains

#### Scenario: A build that the compiler had nothing to say about
- **WHEN** a build succeeds and expires the errors of the build before it
- **THEN** the diagnostics a language server published for those documents are still present

#### Scenario: Two servers claiming one document
- **WHEN** each publishes diagnostics for that document
- **THEN** both sets are current, and neither replaces the other

#### Scenario: A server that answers only when asked
- **WHEN** a server delivers its diagnostics by answering requests rather than by publishing
- **THEN** what reaches the channel is the same shape, under the same source identity, as if it had published

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
orderly server shutdown before terminating the process. The shutdown request SHALL have a two-second
deadline, and the client SHALL attempt `exit` whether shutdown answers, fails or times out, but only
after `initialize` has been answered. An externally owned pipe server SHALL receive neither message;
teardown SHALL close only its connection. A launched pipe server SHALL get two seconds to exit before
its remaining process tree is killed, including during bring-up. Both messages
SHALL omit the `params` member. The stdio transport SHALL allow two seconds for an orderly exit, then
kill the remaining process tree and wait up to two seconds for termination before disposing the process
handle. On Linux and macOS, both transports SHALL allow an additional two seconds for the guard's
group cleanup before killing it. The process exit code SHALL remain observable in the conversation during client stop.

On Windows, each stdio or launched pipe server SHALL be assigned immediately after launch to
`ChildProcessJob`, whose kill-on-close job ends assigned processes and descendants created after
assignment when the IDE ends, including without managed shutdown. Assignment failure SHALL fail
the connection and clean up the launched process. Externally owned pipe servers SHALL NOT be
assigned. Descendants created before assignment are not covered, since `Process.Start` cannot
start suspended.

On Linux and macOS, stdio and launched pipe servers SHALL run under `HexIDE.LspGuard`, which
SHALL create its own process group before starting the server with inherited stdin, stdout and stderr.
Every 500 ms the guard SHALL check whether its parent has changed. On reparenting it SHALL send
SIGTERM to its group and, after two seconds, SIGKILL. When the server exits on its own, it SHALL
perform the same group cleanup, leaving the group before SIGKILL to preserve the server's exit code.
After the normal transport grace period, a remaining guard SHALL be
terminated with its group. Externally owned servers SHALL NOT be guarded. Descendants that leave
the group are outside this guarantee.

An orphaned server process outliving the IDE would hold file handles and consume memory with nothing to
serve. Asking first and terminating second means a well-behaved server exits cleanly while a wedged one
still goes away.

#### Scenario: The Windows IDE is killed without shutdown
- **GIVEN** a launched stdio or pipe server has been assigned to the job and then spawned a child
- **WHEN** the IDE process is killed without disposing the transport
- **THEN** the OS ends both the server and its child by closing the IDE's job handle

#### Scenario: The Unix IDE is killed without shutdown
- **GIVEN** a launched stdio or pipe server and its child remain in the guard's process group
- **WHEN** the IDE is killed without disposing the transport
- **THEN** the guard detects reparenting and terminates the group, escalating after two seconds

#### Scenario: Shutting down the IDE
- **WHEN** the IDE shuts down
- **THEN** an initialized server owned by HexIDE is asked to shut down and its process is ended
- **AND** an externally owned pipe server is left running

#### Scenario: A Unix server exits cleanly with a child still running
- **GIVEN** a launched server has spawned a child in the guard's process group
- **WHEN** the server exits on its own
- **THEN** the guard sends SIGTERM to the group and SIGKILL after two seconds
- **AND** a child that ignores SIGTERM is ended, and the guard preserves the server's exit code

#### Scenario: A server never answers shutdown
- **WHEN** the server receives `shutdown` but never answers it
- **THEN** the client stops waiting after two seconds and attempts `exit`
- **AND** it disposes the connection and transport, killing any remaining child with a bounded exit wait

#### Scenario: A stdio server handles exit cleanly
- **WHEN** the server exits within two seconds of receiving `exit` after `shutdown`
- **THEN** it is allowed to terminate without being killed
- **AND** its exit code is recorded in the conversation

#### Scenario: A stdio wrapper ignores exit
- **WHEN** a launched stdio server remains alive after the exit grace period
- **THEN** its process tree is killed, including its server child
- **AND** the observed exit code is recorded before transport disposal finishes

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

Every form, module and class the IDE offers SHALL carry an extension, including one that has no file yet, so
no URI scheme SHALL take precedence over the extension. A language identifier SHALL NOT act as a claim on its
own: the identifier a server declares names what it is told a document is, and which documents can reach it
is decided by the extensions it claims. Declaring `vb6` narrows an existing claim rather than creating one,
in the way the next requirement describes.

#### Scenario: Two servers claim one extension under different identifiers
- **WHEN** a document of that extension is opened
- **THEN** both servers receive it, each told the identifier it declared

#### Scenario: An extension nothing claims
- **WHEN** a document is opened whose extension no entry claims
- **THEN** it opens with language features absent and no error is reported

#### Scenario: A module with no file yet
- **WHEN** a module that has no file behind it is opened
- **THEN** it is routed by the extension its kind will be saved with, exactly as one with a file is

#### Scenario: An entry that names the language but claims none of its extensions
- **WHEN** an entry declares the identifier `vb6` and claims no VB6 extension
- **THEN** it is not offered the IDE's forms and modules

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

#### Scenario: Another advertiser loses a one-answer feature
- **WHEN** folding ranges, document symbols, formatting or rename selects the highest-priority advertising claimant
- **THEN** each other advertising claimant SHALL record a never-sent explanation naming the winner's registration id and capability, once per capability per connection

### Requirement: Signature help SHALL select a non-empty answer within a shared budget
The registry SHALL ask eligible document claimants concurrently within a shared one-second budget.
Typed trigger characters SHALL select only claimants advertising that character in
`signatureHelpProvider.triggerCharacters`; explicit invocation SHALL ask all document claimants.
The highest-priority answer with at least one signature SHALL win, with registration order breaking ties.
Empty answers and answers exceeding the budget SHALL be ignored, and different servers' signatures SHALL
NOT be merged.

#### Scenario: The preferred server has no signatures
- **WHEN** the highest-priority server returns an empty signatures array and another returns signatures
- **THEN** the other server's answer is shown

#### Scenario: A typed trigger belongs to one server
- **WHEN** a character triggers signature help
- **THEN** only claimants advertising that character are asked

#### Scenario: A preferred server exceeds the budget
- **WHEN** one server has not answered after one second and another has returned signatures
- **THEN** the available answer is used without waiting for the slow server

### Requirement: A server SHALL be given the project's working directory
Each server SHALL be started with the current project's working directory as its working directory, and told
that directory as its workspace root, unless its entry states otherwise.

Servers routinely resolve their own configuration relative to where they are run. A server started somewhere
arbitrary reads none of the user's settings for it and reports subtly different results with no indication
why — a wrong answer rather than a missing one.

Where a project's working directory changes during a session, running servers SHALL be restarted so that
none continues against a root that no longer describes the project. **Every document open in an editor at
that moment SHALL be offered again to each restarted server that claims it**, before any change to it is
sent. A restarted connection starts with nothing open; without that, the next change to a document the
developer already had open reaches a server that was never told it exists, and every document except the
one whose opening happened to trigger the restart goes silently dark. A project's first save moves its
directory, so this is the ordinary path rather than a corner.

**Where that directory does not exist, the server SHALL still be started**, inheriting the IDE's own working
directory, and SHALL still be told the project's directory as its workspace root. The two are separate
questions — *where the process runs* and *which tree it analyses* — and they only coincide by accident. A
project that has not been saved yet has a directory that is real as an answer to the second and not yet real
as an answer to the first: it is where the project's files will be written the moment the user adds a
module. A form or module with no file yet is outside it, and so is a document adopted from somewhere else.
Starting a process there instead fails outright, which costs the whole connection and every language feature with it.

The IDE SHALL NOT create the directory in order to launch there. Nothing reaps these directories, so one
created per server start would accumulate with no owner, and a directory that exists would then no longer
mean the project has content in it.

A working directory named by the server's **own entry** SHALL NOT be checked for existence and SHALL be used
as given. Somebody who named one meant it, and a name that does not resolve is a configuration error they
can correct and must be told about; inheriting silently there would start the server against the wrong tree,
which is a wrong answer rather than a failure.

Where a server cannot be started, the reported reason SHALL name the working directory it was to be started
in. The operating system's own message for an unusable working directory does not name it, so a report
without it points at the executable for a fault that has nothing to do with it.

#### Scenario: A server that reads its own configuration from the workspace
- **WHEN** a server is started for a project
- **THEN** its working directory and workspace root are the project's working directory

#### Scenario: A project that has not been saved yet
- **WHEN** a server is started for a project whose directory does not exist
- **THEN** the server starts, inheriting the IDE's working directory
- **AND** it is still told the project's directory as its workspace root
- **AND** the directory is not created

#### Scenario: An entry naming a working directory that is not there
- **WHEN** a server's own entry names a working directory that does not exist
- **THEN** the server does not start, and the reported reason names that directory

#### Scenario: The root moves while documents are open
- **GIVEN** two documents open in editors
- **WHEN** a project save or startup-project change moves the workspace root
- **THEN** the running servers SHALL restart at the new root without waiting for another document to open
- **AND** each restarted server is told both documents are open with their latest text before it is sent a change to either

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

#### Scenario: A pipe role is misspelled
- **WHEN** a pipe entry carries an unrecognised nonblank `pipeRole`
- **THEN** the entry is kept, the value is reported, and the role falls back to `connect`

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

### Requirement: Every document the editor opens SHALL be offered to the language layer
Every text document the IDE opens in an editor SHALL be opened to the language client, kept synchronized
while it is edited, and closed when its editor closes — including documents the IDE does not itself compile
or understand.

Routing already decides correctly which servers, if any, claim a document. That decision is only ever
reached for documents an editor hands over, so an editor that does not hand its documents over makes the
whole configuration inert for exactly the file types it exists to serve. This requirement is upstream of
routing: it says every editor participates, not which server wins.

A document no server claims SHALL still be offered. Whether anything claims it is the language layer's
answer to give, and an editor that decides in advance not to ask would have to hold an opinion about file
types — which is the coupling the configurable server list removed.

#### Scenario: A file the project carries but does not compile
- **WHEN** the developer opens a carried file whose extension a configured server claims
- **THEN** that server receives the document, and its diagnostics are rendered in that editor

#### Scenario: A carried file nothing claims
- **WHEN** the developer opens a carried file whose extension no server claims
- **THEN** it opens normally with language features absent, no server is started, and no error is reported

#### Scenario: A carried file is edited and closed
- **WHEN** the developer edits a carried file and later closes it
- **THEN** the changes are synchronized as for any other document, and the servers are told it closed

### Requirement: A document on disk SHALL be identified by a URI carrying its extension
A document that has a file SHALL be identified to servers by that file's `file:` URI — a form, module or
class of the project exactly as much as a file the project carries. A form, module or class that has no file
yet SHALL be identified as `untitled:<project>/<name>.<ext>`: the project's name, the document's own name,
and the extension its kind will be saved with.

Routing keys on the extension, so an identifier that discards it cannot be routed. The two schemes are not
a preference: a document with no file cannot have a `file:` URI, and a document with a file must not be
given an opaque one, or it becomes unroutable and unopenable by any server that wants to read it from disk.

A file in a folder the IDE chose, such as the temporary folder the `--newproject` option saves a project into,
SHALL be named by its `file:` URI like any other: it is where the bytes are, and it is inside the root servers are told about.
Naming it `untitled:` would show a server that indexes that root the same module twice, once from disk and
once from the editor. A saved document whose file has since disappeared SHALL keep its `file:` URI:
`untitled:` means a document that has no file yet, not one whose file is missing.

The `untitled:` name SHALL use the document's name, never a file name — there is no file — and SHALL be
written without a leading slash and percent-encoded, one spelling only. Two spellings of one name are two
documents to a server, and a server may reject a name that is not strictly valid without replying at all.
Two `untitled:` names SHALL be compared without regard to case, as VB6 compares the names they are made of.

A document's wire name is not its identity inside the IDE. It SHALL be fixed when the document is opened to
the language layer and SHALL change only as the next requirement describes, never by reading the document's
current path: a build repoints every path into a temporary folder and back, and that is not a move.

#### Scenario: A carried file is opened
- **WHEN** a carried file is offered to the language layer
- **THEN** it is identified by a `file:` URI whose extension is the one servers match against

#### Scenario: A document with no file behind it
- **WHEN** a form or module the IDE holds only in memory is offered to the language layer
- **THEN** it is identified as `untitled:` followed by its project's name, its own name and the extension of
  its kind
- **AND** it is routed by that extension

#### Scenario: A saved module is opened
- **WHEN** a module of a saved project is offered to the language layer
- **THEN** it is identified by the `file:` URI of its file, and a server may read that file

#### Scenario: A module in a project not yet saved
- **WHEN** a module has been added to a project that has never been saved, which writes no file for it (#500)
- **THEN** it is identified as `untitled:` followed by its project's name, its own name and `.bas`

#### Scenario: Building the project
- **WHEN** the project is built and every path is temporarily pointed elsewhere
- **THEN** no server is told any document closed, opened or moved

### Requirement: A server that asked to hear about saves SHALL be told about them
The client SHALL declare that it can send save notifications, and SHALL send one when a document it has
opened is written to disk — to each server claiming that document that asked for them, and to no other.
Whether a server asked SHALL be read from what it declared at initialization.

A server that defers its analysis to save is otherwise indistinguishable, from inside the IDE, from one
that is broken: it connects, it initializes, and it answers nothing. Deferring is the ordinary choice for
anything expensive, so the servers most worth attaching are the ones most likely to be silenced by this.

Declaring and gating SHALL land together. A client that gates on what the server declared without first
declaring its own half creates a deadlock in which both parties are correct and nothing happens: the
server withholds the capability because the client never claimed it, and the client declines to send
because the server did not ask.

#### Scenario: A server that asked for save notifications
- **WHEN** a document it has open is saved
- **THEN** it is told, after the current text has been sent

#### Scenario: A server that did not ask
- **WHEN** a document it has open is saved
- **THEN** it is not told, and nothing is reported as an error

#### Scenario: Two servers claiming one document, only one of which asked
- **WHEN** the document is saved
- **THEN** only the server that asked is told

### Requirement: A save notification SHALL carry the text only when the server asked for it
Where a server declared that save notifications should include the document's text, the client SHALL send
the text it last gave that server. Where it did not, the client SHALL omit the text entirely rather than
sending an empty or null value.

The distinction is not cosmetic. A server tests whether the field is present to decide between reading
the document from disk and using what it was handed, so a null sent in place of an absent field selects
the wrong branch — and it does so silently, which is the failure mode this capability's whole area keeps
producing.

The client SHALL send the document's current text, not a version the server has yet to receive. A save
announced against text the server was never given describes a file it cannot see, which is worse than not
announcing it at all.

#### Scenario: The server asked for text
- **WHEN** a saved document is announced to it
- **THEN** the text accompanies the notification, and matches what the editor wrote

#### Scenario: The server did not ask for text
- **WHEN** a saved document is announced to it
- **THEN** the notification carries no text field at all

#### Scenario: A document saved while an edit is still settling
- **WHEN** the document is saved before pending changes have been sent
- **THEN** those changes are sent first, so the save describes what the server holds

### Requirement: Every write of a document to disk SHALL count as a save
A save notification SHALL follow any path that writes an open document to its own file, including paths
that involve no editor — saving a project, saving every project, and saving from the prompt shown when
something closes with unsaved work.

Raising this in an editor would miss the IDE's primary save gesture, which writes every form and module
without an editor being involved at all. Writing a copy elsewhere is not a save of the document and SHALL
NOT be announced: building an executable writes every document to a temporary location and then restores
the model, and announcing those would report saves the developer never made.

#### Scenario: Saving the project from the File menu
- **WHEN** the developer saves a project whose documents are open
- **THEN** each open document's servers are told it was saved

#### Scenario: Building an executable
- **WHEN** the documents are written to a temporary location as part of producing a binary
- **THEN** no save is announced, because the developer's files were not written

### Requirement: A document SHALL be offered to every server that claims its language
The IDE SHALL route a document by its language identity, derived from the document's extension, and SHALL
offer it to **every** registered server claiming that language, subject to the ambiguity rule below, rather
than selecting one. Where more than one
server answers, the IDE SHALL combine their results: diagnostics from all sources SHALL be shown together,
and list-shaped results SHALL be concatenated except where a feature defines its own selection or merge
rule. Completion SHALL follow the bounded, deduplicated merge below.

Routing to a single server is the simpler design and the wrong one. The arrangement it forecloses — a
language server and a separate linter or checker on the same file — is ordinary rather than exotic, and it is
the arrangement the wider ecosystem is built around. The asymmetry decides it: a combining router can be
configured down to one server, while a router that picks one cannot be widened without changing every caller.

Language identity SHALL come from the document's extension rather than from its role in the project, with
one exception, below, that only ever narrows the set of servers a document reaches. A project member's kind
is a project-file concept, and the documents most likely to need a second server — files carried alongside
the project rather than compiled by it — have no such kind.

**An extension another language also uses SHALL NOT be enough, on its own, to receive a project's forms,
modules and classes.** A project member is VB6 whatever its extension shares, so where its extension is
ambiguous it SHALL be offered only to servers whose claim establishes VB6: one that claims a **VB6** extension
no other language uses — every VB6 source extension but `.cls` — or that declares the identifier `vb6`.
Stated as "an extension no other language uses", the rule would be met by any server claiming any extension
of its own, which is the opposite of its purpose. A file the project carries is not
known to be VB6, and SHALL be routed by its extension, except that a VB6 entry SHALL receive a carried
`.cls` only when its first non-blank line equals `VERSION 1.0 CLASS` case-insensitively. This content
claim SHALL be recorded on open or reload and retained through edits until the next open. The alternative costs a started process and the
developer's source sent to a server with nothing to say about it, for every class module in the project.

#### Scenario: Two servers claim the same language
- **WHEN** a document is opened whose language is claimed by more than one registered server
- **THEN** every claiming server receives the document, and their diagnostics are shown together

#### Scenario: No server claims the language
- **WHEN** a document is opened whose language no registered server claims
- **THEN** the document opens normally with language features absent, and nothing is reported as an error

#### Scenario: A class module and a server claiming only the shared extension
- **GIVEN** a server entry whose only VB6-looking claim is `.cls`, and which does not declare `vb6`
- **WHEN** a class module of the project is opened
- **THEN** that server does not receive it, and the VB6 server does

#### Scenario: A carried file with the shared extension
- **GIVEN** the same server entry
- **WHEN** a `.cls` file the project carries is opened
- **THEN** that server receives it

#### Scenario: A carried LaTeX class does not reach VB6
- **WHEN** a carried `.cls` opens without `VERSION 1.0 CLASS` as its first non-blank line
- **THEN** VB6 entries do not receive it, while other extension claimants still do
- **AND** editing the signature does not change routing until it is reopened

### Requirement: Features that cannot combine SHALL select one server by stable identity
Where combining answers is meaningless — formatting a document, or renaming a symbol — the IDE SHALL select
exactly one server. Every registered server SHALL carry an identity that is stable across restarts, and
selection SHALL be expressed in terms of that identity.

Two edits to the same document cannot both be applied, so these features need a winner rather than a merge.
The identity requirement is what makes choosing one *expressible*: a server known only by its position in a
list cannot be named in a setting, referred to in a message, or remembered between sessions.

Where several servers remain eligible, selection SHALL be deterministic: an explicit priority where one is
declared, and registration order otherwise. Registration order alone is deterministic but accidental, because
it varies with discovery — which changes when a server is installed or removed.

#### Scenario: Two servers offer to format the same document
- **WHEN** a format is requested and more than one registered server offers formatting
- **THEN** exactly one server's edits are applied, chosen by declared priority and then registration order

### Requirement: Requests SHALL be gated on the capabilities the server advertised
For each request, the IDE SHALL send it only to servers that advertised support for it during
initialization, and SHALL treat an unadvertised feature exactly as it treats an absent server — returning
the same empty or absent result rather than raising an error.

A server states what it implements; calling it for anything else produces routine failures indistinguishable
from a broken server, and gives a reader no way to tell "not supported here" from "not working". Reusing the
existing degradation path means gating changes nothing for any caller.

The IDE SHALL NOT interpret an empty capability set as support for everything. A server that advertises
nothing is stating that it serves nothing, and a client that overrides that statement cannot detect the same
omission in any other server.

A capability SHALL be recognised in each of the forms the protocol permits it to take, including both a plain
flag and an options object. A capability arriving in a legal form the IDE fails to parse SHALL NOT prevent
initialization from completing.

The last paragraph is not defensive coding. A parse failure during initialization leaves the connection
uninitialized, which disables **every** language feature including diagnostics — so a single capability
arriving in its other legal shape silently costs the whole backend.

#### Scenario: A feature the server did not advertise
- **WHEN** a language feature is requested that the answering server did not advertise
- **THEN** the request is not sent, and the caller receives the same result it receives when no server is running

#### Scenario: A capability advertised as an options object
- **WHEN** a server advertises a capability as an options object rather than a plain flag
- **THEN** the capability is recognised as supported and initialization completes normally

### Requirement: Connections SHALL be observable
The IDE SHALL expose its language-service connections as inspectable state: for each, a stable identity, what
kind of connection it is, its current state, the languages it serves, and the capabilities it advertised as
received. A server that is configured but not yet started SHALL be observable as such, rather than absent.

This exists so the question people actually bring to a language service — *why is this file getting no help?*
— has an answer. Without it, a server that is quiet because nothing triggered it is indistinguishable from
one that is missing, misconfigured, or crashed, and those need different responses.

Advertised capabilities SHALL be retained as received rather than reduced to a summary. A summary invented
now will be wrong for a server not yet met, and the raw answer is the only honest response to "why is this
feature unavailable here".

The connection description SHALL NOT be specific to one protocol. A debug connection has the same properties
worth showing, and the shape a user interface consumes should not need rebuilding to gain a second kind.

#### Scenario: A configured server that has not started
- **WHEN** a server is configured for a language and no document of that language has been opened
- **THEN** the connection is observable, and is distinguishable from a server that failed to start

### Requirement: Servers SHALL start on first use and own their transport
A server SHALL be started when a document of a language it claims is first opened, and not before. Each
registered server SHALL carry its own transport configuration.

Starting every configured server on project open spends time and memory on languages a project does not
contain, and lets an absent or broken server for an unused language degrade startup for everyone. Transport
is per-server because servers do not agree on one: a single global choice cannot describe two servers that
communicate differently, and the transport a server needs is a property of that server.

#### Scenario: A project containing no documents of a configured language
- **WHEN** a project is opened that contains no document of some configured language
- **THEN** that language's server is not started

### Requirement: The client SHALL ask a server to describe its own work, and SHALL be able to change its mind
The client SHALL declare a trace level when it initializes a connection, SHALL take that level from
per-server configuration, and SHALL be able to change it on a running connection. It SHALL NOT report a
level as having been refused, because no such signal exists.

A wire trace shows what was said. A server's own trace shows why it said it, which is the half a server
author cannot reconstruct from the outside. The two answer different questions and an author chasing a
failure wants both interleaved.

The initial level travels in the initialization request and so must be known before the process exists,
which is why it belongs in configuration; changing it afterwards is what the protocol's own notification is
for. Reporting a refusal is impossible rather than merely unimplemented: that notification carries no reply,
and the protocol defines no capability by which a client can discover whether a server honours tracing at
all. Measured against five servers, every one accepted the request and four then said nothing. The only
honest report is that the server was asked and has not answered.

#### Scenario: Starting a server with tracing on
- **WHEN** a server is configured to start with tracing enabled
- **THEN** the level is declared in its initialization request

#### Scenario: Turning tracing up on a running server
- **WHEN** the developer raises the level on a connection that is already up
- **THEN** the server is told, without being restarted

#### Scenario: A server that ignores it
- **WHEN** a server accepts the request and emits nothing
- **THEN** the IDE reports that it was asked and nothing has arrived, rather than reporting a refusal

### Requirement: What a server says about itself SHALL reach somewhere a person can see
The client SHALL surface a server's own trace output and its log messages rather than discarding them, and
SHALL rank their severities as the protocol defines them, including severity values introduced after the
version this client targets.

A server that starts, connects and then serves nothing explains itself over exactly these channels, and they
are the only account it can give. Discarding them leaves a misconfigured server indistinguishable from a
broken IDE, which is the failure this client's connection diagnosability was built to end and did not
finish.

Severity has to be right for a reason beyond tidiness. An unknown value mapped to a middle rank makes a
newer protocol version's *noisiest* channel the loudest thing in the record, so a client that guesses
upward is worse than one that does not recognise the value at all.

#### Scenario: A server explaining a misconfiguration
- **WHEN** a server reports a problem with its own setup
- **THEN** that report reaches a surface the developer can read, rather than only a discarded log level

#### Scenario: Running commentary reaches the default log
- **WHEN** a server sends `window/logMessage` with type 4 (Log)
- **THEN** the client SHALL write it at Information so the default log retains it
- **AND** type 5 (Debug) SHALL remain at Trace

#### Scenario: A severity this client does not know
- **WHEN** a server sends a severity introduced after the version this client targets
- **THEN** it is not ranked above severities the client does know

### Requirement: The client SHALL ask servers that answer only when asked
A server MAY deliver diagnostics by answering `textDocument/diagnostic` requests instead of publishing
them. The client SHALL support that model as well as the published one, because a server that only answers
when asked is otherwise indistinguishable from a broken one: it starts, completes the handshake, advertises
honestly, and then says nothing about the developer's code for as long as it runs.

The client SHALL declare its ability to ask, and SHALL ask only of a server that advertised the capability.
Both halves are required and neither alone is sufficient: gating on an advertisement the client never
invited produces a state in which both parties are correct and nothing happens, which is the deadlock
already recorded for save negotiation.

A server that does not advertise the capability SHALL NOT be reported as deficient. Its silence is a
complete and correct answer — it publishes instead — and treating the absence as a missing feature would
blame every conformant publishing server for the way it conforms.

The client SHALL ask when a document is opened, after it changes, and when it is saved. For these triggers
it SHALL NOT introduce pacing of its own: the edit stream is already coalesced before it reaches the client, and
a second authority over the same question would answer it differently.

A document pull answered ContentModified (`-32801`) or ServerCancelled (`-32802`) SHALL be retried,
unless ServerCancelled carries `data.retriggerRequest: false`. Retries SHALL wait 250 ms initially,
doubling to a 4-second cap, and SHALL stop when the document closes, changes, a newer pull supersedes them,
or the connection is replaced. Each retry SHALL be recorded as its own protocol request.

A server MAY additionally ask to be asked again, and the client SHALL honour that by re-requesting every
open document. It is the only means such a server has of reporting a change that is not a document — its
own configuration, a rule set, something it watches — and without it the editor would keep showing answers
derived from a configuration that no longer exists, with no event that would ever correct them.

#### Scenario: A server that only answers when asked
- **WHEN** such a server claims a document and that document is opened
- **THEN** the client asks it for that document's diagnostics
- **AND** what comes back reaches the editor as markers

#### Scenario: A server that publishes
- **WHEN** a server did not advertise that it answers when asked
- **THEN** the client does not ask it
- **AND** nothing reports that server as missing a capability

#### Scenario: The document changes
- **WHEN** an open document is edited
- **THEN** the client asks again, and the answer replaces what that server last said about the document

#### Scenario: A diagnostic pull is temporarily unavailable
- **WHEN** a document pull is answered ContentModified and the document remains open and unchanged
- **THEN** the client asks again after back-off and publishes the successful report

#### Scenario: The server refuses a diagnostic retry
- **WHEN** a document pull is answered ServerCancelled with `data.retriggerRequest: false`
- **THEN** the client does not retry that request

#### Scenario: The document closes during diagnostic back-off
- **WHEN** the document closes before a retry is sent
- **THEN** that retry is not sent

#### Scenario: The server says its answers are stale
- **WHEN** such a server asks for diagnostics to be refreshed
- **THEN** the client asks again about every open document, rather than refusing

#### Scenario: A server that both publishes and offers to answer
- **WHEN** a server advertises that it answers when asked and also publishes unbidden
- **THEN** the client takes only what it answers, so that one source of truth decides the document's marks

### Requirement: An answer that says "nothing has changed" SHALL preserve what it refers to
A server answering a diagnostics request MAY say that its previous answer still stands rather than
repeating it. Such an answer carries no diagnostics, and the client SHALL treat it as leaving that server's
diagnostics for the document exactly as they were — never as an empty set.

The distinction is the whole purpose of the model's result identifiers and it fails destructively if
missed: an answer meaning "what I told you before is still true" would erase precisely the diagnostics it
was sent to preserve, and it would do so only on the second request, so the markers would appear correctly
and then vanish.

To make such an answer possible the client SHALL retain the identifier the server gave with its last answer
for a document and send it back with the next request for that document. It SHALL send the identifier the
server named for itself where the server named one, so a server distinguishing its own answers from another
party's can do so.

Where a request has been overtaken — the document changed again before the answer arrived — the client
SHALL discard the answer rather than publish it, because it describes text that is no longer open.

#### Scenario: The server says its previous answer still stands
- **WHEN** the client asks again and the server answers that nothing has changed
- **THEN** the diagnostics already shown for that document remain shown

#### Scenario: An answer arrives after the document moved on
- **WHEN** a further change is made before an answer to an earlier request arrives
- **THEN** that answer is discarded rather than rendered

#### Scenario: The document is closed
- **WHEN** a document served this way is closed
- **THEN** that server's diagnostics for it are cleared, since it has no way to withdraw them itself

### Requirement: A document whose wire name changes SHALL be closed and reopened
Where a document's wire name changes while it is open, each server that has it open SHALL be told it closed
under the old name and then that it opened under the new one, with its current text, in that order on each
connection. The name changes when a document is written to a file for the first time, when it is saved to a
different file, and — for a document with no file — when its name or its project's name changes. Where the
change was caused by a save, a server that asked to hear about saves SHALL be told under the new name.

This is the shape the protocol itself prescribes for a rename, and for the reason it gives: more than the
name can change, including which servers claim the document. Sending both without ordering them lets a
server see two open documents for one file, or close the one it has just been given.

Nothing a developer set on the document SHALL move: its breakpoints and bookmarks belong to the document,
not to the name it is known by on the wire.

Diagnostics SHALL follow the name. Those published or injected under the old name SHALL be withdrawn as the
document is closed under it, whether or not the server that published them withdraws them itself, and the
document SHALL show those that arrive under the new one. A server is entitled to say nothing on close, and a
marker left under a name nothing answers to any more cannot be cleared by anything afterwards.

#### Scenario: Saving a form for the first time
- **GIVEN** a form with no file, open in the editor as `untitled:Project1/Form1.frm`
- **WHEN** the developer saves it to `Form1.frm`
- **THEN** each server that had it open is told it closed as `untitled:Project1/Form1.frm`
- **AND** only then that it opened under the `file:` URI of `Form1.frm`
- **AND** its breakpoints and bookmarks are unchanged

#### Scenario: Renaming a form that has a file
- **WHEN** a saved form is renamed
- **THEN** its wire name does not change, because its file has not

#### Scenario: Renaming a module that has no file
- **WHEN** a module with no file is renamed
- **THEN** each server that had it open is told it closed under the old name and opened under the new one

#### Scenario: Diagnostics on a document that gains a file
- **GIVEN** a document showing diagnostics from a server and from a build
- **WHEN** it is saved for the first time
- **THEN** no diagnostic remains recorded under the name it had before

### Requirement: The client SHALL declare the reply shapes it reads
Where the protocol lets a client's declaration decide the shape of a reply, the client SHALL declare a shape it
reads whenever the shape sent without the declaration carries less of what the client keeps. It SHALL NOT
declare a shape it cannot read.

A server that honours the declaration sends the most basic shape to a client that did not make it, and no error
says so. Servers that send the richer shape regardless hide the omission (hexide-io/HexIDE#463).

#### Scenario: Symbols from a server that sends the tree only when told
- **WHEN** a server that honours the declaration is asked for the symbols of a document that nests them
- **THEN** it answers with the tree, and the symbols arrive nested as the document declares them

### Requirement: The client SHALL declare whole-line folding while it reads folds by line
The client SHALL read a server fold's start and end characters, defaulting each absent character to the
length of its line, and SHALL NOT declare `lineFoldingOnly`. A collapsed server fold SHALL leave the text
before its start visible, without repeating that text as a title.

Nested folds sharing a start SHALL both survive updates. Each surviving range SHALL retain its folded state
after edits and server answers, independently of list order or another range disappearing. A returning server range
absent from the preceding update SHALL take its default folded state.

#### Scenario: A fold ends before an indented closing delimiter
- **WHEN** an external backend sends a fold ending at the character before a closing delimiter
- **THEN** the delimiter stays visible when the fold is collapsed

#### Scenario: A procedure fold disappears while its attribute run remains
- **WHEN** a procedure and its attribute run share a fold start and the next answer omits the procedure fold
- **THEN** the attribute run keeps its own folded state

### Requirement: Server work-done progress
The client SHALL advertise `window.workDoneProgress`, accept string and integer tokens through
`window/workDoneProgress/create` with a null result, and surface `$/progress` work-done begin/report/end
in the status bar. Progress SHALL name the originating registration’s display name and separate title
and message with a colon, omitting absent parts. Server titles and messages SHALL be flattened and capped at 200 characters together.
Tokens SHALL be scoped to their connection. End and disconnect SHALL finish progress immediately without
erasing a later status message from another operation. Cancellation and partial-result progress are not supported.

#### Scenario: An attached server indexes a workspace
- **WHEN** a server creates a token and sends begin followed by report
- **THEN** the status bar shows its title, latest message and optional percentage with its registration’s display name in a localized format
- **AND** omitted report fields retain their prior values
- **WHEN** the server sends end or disconnects
- **THEN** its progress is removed immediately and any remaining task can be shown

#### Scenario: A server begins without creating a token
- **WHEN** a server sends work-done begin without a prior create request
- **THEN** its progress is accepted and displayed

### Requirement: Subprocess launch SHALL accept argument lists and environment overrides
For stdio and launched pipe servers, the IDE SHALL accept nullable `arguments` as either a legacy
command-line string or an array of strings. Arrays SHALL be forwarded through `ProcessStartInfo.ArgumentList`
without trimming their elements. Pipe placeholders SHALL expand within each element. A nullable
`environment` map SHALL override only the named inherited environment variables. Processes SHALL be launched
without a shell or tty.

#### Scenario: Arguments needing embedded quoting
- **WHEN** an entry supplies an argument array containing spaces, quotes or empty strings
- **THEN** the child receives each element as one argument with its contents preserved

#### Scenario: One environment override
- **WHEN** an entry supplies an environment variable value
- **THEN** the child receives that value and inherits variables not named in the map

### Requirement: A launched pipe server can be told its owner's process ID
For a launched pipe server, the client SHALL replace every `{pid}` in the configured arguments with
HexIDE's process ID as invariant decimal digits. This substitution SHALL work without an open workspace.
The server is responsible for watching that process; the placeholder does not enforce process-tree cleanup
after an unclean IDE shutdown.

#### Scenario: A server accepts an owner-process argument
- **WHEN** a pipe entry launches a command with `--owner {pid}` in its arguments
- **THEN** the launched process receives `--owner` followed by HexIDE's process ID
- **AND** no open project is required for that substitution

### Requirement: WebSocket transport direction

The client SHALL support `websocketRole` values `connect` (the default) and `listen` in server
configuration, without changing the transport establishment interface. Listening SHALL accept an
incoming WebSocket at the configured endpoint, use the same JSON-RPC Text-message framing as connecting,
and release the listener on failure, cancellation or disposal.

#### Scenario: Server dials the editor
- **GIVEN** a WebSocket entry with `websocketRole` set to `listen`
- **WHEN** the server opens a WebSocket to its endpoint while the client establishes the transport
- **THEN** the client exchanges JSON-RPC messages over that incoming connection

### Requirement: A listening WebSocket accepts only a local, non-browser dial

Because whatever dials a listening endpoint is taken as the language server, the client SHALL listen only
on a loopback endpoint (`localhost`, `127.0.0.1` or `[::1]`) and SHALL refuse any other host before binding.
It SHALL refuse a dial from a non-loopback address, and a dial carrying an `Origin` header, which every
browser sends on a WebSocket handshake and no language server process does, and SHALL go on listening for
the server after a refusal.

#### Scenario: An endpoint off this machine
- **GIVEN** a WebSocket entry with `websocketRole` set to `listen` and an endpoint on `0.0.0.0` or a host name
- **WHEN** the client establishes the transport
- **THEN** nothing is bound and the connection's failure says the endpoint must be on this machine

#### Scenario: A web page dials first
- **GIVEN** a client listening on a loopback endpoint
- **WHEN** a handshake carrying an `Origin` header arrives, and then one without
- **THEN** the first is refused with 403 and the second is accepted as the server

### Requirement: An entry's arguments SHALL be adjustable without replacing it
The configuration SHALL let a user add arguments to an entry by its identifier, apart from the entry, so that an entry whose command the user cannot write can still be adjusted. The arguments SHALL be added to the entry's own, never replace them, and SHALL apply to whichever entry holds the identifier after layering. They SHALL be added in the entry's own form, and a different form SHALL be reported and left out.

#### Scenario: An argument for the bundled server
- **WHEN** the configuration adds arguments for the bundled server's identifier
- **THEN** the bundled server is started with its found command and those arguments

#### Scenario: Arguments in a different form
- **WHEN** an entry's arguments are a command line and the added ones are a list, or the reverse
- **THEN** the entry is left as it was and the problem is reported

#### Scenario: Arguments for no server
- **WHEN** added arguments name an identifier no entry has, or an entry the IDE starts no process for
- **THEN** they are reported and change nothing

### Requirement: Consented workspace artefacts

A server registration MAY declare a workspace-relative artefact path, provider id and provider settings.
The client SHALL resolve providers through an interface and DI registration, and SHALL materialise the
artefact before launching the server. Providers SHALL return content without writing; backend schemas
SHALL remain outside the core artefact service.

**Known limit.** Between the recheck after consent and the replace, a process that can write into the workspace at that moment could
swap a parent folder for a link, and the temporary file and the replace would follow it. .NET offers no handle-relative
file API to close this. It needs a concurrent local writer inside the workspace, which could edit the project's sources
anyway, so it is recorded rather than defended.

#### Scenario: Scoped consent
- **WHEN** an absent or still-owned artefact needs writing
- **THEN** the prompt names the server and local file and offers Allow, Deny and Allow Always
- **AND** Allow covers the server/workspace for the session, Allow Always persists that pair, and Deny launches without writing and logs why
- **AND** a decision covers only the declaration it was asked for: a changed artefact path or provider asks again
- **AND** persistent grants are listed and resettable in Options

#### Scenario: Path and ownership protection
- **WHEN** a declaration is absolute, drive-qualified, escapes via parent segments, or resolves outside the root through links
- **THEN** the client refuses the write
- **WHEN** a file exists whose bytes differ from the client's last recorded write
- **THEN** the client leaves it alone without prompting and launches the server anyway
- **WHEN** the target is a directory, an empty file (as a FIFO or a device presents), or larger than any artefact the client writes
- **THEN** the client treats it as not owned without opening or reading it, leaves it alone, and launches the server anyway
- **AND** a candidate for ownership is hashed through a stream, never read whole

#### Scenario: Atomic and unchanged writes
- **WHEN** generated bytes match the owned target
- **THEN** the client skips the write and preserves its write time
- **WHEN** generated bytes differ
- **THEN** the client writes a temporary file in the same directory and atomically replaces the target, checking ownership under a lock
- **AND** a failure while writing leaves the previous target intact

#### Scenario: Nothing to describe
- **WHEN** a provider returns nothing for a project, such as clangd for a project carrying no C/C++ files
- **THEN** the client neither prompts nor writes
- **AND** it deletes the target only if the target still holds the client's last recorded write, within the same path confinement and ownership lock as a replace
- **AND** it leaves a file whose bytes differ alone, recording why on the server's connection

#### Scenario: A failed or refused write remains visible
- **WHEN** a path is refused, consent denied, a provider throws, a write fails, or an existing foreign file is left alone
- **THEN** the reason is recorded as a local failure note on the server's connection in the protocol inspector
- **AND** the server still launches
- **AND** notes name the configured workspace-relative path and exception type, never the resolved absolute target or exception message, so exports do not expose workspace locations

#### Scenario: Material project changes
- **WHEN** forms, modules or carried files are added, removed or renamed, or references change
- **THEN** the artefact is regenerated after debouncing, subject to the same consent and ownership checks
- **AND** regeneration sends no watched-file notification in this phase (#456)

#### Scenario: Clangd compilation database
- **WHEN** the clangd provider is configured
- **THEN** it emits one compilation command per carried C/C++ file, using configured compiler arguments followed by that file, with its containing directory as working directory
- **AND** it emits nothing for a project carrying no C/C++ files
- **AND** a real clangd test observes diagnostics changing when the generated arguments define HEXIDE_PROBE

#### Scenario: RDCore project descriptor
- **WHEN** the rdcore provider is configured
- **THEN** it emits a `.rdproj` with PascalCase keys, listing every form, module, class and user control as a module by its host path relative to the workspace root
- **AND** it carries the project's references, and its Conditional Compilation Arguments as precompiler constants, none for a line vb6.exe refuses
- **AND** it lists no other files, since RDCore parses every listed file as code
- **AND** it emits nothing for a project with no code files

#### Scenario: Capability boundaries
- **WHEN** a server declares no artefact
- **THEN** no artefact is generated for it
- **AND** the capability grants neither writes outside the workspace nor writes without consent
- **AND** the public add-in provider contract remains outside this change

### Requirement: Completion SHALL merge within a bounded popup budget
The editor SHALL flush pending document changes before requesting completion, including requests triggered
by letters and advertised trigger characters.

The registry SHALL ask every document claimant concurrently and return when all answer or one
second from the request elapses. It SHALL include only answers received within that budget and SHALL
deduplicate by ordinal label and kind, retaining the complete item from the higher-priority server or the
earlier registration on a tie. Late answers SHALL be dropped, logged at debug level, and recorded in the
protocol inspector as answered after the popup opened. The underlying request SHALL retain its normal
deadline and caller cancellation.

#### Scenario: Typing requests completion before the debounce expires
- **WHEN** a letter or advertised trigger character is typed with a pending document change
- **THEN** the `didChange` carrying that character precedes `textDocument/completion` on the wire

#### Scenario: A server returns a bare completion array
- **WHEN** a completion reply is a `CompletionItem[]` rather than a `CompletionList`
- **THEN** the client SHALL normalize it to a list with `isIncomplete: false` and preserve its items
- **AND** list replies and null replies SHALL continue to provide their items or no items respectively

#### Scenario: Two servers offer the same completion
- **WHEN** two timely answers contain the same label and kind
- **THEN** the higher-priority claimant's item is shown once, with its detail and insertion text
- **AND** registration order breaks equal-priority ties

#### Scenario: One server answers late
- **WHEN** one claimant answers within a second and another answers after it
- **THEN** the timely items are returned at the budget boundary
- **AND** the late answer is recorded and dropped without changing the open list
- **AND** cancellation or timeout without a reply retains its own outcome and is not recorded as a late answer

#### Scenario: Claimants advertise different trigger characters
- **WHEN** the editor receives a character in the union of the document claimants' completion triggers
- **THEN** it requests completion only from claimants advertising that character
- **AND** letters continue to use the ordinary completion route
### Requirement: Refuse multi-document rename answers

The code window SHALL inspect the whole rename WorkspaceEdit before applying any edits. If any URI
other than its own has a non-empty edit list, it SHALL refuse the whole answer and name those documents
using loaded member names resolved by the navigation URI rule, falling back to the URI when unresolved.
Empty lists for other documents SHALL NOT prevent applying edits to the current document.

#### Scenario: Rename reaches another document
- **WHEN** a rename answer edits another document, whether or not it also edits the current document
- **THEN** the current buffer remains unchanged and the refusal identifies the other documents

#### Scenario: Another document has no edits
- **WHEN** the answer edits the current document and has only empty lists for other documents
- **THEN** the current document's edits follow the usual single-document rename checks


### Requirement: Signature parameter label unions

The client SHALL accept signature-help parameter labels as strings or UTF-16 offset pairs into the
signature label. The popup SHALL use the exact offset range for highlighting and parameter text.
The header SHALL preserve the signature label verbatim and show only the active parameter range in bold,
keeping the label's own optional-parameter brackets (#2632).
String labels SHALL retain first-match highlighting; an out-of-bounds offset SHALL produce no highlight.
A reply that fails deferred result deserialization SHALL be recorded as Failed with an answer-dropped
explanation, retaining any captured reply body.

#### Scenario: Offset parameter label
- **WHEN** a server answers signature help with a parameter label `[start, end]`
- **THEN** the popup highlights that range of the signature and displays its text with the parameter documentation

#### Scenario: Unreadable result
- **WHEN** a received reply fails deferred result deserialization
- **THEN** the connection record marks the exchange Failed with an answer-dropped explanation
- **AND** any captured reply body remains available

### Requirement: Semantic tokens SHALL overlay lexical colours
The client SHALL declare relative, full semantic token requests with the standard token types and modifiers,
and without overlapping or multiline tokens. It SHALL ask only the highest-priority document claimant that
advertises full semantic tokens, decoding against that claimant's legend. Editors SHALL ask on attachment,
500 ms after changes, and when the server state changes. Tokens SHALL override lexical foreground colours
only on their ranges; read-only region and diagnostic colours SHALL retain their precedence.

An empty or failed answer SHALL leave lexical colouring, log once per document on that connection, and
permit the next change to ask again. Invalid ranges SHALL be discarded. Responses for an earlier document
version or detached view SHALL NOT be applied. Delta, range and refresh requests are not supported.

#### Scenario: A server colours a name differently from the lexical rules
- **WHEN** a full response classifies a range as a class
- **THEN** that range uses the theme's type colour
- **AND** uncovered text keeps its lexical colour

#### Scenario: Analysis is not ready
- **WHEN** a full request fails
- **THEN** the editor keeps lexical colouring
- **AND** a later edit schedules another full request

### Requirement: Initialize UI locale

The client SHALL send `initialize.locale` as the resolved Options Language setting's BCP 47 tag,
using the same system-language resolution as the IDE and `en` for either pseudo-locale.
The client SHALL read the locale at each handshake and SHALL NOT restart servers automatically
when the UI language changes.

#### Scenario: Language changes between connections
- **WHEN** the UI language changes while a server is connected
- **THEN** the existing connection stays open
- **AND** the next connection's initialize request carries the newly resolved locale

### Requirement: Existing outside sources contribute initial workspace folders
At initialization, the client SHALL include each loaded project's folder before any existing outside
source folders contributed by that project. It SHALL deduplicate folders by full host path and SHALL
keep one project per `.vbp`, irrespective of the folders its sources occupy. Missing source files SHALL
NOT contribute folders. Connections SHALL send deduplicated added and removed folders
to servers declaring `workspace.workspaceFolders.changeNotifications` as true or a string.
The notification SHALL precede project import on the same debounced trigger; unchanged folders SHALL
produce no notification.

#### Scenario: A listed source lives beside the project directory
- **GIVEN** a project in `App` lists an existing module in sibling directory `Shared`
- **WHEN** a language server initializes
- **THEN** its workspace folders include `App` followed by `Shared`
- **AND** the module remains part of the listing project

#### Scenario: A membership change adds an outside folder
- **WHEN** a project-import connection's project gains a source in a new outside folder
- **THEN** its opted-in server receives one folder-change notification before the debounced import
- **AND** removing that source removes only the outside folder when no other source needs it

### Requirement: Project-import declarations SHALL be validated independently of server startup

The configuration loader SHALL recognise a `projectImport` object containing a non-empty command and a
JSON arguments array. Invalid declarations SHALL report configuration problems without rejecting the
server entry. Valid declarations SHALL be retained for runtime dispatch.

#### Scenario: An import declaration omits its argument array

- **WHEN** an entry declares `projectImport` with a command but no arguments array
- **THEN** the loader reports the invalid `projectImport.arguments` field and retains the server entry

#### Scenario: A valid import declaration is retained

- **WHEN** an entry declares a non-empty import command and an arguments array
- **THEN** the loader recognises the declaration without an ignored-field warning

### Requirement: Project-import templates SHALL preserve JSON values

The template model SHALL substitute string values exactly equal to `{uri}` or `{text}` recursively in
objects and arrays. It SHALL preserve property names, other string values, and non-string scalar types.
Substituted values SHALL be JSON-escaped and SHALL NOT be interpreted as templates again.

#### Scenario: Unsaved project text contains a placeholder token

- **WHEN** a template's `{text}` value is expanded with project text containing `{uri}`
- **THEN** the resulting text retains that token literally, along with its original quotes and line endings

### Requirement: Declared project imports SHALL replace generated artefacts

On a registry-started connection after initialization, and on the workspace artefact service's
debounced project-change trigger, the client SHALL call each declared import command with each loaded project's saved URI and in-memory
project text. The command MUST be advertised by the server. An importing entry SHALL skip workspace
artefact generation. Reply notes SHALL reach the server log used for `window/logMessage`; object
notes SHALL use their `message` text. Failures or unavailable saved paths
SHALL be reported as configuration problems without disabling the connection.

#### Scenario: Project membership changes before saving
- **WHEN** a module is removed from a saved project's in-memory membership
- **THEN** the debounced import sends the current project serialization without writing the `.vbp`

### Requirement: Imports SHALL refresh open-document diagnostics until the server does

After a successful import, the client SHALL invalidate previous diagnostic result identifiers and
re-pull open documents. Once that connection receives `workspace/diagnostic/refresh`, the import
fallback SHALL cease for that connection alone and the ordinary server-requested refresh handler SHALL
remain responsible. The client SHALL declare `workspace.diagnostics.refreshSupport: true` during
initialization.

#### Scenario: The server starts issuing refresh requests
- **WHEN** a connected importing server requests diagnostic refresh
- **THEN** subsequent imports do not add a duplicate diagnostic pull

#### Scenario: Another server refreshes
- **WHEN** one connection requests diagnostic refresh
- **THEN** a different connection continues re-pulling open documents after its imports
### Requirement: Explicit file-operation senders SHALL respect server filters
The connection's explicit create, rename and delete notification entry points SHALL send only disk-file
URIs selected by the corresponding `workspace.fileOperations` registration filters. Filters SHALL
respect scheme, glob, file/folder kind and case options. Rename SHALL match either the old or new URI.
A missing registration or an empty matching batch SHALL send no notification.

The client SHALL advertise support for the three notifications in initialize. The registry SHALL route
file operations to every running connection whose operation filter matches, independently of document
language claims. Entries with `workspaceArtifact` SHALL receive no file-operation notifications:
HexIDE's generated artefact describes their project. An entry without `workspaceArtifact` SHALL remain
eligible for matching notifications. File operations SHALL NOT start a server.

Successful first saves and Save As writes to a new destination SHALL report creation after the bytes
reach disk. Overwriting an existing file SHALL NOT report creation. Temporary Make EXE copies SHALL NOT
report file operations. Save As retains the original, so it SHALL NOT report a rename. Explorer name
changes and removal without disk deletion SHALL NOT report file operations.

Disk rename and Remove-with-delete hooks remain outstanding (#2248): current explorer operations change
names and membership only.

#### Scenario: A server requests only Basic modules
- **GIVEN** a server registers `didCreate` with a `file` scheme and `**/*.bas` file pattern
- **WHEN** the connection is explicitly told about a created `.bas` and `.frm` file
- **THEN** one `workspace/didCreateFiles` notification contains only the `.bas` URI

#### Scenario: A server has no delete registration
- **WHEN** the connection is explicitly told about a deleted file
- **THEN** no `workspace/didDeleteFiles` notification is sent

#### Scenario: Save As creates a file
- **GIVEN** a saved module and a destination that does not exist
- **WHEN** Save As successfully writes the destination
- **THEN** matching running servers receive creation of the destination
- **AND** the original file remains and no rename is reported

#### Scenario: A first save creates a form
- **WHEN** a new form is successfully written for the first time
- **THEN** matching running servers receive creation of its disk URI
- **AND** saving it again reports no file creation

#### Scenario: HexIDE generates the server's workspace artefact
- **GIVEN** a running server with `workspaceArtifact` and matching file-operation filters
- **WHEN** a file is created, renamed or deleted
- **THEN** the server receives no file-operation notification

#### Scenario: The server owns its workspace artefact
- **GIVEN** a running server without `workspaceArtifact` and with matching file-operation filters
- **WHEN** a file is created, renamed or deleted
- **THEN** the server receives the corresponding notification

### Requirement: Cancel superseded background work before document changes

Before sending a document's didChange, the client SHALL cancel its outstanding diagnostic pulls,
full semantic-token requests and folding requests through the LSP cancellation strategy, and SHALL
send their cancellation notifications before the change notification. User requests and requests for
other documents SHALL remain outstanding. A fresh diagnostic pull SHALL follow the change; editor
refreshes SHALL request tokens and folds for the changed text.

#### Scenario: Editing while analysis is pending
- **WHEN** diagnostic, semantic-token or folding requests for a document are outstanding at a change
- **THEN** their $/cancelRequest frames precede that document's didChange
- **AND** pending hover or completion requests and requests for other documents are not cancelled
- **AND** a fresh diagnostic pull follows didChange
