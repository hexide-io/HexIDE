# The code window's gutters are not in the control tree, so a gutter click cannot be driven — **CLOSED** (#707, 2026-10-25)

**Symptom (2026-10-01, during #273 task 3.12's live check).**
`dump_visual_tree {"root":"…/Custom[Root]/None[TextEditor]/Pane[PART_ScrollViewer]/None","interactiveOnly":false,"maxDepth":4}`
returns the `TextArea` with two children: an empty `ItemsControl` and the hidden `PART_Watermark`. The
breakpoint gutter, the bookmark gutter and the line-number margin are all absent, because AvaloniaEdit's
`AbstractMargin` has no automation peer, so `interact` has nothing to click. The task's own named case, a
click on a folded header, could only be tested headlessly (`MarkGutterOnReadOnlyLineTests`, which raises
the press on the gutter itself).

**Workaround.** Place the caret with `interact set_property CaretOffset=N` on the code view, then
`invoke_menu_item Debug/Toggle Breakpoint` or `Edit/Bookmarks/Toggle Bookmark`. That reaches the same
toggle a gutter click calls, but not the gutter's own mapping from a point to a visual line, which is
what decides what a click on a folded row marks.

**Suggested fix.** Undecided: a peer on the gutters that `interact` can drive with a line, a `click` action
on `interact` at a point relative to its target (which would cover other drawn surfaces too), or a
dedicated tool if the authoring policy justifies one. Filed as
[#707](https://github.com/hexide-io/HexIDE/issues/707).

**The fold margin is the same (2026-10-02, during #273 task 3.14's live check).**
`dump_visual_tree {"interactiveOnly":false,"maxDepth":40}` over the whole window has no node whose class is
`FoldingMargin` or `FoldingMarginMarker`, so a fold cannot be expanded or folded by its marker. The
`TextEditor` node does report `expandCollapse`, which reads as the answer and is not: `inspect_element` on it
shows no expand state, and the provider is advertised because the editor owns a context menu, which is what
`expand` opens. **Workaround:** placing the caret inside a folded section with `interact set_property
CaretOffset=N` on the code view expands it, because the editor unfolds any fold the caret moves into. Nothing
folds one again. A `click` action on `interact` would cover this margin as well as the gutters.

**A member's Attribute lines fold too since #273 task 4.3**, into a box at the end of the line they describe, and
the workaround reaches them with one difference, read in code and not run. An offset strictly inside the run opens
it, as for any fold. An offset at the run's very end does not: the window moves a caret that lands after a folded
run's box to the end of the declaration, in front of the box (`CodeEditorView.KeepCaretOffFoldedRunsEnd`), and the
run stays folded.

**Fixed by #1033; paperwork in #707.** `interact` now takes `action: "click"` with a
point relative to its target. `UiAutomationDriver.DoClick` translates that point into window coordinates
and hit-tests the rendered window, then delivers pointer press and release to what it hits. The margin
needs no automation peer: address the editor that contains it. The old `expandCollapse` trap above still
applies; use `click` for a fold marker, not `expand` on the editor.

Call shape (substitute the discovered editor path and the measured point; this is a recipe, not a
record of a live call made in this pass):

```json
{"target":"<editor path from dump_visual_tree>","action":"click","value":"<x>,<y> left","window":"ide"}
```

Discover the editor with `dump_visual_tree {"window":"ide","interactiveOnly":false,"maxDepth":40}`,
then `inspect_element {"target":"<editor path>","window":"ide"}` for its bounds. Use
`take_snapshot {"window":"ide"}` to locate the desired gutter and row; subtract the editor's origin
from the point in the window to obtain `x,y`. Coordinates depend on layout, font, scrolling and scaling;
there is no fixed gutter offset to reuse across sessions. The click reply names what the hit test reached;
confirm the resulting mark or fold as well, since delivery alone does not prove a toggle.

**Verification.** Existing `InteractClickTests` cover target-relative coordinates, pointer press/release
on a peerless Border, and delivery to the control at the point rather than necessarily the addressed
control. No product behaviour changes in #707.

**Live check (2026-10-25, Windows, Debug build).** The breakpoint margin is now in the control tree as its own
node, so target it, not the editor:

```json
{"target":"…/Custom[Root]/None[TextEditor]/Pane[PART_ScrollViewer]/None/List/Custom[BreakpointMargin]","action":"click","value":"7,75","window":"ide"}
```

Its reply was `clicked the left button at (7, 75) on 'BreakpointMargin'`; `get_breakpoints` then gave `[21]`, and the
same call again gave `[]`. The margin is 14 units wide: the same row addressed on the editor at `10,77` fell on its
edge, landed on `ScrollContentPresenter`, and toggled nothing, which the reply's "landing on" names. The fold marker
has no node, so it is clicked through the editor:

```json
{"target":"…/Custom[Root]/None[TextEditor]","action":"click","value":"66,10","window":"ide"}
```

Its reply named `FoldingMarginMarker`; the form's header unfolded, and the same call folded it again. Bookmarks have
no gutter click to drive. Not checked live: a breakpoint click beside a folded header.

**Also recorded while the entry was open.**

**The fold margin is the same (2026-10-02, during #273 task 3.14's live check).**
`dump_visual_tree {"interactiveOnly":false,"maxDepth":40}` over the whole window has no node whose class is
`FoldingMargin` or `FoldingMarginMarker`, so a fold cannot be expanded or folded by its marker. The
`TextEditor` node does report `expandCollapse`, which reads as the answer and is not: `inspect_element` on it
shows no expand state, and the provider is advertised because the editor owns a context menu, which is what
`expand` opens. **Workaround:** placing the caret inside a folded section with `interact set_property
CaretOffset=N` on the code view expands it, because the editor unfolds any fold the caret moves into. Nothing
folds one again in that build. Since #1014, Edit → Toggle Fold can toggle the innermost section at the caret
or starting on its row, including a member run beside its procedure fold. This supplies a command workaround;
it does not address clicking the margin itself. A `click` action on `interact` would cover this margin as well
as the gutters.
