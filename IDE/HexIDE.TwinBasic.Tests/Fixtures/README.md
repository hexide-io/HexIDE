# Published container fixtures

Both binary files are unmodified downloads from `twinbasic/documentation`, commit
`7b8f46be210e857a11e15a0d68ee71e2aced4878`, retrieved 2026-10-18:

- [`scripts/impexp/impexp.twinproj`](https://github.com/twinbasic/documentation/blob/7b8f46be210e857a11e15a0d68ee71e2aced4878/scripts/impexp/impexp.twinproj):
  the published import/export utility's packed project, with Settings and source files.
  SHA-256 `f9b9c3241b2534f90b60e9b4f4596a4dab0acac83e59a0ec443829cde625fac5`.
- [`indexer/sample.twinpack`](https://github.com/twinbasic/documentation/blob/7b8f46be210e857a11e15a0d68ee71e2aced4878/indexer/sample.twinpack):
  CustomControlsPackage, including source, resource folders, images and embedded licence/changelog.
  SHA-256 `4afa8819602355d1f95e076c26fbac7a4e1f2fe392d9e2029cbe9a3aabe76ce8`.

`LICENSE` is the upstream repository's MIT licence, copied at that revision. The sample package also
embeds an MIT licence attributed to Wayne Phillips T/A iTech Masters; it is retained inside the original
binary and reproduced in `LICENSE.sample`. These fixtures establish compatibility with published real
containers. No vendor IDE was run to create or resave them in this implementation session.

The adjacent `.json` manifests are expected values from that revision's independent `scripts/impexp.mjs`
parser, not from the C# implementation. They record every entry in traversal order, metadata, revision
trailers, content length and SHA-256 of the opaque content. Tests use these committed manifests offline;
Node and network access are not needed by CI. Git attributes preserve the container bytes on every OS.

To reproduce a manifest from the repository root, download the pinned `scripts/impexp.mjs` into
`artifacts/twinbasic/impexp.mjs`, then run:

```text
node IDE/HexIDE.TwinBasic.Tests/Fixtures/generate-manifest.mjs artifacts/twinbasic/impexp.mjs IDE/HexIDE.TwinBasic.Tests/Fixtures/impexp.twinproj
node IDE/HexIDE.TwinBasic.Tests/Fixtures/generate-manifest.mjs artifacts/twinbasic/impexp.mjs IDE/HexIDE.TwinBasic.Tests/Fixtures/sample.twinpack
```

The helper only reads the fixture and writes its adjacent JSON. The pinned tool's `export` command was
also run on the impexp project (11 files, 5 folders). High-bit UInt64 revisions and non-zero revision
trailers have additional synthetic tests, since these fixtures do not establish those edge cases.
