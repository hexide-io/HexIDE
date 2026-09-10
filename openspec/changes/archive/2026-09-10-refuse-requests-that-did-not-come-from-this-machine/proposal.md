# Refuse automation requests that did not come from this machine

## Why

The automation server binds to loopback, and that was the entire access control. There was no `Host`
validation, no `Origin` validation and no authentication, on the automation endpoint or on the health
endpoint.

Loopback is a weaker boundary than it appears in one specific way, and it is the way that matters here. It
stops a request arriving from the network. It does not stop a browser already running on this machine, and a
browser is the one client that reaches a local port without anybody choosing to.

The vector is DNS rebinding, and it defeats the ordinary same-origin protections rather than being caught by
them. A page served from `evil.example` is same-origin with itself; the name is then re-resolved to
`127.0.0.1`, so the browser sends its request to the local server with no preflight, because as far as it can
tell nothing changed. What the trick cannot hide is the `Host` header, which still names the site the browser
was asked for. Comparing that against the loopback names is the whole of the fix, and it is why the Model
Context Protocol requires a local HTTP server to make exactly this check.

What sits behind the port is not inspection. The tool surface writes file content, creates files, runs the
project and evaluates expressions, so this is a code-execution surface rather than a debugging convenience.

Nothing distributed is affected, because the server is absent from release builds. What is affected is every
contributor running the documented dev loop, for as long as the IDE is up.

## What Changes

- **The server refuses any request whose `Host` header does not name a loopback address on the port it
  actually bound**, ahead of every endpoint including health.
- **An `Origin`, when one is present, must also be loopback on that port.** Absent is the ordinary case and
  is not treated as suspicious: an automation client is not a web page and sends none.
- **Loopback is stated as an endpoint rather than a URL**, so the binding cannot be widened by an
  `ASPNETCORE_URLS` variable in the developer's environment, nor by a careless edit to a URL string.

## Not in this change

**Authentication.** A per-launch token is the control against anything that can already open a socket to the
port, which is a different threat from the browser one and is not addressed here. It changes how every client
is configured and belongs with the question of whether the server should ever ship outside a development
build — hexide-io/HexIDE#352.

Saying that plainly matters, because a partial control described as a complete one is worse than no control:
it invites exactly the assumption it does not support.

## Impact

- `openspec/specs/hexide-mcp-server/spec.md` — one added requirement.
- No change to any client configuration. The dev loop is unaffected, verified against the running IDE.
