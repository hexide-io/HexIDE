# `match: "TextEditor"` did not find the Immediate window, though the description said it did — **CLOSED** (#2513, 2026-10-28)

**Symptom (2026-10-28, Windows, hexide-int, #2496's live check).** With the Immediate window shown and seven lines of a
program's `Debug.Print` output in it, `dump_visual_tree {"window":"ide","match":"TextEditor"}` answered "Nothing matches
'TextEditor'", and so did the same call scoped with `root` to the Immediate pane. `match: "Immediate"` with
`interactiveOnly: false` found only the hidden toolbar button. The tool's description promised that `TextEditor` finds
"the Immediate window's" editor. The window is an `ImmediateToolView` over an unnamed `TextArea`, and no node carried
its text, so a caller could not tell an empty window from one it could not reach.

**Workaround, before the fix.** `inspect_element` on `.../TitleBar[Immediate]/Pane[Immediate]/Custom`, the
`ImmediateToolView`: its `dataContextMembers` give `Document` with the whole text as its value.

**Cause.** The editor had no AutomationId, no provider of its own, and no focus (its `TextArea` takes it), so
`UiAutomationDriver.Classify` folded it away as a structural wrapper. The code window's editor escapes that only because
its context menu gives it a provider.

**Fixed (#2513, PR #2550).** The editor carries the AutomationId `Immediate.Editor` and the window's name as its
automation name, so it is the node `…/Pane[Immediate]/Custom/None[Immediate.Editor]` in every language, which
`match: "TextEditor"` and `match: "Immediate"` both find. `inspect_element` on a `TextEditor` whose document is not a
project file gives its text as `value`, with `isReadOnly`; a code window's editor still leaves its file to
`get_file_content` (#562). Guarded by `ImmediateWindowTreeTests`. Since #2531, the run, step, break and stop replies also
give what the program printed in `printed`, so the commonest check after a run takes no dump at all.

**Live (2026-10-28, Windows, headless `claude -p` from the descriptions alone).** After a run whose `Form_Load` printed
two lines, `dump_visual_tree {"window":"ide","match":"TextEditor"}` returned the one node above, `match: "Immediate"`
returned it among six, and `inspect_element` on it gave `value: "Hello from 2513\nsecond line\n"`.
