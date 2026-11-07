# A lone surrogate in a call's arguments gets a bare `-32603` before any tool runs — **CLOSED** (#1337, 2026-10-23)

**Original symptom.** Recorded in #1274's review through a raw request whose body was not kept. A string argument
holding an unpaired surrogate got the SDK's JSON-RPC error `-32603 "An error occurred."` before any tool ran, naming
neither tool nor parameter and not saying that nothing was done.

**Reproduced.** `McpRestartTests` starts the real SDK server in-process with the IDE's transport and filters. The
SDK 1.3.0 client accepts a C# string containing `\uD800`, but its encoder replaces it with U+FFFD; `echo` returns
that replacement character. It cannot send the escaped lone surrogate this way. An `HttpClient` POST inside the
test, to the same `/mcp` endpoint, with `Content-Type: application/json` and
`Accept: application/json, text/event-stream`, sent this exact body:

```json
{"jsonrpc":"2.0","id":1337,"method":"tools/call","params":{"name":"echo","arguments":{"text":"\uD800"}}}
```

The original HTTP 200 response was an SSE frame (followed by a blank line):

```text
event: message
data: {"error":{"code":-32603,"message":"An error occurred."},"id":1337,"jsonrpc":"2.0"}

```

**Where it failed.** Request-parameter deserialization, before argument binding or any tool-call filter. The SDK's
`RequestHandlers.Set` calls `JsonSerializer.ReadFromNode`; rewriting the parameter node calls
`JsonDocument.WriteComplexElement`, which throws `InvalidOperationException: Cannot read incomplete UTF-16 JSON
text as string with missing low surrogate.` The JSON-RPC envelope itself had already been read. The #1270
argument-binding refusal therefore never got a chance to explain it.

**Fix.** An SDK incoming-message filter checks argument text before that conversion, including strings nested in
arrays or objects. It returns #1270's tool-error shape (`isError: true`), with the same request id. This call's
content now says:

```text
'echo' refused its arguments, and nothing was done: 'text' is not valid text (an unpaired UTF-16 surrogate). Its input schema names each parameter and its type.
```

If an invalid argument property name cannot be quoted, the refusal instead names the request's arguments as
invalid text. The workaround before the fix was to send the surrogate pair whole, or the character itself.

**Guard.** `Unpaired_surrogate_arguments_are_refused_as_invalid_text` sends both lone high and lone low surrogates
over HTTP, asserts the tool-error flag, tool, parameter, reason, no action and request id, and checks that valid
pairs and literal backslash escapes still work through the SDK client. Restoring both production files to HEAD
makes the wire assertions fail with the original bare error; restoring the fix passes.
[#1337](https://github.com/hexide-io/HexIDE/issues/1337).
