# `invoke_command` on a designer context-menu item looks in the wrong view model — **CLOSED** (#1336, 2026-10-23)

**Symptom.** Recorded when #836 closed (#1271), read in the code and not run. The designer's context flyout binds
Delete, Bring to Front and Send to Back through the form's view model
(`#Root.((visualDesigner:FormEditViewModel)DataContext).DeleteSelected` and the like, in `FormEditView.axaml`), while
each item's own DataContext is the control the flyout opened on. `invoke_command` and `inspect_element` read the
DataContext of the control addressed, so on one of these items they look in the control's view model, which has
none of the three, and `inspect_element` does not list them. A command property bound the same way is missed the
same way.

**Workaround.** Open the flyout with `interact` `expand` on the control, then `invoke` the item, which runs the
command it is bound to through its automation peer. That route was measured on the Project Explorer's flyout
(#947), not on the designer's.

**Suggested fix.** For an item whose `Command` is bound through another element's DataContext, have `invoke_command`
and `inspect_element` look where the binding resolves, or have the refusal say that the command is bound elsewhere
and that `invoke` on the item reaches it. [#1336](https://github.com/hexide-io/HexIDE/issues/1336).


**Fixed.** The target’s resolved `Command` is listed and accepted as `invoke_command` value `Command`, with its binding description and a hint that `invoke` runs it. Execution uses the control’s `CommandParameter` and checks `CanExecute`. Headless regressions open a real designer control’s flyout, inspect Delete, invoke it and assert the selected control is removed, and check a wrong name’s refusal. All three fail against the previous driver.
