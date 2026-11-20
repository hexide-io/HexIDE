# The `.twinproj` container, version 1 (#5140)

**What was measured.** Everything below was read from projects that twinBASIC IDE **BETA 1044** saved on
2026-11-20, in a Windows 11 Pro 26300 VM. The fixtures are `tb1044-01` to `tb1044-10` under
`IDE/HexIDE.TwinBasic.Tests/Fixtures/`. That folder's README says how each was made, and #5140's comments hold the
measurements as they were taken. The compiler was launch-and-watch only.

**Not taken as given.** docs.twinbasic.com describes this format, but the community reverse-engineered that
description; twinBASIC didn't publish it. `TwinBasicContainerReader` was built from it. Here each field is checked
against the IDE's own saves:
- **The layout fits.** Every fixture parses to its last byte under it.
- **Meanings are separate.** A field's meaning is stated only where a save moved it. A value seen only once is
  reported as seen, not as a rule.

## Layout

All integers are little-endian. The file is a 4-byte magic number followed by one entry, the root folder.

| Offset | Width | Field | On BETA 1044 |
|---|---|---|---|
| 0 | 4 | magic | `0xEA0BA51C` in every fixture |
| 4 | 2 | the root's first field | 1 in every fixture. The reader takes it as the format version, but no save has shown another value |
| 6 | | the rest of the root entry | as below, from the name-length field on |

Every entry, the root included, has this header at its position:

