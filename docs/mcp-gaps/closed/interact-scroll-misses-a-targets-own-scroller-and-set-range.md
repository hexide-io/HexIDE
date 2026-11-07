# `interact scroll` misses a target's own scroller, and `set_range_value` detaches a scroll bar (#545) — **CLOSED** (#566, 2026-09-22)

> **Closed (#566).** `scroll` now looks in the target's own template before walking upward, so aiming at
> an editor or a `DataGrid` scrolls it. `set_range_value` on a `ScrollViewer`'s bar moves the viewer's
> `Offset`, and any other range control goes through `SetCurrentValue`, so the template binding survives
> and a later `scroll` keeps the bar in step. The `DataGrid` case read from the code was real: setting its
> bar moved the bar and not the rows. A grid is now moved with the public `DataGrid.ScrollIntoView`, a
> whole row at a time, and the reply gives the offset it landed on beside the one asked for. A grid's
> horizontal bar has no such route and is refused. `NaN` and infinities are refused. Known limit: rows are
> located through `ItemsSource`, so a grid the user has sorted by a column can land on a different row
> from the one asked for; the reply still reports where it actually landed.

**Symptom.** (1) `interact(target: "…/Custom[Root]/None[TextEditor]", action: "scroll", value: "down")` on a
141-line code window answers `nothing to scroll vertically: neither 'TextEditor[TextEditor]' nor anything
containing it has content taller than its viewport`. The same call on `…/None[TextEditor]/Pane[PART_ScrollViewer]`
scrolls. The scroller is a template part inside the target, and the search only walks upwards. (2)
`interact(target: "…/Pane[PART_ScrollViewer]/ScrollBar[PART_VerticalScrollBar]", action: "set_range_value",
value: "2391")` moves the view and the thumb together. Afterwards,
`interact(target: "…/Pane[PART_ScrollViewer]", action: "scroll", value: "home")` puts the text back at line 1
and leaves the thumb at the bottom for the rest of the session: the local value outranks the template
binding. From reading, not yet reproduced: a `DataGrid`'s scroll bars take `set_range_value` without
scrolling, and `NaN` is accepted.

**Workaround.** Aim `scroll` at the `PART_ScrollViewer` inside the control, never at the control itself. Do
not use `set_range_value` on a `ScrollViewer`'s scroll bars; `scroll` with `home`/`end`/a page does the same
without breaking the binding.

**Fix.** In the issue.
