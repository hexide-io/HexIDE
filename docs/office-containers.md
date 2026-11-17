# Office containers

`IDE/HexIDE.Office` reads compound files and locates an Office document's VBA project
([#4329](https://github.com/hexide-io/HexIDE/issues/4329)). It has no package dependencies or IDE callers.
It does not interpret the project streams: [MS-OVBA] decoding and IDE integration are later slices
(#4330 and #4331). Legacy PowerPoint needs MS-PPT decoding and is refused with that reason.

The reader was authored from Microsoft's [MS-CFB specification](https://learn.microsoft.com/en-us/openspecs/windows_protocols/ms-cfb/53989ce4-7b05-4f8d-829b-d08d6148375b),
sections 2.2–2.7 (October 26, 2026 edition). It supports version 3 (512-byte sectors), version 4
(4096-byte sectors), DIFAT extension sectors, FAT, mini FAT, the mini stream and directory trees.
Child lookup walks all children and compares names using Unicode simple uppercase of each UTF-16 unit,
including the specification's rule to leave surrogate units unchanged. It does not assume the stored
sibling tree is sorted. The dotless-i mapping follows [UnicodeData](https://www.unicode.org/Public/17.0.0/ucd/UnicodeData.txt),
where .NET invariant casing differs from Unicode simple uppercase. Version 3 stream sizes ignore the high DWORD, as MS-CFB 2.6.3 requires for
older producers.

`CompoundFile.Open` takes a readable, seekable stream. It leaves that stream open by default;
`leaveOpen: false` transfers ownership on success. Keep the reader and its backing stream open
while using entries. `Root`, `Children`, `FindChild`, `Name`, `IsStorage` and `Length` expose the
read-only tree; `ReadBytes` reads a stream on request. The root's length is the mini stream's size,
not the sum of its children. Reads on the same reader are serialized. Callers must not mutate or
independently reposition/read the backing stream during operations.

A file may end inside its last sector, after that sector's last used byte, as some real files do and as
Windows' own reader accepts; a read that runs past the end is refused. Opening reads metadata, not every stream
or the whole mini stream. Chains are bounded by the file's
physical sectors and checked for cycles and out-of-range references. A stream's chain is walked and
its capacity checked before allocating its bytes. Requests exceeding .NET's byte-array limit are
refused. Stream-chain errors can therefore surface when `ReadBytes` is called, rather than at open.
Unreadable content and I/O failures use `OfficeFileException`, with an English `Reason` for callers;
these are library diagnostics, not IDE chrome.

`VbaProjectLocator.Locate(path)` returns a disposable result with three distinct statuses:

- `Found`: `Project` is the project's storage. Dispose the result after reading its streams.
- `NoProject`: the container was read, but does not declare a project.
- `Unreadable`: `Error` is an `OfficeFileException` explaining the refusal.

Content signatures select zip or compound-file reading; the filename extension does not decide.
For zip containers, the locator reads the `application/vnd.ms-office.vbaProject` override in
`[Content_Types].xml`, and opens that compound-file part's root. Each zip part read, including the
content-types part, is limited to **64 MiB**, enforced against both its declared size and the actual
bytes decompressed. XML DTD processing is prohibited. Missing or duplicate declared parts and
multiple project overrides are refused. Part names use OPC's
[ASCII case-insensitive equivalence](https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/8.0/system-io-packaging-case-insensitive-uri). For compound documents, `_VBA_PROJECT_CUR` and `Macros`
identify the project storage; `PowerPoint Document` identifies the unsupported legacy PowerPoint
layout.

Tests in `IDE/HexIDE.Tests/Office` verify every fixture entry, stream length and SHA-256 against
[the independent Windows structured-storage corpus](../corpus/office/compound-files/README.md).
The VBA bytes there are placeholders. Corruption tests patch copies in memory; the corpus remains
unchanged. No VB6 runtime behaviour or oracle measurement is involved, and language coverage counts
are unchanged.
