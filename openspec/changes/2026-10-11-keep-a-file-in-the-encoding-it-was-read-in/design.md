# Design

## The order a file's encoding is decided in

Every read of a VB6 source file (project load, Add File, and reload after an external change) decides
its encoding by the same chain, and stops at the first step that applies:

1. **A UTF-8 byte-order mark** declares UTF-8. It is kept on write.
2. **The encoding the document was last read in**, on a reload, if the bytes decode in it strictly and
   encode back to themselves. This is what keeps a file that lost its byte-order mark to an external editor
   in UTF-8. It is skipped when the last read was step 5: Latin-1 accepts anything, so a file once read byte
   for byte would otherwise stay Latin-1 on every reload, however readable its new bytes.
3. **The signature of a file earlier versions wrote** (phase 2): BOM-less bytes that are valid UTF-8 and
   decode to at least one character above U+00FF. Read as UTF-8, written back without a byte-order mark.
4. **The host's ANSI code page** (the Windows ANSI code page; Windows-1252 elsewhere), if the bytes decode
   strictly and encode back to themselves.
5. **Latin-1**, which maps all 256 byte values to characters and back, so it always succeeds and always
   gives the bytes back. The caller is told this step was reached, and warns.

Step 2 is tried before step 4 and is not final: if a file recorded as UTF-8 is replaced by a revision that
is not valid UTF-8 (a `git checkout` of an ANSI-era revision, say), the chain falls through rather than
failing.

**Every step also checks the round trip, as defence.** With .NET's code page tables it never fires: over
every 1- and 2-byte input in code pages 874, 932, 936, 949, 950 and 1250 to 1258, every strict decode
encodes back to the same bytes (measured 2026-10-11). Where a double-byte code page spells one character
two ways, such as code page 932's `87 90` beside `81 E0` for `≒`, the strict decoder itself refuses the
spelling the encoder does not write.
The check stays so that a provider which maps differently is refused rather than written back changed. It
is the one part of the chain no test can reach.

**A byte-order mark over bytes that are not UTF-8** is kept, and steps 2 to 5 read what follows it. Decoded as
text, the mark would sit in front of the first line, the header would not be recognised, and an unedited save
would add a second one. The document's encoding then records the mark beside whichever code page read the
rest, so the file is written back as it came.

**Why the chain cannot fail.** Step 5 accepts every input. So decoding never throws during load, Add File or
reload, and nothing downstream needs to handle a decode failure. `Vb6TextFile.Decode` with an explicit
encoding still throws, for callers that asked for exactly that encoding.

## What a fallback costs, and where it shows

A file read at step 5 shows its non-ASCII bytes as the Latin-1 characters with those code points. For a
Western file on a Western machine that is nearly identical to what the code page would have shown. For a
file in a double-byte code page it is mojibake. In both cases an unedited save returns the file
byte-for-byte, and an edit Latin-1 cannot hold is refused by the ordinary unencodable-edit refusal, naming
code page 28591.

## How the developer is told

A load collects the members it read byte for byte and shows one notice for all of them, after the load's own
errors rather than among them. Listed under the error dialog's failure heading they read as failures, and ten
of them would push a real error past its cap. A member is listed only once it has joined the project: the
notice says the file will save back unchanged, which is untrue of one that then failed to load. Add File, a
group file and a reload each show the single-file form of the same notice.

## Limits this does not change

- **Line endings.** A module saved from LF line endings is written with CRLF, which is VB6's format. That
  changes bytes but re-encodes nothing, and predates this change.
- **A byte-order mark removed outside the IDE.** The reload keeps the file in UTF-8, as step 2 intends. The
  next open has no earlier read to consult, so unless the text has a character above U+00FF (phase 2) it
  is read in the host's code page. Text typed in between was written as UTF-8 and then shows garbled.
- **Save Project As to another folder** writes a kept member line with its old relative path, as it already
  did for a missing form's line (#737).

## Testing on a single-byte host

Windows-1252, the code page on CI and on the development machines, defines all 256 bytes in .NET's
implementation, so step 5 is unreachable there through step 4. The host's ANSI code page is therefore a
property the project service reads through a seam tests can set, rather than the static alone.
