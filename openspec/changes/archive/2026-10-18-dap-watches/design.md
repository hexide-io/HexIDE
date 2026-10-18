# External watch ownership and freshness

The Watches window switches between the existing interpreter store and a distinct external store when an
external run is active. The external store survives End and later launches until the IDE closes; expressions
are adapter-specific and are neither written to launch JSON nor copied into interpreter watches.

Refresh keys include the stop, selected frame, inspection revision and evaluation availability. Immediate
completion changes the inspection revision. Rows clear immediately when the key or definitions change.
One asynchronous evaluation loop serves the list, coalescing further changes and dropping obsolete results.
Adapter failures belong to individual rows. Watch evaluation does not refresh Locals or emit a Changed event,
preventing recursive refresh. DAP watch context has no optional capability flag.

Remote values reuse the Locals node loader for lazy expansion. The selected frame is shown in Context;
the original definition context is retained for interpreter semantics. External Add/Edit dialogs disable
break-condition types and enforce expression-only saving. Generic UI automation drives this slice;
specialized MCP watch commands continue to refuse external runs.
