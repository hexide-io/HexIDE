# A drag couldn't dock a tool window, and its reply read as a drag that worked — **CLOSED** (#2175, 2026-10-26)

**Symptom (2026-10-26, #1877's live check, Windows).** Dragging AI Chat's title strip onto the Immediate dock's centre,
or the Toolbox dock's, did nothing. The reply was the same as for a drag that docked:
`interact {"target":"…/List[#1]/TitleBar[AI Chat]","action":"click","value":"120,15 to …/List[#0]/TitleBar[Immediate]:460,63","window":"ide"}`
→ `dragged the left button at (120, 15) on 'ToolChromeControl' … release received by 'Grid[PART_Grid]' … (pointer
capturer)`. The same verb drove Dock's column splitters.

**Workaround (before the fix).** None through the surface.

**Cause.** The eight held moves were raised in one synchronous loop, with no layout between them. Dock adds its drop
target on the first move past its drag threshold and finds the drop from that target's layout, so the release came
over a target with no size. A layout pass between the moves was enough to make a headless drag dock, which confirmed
it. A splitter adds nothing mid-drag, which is why it worked.

**Fixed (#2175).** Each held move is input of its own. `FinishAsync` raises them one at a time and lets the
dispatcher run everything above Background between them, layout included. A drag pressed in Dock's layout now ends
its reply with what Dock moved, as `Dock moved 'Properties' from a dock of its own to the dock with 'Immediate'`, or
with `Dock's layout is unchanged, so nothing was docked: …`. Guarded by `DragDockTests`.

**Live (2026-10-26, Windows, a Debug build of the fix, headless `claude -p` through the hexide tools).**

- Properties dragged by its title strip onto the Immediate dock became a tab beside Immediate, and the reply said so.
- A drag back onto its own title strip said the layout was unchanged.
- A drop on the Toolbox dock's upper half split it, with Properties in a new dock above the Toolbox. The reply said
  Properties moved "to a dock of its own".
