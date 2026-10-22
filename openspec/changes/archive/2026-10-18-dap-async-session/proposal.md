# Asynchronous debug sessions

## Why
The wire/process/capture layers can exchange messages but cannot coordinate a program launch or protect
debugger consumers against late inspection results. The interpreter's synchronous execution hooks are not
a remote session API. Tracking issue: #1168, part of #696.

## What Changes
Add a single-launch asynchronous session with configuration sequencing, stop identities, inspection,
resume/pause/disconnect and bounded updates. Add an explicit real-process smoke executable and record
acceptance against an external backend. IDE routing and launch configuration follow in another slice.

## Impact
HexIDE.Dap, focused tests, an opt-in console acceptance tool, and debugger documentation. No dependencies
or interpreter semantics change.
