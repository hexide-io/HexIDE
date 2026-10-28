# No tool says which control has the focus, and `press_key` moves it there

**Symptom (2026-10-23, #1583, Windows).** On a running form with Text1 focused, whose program logs every GotFocus,
`press_key {"target":"Window","key":"Tab"}` answered `{"success":true,"mechanism":"window-input","detail":"pressed Tab
on VBTextBox, at Window/Edit[Text3]"}`. Six of them logged `Text3_GotFocus`, `Text1_GotFocus` six times. `PressKey`
calls `target.Focus()` before delivering the key, and a container's target is its first focusable control. So each
press moved the focus to Text3, raising the program's own events, and Tab then went round to Text1. The reply names
Text3, but not that the focus was moved there.

**Workaround.** Address the control that has the focus. Nothing reports it: `inspect_element` gives
`isKeyboardFocusable`, not focus, and `dump_visual_tree` has no focused node. So a Tab sequence can be driven only by
knowing where each press will land, which assumes the order being checked. A wrong guess moves the focus to the guess,
and the program's log shows it as a GotFocus that no Tab caused.

**Suggested fix.** Report `isFocused` from `inspect_element`, and the focused node in `dump_visual_tree`. Press on the
focused control when the target is a container that holds the focus. Say in the reply when delivering the key moved the
focus, and from where. [#1627](https://github.com/hexide-io/HexIDE/issues/1627).
