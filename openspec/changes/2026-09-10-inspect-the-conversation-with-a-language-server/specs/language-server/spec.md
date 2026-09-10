## ADDED Requirements

### Requirement: The server SHALL emit its own trace, at a level the client sets
The server SHALL read a trace level from the `trace` member of `initialize` params, SHALL treat an absent
member as `off`, and SHALL accept a later `$/setTrace` notification as changing that level for the rest of
the connection. It SHALL emit its trace as `$/logTrace` notifications, and SHALL include the `verbose`
member only when the level is `verbose`.

Measured against five third-party servers, this channel is empty in practice: `trace: "verbose"` at
initialize plus an explicit `$/setTrace` produced zero `$/logTrace` frames between them. A client that
handles trace therefore has nothing in reach to prove itself against, so the bundled server is the
reference implementation — which makes conformance here a contract rather than a courtesy.

#### Scenario: A client that never asks for a trace
- **WHEN** `initialize` carries no `trace` member, or carries `"off"`
- **THEN** no `$/logTrace` notification is sent, and no work is done to produce one

#### Scenario: A client asking for summaries
- **WHEN** the level is `messages` and a document is analysed
- **THEN** one `$/logTrace` is sent carrying a `message` and no `verbose` member

#### Scenario: A client asking for detail
- **WHEN** the level is `verbose` and a document is analysed
- **THEN** one `$/logTrace` is sent carrying the same summary in `message`, plus detail in `verbose`

#### Scenario: Changing the level on a running server
- **WHEN** `$/setTrace` arrives with a recognised value
- **THEN** every subsequent trace is emitted at that level

#### Scenario: A value the server does not recognise
- **WHEN** `$/setTrace` arrives with a value that is not `off`, `messages` or `verbose`
- **THEN** the value is ignored, the current level is unchanged, and the refusal is logged

### Requirement: The server's trace SHALL carry what the client cannot observe
The trace SHALL report facts internal to the analysis — which prediction stage produced the parse tree,
whether the wall-clock parse budget expired and previous results were kept, how long the parse took, and
what came out of it — and SHALL NOT restate method names, payloads or elapsed times that a client capturing
its own traffic already has.

A trace that echoes the wire proves the notification works and teaches nothing. The question a server's own
trace exists to answer is why an analysis cost what it did, and the two-stage SLL→LL prediction strategy is
the single largest determinant of that — invisible from outside the process, and until now reported to
nobody.

#### Scenario: An analysis answered by the fast path
- **WHEN** the SLL stage parses a document successfully
- **THEN** the trace names that stage, the time taken, and the diagnostic and symbol counts

#### Scenario: An analysis that fell back
- **WHEN** the SLL stage bails and the authoritative LL(*) re-parse produces the tree
- **THEN** the trace names the fallback, which is why the analysis cost what it did

#### Scenario: An analysis abandoned on the clock
- **WHEN** a parse exceeds the wall-clock budget and previously published results are kept
- **THEN** the trace says so, rather than leaving an unchanged diagnostic set unexplained

#### Scenario: A document refused rather than analysed
- **WHEN** the server refuses a ranged content change and evicts the document
- **THEN** the trace says so, because on the wire that refusal is an empty diagnostic array and is
  otherwise indistinguishable from a document with nothing wrong

### Requirement: Tracing SHALL cost nothing when it is off
When the trace level is `off` the server SHALL behave exactly as it did before tracing existed: it SHALL
NOT allocate a parse report, read a clock, or send any additional notification.

Diagnostic machinery that taxes the default path is machinery that gets turned off wholesale later. The
level is therefore consulted before the work that would feed a trace line, not after.

#### Scenario: Analysing a document with tracing off
- **WHEN** a document is opened or changed while the level is `off`
- **THEN** the parse runs exactly as it does without tracing, and nothing is measured or emitted
