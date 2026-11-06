# Four tools returned an exception message with the profile path unredacted — **CLOSED** (#606, 2026-09-22)

> **Fixed.** `set_file_content`, `add_file`, `set_control_property` and `add_control` catch their own write
> failures and put the exception's message in their reply. Those messages now go through
> `ToolFailures.WithoutProfile`, the same redaction #603's backstop applies. That redaction now also covers the
> forward-slash spelling of the profile, as a `file:///<drive>:/Users/…` URI carries it, in both the long and the 8.3
> short form. It is for the report, not a security control; the server's exposure is #352.

**Measured.** With the target file read-only, each of the three writing tools tried answered `Access to the
path is denied.` with no path in it, because the writer's own message does not name one here. So the redaction
is defensive at these four sites. It was exercised end to end on the backstop in #605, where an
`UnauthorizedAccessException` did carry the path.
