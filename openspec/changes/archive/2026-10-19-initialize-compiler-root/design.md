## Evidence
The public documentation repository is pinned at `7b8f46be210e857a11e15a0d68ee71e2aced4878`; its `scripts/lib/tb-wire.mjs` observes the four compiler channels and its `tb-ide.mjs`/`tb-registry.mjs` helpers provide isolated stock IDE probes. The observed BETA1011 IDE `main.js` SHA-256 is `b8214edc26b6f8eefa087439498ce72636cc1ef2742339643575a34b798c5233`. Inspection of `_RootConnection.onConnected` and the WebSocket request helpers establishes the exchange below. No vendor source or binary is included.

A live C# probe used the final `CompilerRootSession.ConnectAsync` implementation against a fresh additional compiler requested through an isolated stock IDE's native host. Pre-initialization returned status 0 and a positive PID; initialization returned status 0, five ports and an opaque instance identity. The probe connected to the advertised filesystem, language and debugger endpoints while retaining all sockets until cleanup. No project was loaded into this additional compiler and no language/debug command was issued. Closing the filesystem socket before attaching the others made the next connection fail in an earlier probe; the session layer must eventually own all channel lifetimes together.

The harness used Community Edition, a disposable copy of the published fixture, a private desktop/profile and settings-preserving launch flags. Its finally block stopped its owned process tree and restored the project-state entry. Authenticated endpoints and IDE language data crossed stdin in memory and were not committed or logged. No paid licence key was read or required.

## Exchange
1. Command 22, request id 0. Payload: little-endian int32 DPI 96, then UTF-8 `0,1,10000,0,` (tracing off, one LLVM worker, threshold 10000 ms, cache retention off, empty licence). Expect status 0 and exactly one positive int32 PID.
2. Command 0, request id 1. Payload: int32 flags 0, int32 reserved 0, int32 package-string UTF-8 byte length, package string, then language JSON. Package entries end with `?`; caller-supplied `!` prefixes retain the IDE's user-package representation. Expect status 0, five int32 ports in filesystem/debugger/language/form-image/live-backup order, then a nonempty strict UTF-8 server identity.

The identity is not a version. Unknown nonzero statuses, unsolicited reply ids, duplicate replies and malformed metadata fail closed. Notifications use id -1 and remain opaque; this handshake preserves them in arrival order rather than executing reverse requests.

## Input and lifetime decisions
The caller supplies the complete nonempty compiler language-data object and package entries. A probe using empty language data did not complete initialization; the complete IDE-provided object succeeded. Nonempty validation only catches the known empty case: it is not validation of the undocumented language-data schema. There is no silent fallback to fabricated localization data, no implicit registry access and no automatic package loading performed by this adapter.

Validation/encoding precedes network I/O. Package entries reject ambiguous delimiters and NUL; length accounting uses UTF-8 bytes. The socket upgrade and both replies share one 30-second default deadline, preventing notifications from extending it. Defaults bound each message to 16 MiB, aggregate received bytes to 32 MiB and notifications to 128. All reply and notification bytes count toward the aggregate budget. Configuration is explicit for future larger projects.

Successful initialization transfers root socket ownership into the returned disposable session. Failure/cancellation closes it without replay. Caller cancellation retains the caller token; deadline expiration is a timeout. Peer payloads and authenticated endpoint paths are not included in errors. Disposal closes the owned socket, not an externally launched process. This slice has no idle receive pump or ongoing request API; retaining the initialized socket is preparation for the next session layer, not a claim of complete session supervision.

## Startup limitation and next work
The stock IDE starts the native compiler with a numeric `--compiler=` argument and obtains the root port/passkey through its native host. The standalone host coordination contract is not yet established. This PR deliberately takes an endpoint rather than guessing launch arguments. A production launcher must establish process ownership, discovery, compatibility and teardown; the probe's native-host launch is evidence, not a shipped dependency on the stock IDE.

Follow with owned multi-channel attachment, ongoing replies/notifications, project loading and read-only language services. Reuse the shared DAP client when debugger translation is implemented.

## Validation
Independent byte vectors check the outgoing commands, including Unicode package byte counts. Real loopback WebSocket peers exercise notifications, correlation, status failures, malformed PID/ports/identity, aggregate limits, cancellation, deadline and disposal. The library suite passes 184 tests on Windows and Linux. Live vendor checks are separate from offline CI and establish only the observed BETA1011 initialization and socket attachment behavior.
