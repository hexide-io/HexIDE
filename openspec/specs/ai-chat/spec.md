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

### Requirement: Reading a named file SHALL leave the developer's view in place
The assistant's `get_file_content` SHALL call the add-in host's `TryGetDocument` member and report its
content or refusal reason without navigating to the document.

#### Scenario: Reading a background or closed document
- **WHEN** the model asks for a named file's content
- **THEN** the assistant returns the host's whole-file text without changing the active tab or caret

### Requirement: The input row SHALL fit a narrow dock pane
The assistant view SHALL allow its prompt box to narrow with the dock pane, keeping Send and New within
the pane at widths of 212 and 254 logical pixels.

#### Scenario: Opening AI Chat in the left dock
- **WHEN** the panel is allocated 212 or 254 logical pixels of width
- **THEN** the prompt box has positive width and both buttons fit within the panel

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

#### Scenario: Reopening after editing settings
- **WHEN** the developer edits `chat-settings.json` and reopens the existing assistant panel
- **THEN** the assistant SHALL reload its configuration without an IDE restart and preserve the conversation
- **AND** if a response is in progress, the reload SHALL wait until that response ends
- **AND** a missing or malformed file SHALL use the same defaults as at initial construction

#### Scenario: Using a locally-hosted model
- **WHEN** the configured endpoint is a model server on the developer's own machine
- **THEN** the assistant works and no conversation content leaves the machine

### Requirement: Anthropic endpoints SHALL use the native Messages API
When the configured base URL host is exactly `api.anthropic.com` (case insensitive), the assistant SHALL
use the native streaming Messages API for replies, tool continuations and compaction. All other hosts
SHALL retain the OpenAI-compatible transport. Credential resolution and cleartext refusal SHALL remain
the same for both transports.

The native transport SHALL translate system prompts, tool definitions, tool calls and tool results,
assemble streamed tool input before exposing calls, and preserve native content blocks, including signed
thinking, in history. It SHALL replay thinking only for the current developer message’s tool round; older
assistant turns SHALL retain text and tool-use blocks without thinking or redacted thinking. Compaction
SHALL remove thinking from the kept tail and run only before the first request of a developer message.
The system prompt SHALL be built once per developer message and reused throughout its tool round.
It SHALL report stream errors and premature EOF as
failures rather than successful completion. It SHALL ignore unknown event types.

`chat-settings.json` MAY set `maxTokens` (native output cap, default 64000), `thinking` (the native thinking
configuration object), optional `workspaceId` (sent as `anthropic-workspace-id` only to Anthropic), and `effort` (sent as `output_config.effort`). Omission of thinking and effort SHALL
leave their defaults to the configured model; no model-name allowlist SHALL be imposed. These native
settings SHALL NOT alter requests to other hosts. For example, `"thinking": {"type": "adaptive"}` and
`"effort": "high"` request adaptive thinking at high effort on a model that supports those settings.

