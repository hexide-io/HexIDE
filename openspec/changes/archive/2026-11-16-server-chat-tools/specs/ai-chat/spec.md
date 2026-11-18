## ADDED Requirements

### Requirement: Language servers' tools SHALL come only from servers the IDE runs for the developer
The assistant SHALL offer tools and a context fragment only from a language server the IDE runs for the
developer (its own, one in the developer's configuration, or one a consented add-in contributed) that declares
`experimental.hexide.chatTools`, and only to the IDE's own add-ins. Each request SHALL go only to the server
that named its method. Each tool SHALL be offered under its server's name, and its results and the context
SHALL be labelled as the server's.

#### Scenario: Two servers
- **WHEN** two servers offer tools and the assistant calls one of the second server's
- **THEN** the request goes to the second server only

#### Scenario: A registration made another way
- **WHEN** a server declares the capability but its registration was not made from a configuration entry or a
  consented add-in
- **THEN** it is asked for nothing

#### Scenario: An add-in that is not the IDE's own
- **WHEN** a third-party add-in asks for language servers' tools
- **THEN** it is given none, and cannot call one

### Requirement: The developer SHALL choose, per server, whether the assistant uses its tools
The IDE SHALL let the developer choose, for each server that can offer tools, between following its entry,
using its tools and asking unless a tool only reads, asking before every call, and not using them. The
developer's choice SHALL win over the server's entry, including an entry an add-in wrote, and SHALL NOT turn on
a server whose route cannot offer tools.

#### Scenario: An add-in's server the developer sets to ask
- **WHEN** an add-in's server says its tools may run as their hints say, and the developer chooses to be asked
  before every call
- **THEN** every call to that server's tools asks first

### Requirement: The IDE SHALL decide whether a server's tool asks before it runs
The IDE SHALL decide whether a call asks from the server's own listing at the time of the call, not from
anything the caller passes, and SHALL ask the developer first unless the tool says it only reads and does not
say it runs code. A server set to `ask` SHALL ask for every call and SHALL give no context.

A server's tools are other people's code acting on the developer's behalf, and the method a server names for a
call is its own choice. Deciding in the host, from the server's listing, keeps a caller or a method name from
skipping the question.

#### Scenario: A tool that changes something
- **WHEN** the assistant calls a server tool that doesn't say it only reads
- **THEN** the developer is asked first, and nothing reaches the server unless they approve

#### Scenario: A tool that only reads
- **WHEN** the assistant calls a server tool that says it only reads and doesn't say it runs code
- **THEN** it runs without a question

#### Scenario: A call method with another protocol method's name
- **WHEN** a server names `textDocument/rename` as its call method
- **THEN** approval applies to the call as to any other

#### Scenario: A caller that says the tool doesn't ask
- **WHEN** a caller passes a tool record that says it doesn't ask
- **THEN** the IDE reads the server's listing again and asks where that listing says to

### Requirement: The disclosure SHALL name what language servers' tools send
The disclosure SHALL also say that a language server the IDE runs can give the assistant tools and context,
that what those tools return and that context are sent to the configured endpoint, and that the context and
the results of tools the server says only read are sent without asking. Adding this SHALL move the disclosure
version, so a developer who acknowledged the earlier one sees it once more.

#### Scenario: A developer who acknowledged the earlier disclosure
- **WHEN** the developer acknowledged the disclosure before language servers' tools were added
- **THEN** the current disclosure, naming them, is shown once before the next request
