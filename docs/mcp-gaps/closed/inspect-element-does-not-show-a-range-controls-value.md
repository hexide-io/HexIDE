# `inspect_element` does not show a range control's value (#550) — **CLOSED** (#561, 2026-09-22)

> **Closed (#561).** `inspect_element` reports a `range` object for any control with a range provider:
> `value`, `minimum`, `maximum` and `isReadOnly`. Seen live on a code window's vertical scroll bar as
> `{"value":0,"minimum":0,"maximum":905.39,"isReadOnly":false}`, and as `"value":400` after
> `set_range_value` with `400`.

**Symptom.** `inspect_element(target: "…/Pane[PART_ScrollViewer]/ScrollBar[PART_VerticalScrollBar]")`
reports `"providers":["rangeValue"]` and no value, minimum, maximum or read-only flag, though its description
promises the "current selection/value/toggle state". The bounds could only be learned from a refusal
(`set_range_value` with `999999` answers `… outside 'PART_VerticalScrollBar''s range 0..2391.59375`), and
whether a value took only from a snapshot.

**Workaround.** Probe with an out-of-range `set_range_value` for the bounds, and `take_snapshot` to see the
result.

**Fix.** Add the range provider's `Value`, `Minimum`, `Maximum` and `IsReadOnly` to the inspection.
