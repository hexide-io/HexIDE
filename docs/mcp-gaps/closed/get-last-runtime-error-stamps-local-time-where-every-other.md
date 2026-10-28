# `get_last_runtime_error` stamps local time, where every other timestamp on the surface is UTC

**Symptom.** On a machine at +01:00 (Linux, openSUSE Leap 16, Debug build of main at b15c7cf, 2026-10-13):

```
get_last_runtime_error {}
→ "at":"2026-10-13T03:05:25.1331302+01:00"
/health
→ "timestamp":"2026-10-13T04:07:50.0894348+00:00"
```

`list_lsp_messages`, the conversation export, the connection list and `/health` all stamp UTC, so lining an error
up against the LSP record means converting offsets by hand. Invisible on a machine whose zone is UTC, CI
included. Cross-platform by code reading, and the archived #342 entry quotes the same `+01:00`.

**Workaround.** Compare the stamps as instants, not as text: each carries its offset, so the comparison still
holds. This is an inconsistency, not wrong data.

**Suggested fix.** `DateTimeOffset.UtcNow` in `RuntimeErrorLog`, and a test asserting a zero offset.
[#956](https://github.com/hexide-io/HexIDE/issues/956).

> **Fixed.** `RuntimeErrorLog` stamps an error in UTC, from a `TimeProvider`, so `get_last_runtime_error`'s `at`
> carries `+00:00` as every other stamp on the surface does, and the tool's description says so. The review found two
> more local times a caller meets beside it. `take_snapshot`'s file names are now UTC, with a `Z`
> (`hexide_snapshot_20261012-093015-123Z_1.png`). The IDE log, which a tool failure points to, now writes each
> line's offset. Guarded by `RuntimeErrorLogTests`, whose clock is UTC, so a local reading fails on any machine
> that is not set to UTC, and by `SnapshotCapturePathTests`.
