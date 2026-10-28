# take_snapshot renders DIPs while Win32 coordinates are physical pixels — **CLOSED** (#646, 2026-09-22)

> **Fixed.** `take_snapshot` now reports the image's `width` and `height` in pixels and its `scale`, the pixels
> per device-independent unit, and `inspect_element` says `boundingRect` is in the window's device-independent
> units from its top-left, which is the image's top-left. **One detail below was wrong when re-measured:** on a
> 150% display `boundingRect` was in DIPs, not in the image's pixel space. The Start button's `[270, 26, 22, 22]`
> is at `[405, 39, 33, 33]` in the 1500 × 900 snapshot, and a crop of that region is the button. The entry is kept
> as it was written; its Win32 half (`GetClientRect` in physical pixels) was not re-measured.

**Symptom.** Driving a synthetic mouse click from a `boundingRect` needs a scale conversion that nothing in
the tool output mentions. On the machine this was hit on, `GetClientRect` reported 987 × 560 physical pixels
while `take_snapshot` returned a 1481 × 840 image and `inspect_element` reported bounds in that same 1481-wide
space — a factor of 0.666. Clicking at the raw `boundingRect` coordinates lands roughly 50% off, far enough
to hit a different control and look like "the click did nothing".

**Consequence.** Any fallback that leaves the MCP surface for real input — the only route left when a
control has no usable provider — silently targets the wrong place, and the resulting no-op is easy to
misread as the feature being broken.

**Workaround.** Derive the factor before clicking: `GetClientRect` width ÷ snapshot image width, then
multiply the DIP coordinate by it and pass through `ClientToScreen`. Do not assume 1.0, and do not assume
the usual Windows 1.25/1.5 either — measure it.

**Suggested fix.** Report the scale explicitly. `take_snapshot` returning the render scale alongside the
path (and `inspect_element` naming the space its `boundingRect` is in) would remove the guesswork; the
values are already known to the server.
