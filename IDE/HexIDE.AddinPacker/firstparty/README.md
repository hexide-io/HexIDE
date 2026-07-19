# DEVELOPMENT trust chain — DO NOT use for release

This folder holds a **development-only** signing chain used to package and verify the
first-party bundled add-ins (AI Chat, TestAddin) during local builds and tests.

```
root.key            root PRIVATE key (dev)         — signs the intermediate record
intermediate.key    intermediate PRIVATE key (dev) — signs publisher records
firstparty.key      first-party publisher PRIVATE key (dev) — signs add-in manifests at build
intermediate.json   intermediate record  + intermediate.sig (signed by root)
publisher.json      first-party publisher record + publisher.sig (signed by intermediate)
hexide-root.pub     root PUBLIC key — a copy is embedded in HexIDE at IDE/HexIDE/Addins/Trust/
```

The HexIDE app embeds `hexide-root.pub` as its baked-in root of trust and verifies every
package against it (see `HexIDE.Addins.PackageVerifier`).

## Before any public release

1. Generate a **real** root keypair on an offline machine: `HexIDE.AddinPacker genkeys <dir>`.
2. Keep `root.key` (and ideally `intermediate.key`) **offline** — never commit them.
3. Replace `IDE/HexIDE/Addins/Trust/hexide-root.pub` with the real root public key.
4. Re-issue the marketplace intermediate + first-party publisher records under the real root.

These dev private keys are worthless against a real build: a release bakes in a *different*
root whose private key is offline, so anything signed with this dev chain fails to verify there.
