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
deadline, and the client SHALL attempt `exit` whether shutdown answers, fails or times out. Both messages
SHALL omit the `params` member. After killing a remaining child, the stdio transport SHALL wait up to
two seconds for termination before disposing the process handle.

An orphaned server process outliving the IDE would hold file handles and consume memory with nothing to
serve. Asking first and terminating second means a well-behaved server exits cleanly while a wedged one
still goes away.

#### Scenario: Shutting down the IDE
- **WHEN** the IDE shuts down
- **THEN** the server is asked to shut down and its process is ended
- **AND** no server process outlives the IDE

#### Scenario: A server never answers shutdown
- **WHEN** the server receives `shutdown` but never answers it
- **THEN** the client stops waiting after two seconds and attempts `exit`
- **AND** it disposes the connection and transport, killing any remaining child with a bounded exit wait

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
and list-shaped results SHALL be concatenated.

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

The client SHALL ask when a document is opened, after it changes, and when it is saved. It SHALL NOT
introduce pacing of its own for this: the edit stream is already coalesced before it reaches the client, and
a second authority over the same question would answer it differently.

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
While the client reads a server's fold by its lines alone, it SHALL declare `lineFoldingOnly`. The change that
makes it read a fold's characters SHALL remove the declaration.

A server told nothing may end a fold part-way along its closing line, and a fold read by whole lines then hides
that line (hexide-io/HexIDE#1363).

#### Scenario: A block's fold from a server that honours the declaration
- **WHEN** a server that honours `lineFoldingOnly` is asked for the folds of a block whose closing line stands
  alone
- **THEN** the block's fold ends on the line before, so the closing line stays visible when the fold is collapsed

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
