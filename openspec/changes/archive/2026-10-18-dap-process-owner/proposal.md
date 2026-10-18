# DAP process ownership

## Why

The DAP wire client accepts streams but cannot yet own the adapter producing them. A real adapter requires
explicit launch parameters, continuously drained stderr, exit diagnostics and cleanup when its protocol
connection fails. This is the next review slice of #696, tracked by #1162.

## What Changes

- Add a one-shot process owner that launches a command directly and exposes its DAP connection.
- Keep a bounded stderr tail and report truncation, process exit and cleanup failures.
- Close the connection and reap an unresponsive process tree with bounded cleanup waits.
- Verify the boundary using a separately built, real child-process fixture on Windows and Linux.

## Impact

Only the DAP library, its tests and design documentation change. No debugger UI or launch configuration is
enabled. Protocol-inspector capture is a separate next slice; asynchronous session routing, external-backend
launch sequencing and real-backend acceptance follow it.
