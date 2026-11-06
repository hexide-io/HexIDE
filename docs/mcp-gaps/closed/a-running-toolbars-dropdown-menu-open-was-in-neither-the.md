# A running Toolbar's dropdown menu, open, was in neither the dump nor the snapshot — **CLOSED** (#2323, 2026-10-27)

**Symptom (2026-10-27, Windows at 150%, #2309's live check).** A click on a dropdown's arrow,
`Window/Custom[Toolbar1]/Button[#4]/Text[▾]`, opened the menu: the program raised ButtonDropDown. But `take_snapshot`
showed no menu and gave no note, and `dump_visual_tree` with `match: "First"` or `"MenuItem"` found nothing, in
`Form1`, `auto` or `ide`. The next click landed on a `LightDismissOverlayLayer`, and Down then Enter raised
ButtonMenuClick, so the menu was open. A caller who trusted the snapshot would conclude the dropdown never opened.

**Cause.** On a desktop a popup is a window of its own, so the dump and the snapshot reach a control's context menu or
flyout from the control that owns it (`AttachedPopupsOf`). `FlyoutsOf` read `Button.Flyout` for a Button and
`FlyoutBase.GetAttachedFlyout` only for other controls. `VBToolbar.Press` attaches its MenuFlyout to the dropdown's
Button with `SetAttachedFlyout` and opens it with `ShowAt`, so that route missed it.

**Workaround.** None to see it. Keys reach it blind.

**Fixed (#2323).** `FlyoutsOf` reads the attached flyout for every control, Buttons included, after a Button's own
Flyout and before a ContextFlyout. The snapshot composer asks the same question, so the menu is drawn as well as
listed. `LogicallyParentedPopupTests.An_open_flyout_attached_to_a_button_is_in_the_tree`, for a Button and a
ToggleButton, asserts the owner route. Headlessly a popup is an overlay inside the window, so the walk finds the items
either way, and the owner route is the part that fails with the old `FlyoutsOf`.

**Live (2026-10-27, a Debug build of the fix cherry-picked onto #2309's branch on Windows at 150%, headless `claude -p`
through the hexide tools).** The project was the Toolbar corpus form, with a ButtonMenuClick handler appending
` MC:<key>` to the caption.

- A click on the arrow opened the menu, and `take_snapshot` showed it under Menu ▾, with its one visible item, First.
- `match: "First"` found `Window/Custom[Toolbar1]/Button[#4]/Pane/MenuItem[First]`, offering `invoke`.
- Invoking it closed the menu and raised ButtonMenuClick: the caption became `Form1 MC:m1`.
