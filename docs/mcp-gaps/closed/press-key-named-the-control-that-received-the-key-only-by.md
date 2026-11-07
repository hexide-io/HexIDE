# `press_key` named the control that received the key only by its class — **CLOSED** (#611, 2026-09-22)

> **Fixed.** When the input goes somewhere other than the control addressed, `press_key` and `type_text`
> now give the receiver's path as `dump_visual_tree` prints it, walked down from the addressed control with
> the same segment choice, so the path can be fed straight back as a target. A TextArea that the control
> view folds into its editor is given as its editor's path, which resolves back to the same TextArea. Live, on
> a `--newproject` IDE with Form1's code window open:
> `press_key {"target":"Window","key":"End"}` → `pressed End on TextArea, at Window/Custom[MainView]/…/None[TextEditor]/Pane[PART_ScrollViewer]/None`.
> Pressing End again at that path answered `pressed End`, with no redirect.

**Symptom.** `press_key {"target":"Window","key":"Return"}` answered `pressed Return on TextArea`. The IDE holds
a TextArea for the Immediate window and one per open code editor, so the class said which kind of control got
the key and not which one.
