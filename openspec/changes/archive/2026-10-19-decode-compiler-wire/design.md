## Evidence and scope
The compiler connection is separate from the already-binary project container. The user's transcript welcomes an adapter and warns that the connection format will change. It is not a complete framing specification.

Pinned public evidence is [twinbasic/documentation at 7b8f46be210e857a11e15a0d68ee71e2aced4878](https://github.com/twinbasic/documentation/tree/7b8f46be210e857a11e15a0d68ee71e2aced4878):

- [`scripts/lib/tb-wire.mjs`](https://github.com/twinbasic/documentation/blob/7b8f46be210e857a11e15a0d68ee71e2aced4878/scripts/lib/tb-wire.mjs), `objects`, observes individual UTF-8 JSON objects or batches with a little-endian `0xffeaeaea` marker followed by signed int32 byte lengths and JSON payloads. Its observer tracks `/root`, `/fs`, `/language` and `/debugger` socket suffixes and recognizes `compilationStarted` and `textDocument/publishDiagnostics` with nonstandard diagnostic counts.
- [`test/addin/symbols.test.mjs`](https://github.com/twinbasic/documentation/blob/7b8f46be210e857a11e15a0d68ee71e2aced4878/test/addin/symbols.test.mjs) describes BETA 1010 measurements through the IDE's language socket: project-qualified `twinbasic:` document identities and additional completion fields. This is evidence against assuming stock LSP semantics, not a supported-version declaration.
- [`docs/Documentation/Tools-Compiler.md`](https://github.com/twinbasic/documentation/blob/7b8f46be210e857a11e15a0d68ee71e2aced4878/docs/Documentation/Tools-Compiler.md) explains that its compiler harness drives the stock IDE through CDP. It does not establish a standalone compiler launch/handshake contract. Its IDE-launch flags require BETA 1007 or later; do not apply them to an older installation and assume isolation.

A locally available IDE BETA 983 was inspected read-only. Its `ide/main.js` has SHA-256 `91a7797bb75adc8c5cf56075af043b11f9b5ad17664bb65d348c1f2b5ddb2f62`. No proprietary source or executable is included in this repository. The observations below come from its `webSocket` and `webSocketJson` implementations, not from launching the compiler:

| Channel/operation | Observed complete-message bytes |
| --- | --- |
| Root/filesystem request | uint8 operation, int32 little-endian request id, opaque operation-specific payload |
| Root/filesystem reply | int32 little-endian request id, int32 little-endian status code, opaque payload |
| Root/filesystem notification | request id `-1`, int32 event code, opaque payload |
| Language/debugger receive | One UTF-8 JSON object or the same marked JSON batch described above |
| Ordinary JSON send | JSON object encoded as UTF-8 text |

The inspected root connection initializes after a native `CreateCompilerInstance` call supplies a port and a separate passkey. Root/filesystem endpoints use `ws://localhost:<port>/<passkey>/<channel>`. The filesystem client requests a remote folder structure; do not assume HexIDE must implement a filesystem server from the channel name alone. Native process arguments, ownership/exit behavior, root initialization parameters, supported build identification and opening an isolated project still need verification before a session is exposed.

## Design
`CompilerWireCodec` operates on an already reassembled complete message. It does not open sockets, choose endpoints, hold credentials, correlate requests or dispatch opcodes. Control payloads are immutable copies; JSON elements own their storage after parsing and retain unknown fields and numeric precision. Notification id `-1` is explicit; other negative control ids are refused. Unknown operation/status/event codes are preserved rather than guessed.

Default limits are 16 MiB per complete message, 4,096 JSON objects per batch and JSON depth 64; callers may configure positive limits. Decode checks total bytes before parsing/copying, then validates each signed byte length against the remaining buffer without adding unchecked offsets. Empty batches, incomplete lengths, invalid UTF-8, malformed JSON, non-object roots and trailing non-whitespace content fail with `InvalidDataException`. Batch decoding publishes nothing unless the whole batch validates. Cancellation and WebSocket fragmentation limits belong to the future transport; it must bound reassembly before invoking this codec.

Outbound control encoding accepts a caller-provided payload; the observed request0/request1/request1a/request2/request2a payload semantics are intentionally not embedded here. Ordinary JSON encoding checks the same object/depth/byte contract. The special `textDocument/didChangeV2` length-prefixed text envelope is not implemented; read-only integration has no document-change sender. Batches are inbound only, matching the available evidence. The forthcoming binary protocol must get its own codec and compatibility checks, not a heuristic fallback in this parser.

## Verification and limits
Literal control byte vectors independently establish byte order, request ids and notifications. Batch tests construct the documented envelope directly rather than round-tripping through a production batch encoder. Tests cover Unicode byte lengths, ordered multiple messages, large unsigned JSON numbers, opaque binary payloads, truncated/negative/oversized lengths, later-object failure, UTF-8 errors, scalar/multiple JSON values and limit boundaries.

These are codec tests, not evidence of a successful compiler connection or support for BETA 983/1010. A live session test, bounded process cleanup, handshake/version checks, disconnect/cancellation behavior and read-only project isolation are required in the following slice. No compiler or stock IDE was started for this PR, and no user project was opened by one.
