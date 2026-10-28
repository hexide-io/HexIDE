# `add_to_selection` names the new selection but not its reference, so a caller learns what Align measured against only afterwards — **CLOSED** (#951, 2026-10-24)

**Symptom.** (Linux, openSUSE Leap 16 under XWayland, Debug build of main at b15c7cf, 2026-10-13.)

```
interact {"target":"Window/.../List[ControlsContainer]/ListItem[Command1]","action":"add_to_selection"}
→ {"detail":"added 'Command1' to the selection; 2 items are selected now: Command0, Command1"}
```

Only the next `invoke_format_command` reply, "…, aligned to Command0", named the reference, the control every
Align and Make Same command measures against. A snapshot between the two showed Command0 with the solid handles.
The reply lists the selection in index order, as does `inspect_element`'s `selectionItems`, so no read names the
reference before a command runs, which the form-designer spec requires of the designer. The same goes for
`remove_from_selection`. Cross-platform by code reading.

**Workaround.** Select the intended reference first and `add_to_selection` the rest: by code reading, the
reference stays on the first item selected, and removing it moves it to the lowest-indexed item still selected.
A snapshot shows the reference with the solid handles.

**Suggested fix.** With two or more selected, the reply names the primary item, and when `remove_from_selection`
removes it, names the new one. [#951](https://github.com/hexide-io/HexIDE/issues/951). Which control should be
the reference is #928.

**Fixed.** Multi-item selection replies list the primary first and name it as the designer reference for Align
and Make Same. Removing it reports the replacement. The action description says how to choose it: select it
first. Headless regression tests cover both selection orders and removal; live designer verification remains
for the supervising session. Selection semantics (#928) are unchanged.
