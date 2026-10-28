# `inspect_element`'s `selectionItems` named a selection with no row by its view model's type — **CLOSED** (#2026, 2026-10-26)

**Symptom (2026-10-26, #1981's live check, Windows).** At the IDE's default size the Properties window's categorized
list was 0 px tall, so no row was made. `inspect_element` on `…/List[CategorizedProperties]` answered
`"selectionItems":["HexIDE.VisualDesigner.PropertyViewModel"]`. Once the window was maximized, it answered
`["Caption"]`.

**Fixed (#2026).** A selected item with no row is named by its `AccessibleName`, the name the IDE's view models give
their rows to bind, which is the name the dump shows. Failing that, it is named by its own text. With neither, the
reply says `#25, which has no row to read a name from: a list makes rows only for what it shows`, never a
type's name. Guarded by `UnrealizedSelectionTests`.
