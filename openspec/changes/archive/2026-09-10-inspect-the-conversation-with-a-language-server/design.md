# Design record

Settled with the maintainer across an interview, against a research pass over the codebase, the protocol
specification and eight editors' implementations. Recorded here because the reasoning is worth more than the
conclusions: most of these decisions look arbitrary until you know what was measured.

## Who it is for

**Language-server authors first.** Users and contributors are served as a consequence.

The connection registry already answers the user's question. It records what is attached, how far each
connection's last attempt got, what each advertised, and what this client declined to ask for. What nothing
answers is *what actually went over the wire*, which is the author's question.

It is also the strategic answer. An inspector that makes HexIDE the best place to **develop** a VB6 language
server is a stronger pull than one that helps us debug ourselves.

**It ships in Release, with capture off by default.** The two in-tree precedents point opposite ways: the
automation server is specified as absent from distributed builds, while the debug proxy ships
unconditionally. This follows the proxy, because the audience above needs it in the binary they downloaded
rather than one they compile. Off by default means a user who wants none of it pays nothing.

## What is recorded

**Envelopes always, payloads only when armed.** An envelope is method, direction, id, timestamp, size,
outcome and latency — no user content whatsoever, which is what makes always-on defensible. Payload arming
is the entire disclosure boundary.

A consequence worth stating because it can be built backwards: the tap is installed at every connect,
unconditionally. It cannot be attached at arming time.

**Latency is in version one** and is nearly free, because the tap already pairs responses by id in order to
match them at all. For an author it is the difference between a bug report and an anecdote.

**Bytes are captured and decoded messages are presented**, with the raw body one action away. "Raw" means
the byte-exact JSON body, not the framed message. No tap above the transport sees `Content-Length`; only a
stream tee does, and only for stdio and pipe, never WebSocket. Measurement makes that an easy trade: across
roughly three hundred inbound frames from five servers on four different protocol libraries, every frame
carried exactly one header line, and the reference implementation no longer even declares a content-type
constant. The defect class this project has already paid for is a body question — whether `params` arrived
as `[]`, `{}`, or not at all.

**Four things share the timeline that are not JSON-RPC messages:**

- **Process lifecycle and standard error.** A stdio server must not write to stdout, so standard error is
  its only channel for a crash stack, and today that reaches nowhere a user can see. Exit codes belong here
  for a sharper reason than first assumed: **nothing in this tree reads a language server's exit code at
  all**, verified by grep, and the transport kills the process without looking. The protocol has a server
  exit 0 only when a shutdown preceded exit, which is what made a defect present as every clean exit looking
  like a crash to a supervisor. A signal nobody currently reads is exactly the kind that belongs in a
  timeline. Measurement supports the merge too: when texlab and clangd genuinely failed, the human-readable
  cause went only to standard error and to a JSON-RPC error, and nothing at all went to `window/*`.
- **Requests the client declined to send.** A refusal is invisible and looks identical to a broken feature.
  Free, because each client already keeps its declined list.
- **Capabilities the server advertised that this client does not consume.** The inverse of the above, free
  at handshake time, and the half an author most wants: a list of what they have offered that HexIDE is not
  yet taking.
- **For transports HexIDE did not spawn**, a line saying what cannot be observed. Two shapes, not one: a
  named pipe loses lifecycle and standard error but keeps frames, while a WebSocket has no stream at all.

This merge is the **minority position** in the field. Several mainstream clients keep wire traffic,
`window/logMessage` and standard error in separate channels. That is the right default for a user and the
wrong one for an author chasing a failure across all three.

## The server's own tracing

Client tap and server tracing are both version-one content, because a partner's trace work is imminent.

**Measured, that channel is currently empty.** Five servers driven with `trace: "verbose"` at initialize
*and* an explicit `$/setTrace` produced zero `$/logTrace` frames and zero telemetry, with exactly one
`window/logMessage` in the whole exercise, a startup banner. Three of the binaries contain no trace
machinery at all; a fourth's framework implements it and the server never calls it.

Two consequences, and neither reopens the decision:

