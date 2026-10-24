## Why
An initialized compiler root cannot safely accept service-channel attachment until it has loaded a project and acknowledged readiness. This phase implements that preparation boundary for #1202, part of #1178.

## What Changes
- Copy a project container to an owned temporary folder without changing its original or persistent source projection.
- Correlate project load, reverse settings/package requests, their acknowledgements, and readiness.
- Resolve package bytes through an explicit provider and bound memory, notifications and elapsed time.
- Preserve compiler rejection status and expose retryable cleanup when an external process retains the stage.

## Impact
Library-only functionality and protocol tests. No IDE command, launcher, build/run/save, add-in loading or write-back. The existing internal channel owner remains internal.
