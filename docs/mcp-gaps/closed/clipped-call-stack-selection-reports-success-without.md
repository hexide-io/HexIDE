# Clipped Call Stack selection reports success without changing the frame — **CLOSED** (#1182, 2026-10-20)

During #1180 acceptance on Windows, this call reported success but Watches remained in Worker and refused
`callerValue`; the intended caller frame was not selected:

```json
{"target":"Window/Custom[MainView]/Pane[DockControl]/Pane[Root dock]/List[PART_ItemsControl]/List[#0]/TitleBar[Call Stack]/Pane[Call Stack]/Custom/List/Pane[PART_ScrollViewer]/ListItem[CallStackRow { Frame =    Main, Line = 6, Stop = DapStop { SessionId = 0734e6a8-bc5a-48fe-a5d4-b685c2a2aa05, Generation = 3, ThreadId = 1, Reason = breakpoint }, FrameId = 1 }]","action":"select"}
```

The path came from `dump_visual_tree` with `interactiveOnly:false`. Tool panes were clipped in a small window.
Workaround: hide unused panels, resize, then use
`press_key {"target":"Window/Custom[MainView]/Pane[DockControl]/Pane[Root dock]/List[PART_ItemsControl]/List[#0]/TitleBar[Call Stack]/Pane[Call Stack]/Custom/List","key":"End"}`.
This selected Main and its watch returned 42. Investigate provider dispatch and virtualization; selection
replies should verify/report the resulting model selection. The underlying cause is not yet established.

> **Fixed.** `select` reads the selection back before it reports one: an item its list did not take is refused as "'Main'
> was not selected: its list did not take the selection; 1 item is selected now: Worker", where it used to be reported as
> selected. Three ways a select could come to nothing are covered: an item its list cannot place, a list that puts its
> selection back, and, on the Call Stack, a row of a stop the debugger has already left, which the debugger declines and
> the list now gives back. A list its selection handler replaces lets the item go, so a selection the list announced
> there still counts; one put back on a later dispatcher pass is not seen. `double_click`'s selection is read back too. A
> Call Stack row is named as it reads, `Caller, line 12`, where its name was the row record's text, which carried the
> debug session's id and stop generation and so went stale at every stop; and each row is its own, so a recursive call's
> deeper row can be selected. Not reproduced: with the fixture adapter, a select on a caller row by its path moves the
> debugger, and a row clipped out of view has no container, so no path reaches it. The closest match found is the
> declined row, which needs a select made while the session has moved on. Guarded by `SelectVerifiedTests`.
