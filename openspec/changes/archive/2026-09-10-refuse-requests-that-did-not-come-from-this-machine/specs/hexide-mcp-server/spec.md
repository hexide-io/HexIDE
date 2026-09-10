## ADDED Requirements

### Requirement: The server SHALL serve only requests that originated on this machine
The server SHALL refuse any request whose `Host` header does not name a loopback address on the port it
bound, and SHALL refuse any request carrying an `Origin` that is not itself loopback on that port. The check
SHALL apply to every endpoint, including the health check. A request with no `Origin` SHALL be served, and a
refusal SHALL be distinguishable from the endpoint not existing.

Binding to loopback is not by itself the control it appears to be. It prevents a request arriving from the
network. It does not prevent a browser already running on this machine, and a browser is the only client that
reaches a local port without anyone choosing to.

The vector is DNS rebinding, and it defeats same-origin protections rather than being caught by them: a page
from another site is same-origin with itself, its name is re-resolved to a loopback address, and the request
arrives with no preflight because as far as the browser can tell nothing changed. The one thing the trick
cannot hide is the `Host` header, which still names the site the browser was asked for.

The absence of an `Origin` is not suspicious and must not be refused, because an automation client is not a
web page and sends none. Refusing it would turn a security control into an outage.

This addresses the browser vector only. Anything that can already open a socket to the port may put whatever
it likes in a header, and the control against that is authentication, which this requirement does not claim.

#### Scenario: A request naming this machine
- **WHEN** a request arrives whose `Host` is a loopback address on the bound port
- **THEN** it is served

#### Scenario: A rebinding attempt
- **WHEN** a request arrives on the loopback interface whose `Host` names some other site
- **THEN** it is refused before reaching any endpoint

#### Scenario: An ordinary automation client
- **WHEN** a request arrives with a loopback `Host` and no `Origin` header
- **THEN** it is served

#### Scenario: A page that reached the right host
- **WHEN** a request arrives with a loopback `Host` but an `Origin` naming another site
- **THEN** it is refused

#### Scenario: The health check
- **WHEN** a request whose `Host` is not loopback asks for the health check
- **THEN** it is refused, rather than being told the project name and language-service state
