# External Immediate ownership

Immediate routes to the external host whenever it owns the run. No fallback to the interpreter occurs.
The session sends the entered line unchanged as `evaluate` with `context: repl`, including frame ID zero.
REPL evaluation has no optional DAP capability flag; adapter refusals are shown to the user.

A selection generation distinguishes switching away and back from remaining in the same frame.
The response must still belong to the original session, stop and selection when applied. An expired reply
is replaced by a localized diagnostic, while independent adapter output events remain in the transcript.
The request is not retried: a timeout does not prove a side-effecting statement did not execute.

One evaluation runs at a time. Run controls and frame selection remain available during the request.
Completion refreshes the currently selected frame's locals if the stop is still current, even on refusal,
because a statement can mutate state before failing. Old inspection replies cannot overwrite the refresh.
The view appends asynchronous results at the current document end rather than holding a possibly deleted line.
