# Breakpoint gutter hover target — **CLOSED** (#1176, 2026-10-18)

`dump_visual_tree {"maxDepth":24,"interactiveOnly":false}` did not expose the breakpoint margin.
`hover {"target":"Window/Custom[MainView]/Pane[DockControl]/Pane[Root dock]/List[PART_ItemsControl]/List[#0]/Pane[#0]/Custom[Root]","x":19,"y":229,"dwellMs":500}`
resolved to TextEditor and returned "no tip opened within 500ms": its synthetic move never reached
an unaddressable child margin's pointer handler. Clicking through the editor still hit-tests the gutter.
The fix in #1176 exposes the margin with a control automation peer and stable `BreakpointMargin` ID,
so callers can target its own hover handler. Declarative tooltip visibility remains subject to the
existing synthetic-pointer limitation; assert the reported declared text.

The same acceptance run found a product hit-testing defect: `interact` with the margin target,
`action:"click", value:"7,111"` reported that the press landed on `DockPanel`, not the margin.
Only painted marker shapes were hit-testable. F9 was a working workaround. The fix in #1176 paints
a transparent rectangle across the whole gutter so empty lines accept clicks and real pointer hover.
