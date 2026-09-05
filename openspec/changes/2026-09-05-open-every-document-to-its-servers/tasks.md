# Tasks

## 1. The shared document session

- [ ] 1.1 `LspDocumentSession` in `HexIDE` — owns a document URI, an `ILspClient` and an AvaloniaEdit
      `TextDocument`; opens on start, closes on dispose, sends debounced changes, and raises markers
- [ ] 1.2 Diagnostics matched with `LspDocumentUri.AreSame`, never `!=` — a server that normalises the URI
      it echoes back drops every diagnostic silently, which is what #236 was
- [ ] 1.3 The range-to-offset conversion moves across whole, including all three clamps. The server's copy
      of a document is always slightly stale, so a range past the end of the buffer is normal traffic, not
      a fault
- [ ] 1.4 Conversion posted to the UI thread — `TextDocument` refuses access from anywhere else, and the
      failure is an exception on a background thread rather than a wrong answer
- [ ] 1.5 A hook for what the owner wants to do on each diagnostics arrival, so the code editor keeps
      piggybacking its symbol refresh without the session knowing what a symbol is
- [ ] 1.6 Flush — cancel the debounce and send immediately, for requests that need the server current

## 2. The VB6 code editor moves onto it

- [ ] 2.1 `CodeEditorViewModel` constructs a session and delegates open, change, close and diagnostics
- [ ] 2.2 The per-request methods keep calling the client directly, with the session's URI. Only the
      lifecycle moves — this is not a refactor of everything that touches LSP
- [ ] 2.3 No behaviour change. The existing editor tests are the check, and they SHALL not be edited to
      accommodate this; if one needs changing, the migration is wrong

## 3. The carried-file editor gains it

- [ ] 3.1 `RelatedDocumentEditorViewModel` takes an `ILspClient` and constructs a session
- [ ] 3.2 Identified by a `file:` URI built from the absolute path
- [ ] 3.3 A document with no path on disk is not offered — there is nothing to identify it by, and a
      project that has never been saved is the case (#260)
- [ ] 3.4 A file that could not be read is not offered either. The editor already opens empty and read-only
      rather than lying about content; sending that empty buffer would have a server publish diagnostics
      about a document nobody has
- [ ] 3.5 Closed when the editor closes, like any other document

## 4. The view renders it

- [ ] 4.1 `RelatedDocumentEditorView` attaches `LspTextMarkerService` to its editor and subscribes to the
      view model's markers — the same control the code editor uses, so it attaches unchanged
- [ ] 4.2 Unsubscribed and detached on unload, symmetrically. A marker service outliving its editor holds
      the document alive

## 5. Tests

- [ ] 5.1 A carried file is opened to the client, with a `file:` URI carrying its extension
- [ ] 5.2 Its diagnostics become markers; a diagnostic for a different document does not
- [ ] 5.3 A URI the server has normalised differently still matches — the #236 case, now in a second editor
- [ ] 5.4 Editing sends a change; closing the editor closes the document
- [ ] 5.5 A document with no path, and one that failed to load, are never opened
- [ ] 5.6 A range past the end of the buffer is clamped rather than throwing
- [ ] 5.7 The code editor's existing behaviour is unchanged — by its existing tests, unedited
- [ ] 5.8 Each verified to fail without its fix, by mutation. The last change found a mechanism no test
      covered by exactly this method, and found it only because the method was applied

## 6. Verification

- [ ] 6.1 Suites green on Windows and under WSL
- [ ] 6.2 **Driven against the running IDE**: a real `lsp-servers.json` naming a real foreign server, a
      carried Markdown file opened from the Project Explorer, and its diagnostics visible in the editor.
      This is the check the whole change exists to pass, and the previous change shipped without being
      able to make it
- [ ] 6.3 A carried file no server claims opens with nothing started and nothing reported

## 7. Deliberately not here

- [ ] 7.1 Hover, completion, signature help, formatting, rename (#266) — view work, and it wants one
      deliberate pass over both editors rather than a second completion window
- [ ] 7.2 `didSave` (#267) — a client and protocol gap, equally present for the VB6 editor
- [ ] 7.3 Any UI showing what is attached (#259)
- [ ] 7.4 Merging the two editors. Sharing one narrow collaborator is what makes keeping them separate
      affordable, not a step towards combining them