- **The bundled server implements `$/logTrace` as part of the prerequisite change.** Otherwise the client
  half ships unexercised against everything in reach. Its framework already carries the parameter types and
  lacks only the dispatch, so this is cheap, and it makes HexIDE's own server a reference implementation of
  the thing the partner is about to build.
- **A trace control cannot honestly report a refusal.** `$/setTrace` is a notification with no response, and
  the protocol defines no capability by which a client can learn whether a server honours trace. The only
  observable signal is negative. The wording is therefore "asked, and nothing has arrived", never
  "declined".

Trace level lives in two places because it answers two moments. What a server *starts* at belongs in the
configuration file, since it travels in initialize and must be known before the process exists. Changing it
afterwards is what `$/setTrace` is for.

## Lifetime, arming and cost

**Nothing persists. Export is how a capture is kept.** Writing source text to disk continuously is a
privacy decision taken on the user's behalf without them asking.

**Arming is per server**, visible from the connection list, session-scoped, and reset by an IDE restart. A
capture that dies with the session while its arming survives is incoherent, and a surviving flag quietly
converts off-by-default into off-until-once.

**But the handshake is captured unconditionally.** Servers start lazily, so arming after the fact provably
cannot see `initialize`, and four of the costliest defects in this project's record live in or immediately
after it — one turning entirely on the *content* of the initialize result, which an envelope does not carry.
The cost is bounded and tiny: one request and one response per server, once per process. A configuration
field covers the wider case of wanting a cold start captured in full.

**Within a session, arming and trace level belong to the connection, not the process.** A crash is what you
armed capture to watch, so both survive it and are re-applied to the respawned server.

**Ring sizes, from measurement rather than intuition.** One keystroke is eight frames, about eighteen frames
a second while typing. Frame sizes span four orders of magnitude, so a message-count cap on payloads swings
memory several-fold at one setting. And the worst case is not the bundled server: over an identical
twenty-edit script, clangd returned roughly a hundred times rumdl's inbound bytes, almost entirely
completion responses.

| | Cap by | Default |
|---|---|---|
| Envelopes | entries, per connection | 25,000, roughly 4 to 5 MB |
| Payloads | bytes, per connection, under a global ceiling | 16 MB |
| A single frame | bytes, head and tail retained | 64 KB, true length recorded |

Per-connection payload budgets rather than one pool, because otherwise the noisy server silently evicts the
quiet one's history and a single drop count reports a loss nobody can attribute.

**The handshake is pinned.** Drop-oldest evicts `initialize` first, which is the one thing that must never
go. A reserved, non-evicting prologue per connection fixes it.

**Overflow drops oldest, visibly, with a count.** A record that truncates silently reads exactly like a
complete one.

Two cheap riders: deduplicate `didChange` bodies by content hash, since flushes on open-paren and comma
re-send text identical to the change that just went out; and state the true length wherever a frame is
truncated, a precedent the automation driver already sets.

**All of these limits are configurable.** Per-server overrides in the language-server configuration file,
because a budget is exactly the thing you raise for one noisy server and not the rest. Hand-edited values
are clamped to a stated floor and ceiling, and a corrected value is reported through the
configuration-problems channel that already exists and is already rendered. An Options page comes later; it
is not free, since every label there is a key across thirty language packs.

**Global defaults live in that same file rather than in the application settings file, which reverses what
this record said and is worth explaining.** The requirement above is that a correction reaches somebody, and
the channel named is the one belonging to the language-server configuration: validated on load, and already
rendered in the servers window. The settings file has no problems list and nothing that would show one, so a
mistyped limit there would be clamped in silence — which is the exact failure the reporting exists to
prevent, and it is worse than either accepting or refusing the value. Moving the global tier once Options
gains a page is cheap. Shipping a silent clamp is not.

A shared ceiling written inside one server's entry is reported rather than obeyed. One server's
configuration must not decide what every other server is allowed to cost, and ignoring it quietly would
leave a user believing they had raised something they had not.

## Redaction

**One control, governing egress rather than display.** The live view is for the person who owns the files
and needs no protection. Redacting it would also break the raw-body affordance, which is the only thing that
proves what actually crossed the wire.

