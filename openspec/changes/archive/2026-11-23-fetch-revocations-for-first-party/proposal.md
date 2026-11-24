# Fetch the revocation list for first-party add-ins too (#5572)

## Why

The revocation list can refuse one build of an add-in by its manifest hash, and that applies to first-party add-ins
too, "so HexIDE can pull a bad release of its own add-in" (`RevocationList.cs`). But startup fetches hexide.io's list
only when a discovered package is not first-party (#4022). An install with only first-party add-ins, which is the
default install, never fetches it. A revoked AI Chat build there keeps loading, already consented and fully trusted,
until the user updates HexIDE ([#5572](https://github.com/hexide-io/HexIDE/issues/5572)).

## What changes

- Startup fetches the list whenever a discovered package has a manifest, first-party included, before any package
  loads. The three-second timeout, fail-open behaviour, root verification and the strictly-newer `issuedUtc` check stay
  as they are, and so does an empty URL disabling retrieval.
- An install with no add-in packages still makes no fetch.

The maintainer chose this on 2026-11-23 over a background fetch and a daily check. The cost is up to three seconds at
startup when hexide.io is slow or unreachable.
