# One owner per adapter process

`DapProcess.Start` accepts an executable and individual arguments, an optional working directory and
environment overrides. It uses ProcessStartInfo.ArgumentList with shell execution disabled. It performs no
executable discovery, managed-host fallback, handshake or automatic restart. Callers can explicitly use a
runtime host and pass a managed adapter assembly as the first argument.

The process owns its redirected pipes and DapConnection. A monitor reacts to process exit, connection
completion or local disposal. Process exit first allows buffered protocol messages to drain before closing
the connection. Local disposal closes the connection immediately, which settles outstanding requests and
closes stdin. A grace period allows the adapter to exit; otherwise the owner kills its live process tree
and waits for exit. Concurrent disposals join the same completion task.

Stderr is read in fixed-size character chunks rather than newline-delimited records. The owner retains only
the configured tail and counts discarded characters. This prevents both OS-pipe backpressure and an
unbounded in-memory line. The result retains the exit code separately from the connection failure and any
cleanup failure, so a protocol EOF cannot hide a nonzero process exit.

Each asynchronous cleanup stage has a configurable bound. A descendant holding a pipe open cannot keep
completion pending forever. Process.Kill(entireProcessTree: true) applies while the parent is alive; this is
not an OS job object or process-group sandbox, and descendants that detach or outlive an already-exited
parent are outside that guarantee. Such inherited handles time out visibly rather than hiding a shutdown
hang. Cross-platform job/group containment would require a separate platform-specific design.

The fixture executable has no dependency on the DAP library. It speaks literal frames, floods stderr without
newlines, exits after a response, or stays alive with a child. It is referenced only by the test project and
does not ship with the IDE. These tests prove process mechanics, not interoperability with an external
backend; that remains a later acceptance requirement.