Wire reference read on 2026-11-10, API version **2026-10-21**:
[Messages](https://platform.claude.com/docs/en/api/messages/create),
[streaming](https://platform.claude.com/docs/en/build-with-claude/streaming),
[thinking](https://platform.claude.com/docs/en/build-with-claude/extended-thinking),
[steering thinking](https://platform.claude.com/docs/en/build-with-claude/adaptive-thinking),
[effort](https://platform.claude.com/docs/en/build-with-claude/effort),
[tool use](https://platform.claude.com/docs/en/agents-and-tools/tool-use/implement-tool-use),
[API overview](https://platform.claude.com/docs/en/api/overview) and
[versioning](https://platform.claude.com/docs/en/api/versioning).
These are protocol references, not a claim of a live provider test; `NativeMessagesTests` uses a fake HTTP
handler and synthetic event streams following the documented wire shapes.

The native transport SHALL show the API’s `error.message` for unsuccessful HTTP responses. A
`refusal` stop SHALL use the stream-error display route, including `stop_details.explanation` when
present; a `max_tokens` stop SHALL visibly identify the incomplete reply. Neither SHALL execute tools.

Additional protocol references checked on 2026-11-10 at the same API version:
[authentication](https://platform.claude.com/docs/en/manage-claude/authentication),
[preserved thinking](https://platform.claude.com/docs/en/build-with-claude/preserved-thinking),
[thinking tool workflows](https://platform.claude.com/docs/en/build-with-claude/thinking-tool-workflows),
[stop reasons](https://platform.claude.com/docs/en/build-with-claude/handling-stop-reasons), and
[refusal details](https://platform.claude.com/docs/en/build-with-claude/refusals-and-fallback).

#### Scenario: Native streamed tool call
- **WHEN** Anthropic streams a tool's JSON input across several deltas and finishes with `tool_use`
- **THEN** the assistant receives one assembled call only after message completion
- **AND** its results return as user content containing `tool_result` blocks
- **AND** signed thinking blocks are returned unchanged

#### Scenario: Failure during a native stream
- **WHEN** a stream errors or ends before `message_stop`
- **THEN** the request fails without executing its tool calls

#### Scenario: A different endpoint
- **WHEN** the host is OpenAI, a loopback server, or any host other than exactly `api.anthropic.com`
- **THEN** requests continue to use `/chat/completions` and the OpenAI-compatible format
- **AND** native content metadata is omitted

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

### Requirement: A remote endpoint SHALL require a credential before context is read
Before assembling a request, reading the active file for context, or compacting history, Send SHALL
require a non-whitespace effective API key unless the configured endpoint URL's host is `localhost`,
`127.0.0.1`, or `::1`. The exemption SHALL depend only on that host, never the provider, model, or URL
path. A blocked send SHALL display a localized explanation naming `apiKey` in
`chat-settings.json`, retain the input, and send no request.

#### Scenario: No key for the default endpoint
- **WHEN** Send is invoked without a key in settings or any supported environment variable
- **THEN** no request is made, no active-file context is read, and the chat explains how to set a key

#### Scenario: A keyless local endpoint
- **WHEN** the configured URL names one of the three loopback hosts
- **THEN** chat and history compaction may use that endpoint without a key

#### Scenario: A remote URL mentions localhost
- **WHEN** a remote endpoint has `localhost` only in its path and no effective key
- **THEN** Send refuses before reading context or making any request

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

#### Scenario: Applying a block preserves its ending
- **WHEN** the developer applies a fenced code block
- **THEN** the panel SHALL remove only the single LF or CRLF immediately before the closing fence
- **AND** preserve all preceding final line breaks, blank lines and trailing spaces or tabs in the applied content

### Requirement: A change the IDE refuses SHALL be reported as refused, with its reason
Where the IDE refuses a change to a file that the assistant asked for, or that the developer applied from a
reply, nothing SHALL be written, the assistant SHALL be told that the change was refused and why, and the
developer SHALL see the refusal and its reason in the transcript.

The assistant is shown each file whole, header included, and the IDE keeps the header for itself, so a model that
re-types a form's designer block with one space out of place has its edit refused. That makes a refusal routine
rather than rare. Reporting it as success is the failure the approval requirement exists to prevent, arriving from
the other side: the assistant carries on as though the edit landed, and the developer believes it did.

#### Scenario: The assistant's edit is refused
- **WHEN** the assistant asks to replace a file's content with text whose header differs from the file's
- **THEN** nothing is written, the assistant is told the edit was refused and why, and the transcript shows the
  refusal and its reason

#### Scenario: Applying a block the IDE refuses
- **WHEN** the developer applies a code block whose header differs from the active file's
- **THEN** nothing is written, the transcript says the block was not applied and why, and the assistant is told
  the same

### Requirement: The developer SHALL be able to stop a response
The panel SHALL offer Stop while responding and SHALL pass cancellation to provider streams, including
history summarization. Stopping SHALL finish any partial bubble without showing a request error and SHALL
allow another message. Pending tool approvals SHALL be rejected and remaining tool calls skipped.

#### Scenario: Stopping a provider stream
- **WHEN** the developer clicks Stop while a response is in flight
- **THEN** the provider stream is cancelled and the panel accepts another message
- **AND** any text already received remains visible