**Consistent pseudonymisation, never removal.** Replacing every path with a placeholder destroys the exact
defect class this project has already paid for: a client sending one drive-letter case and a server echoing
another is invisible once both become the same token. Stable pseudonyms keep normalisation defects
diagnosable after redaction. A redactor that makes traces safe and useless gets turned off.

**The surface is enumerable, not a sweep.** VB6 forms and modules ride an opaque scheme carrying only a
component name, so the primary editor's traffic is already path-free. Real paths reach the wire through
three places: the handshake's root URI, its workspace folders, and carried files.

**Server launch configuration is its own category.** Command, arguments, working directory, endpoint and
pipe name are user-authored, and command-line arguments are where a token or an internal hostname lives. No
content-agnostic redactor would recognise one.

**The redacted payload is shown before it leaves.** Every other outbound gate in this tree works that way,
and a preview is the only way somebody catches a secret sitting in a string literal. Disclosure is sized in
terms a person can weigh, so "forty-seven copies of Form1, two megabytes of your source" rather than a
message count.

This is the project's **first** redaction primitive. Two truncations exist and both disclaim being
sanitisation. It belongs in a named, tested component, because a crash reporter, the automation driver and a
future debug-adapter capture carrying live variable values will all want it.

### How a pseudonym is made

**Words from a fixed pool, permuted per session.** Numbered placeholders were the obvious first answer and
are the worst possible thing to ask a person to compare: `path-17` and `path-71` look alike, sort adjacent
and carry no shape. Words are distinguishable at a glance and memorable for exactly as long as a
session-scoped pseudonym needs to be. That they are faintly ridiculous is load-bearing rather than a joke —
nobody mistakes `hx-grumpy-toad` for something that was really on the wire.

The pool is a little over a thousand words, sized to exceed the distinct paths in most codebases. Past the
last word an index is written in base *pool size* and each digit draws a word, so the scheme never wraps
onto a name already in use and a very large project reads as `hx-toad-grumpy-cat` rather than as a failure.

**The pool's order is public and carries nothing.** Each session draws a full Fisher-Yates permutation from
the system's cryptographic generator and never writes it anywhere, so the mapping is deterministic while
the IDE runs and irrecoverable afterwards. A multiply-and-add scramble over the index was considered and
rejected: it is a permutation, but a linear one, so anybody holding the pool file and two names could
recover the stride and read off the rest.

**Case is projected, not folded.** The mapping is keyed on the case-folded value and the original's case
shape is applied to the name, so two spellings of one path come back as the same word capitalised
differently — visibly the same place, visibly not the same string. Three shapes are representable; a value
whose casing is none of them takes a suffix rather than colliding with its neighbour, because an ugly name
is a far smaller problem than a reader believing two strings were one.

**An address is rewritten in place, never rebuilt from a parsed URI.** Measured after the fact, and worth
recording because it was wrong in the first implementation: handing the whole string to a URI parser and
reassembling the answer from its parts produced two misdescriptions rather than a leak. A Windows pipe path
parses as an absolute URI, so it came back as a `file:` URI nobody had written, with its separators flipped;
and a URI with no port came back carrying the scheme's default, stating a choice the user had not made. A
diagnostic record that misdescribes what was configured is worse than one that says less, because a reader
cannot tell the redaction from a configuration mistake. So only the parts that name something are replaced,
and the scheme's spelling, the presence or absence of a port and the separators survive as typed.

A host that names nothing survives too: loopback, because hiding it would make every local server look
remote, and a wildcard bind, because accepting a connection on every interface is a material fact about how
a server was reached rather than anybody's name. Credentials in an address are replaced rather than dropped
— the parsed form discarded them silently, which is safe and is also a record claiming there were none.

**A drive letter and a file extension are kept verbatim.** Neither is somebody's name, and both are
load-bearing: drive-letter case is the most expensive normalisation defect in this project's record, and
routing is by extension, so `.cls` against `.frm` is a diagnosis. A prefix marks every pseudonym, chosen
from characters legal in a URI, a path segment and a shell argument — angle brackets were the first idea
and were dropped for exactly that reason, since `file:///<toad>/x` is not a URI and an export whose point
is that it replays would stop replaying.

