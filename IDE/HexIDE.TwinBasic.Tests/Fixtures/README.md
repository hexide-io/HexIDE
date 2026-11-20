# Container fixtures

## Published containers

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

## Saved by the twinBASIC IDE (#5139)

The `tb1044-*.twinproj` files were saved by the twinBASIC IDE itself, BETA 1044, from its Standard EXE template at
its default options. Each one is byte for byte what the IDE wrote. The template's own icon
(`Resources/ICON/twinBASIC.ico`) is part of every one, as decided on the Team Desk (q-5139-fixture-icon).

**How they were made.** `tools/twinbasic-oracle/` drove the IDE's front end in a Hyper-V VM on 2026-11-20: Windows
11 Pro 26300, WebView2 157.0.4341.60, the zip's SHA-256 matching the release's digest. The twinBASIC compiler was
launch-and-watch only: nothing read, hooked or instrumented it. The IDE serves as an oracle by the maintainer's
decision of 2026-11-20, with its author's agreement (#5138).

**How to read them.** Every save after the first comes from a session the harness opened on an earlier file, and
the saves in one session follow each other. So a file differs from the one it was made from by its change, plus,
for a session's first save, what reopening does: on BETA 1044, every file's revision rises by 2 and every folder
writes its children in reverse order. #5140 has the measurements.

| File | Made from | Change, then save |
|---|---|---|
| `tb1044-01-new-project.twinproj` | the template | A new Standard EXE named `NewProject`, saved where the Save dialog offered. A second save in the same session was byte-identical |
| `tb1044-02-reopened.twinproj` | 01, reopened | No change |
| `tb1044-03-reopened-twice.twinproj` | 02, reopened | No change |
| `tb1044-04-one-character.twinproj` | 01, reopened, then saved | `Form1.twin`'s `[Description("")]` became `[Description("x")]` |
| `tb1044-05-module-added.twinproj` | 02, in its session | Sources > Add > Add Module (.TWIN), keeping `MyModule.twin` |
| `tb1044-06-module-renamed.twinproj` | 05, in its session | `MyModule.twin` renamed `Helpers.twin` |
| `tb1044-07-folder-added.twinproj` | 06, in its session, after `Helpers.twin` was deleted, which saved bytes identical to 02 | Sources > Add > Add Folder, keeping `MyFolder` |
| `tb1044-08-form-added.twinproj` | 07, in its session | Sources > Add > Add Windows Form, keeping `MyForm` |
| `tb1044-09-setting-applied.twinproj` | 01, reopened, then saved | Project Settings: "Use Project ID for type library ID" set to Yes and applied, which saved the project itself |
| `tb1044-10-setting-then-saved.twinproj` | 09, in its session, after the compiler restarted from the file | No change |

Every file except 01 and 08 was made a second time by the harness and matched byte for byte. 01 and 08 can't be:
a new project gets a fresh `project.id`, and a new form a fresh `FormDesignerId`. The harness's own new-project run
differed from 01 only in those two GUIDs.
