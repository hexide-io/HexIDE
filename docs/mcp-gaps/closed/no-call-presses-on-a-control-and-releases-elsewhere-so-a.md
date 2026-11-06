# No call presses on a control and releases elsewhere, so a drag on a running form can't be driven

**Symptom (2026-10-22, #1373, Windows).** Whether dragging off a control cancels its Click is a drag: a press on the
control, moves with the button held, and a release somewhere else. `interact {"target":"<a Label on a running form>",
"action":"click","value":"10,5"}` presses and releases at that one point, and `click`'s own remarks call a drag
"beyond it". No other call raises a pointer press.

**Workaround.** None through the surface. `MouseEventTests` drives the drags headlessly with `MouseDown`, `MouseMove`
and `MouseUp`.

**Suggested fix.** Let `click` take a release point, as in `10,5 to 200,30`: press on the target, move with the button
held, release at the second point to whatever holds the pointer.
[#1402](https://github.com/hexide-io/HexIDE/issues/1402).

**The designer has the same gap (2026-10-26, #2039).** Drawing a control with a tool is a drag too, and which container
it goes into depends on where the drag began. `add_control` places as a drag begun at its top-left would, so the rule was
checked live through it. A drag up and to the left, where the two differ, was driven only headlessly, by
`DrawIntoContainerTests`, through the view's own pointer handlers.


**Fixed (#1402).** `click` accepts `10,5 to 200,30` or `10,5 to <path>:300,190`, with a full
`dump_visual_tree` path (spaces included). Release coordinates are relative to that control, or the target
without a path. Button and keys precede `to`; `double` cannot accompany it. Eight held moves and the release
follow the press's work, routed to the pointer capturer or the hit-tested control. The reply names the press
landing, release point and recipient, marking capture explicitly. Headless tests cover capture routing,
running-form release on/off, ListBox selection and drawing a toolbox control in the designer.
Live verification remains the supervisor's check.
