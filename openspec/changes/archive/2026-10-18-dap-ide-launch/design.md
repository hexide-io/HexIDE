# IDE host

An explicit file picker is the authority to execute a launch file; project loading never discovers or runs
one. Paths resolve from its directory, argument lists remain literal and launch fields belong to the adapter.
The UI host owns one DapSession and consumes its bounded update channel. Mutable presentation state belongs
to the UI thread; stop-scoped inspection validates again before applying. The project runner routes user
commands based on the active host, without implementing interpreter execution hooks remotely.

The existing Locals nodes gain an asynchronous remote expansion path. Interpreter expansion remains
synchronous. Call Stack and Locals clear on resume; source reveal uses existing loaded-document navigation.
An aggregate registry adds the latest DAP row to language connections while preserving the LSP router.
Capture uses the shared log with a unique ID per run. Shutdown waits for disconnect/owned cleanup.

The first UI slice models one global stop and the top frame. Unsupported remote actions explicitly refuse
or stay disabled; they do not fall through to interpreter state. Existing command-specific MCP methods remain
interpreter-oriented, with external state and stop supported and other remote operations refused.
