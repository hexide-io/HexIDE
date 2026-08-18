# Preserve the menu hierarchy through a round-trip

## Why

A `.frm` expresses a menu as nested `Begin VB.Menu` blocks. HexIDE loads every component into
`FormDefinition.Components`, which is flat by construction, so the tree is lost on load and written back
as a flat sibling list on save — a file VB6 will not open as its author wrote it.

The visible cost is not the file, it is the gate. `FormDeserializer` marks a form unfaithful when its
nesting depth exceeds two, so **all six of VB6's menu templates open read-only** — half of the twelve
corpus forms currently held that way. Fixing this is what turns them back into forms a developer can edit,
which is the whole point of the refusal gate being temporary rather than permanent.

## What changes

The tree model already exists: `MenuComponentClass.SubItemsProperty` is a `List<ComponentInstance>` that
expresses exactly this, and the designer's menu editor already builds it. Nothing under `Serialization/`
references it — the deserializer never fills it, the serializer never reads it, and nothing else in the
tree consumes it.

So this change is wiring, not modelling:

- the deserializer populates `SubItemsProperty` from nested `Begin VB.Menu` blocks
- the serializer walks that tree and emits nested, correctly indented blocks
- the unfaithful-save gate stops firing for forms whose only nesting is menus
- the corpus test pins the six menu templates

## What this does not change

`FormDefinition.Components` stays flat, and `ComponentInstance` gains no parent or child concept. That is
deliberate: `.Components` is read at 127 non-test call sites, and changing its shape is the substance of
the container-nesting work tracked separately in #84. Menus can be done without it because their tree
lives on a property rather than in the component list.

## Risk

The gate is the safety net for exactly this class of damage, so relaxing it is the risky half. It must
narrow to "no non-menu nesting" rather than switch off — a form with a `Frame` containing controls must
stay read-only until #84 lands. The corpus test is what proves the narrowing was correct.

Tracked by #83, split out of the epic in #21.
