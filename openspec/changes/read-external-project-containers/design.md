# Design

The implementation is `HexIDE.TwinBasic`, separate from the VB6 runtime and editor. Its synchronous stream
reader snapshots at most 64 MiB by default, then parses little-endian fields using checked spans. A later
UI caller should schedule blocking I/O away from its UI thread. The caller owns the stream; offsets are
relative to its starting position. Read cancellation is checked between 64 KiB reads, at each entry and
within revision trailers; it cannot interrupt a synchronous stream read that itself blocks.

Root version 1 denotes a directory, whereas non-root kind 1 denotes a file. Empty directories, insertion
order, duplicate names, unknown flags/categories, 64-bit revisions and revision trailers are retained.
Entry names are strict UTF-8; content is opaque immutable bytes. Names never become local filesystem paths.
Higher layers must resolve duplicate identities or unrepresentable names before offering file operations.

Default limits are 100,000 entries (including root), depth 128 (root zero), and 16 KiB per name. Depth cannot
be configured above 256 because the implementation recurses. The byte limit bounds serialized input, not
total managed memory: snapshot buffers, immutable content and tree objects add overhead. Lengths/counts
are validated before allocation, and unexplained trailing bytes and unknown structural kinds are refused.
There is no claim of future container compatibility or a writer.

Reference: https://docs.twinbasic.com/Features/Packages/File-Format (consulted 2026-10-18).
The MIT-licensed documentation repository provides two real published fixtures and the independent
`impexp.mjs` parser used for expected manifests, pinned in the fixture README. Its JavaScript numbers
cannot establish preservation above 2^53; a separate synthetic case verifies a full-width UInt64 revision.
Synthetic cases also exercise non-zero revision trailers, invalid input and cancellation. The fixture
provenance does not assert that the impexp project was saved by the vendor IDE; it is the upstream packed
project. An IDE-authored project acceptance pass remains part of the later UI milestone.

The persistent synchronized folder is a first-class follow-up feature, not a hidden temporary directory.
This reader supplies the immutable content/metadata it needs without committing to extraction or a
persistence authority. The existing DAP session and UI work is a dependency, not duplicated here.
