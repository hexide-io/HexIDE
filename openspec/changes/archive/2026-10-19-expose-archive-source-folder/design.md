## Decisions
The archive service serializes synchronization with open/reload/close using its existing gate, and runs the engine on a worker thread. It validates that the addressed project is still open after acquiring the gate, so stale selections cannot export closed projects. It releases the gate before showing the result dialog.

Project Explorer offers the command for any archive node, using that node's owning container path. The command is disabled while its previous invocation is running or its result is displayed, and direct method calls apply the same guard. A selection change during execution does not redirect the operation. Normal VB6 selections cannot invoke it.

Synchronization uses saved disk content, which may differ from the open snapshot. It deliberately does not reload that snapshot or replace read-only tabs. The success message names the actual physical destination and explains that folder edits stop synchronization, are not imported, and previews require explicit reload. Expected filesystem failures show a localized warning containing the engine's diagnostic and affected path; they never report success. Internal engine diagnostics remain in English in this slice.

The action is explicit: opening a project still creates no adjacent folder. A no-op synchronization reports the same ready-to-use folder. The existing engine owns layout, fidelity, divergence detection and recovery; UI code does not duplicate those rules. All three new keys are translated into every shipped language.

## Validation
Headless tests exercise selected-member routing in a two-project workspace, saved-content versus preview behavior, local-edit refusal, stale closed-project calls, command reentry and lifecycle access while the completion dialog is open. Existing composition tests construct the service through production dependency injection. Desktop compilation checks the context-menu binding; language tests check complete packs and placeholders.
