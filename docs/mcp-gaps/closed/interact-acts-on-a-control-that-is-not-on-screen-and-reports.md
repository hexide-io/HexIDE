# `interact` acts on a control that is not on screen, and reports success — **CLOSED** (#1069, 2026-10-20)

> **Fixed.** Every `interact` verb except `invoke_command` and `set_property` is refused on a control that is
> not showing, before its peer is asked to act, and so are `press_key` and `type_text`, which reached the same
> button by other routes (Enter on a hidden Button clicked it). `select` with a text matches among the rows that
> show, so a hidden row is neither selected nor counted against a showing one. The refusal names the verb, says
> the control is not showing, and names the `"isHidden": true` flag the dump shows; for `scroll` it points at the
> container instead. The two reflection actions act on the view model and stay exempt.
>
> "Not showing" gained a case on the way: a control under a container that paints nothing and takes no pointer.
> A running VB6 form hides a Frame or PictureBox that way (`VBVisibility`), so that its controls' code keeps
> running, and those controls had read as visible to both the dump and the tools. Guarded by
> `UiAutomationDriverTests`, including a path read from the dump and resolved, as a caller addresses it.

**Symptom.** Measured on 2026-10-15 in AI Chat's transcript (#273 task 3.22). Each message is templated with an
Approve and a Reject button, shown only while that message is an approval prompt. So `dump_visual_tree` lists a
pair per message, each one `"isHidden": true` except the pair of the prompt awaiting a decision. Invoking a
hidden one:

```
interact {"target":".../TitleBar[AI Chat]/Pane[AI Chat]/Custom/Custom/Pane[Scroller]/List/Button[#0]","action":"invoke"}
→ {"success":true,"mechanism":"peer","detail":"invoked Button 'Approve'"}
```

Nothing happened: that message was never a prompt, and its view model ignores the decision. No person can press a
button that is not shown, and a caller who takes the first `Approve` in the tree reads that it approved the
pending call. A control whose handler has no such guard would act out of sight.

**Workaround.** Pick the node without `isHidden`. In AI Chat's transcript, the pending prompt's pair is the only
one without it.

**Suggested fix.** Refuse a provider or pointer action on a control that is not effectively visible, saying why
and naming the `isHidden` flag. [#1069](https://github.com/hexide-io/HexIDE/issues/1069).
