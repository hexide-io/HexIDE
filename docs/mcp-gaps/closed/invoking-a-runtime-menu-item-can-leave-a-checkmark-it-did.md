# Invoking a runtime menu item can leave a checkmark it did not request — **CLOSED** (#947, 2026-10-20)

> **Fixed.** Avalonia's menu flips a CheckBox item's `IsChecked` before it raises Click, and every run-time menu item
> is a CheckBox item so that code can tick it. `VBMenu` read `IsChecked` inside its Click handling to keep the
> program's value, so it kept the flipped one: a pointer click, Enter or an access key on a running form's menu item
> ticked or unticked it. `VBMenu` now holds the program's `Checked` and puts it back before the item's Click runs, so
> a Click that sets Checked still has the last word. The `interact` calls recorded below do not reach that path on
> the build they ran on: `invoke` then only raised Click, which never moves a tick. So the ticks seen on 2026-10-18
> most likely came from a click or key that was not recorded, and they were not reproduced through those calls.
> Since #947 `invoke` clicks as the menu does, tick included, and meets the same guard. Guarded by
> `RuntimeMenuTests`, by pointer and through `interact` `invoke`, and measured on the running Minesweeper demo:
> invoking New moved no tick, and invoking Intermediate moved the tick there from Beginner, as its Click sets it.

**Symptom (2026-10-18, #1156 live verification).** On the running Notepad form, these calls succeeded:

```text
interact {"window":"frmNotepad","target":"Window/Menu/MenuItem[&Edit]","action":"expand"}
interact {"window":"frmNotepad","target":"Window/Menu/MenuItem[&Edit]/Pane/MenuItem[&Copy]","action":"invoke"}
```

After invoking Copy and Select All, `take_snapshot {"window":"frmNotepad"}` showed checkmarks beside both items.
Neither handler sets Checked and neither menu is declared checked. The VBMenu click override normally preserves
Checked, so the automation peer's invocation path needs checking against the ordinary click path. A breakpoint
confirmed a subsequent Copy invocation did reach `mnuEditCopy_Click`; the unexpected tick is separate from dispatch.

**Workaround.** Drive the runtime menu shortcut with `press_key`, for example
`{"window":"frmNotepad","target":"Window/Edit[#1]","key":"C","modifiers":"Ctrl"}`. Select text first and
use a breakpoint to distinguish the VB6 handler from a TextBox's own shortcut handling. For opening a menu,
`expand` only opens it; explicitly invoke its parent when testing that parent's VB6 Click handler.

**Suggested fix.** Make the runtime menu's automation invocation preserve the program's Checked value, as its
pointer click path does, and cover both paths with a regression test.
