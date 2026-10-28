# language-server Specification

## Purpose
Define the language server HexIDE ships: what it must do to serve the IDE, what it deliberately does not
do, and the licensing constraint it exists under.

Two things make this worth stating as a contract rather than leaving as whatever the current implementation
happens to do. The server is meant to be replaceable, and a replacement can only conform to something
written down — several of the behaviours the editor depends on are choices the protocol permits rather than
requires, so an implementation could be perfectly protocol-conformant and still break the editor. And the
licence constraint is not a property of the code so much as a property of everything it embeds, which is
the kind of thing that erodes silently unless it is a requirement.

## Requirements

### Requirement: The source tree SHALL be uniformly permissively licensed
The language server and every artifact it embeds SHALL be permissively licensed, and the tree SHALL NOT
contain a copyleft licence file, a copyleft-derived grammar, or test inputs derived from a copyleft
project.

The licence claim is either unconditional or it is not worth making. A single embedded dependency is
enough to change the terms of everything that links it, and the obligation travels in forms that are easy
to overlook — generated parser output, and fixtures copied from another project's test suite look like
ordinary files.

#### Scenario: Auditing the tree
- **WHEN** the tree is searched for copyleft licence files, grammars, or derived test inputs
- **THEN** none are found

#### Scenario: Adding a parsing dependency
- **WHEN** a new grammar or parsing dependency is proposed
- **THEN** it is adopted only if its licence permits redistribution under the project's own terms

### Requirement: The language server SHALL run as a replaceable external process
The server SHALL run outside the IDE process and communicate over a duplex byte stream, and the IDE SHALL
NOT link it.

Parsing is unbounded work on hostile input, so a wedged parse must not be able to wedge the editor.
Keeping the boundary also means any implementation that speaks the protocol can take its place — including
one whose licence would forbid linking — which is what makes "replaceable backend" a fact rather than an
intention.

#### Scenario: The server becomes unresponsive
- **WHEN** the server hangs or crashes while handling a request
- **THEN** the IDE continues running and editing is unaffected

#### Scenario: Substituting a different backend
- **WHEN** a different language backend is placed behind the seam
- **THEN** it serves the IDE without any change to the IDE, provided it honours the contract below

### Requirement: Syntax diagnostics SHALL name whitespace-only offending tokens
The server SHALL describe a whitespace-only offending token as `end of line` in mismatched-input,
extraneous-input and no-viable-alternative diagnostics.

#### Scenario: An expression is unfinished at the end of a line
- **WHEN** a newline is the offending token in an unfinished expression
- **THEN** the diagnostic names `end of line` rather than displaying empty quotes

### Requirement: The server SHALL publish an empty diagnostic set when a document closes
On being notified that a document closed, the server SHALL publish diagnostics for that document with an
empty set, rather than publishing nothing.

Consumers evict cached state for a document when they see its diagnostics go empty. A server that simply
stops publishing leaves them holding markers for a document that is no longer open — which surfaces as
stale squiggles rather than as an obvious protocol fault.

#### Scenario: Closing a document that had errors
- **WHEN** a document with reported errors is closed
- **THEN** the server publishes an empty diagnostic set for it
- **AND** consumers clear the state they were holding for that document

### Requirement: The server SHALL publish diagnostics on every open and change
The server SHALL publish diagnostics for every open and change notification it receives, and SHALL NOT
debounce, coalesce, or suppress a publish because the contents match the previous one.

The publish is used as a signal as well as a payload: a consumer refreshes derived state on its arrival,
not on its contents. Suppressing an identical publish is a reasonable-looking optimization that silently
stops that refresh from happening.

#### Scenario: A change that does not alter the diagnostics
- **WHEN** a document changes and the resulting diagnostics are identical to the previous set
- **THEN** the server publishes them again rather than suppressing the notification

### Requirement: The server SHALL process requests in order with respect to document changes
The server SHALL apply a document change before handling any request that arrives after it, except
completion. Completion SHALL answer without waiting for pending analysis, using the last published
per-document declaration snapshot. Once the first open starts analysis, completion SHALL offer the
static keywords and built-ins even if no declarations have been parsed yet.
Diagnostic publication and all other request ordering SHALL remain sequential.

The IDE flushes pending edits and then immediately asks a question about the edited text — formatting on
save is the clearest case. Concurrent handling would let the question be answered against the text as it
was before the edit, producing a result that is wrong in a way that looks intermittent.

