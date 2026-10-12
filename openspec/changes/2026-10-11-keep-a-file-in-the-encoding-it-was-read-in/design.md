# Design

## The order a file's encoding is decided in

Every read of a VB6 source file (project load, Add File, and reload after an external change) decides
its encoding by the same chain, and stops at the first step that applies:

1. **A UTF-8 byte-order mark** declares UTF-8. It is kept on write.
2. **The signature of a file earlier versions wrote**: valid UTF-8 throughout, with at least one character
   above U+00FF. Those versions wrote UTF-8, without a byte-order mark, exactly then, and Latin-1 otherwise.
   Read as UTF-8 and written back the same way. Accented text whose bytes merely form valid UTF-8, the #728
   case, has no such character and goes on to step 4. It comes before step 3 so that a reload and a fresh
   open read the same bytes the same way.
3. **The encoding the document was last read in**, on a reload, if the bytes decode in it strictly and
   encode back to themselves. This is what keeps a file that lost its byte-order mark to an external editor
   in UTF-8. It is skipped when the last read was step 5: Latin-1 accepts anything, so a file once read byte
   for byte would otherwise stay Latin-1 on every reload, however readable its new bytes.
4. **The host's ANSI code page** (the Windows ANSI code page; Windows-1252 elsewhere), if the bytes decode
   strictly and encode back to themselves.
5. **Latin-1**, which maps all 256 byte values to characters and back, so it always succeeds and always
   gives the bytes back. The caller is told this step was reached, and warns.

Step 3 is tried before step 4 and is not final: if a file recorded as UTF-8 is replaced by a revision that
is not valid UTF-8 (a `git checkout` of an ANSI-era revision, say), the chain falls through rather than
failing.

**Every step also checks the round trip, as defence.** With .NET's code page tables it never fires: over
every 1- and 2-byte input in code pages 874, 932, 936, 949, 950 and 1250 to 1258, every strict decode
encodes back to the same bytes (measured 2026-10-11). Where a double-byte code page spells one character
two ways, such as code page 932's `87 90` beside `81 E0` for `≒`, the strict decoder itself refuses the
spelling the encoder does not write.
The check stays so that a provider which maps differently is refused rather than written back changed. It
is the one part of the chain no test can reach.

**A byte-order mark over bytes that are not UTF-8** is kept, and steps 3 to 5 read what follows it. Decoded as
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

## How a save refuses

Every save goes through one path, in three steps:

1. **Choose.** Editor buffers are flushed into the model once. Then each file's destination is settled:
   - a document with no file yet, or a Save As, asks the developer;
   - a document HexIDE cannot reproduce is left out and reported, as before;
   - after the last picker, that is decided again. A picker waits on the developer, and the file watcher
     can reload a document changed on disk meanwhile, so one that passed before a later picker may not now.
2. **Check.** Every file is rendered as it will be written and encoded in its own code page.
3. **Write.** Only once every file has passed is the first one written.

So one character a code page cannot hold leaves every file of the operation as it was. This holds for:

- Save and Save As;
- Save Project and Save All;
- Make Project Group;
- the save offered when a project closes;
- writing a new project to its folder.

A file is checked as it will be at its destination. A form names its companion by file name, and a project
or group file names its members by where they are going. So Save Project As to another folder is checked
against the paths the new project file will hold, not the old ones.

A Save As also checks the file that will name the document in its new place, when that file is not part of
the save: a form or module's project file, or a project's group file. A name that file cannot hold is refused
then, in that file's name. Before, the document was written and repointed, and every later save of the
project refused, naming the project rather than the document. A file that cannot be written as it stands is
left for its own save to refuse; a Save Form As is not stopped for a project file it does not write.

Make checks the files it will package, each in its own code page, before it asks where to put the program. No
source file's text depends on that answer, and the question is wasted if the answer will not be used. The
same reasoning keeps the faithful-save refusal above the Save As picker, and puts Make's there too: a form or
module HexIDE cannot reproduce is refused before the picker, all of them named in one warning.

Adding a new form or module checks two things before a folder is chosen or anything is created: the new
file itself, and the line the project file will hold for it. A refusal therefore leaves nothing behind.

A refusal names the document, its code page, the first character it cannot hold and the line of the file
that holds it. The character is not always in the edit just made: a form is written whole, so it can be in a
property set earlier, and a project file names its members by file, so it can be in a name. The line says
which. A person sees a warning that says nothing was written and quotes that line; it replaces one that named
only the document and the code page, and said that document "was not saved because an edit" held the
character, which a batch, a path and a Make each made untrue. An automation tool saves with
`interactive: false`: no dialog is shown, and the refusal comes back in the tool's reply. Before this, the
reply waited on a dialog nobody could answer (#751). A refusal for a file HexIDE cannot reproduce is quiet
in the same way.

Behaviour that changes with it:

- **Pickers come first.** Every picker an operation needs is answered before anything is written.
  Dismissing one part-way now leaves every file as it was; before, the files saved ahead of it stayed
  written.
- **Save Project As** asks where the project file goes before writing any member.
- **Writing a new project to its folder** now refuses a file HexIDE cannot reproduce before the first file,
  not after the forms, and announces each form it writes to the language server, as it already did modules.
- **The sidecar** is saved after the last VB6 file of the operation, not after each project file.
- **Make** refuses an unencodable document, or one HexIDE cannot reproduce, before asking for the program's
  place, and before checking for the standalone runtime.
- **Make Project Group** asks for every destination, the group file's included, before writing any project.

Only what can be decided in advance is checked in advance. An I/O failure part-way through still leaves the
files written before it.

## Limits this does not change

- **Line endings.** A module saved from LF line endings is written with CRLF, which is VB6's format. That
  changes bytes but re-encodes nothing, and predates this change.
- **A byte-order mark removed outside the IDE.** The reload keeps the file in UTF-8, as step 2 intends. The
  next open has no earlier read to consult, so unless the text has a character above U+00FF (step 2) it
  is read in the host's code page. Text typed in between was written as UTF-8 and then shows garbled.
- **The signature can be wrong, rarely, and only for display.** A Windows-1252 file that happens to hold
  `â€™` (bytes `E2 80 99`, often itself the trace of an earlier mis-decoding) is valid UTF-8 for `’`, and is
  read that way. Its bytes still come back unchanged, since it is written back as UTF-8.
- **Save Project As to another folder** writes a kept member line with its old relative path, as it already
  did for a missing form's line (#737).
- **A carried file's editor** writes outside this path, as UTF-8, and rewrites any byte that is not valid
  UTF-8 even when nothing was edited (#745).

## Testing on a single-byte host

Windows-1252, the code page on CI and on the development machines, defines all 256 bytes in .NET's
implementation, so step 5 is unreachable there through step 4. The host's ANSI code page is therefore a
property the project service reads through a seam tests can set, rather than the static alone.
