# `hover` couldn't say that the real mouse, resting over the window, took the tip — **CLOSED** (#2376, 2026-10-27)

**Symptom (2026-10-27, Windows, #1303's live check).** With the real cursor resting over the IDE's window, a second
`hover` on `…/Button[Standard.AddForm]` answered `no tip opened within 1500ms (its ToolTip.ShowDelay may be longer than
the watch, or a real pointer moved over the window and took the tip); declared tip: Add Form`, though the first had
reported the tip. A first Auto Data Tip in break mode missed the same way. With the cursor on the taskbar, nine hovers
running each reported their tip.

**Cause.** Windows sends the window under a resting cursor a real mouse move when windows change around it, a tip's own
popup among them. Avalonia's pointer goes back under the real cursor, and its tooltip service moves to whatever is there,
as it does for a person. The reply offered that as one of two causes and couldn't say which; with no tip declared, as for
quick info and Auto Data Tips, it offered none.

**Workaround.** Move the real mouse off the window before hovering, which no tool can do.

**Fixed (#2376).** While `hover` watches for a tip, `UiAutomationDriver.ForeignPointerWatch` reads the input manager's
stream, which is what Avalonia's tooltip service follows, for pointer input the hover didn't send. Routed pointer events
that `click` and `drag` raise on a control can't move a tip, never reach that stream, and aren't counted. Tool calls run
concurrently, so the input is told apart by its device: every hover sends through one device of its own, and anything
else is the platform's. When no tip opened and such input came, the reply says which, declared tip or not:

- the real mouse: `the real mouse moved the pointer to (x, y) in the window, over …, during the watch, and the tip went
  with it, as it would for a person: one resting over the window is sent such moves as windows change around it. Move it
  off the window and hover again`;
- another hover moving elsewhere, or ending: `another hover moved the pointer to (x, y) …` or `another hover ended during
  the watch, …`, each ending `Hover one control at a time`.

It names the control whose tip a person would now see, else the nearest named one. When nothing came, the ShowDelay
explanation stands alone. `HoverThroughWindowInputTests` covers the hover's own move and a routed move, which aren't named,
another hover's move and its end, and the headless platform's mouse standing in for the real one. Removing the watch, the
device check or the hovered-point filter each fails it.

**Live (2026-10-27, Windows at 150%, a Debug build of the fix, headless `claude -p` through the hexide tools).** A harness
held the real cursor still and logged it every half second.

- **Cursor over the IDE**, at (100, 540) on the screen. Of eleven hovers, five on Add Form, three on Add Project and three
  of quick info on `count`, six reported their tip. Each of the five that didn't said `the real mouse moved the pointer to
  (66.7, 337.3) in the window`, the cursor's point at 150% in window units. The quick-info miss, `no tip opened within
  3000ms (the real mouse moved the pointer to (66.7, 337.3) in the window, over
  ScrollContentPresenter[PART_ContentPresenter], …)`, would before this have said only that no tip opened.
- **Cursor parked on the taskbar.** Disabled End and Break gave `…; no tip opened within 1500ms; declared tip: End` (and
  `Break`), with no note: nothing is claimed when nothing came. Three Add Form hovers, then three pairs of Add Form and Add
  Project sent as parallel calls, each reported its tip. The pairs didn't overlap enough to interfere, so the other-hover
  wording is covered headless only.