#### Scenario: Requesting formatting immediately after an edit
- **WHEN** a change notification is followed by a formatting request for the same document
- **THEN** the request is answered against the changed text

#### Scenario: Completion while a change is being analysed
- **WHEN** completion arrives while a document analysis is pending
- **THEN** it returns the last published declaration snapshot without waiting for that analysis
- **AND** subsequent completion sees the new declarations after that analysis publishes

#### Scenario: Completion before the first analysis finishes
- **WHEN** completion arrives while the first document analysis is pending
- **THEN** it answers with static keywords and built-ins without waiting for declarations to be parsed

### Requirement: The server SHALL answer "nothing found" with an empty result
Where a request finds nothing, the server SHALL return an empty result rather than a protocol-level error.
A refused rename SHALL instead return `-32803 RequestFailed` with a message explaining the refusal.

The two are not interchangeable to the caller: an empty result is an ordinary outcome that the editor
absorbs silently, whereas an error is surfaced. Reporting "no definition at this position" as an error
turns a normal interaction into a visible failure.

#### Scenario: Requesting a definition where none exists
- **WHEN** a definition is requested at a position with nothing to resolve
- **THEN** an empty result is returned rather than an error

### Requirement: The server SHALL parse a document once per change
The server SHALL parse a changed document once and share the resulting tree across the features that need
it, rather than parsing separately per feature.

Diagnostics, document symbols and completion all want the same tree for the same text. Parsing once is
both faster and safer: separate parses of the same document can disagree if the text changes between them,
producing symbols that do not match the diagnostics beside them.

#### Scenario: A change requiring diagnostics and symbols
- **WHEN** a document changes and both diagnostics and symbols are produced
- **THEN** both are derived from a single parse of that text

### Requirement: Option prefixes preserve procedure parsing on syntax errors
The server SHALL retain the same procedure tree and syntax diagnostics for a malformed procedure preceded
by Option lines as for the same source without those lines, with diagnostic positions shifted accordingly.
It SHALL continue to accept an options-only module with a trailing colon separator.

#### Scenario: Option Explicit before a broken first procedure
- **WHEN** Option Explicit directly precedes a Sub containing `x = = 1` and `List0.`
- **THEN** that Sub remains in the parse tree and document symbols
- **AND** diagnostics match those without the Option line, shifted down one line

#### Scenario: Options only with a trailing separator
- **WHEN** a module contains only `Option Explicit:` or `Option Explicit: Option Base 1:`
- **THEN** no syntax diagnostics are produced

### Requirement: The server SHALL confine itself to syntactic analysis
The server SHALL operate on the syntax tree only, and SHALL NOT build a bound semantic model, resolve names
across documents, or perform project-wide analysis.

This is a scope boundary rather than a limitation to be lifted later. Producing a semantic model is the job
of a real language engine behind the replaceable seam; a half-built one here would be a compiler frontend
nobody committed to maintaining, and it would make the seam harder to hand over rather than easier.

#### Scenario: A feature that needs cross-document resolution
- **WHEN** a proposed feature requires resolving a name defined in another document
- **THEN** it is out of scope for this server and belongs behind the replaceable seam

#### Scenario: Checking for undeclared variables
- **WHEN** the undeclared-variable check runs without a symbol table covering intrinsics, controls and project types
- **THEN** it is disabled by default, because it cannot distinguish an undeclared variable from a name it simply cannot see

