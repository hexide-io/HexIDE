# `get_diagnostics` answered `[]` for clean code, for no server running and for an analysis not yet back — **CLOSED** (#664, 2026-09-22)

> **Fixed.** The reply now carries `analysed`, the documents whose diagnostics are in, clean ones
> included. It also carries a `note` saying which case an empty or partial answer is: no language server
> started; a document opened or edited whose analysis has not come back; or every analysed document clean.
> An edit is tracked by the document's own session (`LspDocumentSession.AwaitingDiagnostics`), so a stale
> "clean" for text the caller has just changed is reported as pending, not as an answer. Found and fixed in
> the same pass, so this entry never sat in the live file.

**Symptom.** `set_file_content` then `get_diagnostics`, the obvious way to check an edit, answered
`{"diagnostics":[]}` for code with a syntax error in it. Measured with `get_diagnostics` on `--newproject`:
`[]` with no server started (`get_lsp_capture_state` → `connections: []`); `[]` after `set_file_content`,
because nothing was open and only open documents are analysed; `[]` for about 3 s after `open_file`, while
the server started (`initialize` alone took 1.7 s); and, found while checking the fix, `[]` for about a
second after an edit to an open document, when the diagnostics held were for the text before it.

**Also.** `DiagnosticsCache` keyed documents by raw URI string, where `AddinDiagnosticsService` had moved
to `LspDocumentUri.Comparer`; it now uses the comparer too.
