## Choosing a menu item leaves Checked to the program (2026-10-18, #1158; measured by #1159)

**The probe**, written and run by #1159 (Codex) and kept here because it measures something nothing else in this file
does. A shown Form1 with a Root menu and two children, Plain and Toggle. A one-shot Timer finds each child's native
menu ID with `GetMenuItemID(GetSubMenu(GetMenu(Me.hwnd), 0), position)` and dispatches it with
`SendMessage Me.hwnd, &H111, id, 0` (WM_COMMAND), first with each child's Checked set False, then True. Each Click logs
Checked as it enters, and Toggle's also runs `mnuToggle.Checked = Not mnuToggle.Checked`. The Timer logs Checked once
SendMessage returns. Built native and p-code in the oracle guest, and both runs agreed, with Err.Number 0.

| Handler | Checked before | At Click entry | After dispatch |
|---|---|---|---|
| Plain, logging only | False | False | False |
| Plain, logging only | True | True | True |
| Toggle, `Checked = Not Checked` | False | False | True |
| Toggle, `Checked = Not Checked` | True | True | False |

So choosing an item never changes its tick, and the program's Click sees the tick as it was. This is VB6's menu command
dispatch, not a physical mouse gesture and not the VB6 IDE.

**In HexIDE:** Avalonia's menu ticks a CheckBox item before it raises Click, by pointer and by Enter alike. VBMenu puts
the tick back in its class handler for Click, which runs before any handler of the program's (#1231, for #947). #1159's
twelve cases, pointer, Enter and automation from both starting states, with and without a handler that changes Checked,
run in `MenuMembersTests` and pass, so #1158 no longer reproduces.

