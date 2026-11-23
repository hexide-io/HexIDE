## MODIFIED Requirements

### Requirement: Name archive text to language servers
An available text preview MUST use
`archive-entry:/<percent-encoded segments>?container=<percent-encoded file URI>`.
Entry segments MUST compare ordinally; the container MUST compare as a host file path, ignoring case on Windows.
A twinBASIC source entry (`.twin` or a VB6 source extension, ignoring case) MUST route only to servers
that claim its extension and declare language `twinbasic`, ignoring case. Other entries MUST route by extension.

#### Scenario: A source name contains a space and a hash
- **WHEN** a text entry named `Module # 1.twin` opens with a server claiming `.twin` and declaring `twinbasic`
- **THEN** didOpen names `Module%20%23%201.twin` under the archive-entry scheme and carries its text

### Requirement: Maintain read-only archive document sessions
For twinBASIC entries, servers matching the entry's extension and language label, where present, MUST receive
its text on open and a close when the tab closes. Reload, reopen and workspace replay MUST preserve the label.
When no server matches, the preview MUST remain available without language-server diagnostics.
Read-only entries MUST NOT send changes. Published diagnostics MUST reach the matching tab, including
when a server changes the container drive letter's case on Windows. Reload MUST recreate the session and markers.

#### Scenario: Reload an open text entry
- **WHEN** a validated replacement updates an open entry
- **THEN** its old session closes and a new session opens with the replacement text
- **AND** diagnostics reach the new buffer while edits remain refused

