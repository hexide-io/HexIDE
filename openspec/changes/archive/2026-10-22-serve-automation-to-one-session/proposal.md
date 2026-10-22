## Why
Several agent sessions run the automation dev loop on one machine, each driving its own IDE on its own port (#1212). Ports keep them apart only while every session sets its own. Two sessions that set nothing share the default port, and a session given the port of a stale IDE drives that IDE. Either way every reply looks exactly as it would from the session's own build, so a change can be verified against a build that does not contain it (#1306).

## What Changes
- The IDE accepts the name of the agent session it serves, and the server answers a tool call from a client naming any other session, or none, with an error result that says whose IDE this is, instead of running it.
- The health check reports that session beside the process ID.
- An IDE given no session serves every caller, as before.

## Capabilities
### Modified Capabilities
- `hexide-mcp-server`: an IDE started for one agent session serves tool calls to that session only.

## Impact
The automation server, its command line, the repository's client configuration and the dev-loop launcher. Debug builds only, like the server. Not access control: the session's name is not a secret, and the server still has no authentication (#352).
