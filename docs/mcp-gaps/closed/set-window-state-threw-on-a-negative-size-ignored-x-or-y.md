# `set_window_state` threw on a negative size, ignored `x` or `y` alone, and could put the IDE off every screen — **CLOSED** (#602, 2026-09-22)

> **Fixed.** Everything is checked before anything changes, and a refusal says "Nothing was changed.":
> - a non-positive `width` or `height` is refused;
> - `x`, `y`, `width` or `height` with a state other than Normal is refused rather than ignored;
> - a position that would leave the window intersecting no screen's working area is refused, and the reply
>   names the screens.
>
> `x` and `y` now apply independently. A left-out coordinate keeps the window's current value, read after
> restoring from Maximized, so it is the restored position, not the maximised frame's -11. The reply carries
> the resulting `window` (state, position, size) as `get_window_state` reports it, read after layout, so a new
> size is not reported as the old one on synchronous backends. This layout-only wait did not cover X11
> window-manager round trips (#946).

**Symptom.** On a window at (190,190) 945×447:
- `set_window_state {"state":"Normal","width":-50,"x":-99999}` answered only `An error occurred invoking 'set_window_state'.`
- `{"state":"Normal","x":300}` answered success and left the window where it was.
- `{"state":"Normal","x":-99999,"y":-99999}` answered success and moved the IDE to (-32768,-32768), where nobody could see or reach it.

**Found while fixing it.** Applying the geometry before `WindowState = Normal` lost it whenever the window was
maximised: the restore put the window back at its restore bounds. Reading `ClientSize` straight after setting
`Width` gave the size before the call.
