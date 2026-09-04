# Tasks

## 1. Model — carry the file without compiling it

- [x] 1.1 Add a definition type for a carried file, holding its name, path, and the original item line where
      one must be preserved
- [x] 1.2 Give the project its own collection for carried files, separate from modules
- [x] 1.3 Recognise the carried-file key on read, counting it as a known key
- [x] 1.4 Reclassify a non-source file found on a module or class line, preserving its original line
- [x] 1.5 Leave a file with no extension as a module — an existing claim is believed
- [x] 1.6 Emit the modern key for a carried file the developer added, and the preserved line for one that was
      reclassified
- [x] 1.7 Fixtures for all of the above. The round-trip corpus cannot cover this: the shipped template tree
      contains no carried-file entry at all, so a regression would pass that gate silently

## 2. View — see it and edit it

- [x] 2.1 A Project Explorer node for a carried file, placing itself in the filesystem hierarchy and picking
      up the out-of-cone location caption
- [x] 2.2 A plain text editor view model — no designer, no procedure navigation, no language server, no
      faithfulness gate, no companion binary
- [x] 2.3 Preserve the file's byte-order mark across a save, and write through a temp file so an interrupted
      save cannot truncate the original
- [x] 2.4 Show a banner and open read-only when the file cannot be read
- [x] 2.5 Route opening by member kind, through its own door rather than the code editor's
- [x] 2.6 Register the view — the view locator uses an explicit table, and an unregistered view renders as a
      blank pane with no error
- [x] 2.7 Choose highlighting from the file's own extension, and none when unrecognised
- [x] 2.8 Re-tint a bundled highlighting definition for a dark background, preserving hue and meeting the
      contrast bar (closes #250)

## 3. Add — adopt a file that already exists

- [x] 3.1 Classify a picked file by extension, defaulting to carried
- [x] 3.2 Exclude the VB6 file kinds the IDE does not model, so the dialog does not promise a designer that
      does not exist
- [x] 3.3 Service methods that adopt an existing form, module or carried file, reading through the same path
      project load uses so an adopted member is indistinguishable from a loaded one
- [x] 3.4 Name an adopted module from its own header attribute rather than its filename
- [x] 3.5 Add nothing at all when a form will not parse
- [x] 3.6 Open an already-carried file rather than adding it twice, comparing paths by the host's case rule
- [x] 3.7 Bind both entry points — the project menu and the toolbar flyout — reusing existing localization
      keys (closes #247)

## 4. Cross-platform correctness

- [x] 4.1 Own the project-file path rule in one place: last-segment, to-host, and to-project-file
- [x] 4.2 Apply it at every read and write site, including the two emission sites that predate this change
- [x] 4.3 Collapse the duplicate copy of the read-side rule that had drifted from it
- [x] 4.4 Fix the three round-trip tests whose expectations were composed with a host path API, and which
      therefore certified the defect on the platform where it mattered
- [x] 4.5 Tests stating the rule directly, not only through a round-trip

## 5. Verification

- [x] 5.1 Full suites green
- [x] 5.2 Driven against the running IDE: add a document and a module, confirm each lands in the tree with the
      right icon and opens in the right editor, confirm re-adding opens rather than duplicates, and confirm a
      save leaves both files byte-unchanged
- [x] 5.3 Record the tooling gaps hit while verifying
- [ ] 5.4 Confirm green on the Linux CI job — the only platform that can falsify section 4, and not
      reproducible locally

## 6. Follow-ups deliberately not done here

- [ ] 6.1 Virtual folders for out-of-cone carried files, needing a custom project-file section
- [ ] 6.2 A rendered preview for carried formats that have one
- [ ] 6.3 Ctrl+S in a module code editor (#244) — measured during this work, filed rather than fixed, because
      it belongs to the code editor's save wiring rather than to this member kind
