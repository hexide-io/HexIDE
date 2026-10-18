# Shared capture, DAP envelopes

DapCapture pairs a ConversationLog with a session-specific connection id. The caller retains log ownership.
The wire connection accepts an optional tap, and process options pass the same tap through to the connection
and process diagnostic reader. Without a tap, no conversation capture is produced.

Incoming bodies are recorded from the bytes read before envelope handling. Outgoing bodies are recorded
before writing, since an adapter can respond before a write continuation resumes. The outbound detail calls
this a write attempt: it is not proof of delivery. A transport failure adds a closure diagnostic and abandons
the unanswered exchange. Capture never waits for the log pump and catches its own failures.

Requests use seq and replies use request_seq as their correlation id. Direction remains part of the key,
so an adapter-initiated request cannot collide with a client request. DAP events map to the shared model's
Notification kind (unanswered messages); their event name is the method label. Full DAP envelopes remain in
the raw body. Failed responses use ErrorResponse, so the existing request-row outcome and failure filter work.

The shared log receives ordered end-request markers for local cancellation and connection closure. A late
reply is still captured but cannot change a locally completed request's outcome. Markers are nonblocking
and participate in the same visible overflow count as frames; an overflowing capture is incomplete evidence.

Opening-frame retention, arming, body budgets, truncation and export behavior are inherited from the shared
log. No private unbounded payload history is introduced. Stderr is recorded in bounded chunks, not lines.
Process lifecycle does not record launch arguments or environment values.

The existing textual export redactor is extended to complete JSON path string fields, including escaped
Windows paths and relative paths. It decodes and rewrites only those strings, retaining unrelated bytes.
Document URIs use the existing URI rule and are not transformed twice. This deliberately handles complete
path fields within malformed/truncated messages too. It does not promise anonymisation of variable values,
expressions, source text, output, arbitrary launch fields or free-form diagnostics.

The inspector already discovers ids from ConversationLog; no UI implementation changes are needed here.
Live UI verification belongs to the subsequent host slice that creates DAP sessions in the IDE. This slice
uses literal wire peers, a real child process, shared-log consumers and export tests.
