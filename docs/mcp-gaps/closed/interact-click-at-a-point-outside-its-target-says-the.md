# `interact click` at a point outside its target says the control pressed there covers the target — **CLOSED**

> **Fixed (#1530, PR #1549, 2026-10-23).** A click outside the target now says the point is outside, gives the
> target's size, and names where the press landed. "Covers the target there" is kept for points inside its
> bounds. Guarded by `InteractClickTests`.

**Symptom (2026-10-23, #1503, Windows).** On a running form, `Command2` is 120 × 60
(`inspect_element`: `boundingRect [64,40,120,60]`).
`interact {"target":"Window/Button[Command2]","action":"click","value":"150,60","window":"Form1"}` answered
`clicked the left button at (150, 60) on 'VBCommandButton', which is (214, 100) in the window; the press landed on
'VBLabel', which covers the target there`. The point is past the button's width and height. The Label lying there was
pressed, and `Command2_Click` didn't run. The reply reads as a stacking defect in the product.

**Workaround.** `inspect_element` the target first and keep the point inside its `boundingRect`'s width and height.

**Suggested fix.** When the point is outside the target's bounds, say so and give the target's size, and keep "covers
the target there" for a point inside them. Or refuse such a point, as a point where nothing takes the pointer is
refused. [#1530](https://github.com/hexide-io/HexIDE/issues/1530).
