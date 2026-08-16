# ai-chat Specification

## Purpose
Define the bundled assistant panel: a chat surface that can read the developer's project and act on it
through the add-in host surface, and the constraints that keep that from being a liability.

It exists for two reasons. It is genuinely useful — a VB6-literate assistant that can see the file you are
editing and the errors in it. And it is the proof that the add-in host surface is sufficient for real work:
everything it does, it does through the same surface a third-party add-in would use, so a gap in that
surface shows up here first rather than in a contributor's bug report.

The behaviour that matters most is not conversational. This is a feature that sends the developer's source
code to a third party and can modify files on their disk, so the requirements below are mostly about
disclosure, consent and the handling of credentials.

## Requirements
### Requirement: The model provider SHALL be chosen by the developer
The assistant SHALL take its endpoint, model and credential from the developer's own configuration, and
SHALL NOT require any particular provider.

There is no single right answer here and the answer changes: developers have existing accounts, employers
have policies about where code may be sent, and some will want a model running on their own machine with
nothing leaving it at all. Pinning the feature to one provider would make it unusable for whoever picked
differently.

#### Scenario: Pointing the assistant at a different provider
- **WHEN** the developer configures a different endpoint and model
- **THEN** the assistant uses them with no change to the IDE

#### Scenario: Using a locally-hosted model
- **WHEN** the configured endpoint is a model server on the developer's own machine
- **THEN** the assistant works and no conversation content leaves the machine

### Requirement: The assistant SHALL refuse to transmit over a cleartext connection
The assistant SHALL refuse to send a request over an unencrypted connection to a remote host, and SHALL
permit an unencrypted connection only to the local machine.

Every request carries both the developer's credential and their source code. Sending that in the clear to a
remote host is indefensible whatever the developer configured, so it is refused rather than warned about.
The loopback exception is what makes a locally-hosted model usable, and it gives up nothing — the traffic
never reaches a network.

#### Scenario: A remote endpoint without encryption
- **WHEN** the configured endpoint is a remote host over an unencrypted connection
- **THEN** the request is refused with an explanation, and nothing is sent

#### Scenario: A local endpoint without encryption
- **WHEN** the configured endpoint is on the local machine over an unencrypted connection
- **THEN** the request proceeds

### Requirement: The credential SHALL be resolvable without storing it in a settings file
The assistant SHALL accept its credential from the environment in preference to its settings file, and
where the credential is being read from the settings file it SHALL say so.

A credential in a plaintext settings file is a credential that ends up in a backup, a screen share, or a
support bundle. The environment is not a secret store either, but it is a meaningful improvement and it is
available today; surfacing which one is in use means the developer can make that choice knowingly rather
than discovering it later.

#### Scenario: A credential present in the environment
- **WHEN** the credential is available from the environment
- **THEN** it is used in preference to any value in the settings file

#### Scenario: A credential only in the settings file
- **WHEN** the credential is read from the settings file
- **THEN** the developer is told, once, that the environment is the better place for it

### Requirement: The developer SHALL be told what leaves their machine, before it does
The assistant SHALL disclose, before its first request of a session, that the conversation, the active
file's contents and its diagnostics are sent to the configured third-party endpoint, naming that endpoint.

The thing a developer would most want to know is exactly the thing a chat panel makes easiest to forget:
the code in the editor is part of every request, not just what was typed into the box. Disclosing it once,
before anything is sent, and naming the actual destination is the difference between a feature they chose
and a surprise.

#### Scenario: First use in a session
- **WHEN** the developer sends their first message
- **THEN** they are told what is transmitted and where, before the request is made

### Requirement: Actions that change the project SHALL require explicit approval
Where the assistant proposes an action that modifies a file or starts or stops execution, the IDE SHALL
obtain the developer's approval for that specific action before performing it, and SHALL report a refusal
back to the assistant so it can adapt. Actions that only read SHALL NOT require approval.

An assistant acting on the project is the point of the feature, and also the risk: the failure mode is a
confident, wrong, unattended edit. Gating on the write, rather than on the conversation, keeps the useful
part fluid — reading files and diagnostics needs no ceremony — while making every destructive step a
decision the developer actually made. Telling the assistant it was refused matters too; otherwise it
proceeds as though the edit landed.

#### Scenario: The assistant proposes an edit
- **WHEN** the assistant asks to replace a file's contents
- **THEN** the developer is shown what is proposed and the edit happens only if they approve

#### Scenario: The developer refuses
- **WHEN** the developer refuses a proposed action
- **THEN** it does not happen, and the assistant is told it was refused

#### Scenario: The assistant reads project state
- **WHEN** the assistant reads the active file, the file list or the diagnostics
- **THEN** no approval is required

### Requirement: A failed request SHALL be visible rather than silent
Where a request fails, the assistant SHALL show the failure in the transcript and SHALL return the panel to
a state where the developer can try again.

A chat panel that stops responding is indistinguishable from one that is thinking. Since the most common
failures — a rejected credential, a rate limit, an unreachable endpoint — are all things the developer can
act on, saying what happened converts a dead panel into a fixable problem.

#### Scenario: The provider rejects the request
- **WHEN** a request fails for any reason
- **THEN** the failure and its reason appear in the transcript
- **AND** the panel accepts a new message

### Requirement: Conversation history SHALL persist and SHALL be bounded
The assistant SHALL persist its conversation across restarts, and SHALL bound what it sends by summarizing
older turns rather than sending an unbounded history.

Losing the thread on restart makes the panel feel disposable. Sending everything forever is the other
failure: cost and latency grow with the length of the conversation until it stops working. Summarizing the
older part keeps the recent exchange exact, which is the part that is actually being referred to.

#### Scenario: Restarting the IDE
- **WHEN** the IDE restarts
- **THEN** the previous conversation is still there

#### Scenario: A long conversation
- **WHEN** the conversation grows past the configured bound
- **THEN** older turns are replaced by a summary and the recent turns are sent unchanged

### Requirement: Code the assistant produces SHALL be applicable in one step
Where a reply contains a fenced code block, the panel SHALL offer to apply it to the active document.

Copying code out of a chat panel and pasting it into an editor that is six inches away is the kind of
friction that makes a feature not get used. The apply action is subject to the same approval requirement as
any other modification.

#### Scenario: A reply containing code
- **WHEN** a reply contains a fenced code block
- **THEN** that block is offered for direct application to the active document
