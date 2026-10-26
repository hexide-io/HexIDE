# Synthetic preparation recording

`preparation.jsonl` is synthetic, not evidence of a real twinBASIC beta. It follows the
scripted exchange in `CompilerProjectPreparationWireTests`: preinitialization, initialization,
load, settings and package reverse requests, their acknowledgements and readiness.
`CompilerRecordingTests` drives the real root session and project-request codec against it,
records those frames again and compares the complete recording.

The first line has `FormatVersion` 1, `Beta`, `Edition` and `Scenario`. Each later line has
`Sequence` (starting at 1), `Channel` (`root`, `fs`, `language`, `debugger`), `Direction`
(`send` means adapter to compiler), `Type` (`Text` or `Binary`) and `Payload`.
Text payloads are strings; binary payloads are base64. Decode base64 to review control bytes.
Placeholders in binary payloads are UTF-8 marker bytes **inside** the base64, not base64
fragments. This preserves opaque bytes while allowing substitution of values of different lengths.

Construct one `CompilerRecording` with a caller-owned writer and explicit `CompilerRecordingValues`,
then call `RecordTo` on each transport before starting its session. Share the recorder across channels
for a single sequence. Supply exact wire bytes for `{stage}`, `{pid}`, `{instance}` and
`{port:root}`, `{port:fs}`, `{port:language}`, `{port:debugger}`, `{port:formImage}` or
`{port:liveBackup}` as applicable. `{language}` stands for the language data an initialization carries, when that data
is an install's own pack and not ours to keep (`../beta-1024/README.md`). Integers in this fixture are little-endian int32.
Values must already be known; start a separate capture if those values change. JSON substitution works
on decoded strings and int32 process/port numbers, preserving escaped paths and restoring numeric types.
A numeric placeholder is unquoted inside the stored text template; substitution makes it valid JSON again.
Binary substitution is bytewise: callers must account for embedded length fields in other wire encodings.
Do not use overlapping run values that could also match unrelated protocol bytes.

`CompilerRecordingReplay.PlayAsync` uses the compiler side of caller-owned loopback transports.
It substitutes the replay run's bytes, rejects unresolved markers, sends compiler frames and compares
adapter frames. Text frames compare as structural JSON (property order and whitespace do not matter);
binary frames compare exactly. A mismatch reports the first sequence and both texts (base64 for binary).
Keep recordings small and inspect them before sharing. The recorder refuses user-home paths and
licence-shaped values before writing a line; the suite checks every checked-in recording, decoding binary
payloads too. This is a guard against accidental inclusion, not permission to capture secrets.

No live compiler was used for this fixture. Real beta recordings belong to #1689; cross-beta comparison
and the work in #1219 are outside #1218.