## Export

**Raw JSON-RPC, one message per line, plus a manifest.** It needs no decoder, it greps and it pipes, it
survives a message HexIDE could not decode — which is exactly when an export matters — and it replays
straight into this project's own pipe-pair tests.

Rejected: the archived Microsoft LSP Inspector's envelope, because the viewer no longer exists and its
published format and its reference implementation disagree about the message-type strings, so we would
inherit an inconsistency that later reads as a HexIDE defect. Also rejected: a SQLite schema whose own
authors decline to stabilise it.

Borrowed: the Visual Studio Code **text** trace shape for the copy-one-message action. It is the lingua
franca an author recognises on sight, and it costs a format string.

## The surface

**A new document tab named Protocol Inspector**, tabbed alongside the Language & Debug Servers window and
linked from it.

**One interleaved timeline with the server as a filter**, not one server at a time. This IDE routes a
document to every server that claims it and merges the answers, so "which of you answered, and was the other
even asked" is a native question here in a way it is not in editors that assume one server per language. A
channel model cannot express it.

**A grid, not a log.** Sortable columns for time, direction, server, method, latency and size are most of
the value for an author, and the copy and export actions already cover what a text shape would buy. The
localisation delta is bounded to roughly a dozen keys, because the existing rule already exempts verbatim
machine text, which is most of what this window shows. `DataGrid` is pinned, themed, licence-recorded and
already virtualising hundreds of rows in production; `TreeDataGrid` is a commercial product that fails the
build and is absent from the licence table, so it is refused twice over.

**Failures are marked in place with an unmissable count**, not split into a second view. A failure's meaning
is almost always in what preceded it, and connection-level failure already has its own surface.

**It never opens itself**, but is one action from where trouble is reported. A window that appears uninvited
is one people learn to close reflexively.

### Approaches considered and rejected

**An Output window with selectable channels.** Rejected on four grounds. Visual Studio, the reference for
this shape, does not do it for language servers at all — it writes a file to temp and shows nothing in the
IDE, which is the same off-to-the-side shape the existing proxy already has. Visual Studio Code does use
channels, and Microsoft then had to ship a *separate* inspector to get pairing and columns, since archived
with nothing in-box replacing it in any editor surveyed. A channel is one stream at a time, which cannot
express the merged-routing question above. And an LSP channel would reverse two recorded decisions here:
that `window/logMessage` is never put in front of anyone, and that `showMessage` goes to the status bar so a
voluble server cannot make the IDE unusable.

If an Output window is ever built, the arguments for it are elsewhere: unparseable compiler output currently
goes into a modal dialog, and the application log has no in-IDE viewer at all.

**A tool-dock panel.** Reasonable, and rejected once it was established that the document region splits
today. The content is wide and column-shaped rather than a glanceable strip, the region gives it most of the
window, and the project's own precedent puts non-VB6 panels there.

**A second view inside the Language & Debug Servers window.** Rejected on that window's shape rather than in
principle. Its root is one scroll view over a stack panel, it has no selection concept anywhere, its rows
carry no change notification, and it rebuilds every row wholesale on each connection event — deliberately,
because a connection is a value and patching one in place is how a view ends up showing a state and a
capability set that never coexisted. A live timeline inside it would need its root rewritten and selection
invented, and the rebuild would fight a running capture.

That window does gain the arming controls, which is its first interactivity of any kind. Arming state is
held in a service keyed by connection id, so it survives the wholesale rebuild by construction rather than
by being patched, leaving the documented refresh model alone.

## Automation is a co-primary consumer

The capture is a data structure with a contract; the window is one renderer of it. That is a real cost, paid
because the development loop here is the tightest feedback path this project has, and a capture only a human
can read undoes the reason for building it.

The envelope-versus-payload split is already the two-tier shape such a surface needs: list envelopes
cheaply, fetch one payload by id. Dumping a conversation whole would flood a context window.

