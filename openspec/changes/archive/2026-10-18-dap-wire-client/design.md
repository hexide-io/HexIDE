# DAP wire boundary

The library takes streams rather than an LSP transport: ILspTransport creates JSON-RPC message handlers, so
using it would couple DAP to the wrong envelope and serialization model. Process ownership and reuse of the
lower-level launching infrastructure are a later slice. The client owns streams after successful construction.

One read loop frames and validates incoming messages. One asynchronous writer gate prevents interleaved
frames and assigns increasing outgoing sequences in send order. A response resolves the matching request;
an event enters a bounded channel for one consumer. The consumer decides scheduling and UI dispatch.

Responses and events own cloned JSON elements. The wire layer does not need to predict every capability or
extension field and never deserializes a backend's capabilities into a closed schema. Failed responses throw
an exception retaining the entire response for later presentation.

The first slice refuses all reverse requests explicitly and must not advertise support for them. This keeps
an unexpected reverse request from hanging an adapter. A later host can introduce a supported-request handler.

Timeout and cancellation abandon a local wait, without resending the command or claiming the program stopped.
Late replies cannot satisfy a newer request. A failure during a write closes the connection because it may
have left a partial header or body. EOF, protocol errors and event overflow also close it and settle pending
work. Disposal unblocks reading through cancellation and stream disposal; the stream contract requires one
of those mechanisms to work. Completion reports the first closure cause, or null for intentional disposal.

No wire-level operation knows which thread/frame is selected, decides launch ordering, captures source
payloads, or automatically retries execution commands. Those belong to the later session and capture layers.
