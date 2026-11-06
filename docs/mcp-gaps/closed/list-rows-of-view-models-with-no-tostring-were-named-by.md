# List rows of view models with no ToString were named by their type, or not at all (#1299)

A row's peer names it by its item's `ToString()`, which for a view model with none of its own is its type name; the dump
drops a type name, so such a row had no name, `select` by value could not find it, and `inspect_element` named the
selection "ListItem". The Object Browser's class, member and search lists were all of this shape.

> **Fixed.** `OBClassViewModel`, `OBMemberViewModel` and `OBSearchResultViewModel` name themselves by their names, as
> `OBLibraryViewModel` beside them already did. A survey of the IDE's lists found no other rows of this shape. A
> driver fallback naming such a row by the text it shows was tried and dropped: paths and `select` match on the peer's
> name, so the dump would have shown a name no tool accepts. Guarded by `ListRowNameTests`.
>
> **Live (#1330, 2026-10-25).** Windows, a Debug build of main at beada72, from a Claude Code session's hexide tools.
>
> - **Rows are named by name.** In the Object Browser the class rows are `ListItem[Form1]`, `ListItem[CheckBox]`
>   and the rest, and CommandButton's member rows are `ListItem[Caption]`, `ListItem[Click]` and the rest.
> - **Selection.** `interact` `select` with value `Form1` on `…/List[Classes]` replied "selected 'Form1'". With value
>   `Click` on `…/List[Members]`, CommandButton selected, it replied "selected 'Click'". `inspect_element` on each list
>   named that selection in `selectionItems`.
> - **Form1's own member list was empty,** since the browser lists only a project form's code procedures:
>   [#1968](https://github.com/hexide-io/HexIDE/issues/1968).
