## ADDED Requirements

### Requirement: The browser SHALL search the whole workspace, not only what it has loaded
Searching SHALL ask every running language server that offers workspace-wide symbol search, in addition to
filtering the libraries the browser already holds, and SHALL present those results with enough location
information to navigate to each one. Activating a result SHALL open its document at the position the server
reported.

The browser holds *containers* — libraries, modules, forms, classes. The name a developer is hunting for is
usually a procedure inside one of them, which no filtering of the container list can reach because the
browser never held it. A server that can find it has no other way to be asked.

When no result can be shown, the browser SHALL say which of these is the case: no server has started, no
running server offers the search, nothing matched, or the search failed.

That distinction is not presentation. Servers start lazily — one starts when a document of its language is
opened, because that is the first moment the language is known to be present — so a search run before any
code is open reaches nobody and comes back empty. So does a search against a server that never offered the
feature. Reporting either as "nothing matched" answers the developer's question confidently and wrongly, and
points them away from the reason.

#### Scenario: Finding a procedure the browser never loaded
- **WHEN** the developer searches for a name a running capable server knows about
- **THEN** it is listed with the document and line it was found at

#### Scenario: Opening a result
- **WHEN** the developer activates a search result
- **THEN** its document opens at the reported position

#### Scenario: Searching before anything has started
- **WHEN** the developer searches with no language server running
- **THEN** the browser says no server has started, rather than that nothing matched

#### Scenario: Searching where no server offers it
- **WHEN** every running server declines workspace-wide search
- **THEN** the browser says so, and asks none of them

#### Scenario: A search that genuinely finds nothing
- **WHEN** a capable server is asked and returns no symbols
- **THEN** the browser says nothing matched, naming the query
