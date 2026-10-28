# `set_value` on a read-only ComboBox throws, though the combo lists the `value` provider

**Symptom (2026-10-24, #1632, Windows).** In a UserControl's code window, `inspect_element` on the Object box,
`ComboBox[ObjectCombo]`, listed providers `selection`, `value` and `expandCollapse`.
`interact {"target":"…/ComboBox[ObjectCombo]","action":"set_value","value":"UserControl1"}` answered
`{"success":false,"mechanism":"peer","error":"action 'setvalue' threw: Specified method is not supported."}`.
- **Why it throws.** The combo isn't editable, so its value pattern is read-only, and setting it throws
  `NotSupportedException`.
- **Why the reply doesn't help.** It passes the exception's text through and doesn't say what does work. A caller who
  was shown `value` among the providers has no reason to expect the refusal.

**Workaround.** `expand` the combo, then `select` the item. In the PropertyPage's code window,
`…/ComboBox[ObjectCombo]/Pane/ComboBoxItem[PropertyPage1]` answered `selected 'PropertyPage1' and closed the dropdown,
as a click does`. Or use `set_property` on the code window's view model: `SelectedObject=UserControl1` answered `set
CodeEditorViewModel.SelectedObject = UserControl1`.

**Suggested fix.**
- On a ComboBox that isn't editable, have `set_value` select the item whose text matches, as `select` does, and report
  the selection.
- Where no item matches, refuse, and name the items.
- Either way, don't list `value` as settable on a read-only control without saying so: give `inspect_element` an
  `isReadOnly` for the value pattern.

[#1672](https://github.com/hexide-io/HexIDE/issues/1672).
