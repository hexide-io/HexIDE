# Model archive-backed projects and documents

## Why

The merged container reader (#1181) can inspect bytes, but does not provide document identity, bounded
text access or an open/reload/close lifecycle. Those are needed before a read-only project can safely join
the IDE. Existing project/document definitions assume VB6 forms and modules; forcing foreign source into
them would also offer that source to the VB6 serializer, interpreter and language layer.

## What changes

- Project snapshots expose the complete ordered tree, metadata and opaque document content.
- Entry identity combines a normalized container path with exact archive-name segments, without resolving
  those names as filesystem paths or treating display names as identifiers.
- Known text types have bounded, strict UTF-8 previews, including distinct refusal states.
- Explicit open/reload/close operations publish complete snapshots only and never write to disk.

## Impact

Implements #1184, part of #1122 and roadmap #1178. This is a model/lifecycle API, not a new UI opening path.
The next slice connects Project Explorer, read-only tabs and command guards, coordinating with #1118.
No changes to VB6 project definitions, language-service or debugger routing. The persistent source-control
folder remains a separate first-class feature; this model supplies its original bytes and metadata.
