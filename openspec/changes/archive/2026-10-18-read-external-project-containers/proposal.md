# Read an external project container without changing it

## Why

The first slice of #1122 needs access to the source and resources embedded in a project container.
The reader must be independently testable before IDE lifecycle and editing operations can use it.
This implements #1179. The full agreed roadmap, including persistent source-control folders and reuse
of the DAP client being built separately, has moved from the planning document into #1178.

## What changes

- Add a framework-independent library that reads version 1 containers into immutable snapshots.
- Retain metadata, ordered children and opaque content without extracting files or launching a backend.
- Report invalid input with offset/path context and enforce configurable resource limits.
- Compare published fixtures against an independent reader and run tests on desktop CI platforms.

## Impact

Adds a library and test project, solution entries, binary Git attributes and CI test steps. No IDE opening,
save, language-service or debugger path changes yet. The project-format seam (#1118), browse-only UI
(#1122) and synchronized folder feature (#1178) follow in separate PRs after CI and review.
