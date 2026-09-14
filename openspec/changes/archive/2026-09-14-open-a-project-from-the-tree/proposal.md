# Open a project from the tree

## Why

The project node is the one thing in the Project Explorer that could not be opened. Every member has a
gesture that leads somewhere; the node they all hang from led only to expand/collapse, which the chevron
beside it already does.

VB6 never showed a developer what was in a `.vbp` except through the Project Properties dialog, which
answers a different question — it edits settings, it does not show membership, references and startup
together. The information exists and the IDE already holds all of it.

## What changes

Double-clicking a project node opens a read-only document showing what the project contains: name, type,
startup object, description where there is one, location, its members with their kinds and files, and its
references.

It renders the **model**, not the `.vbp` file. The file is one serialization of the project as it was last
saved; `ProjectDefinition` is the project as it is now, which is what the tree beside it already shows.
Rendering the model means the document tracks a member being added with no file watcher, and means a
project that has never been saved — and therefore has no file at all — still has something to show.

Read-only. Editing would put two authorities over one file and require merging text edits back into a live
model. That is tractable here in a way it is not for evaluated project formats — a `.vbp` is a flat ordered
declaration list, the deserializer already preserves the lines it does not model, and the parser is ours —
but it is a separate decision and nothing here forecloses it.

## Impact

- Double-click on a **project** node stops toggling expansion. A deliberate divergence from VB6, recorded
  in the requirement: the chevron is what expansion is for, and fidelity does not extend to spending the
  primary gesture on a second way to do it.
- A **project group** node is unaffected and keeps expand/collapse, because `.vbg` is not read yet and
  there is nothing for it to open into.
- No change to any member's behaviour.
