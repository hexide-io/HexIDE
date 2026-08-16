# object-browser Specification

## Purpose
Define the Object Browser: a browser over every type and member visible to the project, from the project's
own code to the libraries it references.

In a language with no cross-file navigation and no import statements to read, it is how a developer finds
out what exists. That makes it more central than the equivalent in a modern IDE, where the same questions
are answered by go-to-definition and completion — here it is often the only way to discover a member at all,
which is why VB6 gave it a dedicated key.

> **Known divergences.** The requirements below state intended behaviour. Three are not currently met — the
> member list is cached and does not reflect later edits, double-click opens the containing file rather than
> the member's definition, and Escape does not close the window. Tracked in
> [#24](https://github.com/hexide-io/HexIDE/issues/24). They are stated as requirements rather than written
> down to match the code, because the code is wrong here, not the intent.

## Requirements
### Requirement: The browser SHALL show everything visible to the project
The browser SHALL present the project's own forms, modules and classes, the runtime control library, the
built-in language functions, and the types in any referenced library, and SHALL allow narrowing to one
library at a time.

The value is in the union. A developer asking "what can I call here" does not care which of those four
sources answers, and a browser covering only the project's own code would answer the easy question while
leaving the one they actually opened it for. Narrowing exists because the union is large enough to be worth
filtering once you know where you are looking.

#### Scenario: Browsing everything
- **WHEN** the browser is opened
- **THEN** types from the project, the runtime library, the built-in functions and referenced libraries are all reachable

#### Scenario: Narrowing to one library
- **WHEN** the developer selects a single library
- **THEN** only that library's types are listed

### Requirement: Selecting a type SHALL show its members, and selecting a member SHALL describe it
Selecting a type SHALL list its members; selecting a member SHALL show its signature and what it belongs to,
along with any description available for it.

The two-step shape is the whole interaction: narrow to a type, scan its members, read one. The description
is what makes the last step worth taking — a member name alone rarely answers the question, and the
signature is what the developer is about to type.

#### Scenario: Selecting a type
- **WHEN** the developer selects a type
- **THEN** its members are listed

#### Scenario: Selecting a member
- **WHEN** the developer selects a member
- **THEN** its signature and owning type are shown, with a description where one exists

### Requirement: The member list SHALL reflect the current state of the code
Members shown for a type in the project SHALL reflect that type's code as it currently stands, including
procedures added since the browser was last opened or the type last selected.

A browser that answers from a stale snapshot is worse than one that is slow, because it answers confidently
and wrongly: the developer adds a procedure, goes looking for it, does not find it, and concludes they made
a mistake somewhere else. The failure is silent and it points away from the real cause.

#### Scenario: A procedure added after the type was last viewed
- **WHEN** a procedure is added to a type and that type is selected in the browser
- **THEN** the new procedure appears in the member list

### Requirement: Double-clicking a member in the project SHALL open it at its definition
Double-clicking a member belonging to the project SHALL open its containing document and position the caret
at that member's declaration. Double-clicking a member of an external library SHALL do nothing.

Opening the file is only half the job — in a module of any size, landing at the top means the developer
still has to search for the thing they just double-clicked, which is the work they were trying to skip.
External members have no source to open, so doing nothing is the honest response.

#### Scenario: Double-clicking a member of the project
- **WHEN** the developer double-clicks a member defined in the project
- **THEN** its document opens with the caret at that member's declaration

#### Scenario: Double-clicking an external member
- **WHEN** the developer double-clicks a member from a referenced library
- **THEN** nothing happens

### Requirement: The browser SHALL support searching across libraries
The developer SHALL be able to search by substring, matching case-insensitively across every library rather
than only the one currently selected, and SHALL be able to return to browsing.

Search is used when the developer does not know where something lives — which is precisely when scoping the
search to the library they happen to have selected guarantees a miss. Searching everything is the only
behaviour that answers the question being asked.

#### Scenario: Searching for a name
- **WHEN** the developer searches for a substring
- **THEN** matches from every library are listed, regardless of which library was selected

#### Scenario: Clearing the search
- **WHEN** the search is cleared
- **THEN** the browser returns to browsing

### Requirement: The browser SHALL keep a navigation history
The browser SHALL allow moving back and forward through previous selections within the session.

Browsing is exploratory — the developer follows a type, finds it is not the one they wanted, and needs to
get back to where they were. Without history that means reconstructing the path by hand, which is enough
friction to stop them exploring.

#### Scenario: Going back
- **WHEN** the developer moves back after changing selection
- **THEN** the previous selection is restored

### Requirement: The browser SHALL close on Escape
Pressing Escape while the browser has focus SHALL close it.

It is opened to answer one question and then it is in the way. Escape is what a developer presses to dismiss
something they opened deliberately, and requiring them to reach for the close control instead adds friction
to every single use.

#### Scenario: Dismissing the browser
- **WHEN** the developer presses Escape with the browser focused
- **THEN** it closes

### Requirement: The browser SHALL be a document rather than a floating child window
The browser SHALL be presented as a document in the IDE's document area, keeping its own title and close
control at all times.

This diverges from VB6 deliberately. There, the browser was a child window inside the main frame, and
maximising it lost its title bar and close button — a limitation of the window system it was built on rather
than anything anyone designed. Presenting it as a document means the shell always owns its chrome, so the
behaviour VB6 developers worked around cannot occur.

#### Scenario: Maximising the browser
- **WHEN** the browser is maximised
- **THEN** its title and close control remain available
