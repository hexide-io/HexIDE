## RENAMED Requirements

- FROM: `### Requirement: The server SHALL parse a document once per change`
- TO: `### Requirement: The server SHALL analyse a document once per change
The server SHALL analyse a changed document once and share the tree that analysis keeps across the features
that need it, rather than parsing separately per feature. The analysis MAY parse the text again to recover,
and SHALL keep one tree from those parses for every feature to read.

Separate parses per feature can disagree if the text changes between them. A recovery parse cannot: it reads
the same text inside the same analysis.

#### Scenario: A change requiring diagnostics and symbols
- **WHEN** a document changes and both diagnostics and symbols are produced
- **THEN** both are derived from the one tree that document's analysis kept for that text

#### Scenario: SLL abandons a parse of valid code
- **WHEN** the fast SLL prediction abandons a parse, as it does on VB6's call-versus-array ambiguity
- **THEN** the text is read again with full LL, reusing the lexed tokens, and only that tree is kept

#### Scenario: A conditional block nobody can choose
- **WHEN** the parse stumbles inside a conditional-compilation block whose branch cannot be chosen
- **THEN** the text is read again with each such block's last branch, and the tree of whichever parse got
  further is kept

#### Scenario: An error in a protected line
- **WHEN** an error lands in the file's header or in a member's `Attribute` lines
- **THEN** the text is read again with those lines blanked, and that tree is kept

#### Scenario: An analysis abandoned at the parse budget
- **WHEN** a changed document's parse exceeds the parse budget
- **THEN** no tree is kept for it, and the features that read a tree go on reading the previous one