| From the entry's start | Width | Field |
|---|---|---|
| +0 | 2 | kind: 1 for a file, 2 for a folder (the root's is the version above) |
| +2 | 4 | name length *n*, in bytes |
| +6 | *n* | name |
| +6+*n* | 8 | revision |
| +14+*n* | 4 | flags |
| +18+*n* | 1 | category |

The header is followed by one of two bodies.

**A file:**

| From the entry's start | Width | Field |
|---|---|---|
| +19+*n* | 4 | content length *c* |
| +23+*n* | *c* | content |
| +23+*n*+*c* | 4 | revision trailer count *t* |
| +27+*n*+*c* | 4 × *t* | revision trailer |

**A folder:**

| From the entry's start | Width | Field |
|---|---|---|
| +19+*n* | 4 | child count |
| +23+*n* | | the children, one after another |

In `tb1044-01`, for example, the root's name length is at 0x6 (10), its name `NewProject` at 0xA, its revision at
0x14 (0), its flags at 0x1C (0), its category at 0x20 (0) and its child count at 0x21 (7). The first child,
`.meta`, starts at 0x25.

## What each field showed

**kind.** 2 for every folder, including the root's children and a folder added through the tree (`tb1044-07`); 1 for
every file. Nothing else was seen.

**name.**
- **The root's** is the project's name: `NewProject`, which `Settings`' `project.name` also holds.
- **The encoding is untested.** Every name in every fixture is ASCII, so whether a non-ASCII name is UTF-8 is unmeasured.
- **Rename:** renaming a module through the tree changes only its name and name length (`tb1044-06`).

**revision.** Every step seen is listed. What a revision counts isn't known.

| What happened | Files | Folders and root | Fixture |
|---|---|---|---|
| A new project's first save | from 0 (`#1.xml`) to 2692 (`Settings`) | 0 | 01 |
| Reopen, then save with no change | +2 each | unchanged, 0 | 02, 03 |
| One character typed in the editor | +2 for that file | unchanged | 04 |
| Settings applied through Project Settings | +1 for `Settings` | unchanged | 09 |
| Save after the compiler restarted from the file | +2 each, except `Form1.twin` +3 | unchanged | 10 |
| An entry added through the tree | the new entry is 0 | the new folder is 0 | 05, 07, 08 |

**flags.** 0 on every entry of every fixture. No save moved it.

**category.**

| Category | Entries |
|---|---|
| 0 | the root, `.meta`, and every entry inside a top-level folder, including subfolders such as `Resources/ICON` and a folder added to `Sources` |
| 2 | `Resources` |
| 3 | `Sources` |
| 4 | `Settings` |
| 5 | `ImportedTypeLibraries` |
| 6 | `Miscellaneous` |
| 7 | `Packages` |

Category 1 wasn't seen. No change moved an existing entry's category.

**content length and content.** Contents are opaque to the container. The text entries the IDE wrote have no
byte-order mark, and their line endings differ by where they came from:

| Entry | Written by | Line endings |
|---|---|---|
| `.meta` (the IDE's view state as JSON) | every save | LF |
| `Settings` (JSON) | the template | LF, keys in no particular order |
| `Settings` | Apply Changes in Project Settings (`tb1044-09`) | **CRLF, keys sorted** |
| `Form1.twin`, `Form1.tbform` | the template | CRLF |
| `MyModule.twin` | Add Module (`tb1044-05`) | **CR CR LF** |
| `MyForm.twin` | Add Windows Form (`tb1044-08`) | CRLF |
| `MyForm.tbform` | Add Windows Form | one line of JSON, no line ending |
| `Resources/MANIFEST/#1.xml` | the template | LF |

**revision trailer count.** 0 on every file of every fixture, so the trailer's meaning is unmeasured.

**child count.** It counts the children that follow. Adding an entry raises it by one, and a form adds two
(`MyForm.twin` and `MyForm.tbform`).

## Entry order

**The template's first save** (`tb1044-01`) writes the root's children in this order: `.meta`, `Settings`,
`Sources` (`Form1.twin`, `Form1.tbform`), `Resources` (`MANIFEST/#1.xml`, `ICON/twinBASIC.ico`),
`ImportedTypeLibraries`, `Packages`, `Miscellaneous`.

**After a close and reopen,** the next save writes every folder's children in **reverse order**, at every level
(`tb1044-02`). A second reopen reverses them back (`tb1044-03`). A save after the compiler restarted from the file
reverses them as well (`tb1044-10`).

**A new entry is written first in its folder:** a module, a folder and a form (`tb1044-05`, `07`, `08`). A form's
two entries go `.twin` then `.tbform`. Renaming keeps an entry's place (`tb1044-06`).

That would follow if loading a project added each entry to the front of its folder, as adding one through the tree
does. That's reasoned, not measured.

## Saving with no change

- **Two saves in one session are byte-identical.** That held for a new project's first and second saves, and after
  a reopen.
- **A save after a close and reopen differs** in its order and its files' revisions, and nothing else. Every
  content is unchanged (`tb1044-01` against `tb1044-02`).
- **Deleting an added module restores the bytes.** Its save was identical to the save from before the module was
  added.
- **Two new projects differ only in their GUIDs.** Made separately, they differed in `Settings`' `project.id` and
  in the form's `FormDesignerId`, which appears in both `Form1.twin` and `Form1.tbform`. Sizes, revisions and
  everything else matched.

So a writer can't aim for "the bytes the IDE would write next": the IDE's own saves of one unchanged project
differ. #5141 holds round-tripping to reading then writing the same file, carrying order and revisions as data.

## One change at a time

| Change | What the save changed | Fixture |
|---|---|---|
| One character in `Form1.twin`, typed through the editor | that file's content, its length and its revision (+2) | 04 |
| Add Module (.TWIN) | a new file entry with 60 bytes of content, first in `Sources`, whose count rises by one | 05 |
| Rename the module | its name and name length; content, place and revision unchanged | 06 |
| Delete it permanently | the entry and its count; the file returns to the bytes it had before the add | (not kept: identical to 02) |
| Add Folder | a new folder entry, first in `Sources` | 07 |
| Add Windows Form | two new file entries, first in `Sources` | 08 |
| A setting through Project Settings | the whole `Settings` entry rewritten (below), revision +1 | 09 |

`.meta` didn't change in any of these saves, though editor tabs opened and closed along the way.

**What Apply Changes does to `Settings`.** Applying "Use Project ID for type library ID" saves the project on its
own, with no CTRL+S. It then restarts the compiler from the file. The entry it writes isn't the old one plus one
key:
- its line endings become CRLF, and its keys are sorted;
- each reference gains what the dialog's references list resolved, such as package names, `symbolId` and
  `publisher`;
- `project.warnings` gains empty `hints`, `info` and `warnings` lists;
- the setting itself is added.

Applying before that list has loaded wrote a different `Settings`, without the resolved references. The harness
therefore waits for it.

## Not explained yet

- **What a revision counts.** Most steps are 2, but `Settings` rose by 1 on Apply and `Form1.twin` by 3 after the
  compiler's restart.
- **Why reopening reverses the order,** beyond the reasoning above.
- **Why Add Module writes CR CR LF,** where the template and Add Windows Form write CRLF.
- **When `.meta` is rewritten.** No save within a session changed it.
- **Flags, the revision trailer and category 1.** Never seen other than 0, empty and absent.
- **Non-ASCII names.**
- **What the IDE does with a container HexIDE wrote.** #5143 measures it, once #5141's writer is in: the IDE
  reopens HexIDE's save and shows the edit.
