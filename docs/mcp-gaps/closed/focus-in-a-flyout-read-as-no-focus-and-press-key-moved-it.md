# Focus in a flyout read as no focus, and `press_key` moved it out before pressing — **CLOSED** (#2030, 2026-10-26)

**Symptom (2026-10-26, #1960's live check, Windows).** With Replace's Cut/Copy/Paste flyout open,
`dump_visual_tree {"window":"auto","match":"Paste"}` gave `"focusedPath":null`, and
`press_key {"target":"Window","key":"Escape","window":"Replace"}` replied `moved focus from
Window/List/Pane/MenuItem[Paste] to Window/Custom/Edit[#0]; pressed Escape on TextBox …`. That one Escape closed the
whole dialog, where a person's closes only the flyout.

**Cause.** On the desktop a flyout's items sit in a popup root of their own, and the focus checks walked visual
ancestors, which never reached the dialog. The popup root has no logical parent leading back either: a first fix that
followed it was measured live and changed nothing.

**Fixed (#2030).** Focus is placed in the control view as the dump places popups. The walk goes from a popup's
content to the control the popup is placed at, so focus in a flyout counts as in its owner's window. `focusedPath` now
names `Window/Custom/Edit[#0]/Pane[#0]/MenuItem[Paste]`, `press_key` presses Escape on the flyout's item without moving
focus, and its reply names that item by the same path. Guarded by `FocusInPopupTests`.
