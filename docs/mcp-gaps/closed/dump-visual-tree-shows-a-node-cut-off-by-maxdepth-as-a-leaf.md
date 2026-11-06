# `dump_visual_tree` shows a node cut off by `maxDepth` as a leaf, and under `interactiveOnly` drops it and its ancestors — **CLOSED** (#953, 2026-10-20)

> **Fixed.** A node at the cut-off still has its children counted, without walking them, and carries
> `"truncated": true` when it has any. Under `interactiveOnly` such a node is kept rather than pruned, since what
> lies below it was not looked at, so the cascade that removed its non-interactive ancestors stops there. The
> `maxDepth` and `interactiveOnly` parameter descriptions say so and give the next step: dump again with the
> node's path as `root`. Guarded by `UiAutomationDriverTests` (a node cut off at the cap is kept and marked, a
> real leaf at the cap is not, and a deep enough walk marks nothing).

**Symptom.** With a form open in the designer (Linux, openSUSE Leap 16 under XWayland, Debug build of main at
b15c7cf, 2026-10-13):

```
dump_visual_tree {"maxDepth":8}
dump_visual_tree {"root":"Window/Custom[MainView]/Pane[DockControl]/Pane[Root dock]/List[PART_ItemsControl]/List[#0]/Pane[#0]","maxDepth":2}
→ both: Pane[#0] → Tab[PART_TabStrip] with "children":[], and no Custom[Root]
dump_visual_tree {"root":"<the same path>","maxDepth":6,"interactiveOnly":false}
→ Custom[Root] → … FormContainer → ControlsContainer → ListItem[Picture0] …
```

The designer pane appears to hold only a tab strip. A node at the cap is emitted with `"children":[]`, the same
as a real leaf, and nothing records that the walk stopped. Under the default `interactiveOnly: true` that empty
list prunes a non-interactive node at the cap, and the prune cascades up through every non-interactive ancestor:
here the designer's ScrollViewer, then `Custom[Root]`, the designer itself. That breaks `interactiveOnly`'s
promise to keep a node with an interactive descendant. It is the #362 trap from this document's header, and a
smaller `maxDepth`, the natural answer to an overflowing whole-window dump (#699), walks into it. Cross-platform by
code reading.

**Workaround.** Do not read `"children":[]` as a leaf, or a missing node as absent. Dump again from the deepest
node shown, with its path as `root`, a larger `maxDepth` and `interactiveOnly: false`.

**Suggested fix.** At the cap, check for children without recursing and mark a node whose children were not
walked `truncated: true`; under `interactiveOnly`, keep such a node instead of pruning it. Say so in the
`maxDepth` and `interactiveOnly` parameter descriptions. [#953](https://github.com/hexide-io/HexIDE/issues/953).
