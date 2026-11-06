# `inspect_element` reports `isOffscreen: false` for a control scrolled out of its scroll viewer

**Symptom (2026-11-02, #2836's live check, Windows, 150%).** The Language & Debug Servers tab is a scroll viewer whose
visible area ended at about y 383 in a 1280×649 window.
`inspect_element(target: "Window/Custom[MainView]/Pane[DockControl]/Pane[Root dock]/List[PART_ItemsControl]/List[#0]/Pane[#0]/Custom/ScrollViewer/List[#0]/List/Text[Documents open]")`
replied `isOffscreen: false` and `boundingRect: [100.7, 529.3, 96, 16]`. The first-contact caller, asked to make sure
the row was in view, read those bounds as inside the window, said no scrolling was needed, and took a snapshot the
row is not in. The peer's `IsOffscreen` does not count a scroll viewer's clip on Avalonia 12.0.4.

**Workaround.** Judge by a `take_snapshot` with `inline: true`, or compare the bounds with each enclosing
`ScrollViewer`'s by hand.

**Suggested fix.** Compute `isOffscreen` from the control's bounds against each enclosing scroll viewer's viewport and
the window, not from the peer alone. [#2867](https://github.com/hexide-io/HexIDE/issues/2867).
