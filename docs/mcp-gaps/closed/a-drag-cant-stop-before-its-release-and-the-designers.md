# A drag can't stop before its release, and the designer's preview and grab handles were not in the tree — **CLOSED** (#2231, 2026-10-27)

**Symptom (2026-10-26, Windows, #2216's live check).** `click` with `to` presses, raises its held moves and releases in
one call, so nothing that shows only while the button is down can be seen: the designer's draw preview
(`NewControlSpawnerPhantom`), the rubber band, a control's outline while it moves or resizes, Dock's drop targets, and a
running form's MouseMove with a button held. `dump_visual_tree {"window":"ide","match":"NewControlSpawner","interactiveOnly":false}`
answered `Nothing matches 'NewControlSpawner'.`, though a drag's reply named `Canvas[NewControlSpawner]`. On 2026-10-27,
#2232's live check found a selected control's grab handles missing too: `match "Resize"` found nothing, though a move
drag's reply named `'ResizeAdorner'` as the pointer capturer.

**Workaround.** Check only where a control lands. For a resize, press at a guessed point on the control's edge.

**Part 1, fixed (#2231).** The preview, its canvas, the rubber band, the resize adorner and its eight handles carry
AutomationIds, and the control view lists an adorner under the element it adorns. A handle is
`…/ListItem[Command1]/None[ResizeAdorner]/None[PART_BottomRight]`, and `inspect_element` reads the preview's bounds.

**Part 2, fixed (#2231).** `click … to x,y hold` stops after the held moves with the button still down, and the new
`release` action lets it go in a later call, at an optional point it moves to first, with an unheld drag's reply. While
it is held, every reply carries a `heldButton` note (`HeldButtonNote`, beside `OpenDialogNote`), `click`, `double_click`
and `hover` are refused, and keys still go. After 120 s with no call the hold is abandoned, as decided on the issue:
Escape, held moves back to the press, and a release there. The next reply says what the designer's undo stack recorded,
if anything. Measured headlessly (`HeldButtonTests`): an abandoned move and an abandoned Dock drag change nothing, and an
abandoned draw places a 2 by 2 control, `Add: Command1`.

**Live (2026-10-27, a Debug build of the fix on Windows, headless `claude -p` through the hexide tools).**

- With the Command tool on, `interact click` on `…/None[NewControlSpawner]` with `"20,20 to 100,60 hold"` replied that
  the left button was still held and that the canvas had the pointer capture, with a `heldButton` note. `inspect_element`
  on `…/None[NewControlSpawnerPhantom]` then had no `isHidden` and a `boundingRect` 80 by 40, and `take_snapshot` showed
  the preview, a solid 2-pixel outline, on the form.
- `get_project_info` carried the same `heldButton`. `click "5,5"` and `hover` were refused, naming the held button.
- `release` with `"140,90"` replied as an unheld drag does, released at (140, 90), with no `heldButton` after it.
- Holding `…/ListItem[Command1]/None[ResizeAdorner]/None[PART_BottomRight]` with `"4,4 to 44,34 hold"` grew Command1
  from 121 by 65 to 160 by 88 while held. With nothing called for two minutes, the next `get_project_info` said the hold
  was abandoned at 07:46:27 UTC: Escape went to the focused `ControlItem`, and it was let go where it was pressed.
  Command1 was then 120 by 64, not 121 by 65: a resize returns to the grid, and a drawn control is a pixel larger than
  its grid cells, so the note said `The designer recorded 'Resize: Command1' anyway, so it did not cancel; Undo takes it
  back.` The call after that had no `heldButton`, and `release` was refused, since nothing was held.
