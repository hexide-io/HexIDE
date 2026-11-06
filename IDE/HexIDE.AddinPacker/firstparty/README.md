# DEVELOPMENT trust chain — DO NOT use for release

This folder holds a **development-only** signing chain used to package and verify the
first-party bundled add-ins (AI Chat, TestAddin) during local builds. Package tests generate their own
ephemeral chains rather than trusting these records.

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

## Release tooling

`genkeys <dir>` produces development identities only. For explicit release identities, separate
private-key outputs, and the complete root rotation file checklist, see the
[packer README](../README.md). The public records in this folder remain the development chain until that rotation.
