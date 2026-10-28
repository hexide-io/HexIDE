# No tool said which mouse pointer a control shows — **CLOSED** (#2453, 2026-10-28)

**Symptom (2026-10-28, hexide-int, Windows, #2448's live check).** `inspect_element {"target":"Window","window":"Form1"}`
on a running form with `Screen.MousePointer` at 11 had no cursor field, `take_snapshot` draws no pointer, and
`dump_visual_tree` carries nothing about it. So `Screen.MousePointer`, a control's own MousePointer and a TextBox's
I-beam could not be checked in the running IDE. Avalonia's `Cursor` does not say what it shows.

**Fixed (#2453).** The runtime records the MousePointer each `Cursor` it makes came from (`VBCursorTypeExtensions.PointerOf`),
without caching them, since callers compare cursors by reference. `inspect_element` gives `pointer`: that MousePointer
by number and VB6 name, the standard pointer drawn, where it is set, and `setBy`, read from the property's value
priority: `Own` for a control's own value, `Override` for one set above it as `Screen.MousePointer` sets it, `Style`
for a style's. A pointer the runtime did not make is reported without a kind, saying it is not recorded. Guarded by
`InspectPointerTests`.

**Live (2026-10-28, Windows, headless `claude -p` through the hexide tools).** On main: a button designed with
MousePointer 11 read `{"mousePointer":11,"name":"Hourglass","shows":"Wait","setBy":"Own","setOn":"self"}`, and a
TextBox given 2 in `Form_Load` read `{"mousePointer":2,"name":"Cross",…}`. A label left at its default read
MousePointer 1, Arrow, which is #1970 (the design-time default is 1 where VB6's is 0), now visible. On #2448's branch,
with this change applied: `Screen.MousePointer = 11` in `Form_Load` over a button whose own MousePointer is 2 read
`{"mousePointer":11,"name":"Hourglass","shows":"Wait","setBy":"Override","setOn":"self"}`, and after a button set
`Screen.MousePointer = 0` it read `{"mousePointer":2,"name":"Cross","shows":"Cross","setBy":"Own","setOn":"self"}`.
