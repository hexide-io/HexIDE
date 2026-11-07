# inspect_element named a templated dropdown's selection by its view model's type (#1288)

On the Properties window's object selector, `inspect_element` gave `selectionItems` and `value` as
`["HexIDE.VisualDesigner.ComponentInstanceViewModel"]`: the selected item's `ToString()`, which the closed dropdown's
peer names it by, not the text the dropdown shows. A caller could not tell which control was selected without expanding
it and dumping it.

> **Fixed.** `ComponentInstanceViewModel` names itself by the control's name, as `ConflictedFileViewModel` and others do,
> so the object selector reads "Command1" closed and open, its rows are named in the dump, and `select` by value finds
> them; its template shows the name and the class, "Command1 CommandButton", which names no control. For any dropdown,
> a selection or value that is only the text an item gives with no `ToString` of its own, its type, generic arguments
> and all, or a record's members, is read as the closed box shows it: the visible text of the presenter of its
> selection box item. An item's own text is kept, whatever the template shows. Guarded by `InspectItemCountTests`, one
> on the real `PropertiesToolView`. A list box's unrealized rows, and rows a multi-block template leaves unnamed (the
> Object Browser's lists), are #1299.
>
> **Live (#1330, 2026-10-25).** Windows, a Debug build of main at beada72, from a Claude Code session's hexide tools,
> with Command1 selected on a new project's Form1:
>
> - **Closed and expanded.** `inspect_element` on `…/Pane[Properties]/Custom/ComboBox[ObjectSelector]` gave
>   `"selectionItems":["Command1"]` and `"value":"Command1"` both times.
> - **The expanded dump** named its rows `ComboBoxItem[Form1]` and `ComboBoxItem[Command1]`, each holding the name
>   and the class as two `Text` nodes.
> - **`interact` `select` with value `Form1`** replied "selected 'Form1' and closed the dropdown, as a click does",
>   and `inspect_element` then gave `Form1`.
> - **The first `select` was refused**, with "'ObjectSelector' has no selectable items realized — 'expand' it first,
>   then select". It was sent after an `inspect_element` and a dump made with the dropdown open, and the dropdown
>   had closed between the calls: the next `expand` replied "expanded", not "already open". Why it closed was not
>   established, and it happened once in this run. The refusal said what to do, and the retry worked. Not filed
>   without a reproduction.
