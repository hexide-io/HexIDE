## Protocol evidence
The BETA1011 stock IDE and public documentation informed this implementation; provenance is recorded in the archived channel-lifecycle design. No vendor source, package data, credentials or binaries are committed.

A live Community Edition test used the production preparation API against a fresh additional compiler created by the isolated stock native host. A disposable copy of the published impexp fixture generated one settings request and four package requests. Preparation succeeded, all four channels attached, and filesystem command 0 returned a successful 25,175-byte payload: eight bytes of structure-version metadata followed by a JSON object. The original hash remained unchanged.

The package bridge used the stock native host only in the ignored harness: base64 data was decoded to a raw-byte stream for the production provider interface, then encoded by the production protocol implementation. This establishes the envelope for the tested packages, not standalone package discovery or compatibility with every project/version. Native launch and discovery remain unresolved.

## Sequence and correlation
Claim exclusive ownership of the initialized root. Copy only the packed container into a GUID-named direct child of system temp. Send command 1 with the staged absolute path. Notifications 243 carry an int32 prefix plus a settings JSON object; command 24 returns int32 zero and empty text, preserving settings. Notifications 255 carry an opaque UTF-8 package ID; command 4 returns int32 name byte length, name bytes and base64 package text.

Allocate distinct request IDs after the two initialization IDs. Track every outstanding request, including reverse-response acknowledgements. Drain all acknowledgements before command 3 and again before returning readiness. Unknown notifications are retained without executing actions. Unexpected or duplicate reply IDs terminate preparation. Nonzero responses preserve only the status code; DEP/LAA mismatch never changes settings or restarts the process.

## Providers and bounds
The provider receives opaque IDs and returns owned raw-byte streams. The file provider snapshots an exact case-sensitive allowlist of IDs to explicitly chosen absolute paths. It never treats peer IDs as paths, URLs or commands.

Defaults: 64 MiB project, 16 MiB message, 32 MiB aggregate received data, 8 MiB individual package, 32 MiB aggregate package data, 128 notifications and 60 seconds total preparation. Base64 expansion counts toward message limits. Existing transport deadlines may be stricter. Provider implementations must return promptly from synchronous entry/disposal; asynchronous acquisition and reads are cancellation-bounded. Streams arriving after cancellation are disposed when they arrive.

## Ownership and cleanup
The caller stops using the root after preparation claims it. The result owns root and staging, but does not own or terminate the external compiler process. Close attached channel readers, stop the separately owned compiler process, then dispose the prepared result. Live testing proved socket closure alone leaves the native compiler holding the staging directory on Windows; stopping the owned process allowed removal.

Cleanup validates the GUID-named direct temp child and refuses a replaced root link. Failure reports a retained-stage exception with its directory and a retry method; preparation failures retain their original cause as well. Successful-result disposal is also retryable after a cleanup error. No global process termination, retries of compiler commands, or silent cleanup failure.

Staging is separate from the persistent source projection. The archive and projection remain untouched. Relative external dependencies are not copied; this phase is suitable for packed containers whose required packages are available from the provider. There is no public combined prepare/attach API yet, and no read-only filesystem model beyond the live validation query.

## Verification
Loopback peers independently check command bytes, Unicode package IDs, base64 contents, out-of-order acknowledgements, strict request correlation, unknown notifications, malformed input, budgets, timeouts, late provider cancellation, ownership and cleanup. Windows additionally checks cleanup retry after releasing an external file handle; Unix checks its normal unlink semantics. Live evidence covers production preparation, read-only tree query, source preservation and cleanup after native shutdown.