A launch flag arms capture at start, because the documented rebuild cycle restarts the IDE every iteration
and session-scoped arming would otherwise be lost on each one.

**The tools are compiled out of Release and the capture is not.** The automation server is absent from
distributed builds while the capture ships, so the capture service must live outside the server's folder
rather than beside the tools that read it.

## Scope held back

**Read-only.** Composing or replaying a request is a second feature with its own editor, its own validation
and its own ways to wedge a server. The record is shaped so replay is a later addition rather than a
rewrite. Worth knowing that **none** of the eight editors surveyed offers it, so it would be a
differentiator rather than catching up.

**`$/progress` is shown, not implemented.** Measured rather than assumed: HexIDE declares no `window`
client capability at all and answers `window/workDoneProgress/create` with method-not-found, so one of the
four fixtured servers sends progress anyway and ignores the refusal, and a second starts sending it the
moment the capability is declared. Nothing here handles any of it. The inspector's job is to report what
happened, and "the server asked to show you progress, was refused, and told you anyway" is exactly the
finding it exists to produce. Let it make the case with evidence, then file it — which it now has, as
hexide-io/HexIDE#360.

**Protocol-agnostic model, LSP-only content.** Both protocols are JSON-RPC over a stream, so envelope,
correlation and transport concerns are genuinely shared rather than speculatively abstracted. HexIDE's own
automation traffic is explicitly out of scope.

## Open questions

Everything above is settled. This section exists because a record of forty-one decisions and no doubts reads
as a record of a design that had none, and the next person to open it would rediscover these from scratch —
most of them surfaced within minutes of the first sketch of the window.

None of these blocks the capture model or the automation surface. All of them are phase four's, and each is
answerable by looking at a real timeline rather than by reasoning, which is the honest reason they are not
decided here.

**Where the detail pane goes.** A pane below the grid keeps a selected message and its neighbours in view at
once, which is how most of the value is read: a request, its answer, and the thing that came in between. It
also halves an already-wide grid. A pane that replaces the grid gives a large body the room it needs and
loses the context that made the row worth opening. There is a third shape — the body in its own document tab
— which is the only one that survives a body larger than the window, and the only one that lets two
messages be compared side by side.

**Whether a drop is marked inline as well as in the header.** The header carries a count, because a record
that truncates silently reads exactly like a complete one. Whether the gap is also marked *at the point it
happened* is a different question: inline, it says which part of the conversation is missing rather than
merely that some of it is; equally, an eviction is oldest-first, so on a busy connection the marker would
sit permanently at the top of the list and say nothing a reader does not already know.

**What the detail pane shows for a body that has been evicted.** Envelopes outlive payloads by design, so a
row whose body is gone is a normal state and not an error — the row is still worth selecting for its method,
latency and size. It needs wording that distinguishes three cases a reader will otherwise conflate: nothing
was kept because the connection was not armed, something was kept and has since been evicted, and the frame
was truncated with its true length recorded. A single "not available" collapses all three and invites the
bug report that the capture is broken.

**Whether the server column earns its width.** The interleaved timeline is the point, so the column is
load-bearing while more than one server is in view. Filtered to one, it is the same value on every row.
Hiding it automatically is a grid that rearranges itself under the reader, which is its own cost.

**Where the opt-out from pseudonymisation lives, and how loudly it declares itself.** The decision that it
exists is settled and it is strictly non-default. Its surface is not: Options has no page for any of this
yet, and a flag that makes real paths and launch arguments leave the machine needs to be unmissable both
where it is set and in anything exported while it is on. An export that does not carry its own redaction
state is worse than one that was never redacted, because the reader cannot tell which they are holding.

## Order

Five changes. The automation tools land **before** the window, so the capture can be driven and verified
before any interface exists — which is how everything else here has been checked — and so the differentiator
is usable a change earlier than the panel is.

1. The protocol prerequisite, including `$/logTrace` in the bundled server.
2. The capture model, with no user interface.
3. The automation tools and the launch flag.
4. The window, and the servers window's first controls.
5. Retiring the proxy.
