# `hover` quoted a declared tip from an unrelated descendant, and accepted a negative dwell — **CLOSED** (#610, 2026-09-22)

> **Fixed.** The declared tip now comes from the control under the hovered point, or its nearest ancestor
> that declares one, found by bounds so a disabled button still counts. `dwellMs` outside 0 to 10000 is
> refused. When the pointer goes to an editor under the target, the reply gives that editor's path. Live,
> on a `--newproject` IDE:
> `hover {"target":"Window","dwellMs":-5}` → `dwellMs must be between 0 and 10000; it was -5`.
> `hover {"target":"Window","dwellMs":0}` → `hovered (0, 8.5) on TextEditor[Editor], at Window/…/Pane[Immediate]/Custom; no tip opened within 0ms`, with no declared tip.
> `hover` on `…/None[StandardToolbar]` → `declared tip: End`, the disabled button at its centre. At (5, 10) → `declared tip: Add Project`.

**Symptom.** `hover {"target":"Window","dwellMs":-5}` (x, y, window left at null) answered
`hovered (0, 25.6) on TextEditor[Editor]; no tip opened within -5ms (…); declared tip: Add Project`. The pointer
went to a code editor. The declared tip was the Standard toolbar's, found by a search of the target's descendants.
The negative dwell had been clamped to 0 but was reported as given.
