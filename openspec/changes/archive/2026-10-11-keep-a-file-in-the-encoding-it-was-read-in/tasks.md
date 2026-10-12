# Tasks

## 1. No file is dropped for its encoding

- [x] 1.1 One decision chain for every read (design.md): byte-order mark, the encoding last read in,
  the host's ANSI code page with a round-trip check, then Latin-1. It reports which step was taken.
- [x] 1.2 Project load, Add File and reload all use it, so none of them can fail to decode. A file read
  as Latin-1 is named in a warning, localized into the 29 shipped packs.
- [x] 1.3 A form or module that fails to load for any reason keeps its line in the project file.
- [x] 1.4 Tests: a Western file on a double-byte host loads and saves back byte-for-byte; a member whose
  load throws keeps its `.vbp` line; a file recorded as UTF-8 and replaced by an ANSI revision reloads.

## 2. Files earlier versions wrote as UTF-8

- [x] 2.1 The signature step: BOM-less, valid UTF-8, at least one character above U+00FF.
- [x] 2.2 Tests: such a file reads as UTF-8 and saves back byte-for-byte; a file without a character above
  U+00FF still reads as ANSI (the #728 case).
- [x] 2.3 The PR description and `Vb6TextFile`'s documentation say the narrowed guess is back, and why.

## 3. Every save refuses an unencodable edit the same way

- [x] 3.1 Make Project refuses through the same warning as Save, for forms and the project file.
- [x] 3.2 Adding a new form or module whose name the code page cannot hold is refused the same way.
- [x] 3.3 A save of several files checks that every one encodes before writing any.
- [x] 3.4 An automation save returns the refusal in its reply, naming the document, the code page and the
  first character it cannot hold, and shows no dialog.

## 4. Tidying

- [x] 4.1 A designer module keeps one encoding, not one on the module and another on its designer part.
- [x] 4.2 Readers with no callers left are removed.

## 5. Record

- [x] 5.1 File an issue for what "ANSI" should mean on Windows set to UTF-8, needing the oracle.
- [x] 5.2 CHANGELOG, and the PR description brought up to date.
- [x] 5.3 Validate strictly, archive, and check the merged spec's Purpose.
