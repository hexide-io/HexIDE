# `dump_visual_tree` kept a row a list had scrolled away from, with `isOffscreen: true` — **CLOSED** (#1981, 2026-10-26)

**Symptom (2026-10-25, #1330's check of #1292, Windows).** In the Properties window, after `select` with value `Width`
scrolled the list to that row, `scroll` `home` and a collapse of Appearance, the next dump listed Width with
`isOffscreen: true`, first, before the rows in view. #1292 had left rows out of view out.

**Cause.** A virtualizing panel keeps its list's tab-once row realized when it scrolls out of view, so a Tab into the
list can land on it, and selecting a row makes it that row (Avalonia 12.0.4's `VirtualizingStackPanel`, decompiled). It
is arranged where it would be, outside the view, and its index is real, so #1292's test for a released row kept it.

**Fixed (#1981).** A row a virtualizing panel holds apart from the rows it has realized for the view, one its list
still indexes but no longer counts as realized, is left out of the dump, of path addressing and of `select`'s
candidates, as rows out of view are. It is told by membership, not position: a hidden row's bounds are stale, and the
held row's place is an estimate. `select` with its value now scrolls to it and says so, as for any row out of view.
`press_key`, `type_text` and hover no longer reach an editor in it. A row with keyboard focus in it, or with a dropdown
of its own open, is still listed. A hidden row in view is listed with `isHidden: true` as before, and a
non-virtualizing list's rows out of view are listed as before. `dump_visual_tree`'s description now says a long ListBox
lists its rows in view. Guarded by `HeldRowTests`.
