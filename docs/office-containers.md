# Office containers

`IDE/HexIDE.Office` reads compound files and locates an Office document's VBA project
([#4329](https://github.com/hexide-io/HexIDE/issues/4329)). It has no package dependencies or IDE callers.
It also reads VBA project metadata and source ([#4330](https://github.com/hexide-io/HexIDE/issues/4330));
IDE integration remains #4331. Legacy PowerPoint needs MS-PPT decoding and is refused with that reason.

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

## VBA projects

`VbaProjectReader.Read(result.Project)` reads the storage found by the locator. The implementation
is authored from [MS-OVBA](https://learn.microsoft.com/en-us/openspecs/office_file_formats/ms-ovba/575462ba-bf67-4190-9fac-c275523c75fc),
revision 15.0 (May 19, 2026), sections 2.3.1, 2.3.4.2, 2.3.4.3, 2.4.1 and 2.4.3.
No other reader's code is used. The model includes project information, references (registered,
project, control and the control's original library), module names, stream names, source offsets,
read-only/private flags, editor kinds and decoded source. `VbaDirectory.Read` accepts already
decompressed dir bytes; `VbaCompression.Decompress` accepts an MS-OVBA compressed container.

Unicode record forms take precedence. Source encoding tries the declared code page, then Windows
ANSI pages 874, 932, 936, 949, 950 and 1250–1258, accepting a page only when every available Unicode
module name matches its decoded MBCS name. ASCII names retain the declared page. If no page matches,
the reader refuses rather than choosing an unexplained encoding. The `dir` strings with no Unicode
form, which are the project's name, its help file and reference paths, are decoded with the same page as the source. Encodings come from
`CodePagesEncodingProvider.Instance`, without changing process-wide registration.
This policy follows the [real Office corpus](../corpus/office/m365-x64/README.md): its Japanese files
declare 1252 but store names and source as cp932. It is not a new VB6 runtime measurement.

**What the VBA editor shows of such a file, measured 2026-11-16** in Excel 365 (64-bit) on a machine whose ANSI code page
is 1252. The editor shows `cp932.xlsm`'s module name, in the Project Explorer and the code window's caption, and its source
as the cp932 bytes read as 1252, which is mojibake. The Properties window shows its (Name) as eight question marks. So
reading the Japanese, as this reader does, shows more than VBA does on that machine. That is not a fidelity error,
because the reader only reads. Whether VBA follows the machine's code page or the declared one is unmeasured, since
both were 1252 there; it matters only once a project is written back.

The PROJECT stream supplies standard/class/document/UserForm kinds. Its GC visibility field is
decrypted using MS-OVBA 2.4.3; a project marked not visible is returned locked, with names and kinds
but null source. The reader does not read locked module streams. CMG and DPB are retained as opaque
protection fields; passwords are not recovered. UserForm designer storages are held as compound
entries, without decoding their MS-OFORMS contents (#1114). Keep the locator result open while using
these borrowed entries. Module performance-cache bytes preceding MODULEOFFSET and `_VBA_PROJECT`
are never interpreted or executed.

Each decompression, including dir and each module, has a **16 MiB** output limit (callers can lower
it). Chunk signatures, sizes, token bounds, chunk-local copy offsets, record bounds and module
source offsets are checked and refused with `OfficeFileException`. Reasons are program-facing
English; the library adds no IDE chrome or localization keys.

`VbaProjectReaderTests` compare all eleven real Office files against the independent
`oletools-expected.json` metadata and decompressed source hashes, as well as lines recorded by the
corpus build scripts, Japanese decoding, references, viewing locks, UserForms and equivalent Excel
containers. Locked source hashes are checked through the decompressor separately from the public
project model, which withholds source. Synthetic corruption and record-edge tests supplement these
real-file checks. This adds no executable language surface: MISSING_LANGUAGE counts and IDE feature
statuses are unchanged. 32-bit Office remains unmeasured; the supplied corpus was saved by 64-bit Office.
