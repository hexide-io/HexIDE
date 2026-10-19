# Design

The existing `HexIDE.TwinBasic` reader library gains an immutable project/document projection and an
explicit lifecycle owner. There is no new generic filesystem framework and no reference to the VB6 runtime,
Avalonia, LSP or DAP. The UI integration will consume these APIs without making twinBASIC sources VB6 modules.

`ArchiveEntryIdentity` is a value compared by canonical origin path and ordinal name segments. Origin paths
use `Path.GetFullPath`, with ordinal-ignore-case comparison on Windows and ordinal comparison elsewhere;
symlink aliases are not resolved. Members always remain ordinal, including on case-insensitive hosts. A
name containing a slash, backslash, dot, percent sign or an empty string stays a literal segment. Joining
segments for display does not define identity. The root has zero segments, independent of its display name.
Unchanged paths retain identity across reloads, while renames deliberately create new identities. These
are internal addresses, not promises about future language-server URI support or physical export names.

The projection rejects duplicate sibling names with parent identity context. It must not silently overwrite
an entry in its lookup. The raw container reader still retains duplicates for inspection. Case variants and
same-named leaves in different directories remain distinct. Empty directories and original order survive.
Entries retain their original immutable source records, including hidden flags and unknown categories;
the model does not filter visibility. A later UI must choose how internal items are displayed.

Text access recognizes `.twin`, `.txt`, `.md`, `.json`, `.xml`, `.meta` and category 4 settings. It preserves
line endings, strips only an initial UTF-8 BOM from the preview, and retains all raw bytes. Invalid UTF-8,
unknown types and text beyond the caller's display limit (4 MiB by default, including BOM) return separate
statuses with no replacement text. JSON syntax is not validated: even malformed settings should be readable.
Designer `.tbform` data and resources remain opaque unless authoritative category metadata identifies a
settings entry. No language server receives preview text. Source highlighting and other encodings are later
consumer decisions, not an inferred parser capability.

`ArchiveProjectWorkspace` is an owner-serialized, synchronous API. A UI caller must schedule I/O off the UI
thread and serialize operations; this class does not claim thread safety. Open on a normalized existing
path is idempotent. Reload reads and validates a replacement before updating the collection, so failure or
observed cancellation retains the original snapshot. Old documents continue to reference old immutable
bytes; the next UI slice must deliberately rebind tabs by identity and handle removed members. File handles
are closed immediately after loading. Close has no save prompt or persistence operation.

Only explicit reload changes a snapshot: there are no watchers or speculative filesystem writes. FileShare.Read
blocks cooperative writers while reading, but no cross-process transactional guarantee is made against
native writers that ignore sharing. Future watching/conflict detection must account for that. Input and
depth limits remain the reader's policy; projection allocates identity/index/tree objects in addition to
the reader's content. No browser-host path integration is claimed by this desktop filesystem API.

The next PR should add the actual IDE open route and views. At that point read-only editor/service guards
and disabled build/run/debug commands become enforceable user-facing contracts. The model's lack of any
mutation or save API is a foundation, not a claim that those UI guards have already been implemented.
