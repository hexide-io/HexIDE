# Every designer drag's reply said "nothing was docked" — **CLOSED** (#2232, 2026-10-27)

**Symptom (2026-10-26, Windows, #2216's and #2217's live checks).** All 18 drags on the form designer, moving, resizing
and drawing controls, ended their replies with a sentence about Dock, though none of them involved it:
`interact {"target":"…/List[ControlsContainer]/ListItem[Command1]","action":"click","value":"5,8 to 5,56","window":"ide"}`
→ `dragged the left button at (5, 8) on 'ControlItem', … (pointer capturer); Dock's layout is unchanged, so nothing was
docked: …`. Each drag did what it should. A first-time caller reads "nothing was docked" as the drag having failed.

**Cause.** #2175 added the clause for every drag pressed inside a `DockControl`, and the designer is inside one.

**Fixed (#2232).** The reply says what Dock moved whenever it moved something. It says the layout is unchanged only for a
drag pressed on one of Dock's drag areas (`DockProperties.IsDragArea`: a tab, a tool window's title strip), which is
where Dock itself starts a drag. Any other drag says nothing about Dock. Guarded by `DragDockTests`.

**Live (2026-10-27, Windows, a Debug build of the fix, headless `claude -p` through the hexide tools).**

- Moving Command1 by `5,8 to 45,58`, and drawing a TextBox with the toolbox's Text tool, replied without the clause.
- Properties dragged along its own title strip still said the layout was unchanged, and dragged onto the Project dock
  said `Dock moved 'Properties' from a dock of its own to the dock with 'Project Group - Group1'`.
