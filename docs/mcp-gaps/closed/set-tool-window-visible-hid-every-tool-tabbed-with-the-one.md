# `set_tool_window_visible` hid every tool tabbed with the one it was asked to hide — **CLOSED** (#2362, 2026-10-27)

**Symptom (2026-10-27, Windows, #2351's live check).** With AI Chat and the Toolbox tabbed together in the left dock,
`set_tool_window_visible {"name":"AI Chat","visible":false}` answered `{"success":true,"note":"AI Chat is now hidden."}`,
and `get_tool_windows {}` then gave both `"Toolbox"` and `"AI Chat"` as `visible: false`. A person closing AI Chat's tab
closes only AI Chat.

**Workaround.** Close the tool with its own tab's close button through `interact`, or hide only tools alone in their dock.

**Cause.** `MainViewViewModel.SetToolWindowVisible` closed the tool's parent dock, on the assumption that a dock holds
exactly one tool. Since #2239 and #2277 a drag can put several tools in one dock as tabs. An add-in tool window
unregistering closed its parent dock the same way.

**Fixed (#2362).** Both close the tool itself, as its tab's close button does; its dock goes only when it is left
empty. The reply names the window as `get_tool_windows` does, says when it was already in the state asked for, and
names any other tool window that opened or closed with it. Two tests in `MainViewViewModelTests` tab Properties
with the Toolbox, hide Properties and show it again; both fail with the dock closed instead.

**Live (2026-10-27, Windows, a Debug build of the fix, headless `claude -p` through the hexide tools).** The session's
saved layout put the Toolbox and Properties on the left, which builds them as two tabs of one dock; `dump_visual_tree`
showed `…/TitleBar[Toolbox]/Pane[Toolbox]/Tab[PART_TabStrip]/…/TabItem[Properties]`.

- `set_tool_window_visible {"name":"Properties","visible":false}` answered `{"success":true,"note":"Properties is now
  hidden."}`. `get_tool_windows` then gave Toolbox `visible: true` and Properties `visible: false`, and the snapshot
  showed the Toolbox filling the left panel with no tab strip.
- `{"name":"Properties Window","visible":true}` answered `Properties is now shown.`, and both were visible again.
- The same call with `"Properties"` answered `Properties is now shown, as it was already.`