The retained scope analyzer SHALL exclude the names collected from the current form's designer header,
the class and module names explicitly listed by its single owning `.vbp`, and its fixed built-in list
(including `vbPixels`, #54). This baseline SHALL NOT bind references across files or enable the live
check. Project names are supplied only when the project lookup is unambiguous and its compilation
arguments are valid; a document with no owning project has no project baseline.

#### Scenario: A control beside a real undeclared name
- **WHEN** a form's header declares `Timer0` and `Label2`, and its code uses them alongside an undeclared name
- **THEN** the directly invoked analyzer excludes those controls and still reports the undeclared name

### Requirement: The server SHALL advertise exactly the capabilities it implements
The bundled server's `initialize` result SHALL declare every feature it has a live handler for, and SHALL
NOT declare any feature it does not. Both directions are the same defect: understating leaves a
capability-respecting client with nothing to call, and overstating invites requests that nothing answers.

This is the companion half of the client's capability gate, and it SHALL land before that gate does. A gate
applied to a server that advertises nothing blacks out every feature, so the ordering is a correctness
requirement rather than a preference.

Full document synchronization SHALL be advertised, and a ranged content change SHALL be refused rather than
mis-applied. Refusal SHALL evict the affected document, because leaving a stale buffer in place is what
turns the refusal into a destructive write: whole-document formatting computed from a fragment returns an
edit spanning the real file.

#### Scenario: A capability-respecting client connects
- **WHEN** a client reads the server's `initialize` result
- **THEN** every feature the server implements is named in it, in a shape the protocol permits
- **AND** no feature the server does not implement is named at all

#### Scenario: A feature is implemented without being advertised
- **WHEN** a handler is registered for a request the capabilities do not declare
- **THEN** the capability assertions fail the build
- **AND** the omission is corrected rather than the assertion relaxed

#### Scenario: A client sends a ranged content change anyway
- **WHEN** a `textDocument/didChange` notification carries a change with a range
- **THEN** the change is refused and the document is evicted from the server's store
- **AND** the refusal is reported rather than left indistinguishable from a document with nothing wrong

#### Scenario: The shipped binary is stale
- **WHEN** the packaged executable is driven over its real transport
- **THEN** its advertised capabilities are asserted against the same set
- **AND** a binary predating the advertisement fails rather than answering with an empty capability object

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

### Requirement: The server SHALL accept a whole VB6 file, and SHALL leave its header alone
The server SHALL accept a document whose text is a complete VB6 file: a class's `VERSION` and `BEGIN … END`
block, a form's `VERSION` line, object references and designer block, the run of `Attribute` lines that opens
the code, and `Attribute` lines describing a member further down. It SHALL report no diagnostic inside any of
those, and no formatting, rename or highlight answer it gives SHALL change or point inside them, with one
exception: renaming a member SHALL change that member's own name where it qualifies one of its attribute lines
(`Attribute Total.VB_Description`), and nothing else on that line. A highlight asked for from a
position inside any of those lines SHALL have no answer. A rename there, of a reserved keyword, or of a name
the designer block declares (the form's own, a control's or a menu's) SHALL be refused with
`-32803 RequestFailed` and an explanation. So SHALL a rename to a new name that
is not a VB6 name. A header the server cannot parse SHALL NOT stop it analysing the code after the header.

The client now sends the file rather than the code, because that is the only way a position means the same
thing to the editor, to a server reading the file from disk, and to anything reading it afterwards. A server
that reported the designer block as a syntax error would bury every real diagnostic in a form under hundreds
of false ones.

The formatter is the sharper case. An answer that reformatted the header would rewrite the file's layout on
every save: VB6 indents a designer block by three spaces, and the header carries meaning at column zero. An
answer of one edit spanning the document would point inside the header even if its text left the header
alone, so the bundled server answers with an edit for each run of lines it changes. The client discards edits
that fall inside a protected region, and this requirement stops the server producing them in the first place,
so that a replacement backend is held to the same rule rather than relying on the client to repair it.

Rename and highlight are lexical in the bundled server, matching whole words across the document. With the
designer block in view, a developer renaming a local called `Text`, `Top` or `Caption` would otherwise be
offered edits inside the layout. The exception for a member's own qualifier is the code editor's rule that a
member's attribute lines follow its rename, whether the IDE or a language server makes it: left behind,
`Attribute Total.VB_Description` describes a member that no longer exists. A member's own lines are the run VB6
gives that member, which for a procedure is the run under its declaration whatever its qualifiers name, so a run
naming `Total` under another procedure is that procedure's, and renaming `Total` leaves it alone.

A new name that is not a VB6 name, a letter followed by letters, digits and underscores, would write text that
does not compile wherever the name stands, and `Grand Total` would split one name into two. Nor is one of the six
words VB6 was measured to refuse as a name though each has a name's shape, `Rem`, `Empty`, `New`, `Nothing`, `Me`
and `Attribute`:
renamed `Rem`, a function's `Rem = mSum` would be a comment. The code editor refuses such a name before it asks; a
client that does not is answered with nothing.

Keeping off the header makes a declared name the one rename that has to be declined outright. Its
declaration is the `Begin` line in the designer block, so a rename that kept off the header would rename every
reference in the code and not the control, and the code would name a control that no longer exists. A lexical
rename cannot tell such a reference from a local that happens to share the control's name, so both are
declined: refusing that rare rename is the price of never breaking the common one. A control is renamed in the
Properties window, as in VB6.

The last clause is not implied by the others. A parser recovering from a designer line it cannot read can
consume the code after it, so a server that merely withheld the header's errors would report nothing for the
whole file, the code's real errors included, and give no reason why.

#### Scenario: Opening a form
- **WHEN** a form's whole file is opened, designer block and all
- **THEN** no diagnostic is reported against any line of the header

#### Scenario: Opening a form whose designer block cannot be parsed
- **GIVEN** a form with a designer line the server cannot parse, and a syntax error in its code
- **WHEN** the form's whole file is opened
- **THEN** the syntax error in the code is reported, and nothing in the header is

#### Scenario: Formatting a class
- **WHEN** formatting is requested for a class whose file begins with the standard class header
- **THEN** the answer leaves every line of that header exactly as it was

#### Scenario: Renaming an identifier that also appears in the layout
- **GIVEN** a form whose designer block sets a property, such as `Caption`, and a local variable of that name
- **WHEN** the local is renamed
- **THEN** no edit in the answer falls inside the designer block

#### Scenario: Renaming a control from its code
- **GIVEN** a form with a control, and code that refers to it by name
- **WHEN** a rename is asked for on that name in the code
- **THEN** the rename returns `-32803 RequestFailed` with a reason

#### Scenario: Renaming a member that has a description
- **GIVEN** a property followed by an `Attribute <Name>.VB_Description` line
- **WHEN** the property is renamed from its declaration
- **THEN** the answer changes the name that qualifies the attribute line, and nothing else on that line

#### Scenario: Renaming to a name VB6 does not accept
- **WHEN** a rename is asked for with a new name that does not start with a letter, holds a character other than
  a letter, a digit or an underscore, or is `Rem`, `Empty`, `New`, `Nothing`, `Me` or `Attribute` in any case
- **THEN** the rename returns `-32803 RequestFailed` with a reason

### Requirement: Physical line endings

The server SHALL count CR, LF and CRLF as line ends in every position it reads or sends, including files
with mixed endings. Formatting and protected-region blanking SHALL preserve the original terminators.

#### Scenario: A diagnostic below a lone CR
- **WHEN** an opened document contains a syntax error after a lone CR
- **THEN** the published diagnostic uses the same zero-based physical line and character as the editor

#### Scenario: Protected regions in a file with lone CR endings
- **WHEN** a whole file uses lone CR line endings
- **THEN** formatting, rename, highlight and diagnostics respect the same header and member-attribute lines as the IDE

### Requirement: Completion SHALL preserve declaration kinds and unique labels
The server SHALL return completion labels unique under ordinal case-insensitive comparison, preferring a
name declared in the document over a static keyword or built-in. The static `Date` entry SHALL be a
Function. Declared names SHALL carry their syntactic completion kind: Sub as Method, Function as Function,
Property as Property, Const as Constant, Enum as Enum, enum member as EnumMember, Type as Struct, and
variables and parameters as Variable.

#### Scenario: Completing a procedure and a variable
- **WHEN** completion is requested for a document declaring a Sub and a variable
- **THEN** the Sub has completion kind Method (2) and the variable has kind Variable (6)

#### Scenario: A declaration shadows a static entry
- **WHEN** a declared name matches a static entry ignoring case
- **THEN** one item is returned with the declaration's spelling and kind

#### Scenario: Completing Date without a declaration
- **WHEN** completion is requested for a document without a Date declaration
- **THEN** Date appears once, with completion kind Function (3)

### Requirement: Symbol and scope visitors SHALL bound parse-tree recursion
The symbol, declaration and Option Explicit visitors SHALL stop descending at 500 active
`VisitChildren` calls, so a left-associated expression cannot overflow their call stacks.
Traversal SHALL continue with subsequent statements after a bounded subtree is skipped.

#### Scenario: A long addition chain below the source-size gate
- **WHEN** a parsed module contains 100,000 additions followed by declarations and another procedure
- **THEN** symbol and declaration collection completes and retains the later declarations and procedure
- **AND** Option Explicit checking can still report an undeclared reference in a later statement
