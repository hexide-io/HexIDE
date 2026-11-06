# `add_file` created a document under a name VB6 does not accept, and could write outside the project — **CLOSED** (#596, 2026-09-22)

> **Fixed.** `add_file` now applies `ProjectNaming`, the rule every other way of naming a document uses:
> `add_file {"name":"My Module","type":"module"}` answers `'My Module' is not a valid name. A name starts
> with a letter and continues with letters, digits and underscores. Nothing was added.`, and a name already
> used by any form, module or class, in any case, is refused the same way. A successful reply now carries a
> `note` saying that the project file does not list the new file until the project is saved, and when the
> file went to the project's temporary folder.

**Symptom.** `add_file {"name":"My Module","type":"module"}` on a saved project answered
`{"success":true,"path":"…\\My Module.bas"}` and wrote `Attribute VB_Name = "My Module"`. It was the one route
to such a document: Project → Add names new documents with `NextFreeName`, and adopting a file refuses an
invalid name. The tool also kept its own copy of the collision check.

**Security, and the larger half.** The name also reached a file path unchecked: `AddNew*` builds
`Path.Join(dir, name + ".bas")`, so `add_file {"type":"Module","name":"..\\..\\somewhere\\X"}` wrote a
VB6 source file outside the project folder, anywhere the user can write, and `set_file_content` could then
overwrite it. The extension is fixed (`.bas`, `.cls`, `.frm`, `.ctl`, `.pag`), and the server is DEBUG-only and
loopback-only, but it has no authentication (#352). It failed open, with success and the escaped path in
the reply. `IsValidName` admits no separator, dot or colon, which closes it: `..\x`, `../x` and `C:x` are
each refused, and nothing is written. The reviewer found this, not the audit.

**Why it mattered.** Nothing refused the name until something downstream tried to use it as an identifier.
The reply said "saves it to disk" while the `.vbp` did not list the file, so a caller checking the project
file concluded the add had failed.
