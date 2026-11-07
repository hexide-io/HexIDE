# `set_value` on a Properties window row reported success and did not commit — **CLOSED** (#625, 2026-09-22)

> **Fixed, for a person as well.** The row's text box commits on focus loss, and nothing handled Enter, so
> Enter did nothing for a person either. Now:
> - `PropertyBox` commits on Enter.
> - `set_value` on a bound text box commits as Enter would. The `interact` tool then reads the box back
>   after the dispatcher has run the source's reaction, so a refused value is reported as refused.
> - The Properties window posts its revert of a refused value, which was being dropped because it was raised
>   inside the binding's write. So the row no longer keeps showing a value the control does not have.
>
> Live, on a `--newproject` form:
> - `set_value` `Hello 625` on the Caption row → `…, and committed it`, and `get_form_controls` shows the new
>   caption.
> - `type_text` then `press_key` Enter on the same row → committed.
> - Height `tall` → `…, but after committing it shows '300': the change was refused or rewritten…`, with the
>   Invalid property value box open.
> - `(Name)` `1st` → `…shows 'Form1'…`.


**Symptom.** `interact` with `set_value` on a Properties row's `Edit` (e.g.
`…/Pane[Properties]/Custom/Tab[TabProperties]/List[AlphabeticProperties]/Pane[PART_ScrollViewer]/ListItem[Caption]/Edit`,
`value` `Hello 494`) answers `set value of 'Edit' to 'Hello 494'`, and `get_form_controls` still reports the
old caption. `press_key` Enter on the same `Edit` does not commit it either. The row's binding commits on
focus loss, which neither causes.

**Workaround.** Use `set_control_property`, which sets the property itself. Or, in the Properties window,
use `interact` `set_property` with `Value=…` on the row, which is the reflection fallback.

**Suggested fix.** Push the text box's binding to its source after `set_value`, or say in the reply that
nothing has been committed: [#625](https://github.com/hexide-io/HexIDE/issues/625).
