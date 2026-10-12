# Keep a file in the encoding it was read in

Tracks hexide-io/HexIDE#728, and finishes the change in #729.

## Why

VB6 writes its source files (`.vbp`, `.vbg`, `.frm`, `.bas`, `.cls`, `.ctl`, `.pag`) in the authoring
machine's ANSI code page, and records nothing about which one. Until #729 the IDE guessed: bytes that were
valid UTF-8 were read as UTF-8, and anything else as Latin-1. It then chose the write encoding separately,
Latin-1 whenever the text fitted. The two rules disagreed, so a legacy file whose accented bytes happened to
form valid UTF-8 (`C3 A9`, which Windows-1252 reads as `Ã©`) came back from a save changed, on a line nobody
edited.

#729 replaces the guess with provenance: each document remembers the encoding it was read in and is written
back in it. A review of #729 found that the stricter reading it introduced creates new ways to lose work:

- a file the host's code page cannot decode, such as a Western file on a Japanese machine, is left out of
  the project, and the next Save Project removes its line from the `.vbp`;
- a reload that fails to decode leaves the old text in place, and the next save writes it over the change;
- files earlier versions of the IDE wrote as UTF-8 without a byte-order mark open garbled;
- several save paths (Make, adding a new member, automation) bypass the refusal #729 added, and an
  automation save blocks on a dialog nobody can see.

## What changes

- **No file is ever dropped for its encoding.** A file the code page cannot read is loaded byte-for-byte
  instead (as Latin-1, which maps every byte), saved back unchanged, and named in a warning.
- **A member that fails to load for any reason keeps its line** in the project file, as a missing file
  already does.
- **Files earlier versions wrote as UTF-8 are read as UTF-8.** Those versions wrote BOM-less UTF-8 only
  when the text held a character above U+00FF, so exactly that signature is honoured, and nothing else.
- **Every save path refuses an unencodable edit the same way**, before writing any file of the operation,
  and an automation caller gets the refusal in its reply rather than a dialog.

## Decisions

- **Migration by signature, not by prompt.** Decided by the maintainer on 2026-10-11. A per-file prompt
  showing both readings was rejected: it is the encoding picker #729 deliberately left out, for a case the
  signature already identifies. Leaving the break documented was rejected because it garbles every such file
  for every user who upgrades. The signature re-admits a guess, but a narrow one, and a wrong guess can no
  longer damage bytes: the file is written back in whatever encoding it was read in.
- **Latin-1 as the last resort, not a lenient decode.** A lenient decode in the code page substitutes
  characters and cannot give the bytes back. Latin-1 can, for every input. The cost is display: a file that
  really is in another code page shows as mojibake, and characters outside Latin-1 cannot be typed into it.
- **What "ANSI" means on Windows set to UTF-8 is not decided here.** It depends on what VB6 itself does on
  such a machine, which needs the oracle VM; it gets its own issue.

## Impact

- `serialization-round-trip` gains the encoding requirements.
- `HexIDE.Core/IDE/Vb6TextFile.cs`, `HexIDE/Projects/ProjectService.cs`, the automation save tools.
- One or more new localized strings, each translated into the 29 shipped packs.
