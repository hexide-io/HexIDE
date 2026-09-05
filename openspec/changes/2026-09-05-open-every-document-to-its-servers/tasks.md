# Tasks

## 1. The shared document session

- [x] 1.1 `LspDocumentSession` in `HexIDE` — owns a document URI, an `ILspClient` and an AvaloniaEdit
      `TextDocument`; opens on start, closes on dispose, sends debounced changes, and raises markers
- [x] 1.2 Diagnostics matched with `LspDocumentUri.AreSame`, never `!=` — a server that normalises the URI
      it echoes back drops every diagnostic silently, which is what #236 was
- [x] 1.3 The range-to-offset conversion moves across whole, including all three clamps. The server's copy
      of a document is always slightly stale, so a range past the end of the buffer is normal traffic, not
      a fault. Each clamp is covered by a test that fails without it — the end-of-buffer one needed a case
      the obvious "past the end" test does not reach (see 5.6)
- [x] 1.4 Conversion posted to the UI thread — `TextDocument` refuses access from anywhere else, and the
      failure is an exception on a background thread rather than a wrong answer. The hop is a constructor
      parameter defaulting to the dispatcher, not a static call: only the thread that initialised Avalonia
      may pump it, so a hidden static is untestable in a suite where another class got there first
- [x] 1.5 A hook for what the owner wants to do on each diagnostics arrival, so the code editor keeps
      piggybacking its symbol refresh without the session knowing what a symbol is
- [x] 1.6 Flush — cancel the debounce and send immediately, for requests that need the server current

## 2. The VB6 code editor moves onto it

- [x] 2.1 `CodeEditorViewModel` constructs a session and delegates open, change, close and diagnostics
- [x] 2.2 The per-request methods keep calling the client directly — with the **live** `GetDocumentUri()`,
      not the session's URI as this task first said. The view pairs each response against
      `GetDocumentUriPublic()`: go-to-definition compares `loc.Uri` to it to decide same-file versus
      cross-file, and rename looks up its `WorkspaceEdit` by it. Freezing one side of those comparisons
      and not the other turns a same-file definition into the cross-file no-op and drops a rename edit
      silently. The two identities can only diverge if a form is renamed with its editor open — recorded
      on #269, which wants one fix for that whole path rather than a patch per call site
- [x] 2.3 No behaviour change. No existing test was edited to accommodate the migration. Two test
      changes were made, both *before* it and both to make the net real rather than to fit the change:
      `Dispose_UnsubscribesFromDiagnosticsPublished` had **no assertion at all** — a bare `-=` on the
      substitute with no `Received()`, so it could not fail — and `Dispose_WritesTheBufferBackBefore
      ClosingTheDocument` is new, pinning an order nothing pinned. Both were confirmed to fail against a
      deliberately broken version before being relied on
- [x] 2.4 Six behaviour differences accepted deliberately, all fixes, none test-visible: a never-initialized
      view model no longer sends a `didClose` for `vb6://form/untitled`; dispose now cancels the pending
      debounce, so closing a tab within the debounce window no longer sends a change *after* the close;
      `TextChanged` is now unsubscribed; a flush racing a close no-ops; and diagnostics arriving after
      dispose no longer fire a symbol request for a closed document

## 3. The carried-file editor gains it

- [x] 3.1 `RelatedDocumentEditorViewModel` takes an `ILspClient` and constructs a session
- [x] 3.2 Identified by a `file:` URI built from the absolute path, via a new `LspDocumentUri.ForFile` —
      beside `AreSame`, because the class that owns what makes two URIs the same document is where
      construction belongs. The path is normalised first, so two spellings of one file are one document;
      the extension survives, because it is the whole basis on which a server claims the file
- [x] 3.3 A document with no path on disk is not offered — there is nothing to identify it by, and a
      project that has never been saved is the case (#260)
- [x] 3.4 A file that could not be read is not offered either. The editor already opens empty and read-only
      rather than lying about content; sending that empty buffer would have a server publish diagnostics
      about a document nobody has
- [x] 3.5 Closed when the editor closes, like any other document

## 4. The view renders it

- [x] 4.1 `RelatedDocumentEditorView` attaches `LspTextMarkerService` to its editor and subscribes to the
      view model's markers — the same control the code editor uses, so it attaches unchanged. The
      diagnostics colorizer comes with it, so a diagnostic looks the same wherever it appears; it limits
      its red text to errors, so a linter's warnings on prose stay squiggles rather than reddening a
      paragraph
- [x] 4.2 Unsubscribed and detached on unload, symmetrically, and the renderers are **removed from their
      lists** rather than merely dropped — this view is re-attachable, so a second attach would otherwise
      stack another pair on the first and draw every diagnostic twice
- [x] 4.3 The view catches up on attach, from a `Markers` property the view model now keeps. Not in the
      original plan: `MarkersChanged` is a notification, not a state, and moving this document to another
      dock re-materialises the view while the server has no reason to re-publish for an unchanged
      document — so the squiggles would vanish on a dock move and stay gone until the next edit. The VB6
      editor has the same gap and is unchanged here (#270)

## 5. Tests

- [x] 5.1 A carried file is opened to the client, with a `file:` URI carrying its extension
- [x] 5.2 Its diagnostics become markers; a diagnostic for a different document does not. Closed at the
      **integration** level rather than the view-model one, which is where it belongs: under
      `[AvaloniaFact]` the test body runs on the thread that owns the dispatcher, so the UI-thread hop can
      be pumped and the whole path driven for real — publication through conversion to a renderer holding
      the markers. A view-model test could only have asserted the negative case, and would have passed
      because the posted work never ran
- [ ] 5.3 A URI the server has normalised differently still matches — the #236 case, now in a second editor
- [x] 5.4 Editing sends a change; closing the editor closes the document. The change half is the session's
      (it owns the debounce and is tested for it); the close half is here
- [x] 5.5 A document with no path, and one that failed to load, are never opened — both verified by
      mutation to fail without their guard
- [x] 5.6 A range past the end of the buffer is clamped rather than throwing. Three shapes, because
      mutation testing showed the first two were not enough: a line past the document, a column past the
      line, and — the one that survived deleting the clamp — a diagnostic whose START is the last valid
      offset while its END line is past the document, which takes the fallback branch and lands outside
      the text. AvaloniaEdit already clamps a column within a line that exists, so only that path needs it
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
