# Launch external debug sessions in the IDE

## Why
The asynchronous session API has real-process acceptance but no user can launch it inside the IDE. This
slice makes a saved external workspace usable from the existing run controls and debugger windows (#1170).

## What Changes
Add an explicitly selected JSON launch file, one owned UI session, asynchronous stack/locals, output and
connection capture, and shutdown cleanup. Preserve the interpreter route outside an external session.

## Impact
Run menu, debugger consumers, project runner, connection registry, localization and desktop automation
refusals. No workspace conversion, expression evaluation or remote-specific MCP execution surface yet.
