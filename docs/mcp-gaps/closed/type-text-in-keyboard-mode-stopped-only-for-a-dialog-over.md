# `type_text` in keyboard mode stopped only for a dialog over its own window (#1257)

Keyboard mode stops when a dialog opens, since a person's next key would go to it. It looked only at the dialogs owned
by the window it typed into. The one dialog typing itself raises, the Edit-and-Continue reset prompt, is shown over
whichever window is active, often the running form. Headless, with the dialog over a second window, a second key went
into the text under it, and the typing stopped a key late on the focus check, with a reason that did not name the
dialog.

> **Fixed.** The typing records the modal dialogs open over every window before the first key, as the dialog note
> does, and stops after any key that opens a new one, wherever it opens. A program's own MsgBox that opens as the keys
> go in counts too. A MsgBox that moves to a window of its own when its form is hidden is the same box, not a new one.
> The reply names where the dialog opened: "'Reset?' opened over the running form 'Form1'", "over the window" for its
> own, or "in a window of its own". It records the dialog as named, so the reply's note of open dialogs does not name it
> a second time. `type_text`'s `mode` description says so. Guarded by `TypeKeysTests`.

> **Live, through the hexide tools (2026-10-27, Windows at 150%, a Debug build of main at f9b5649c, headless
> `claude -p`, #1330).** The review of #1291 said it was checked live without saying how, so it was checked again, with Form1
> running and the caret on a blank line of its code. `type_text {"text":"abcdef","mode":"keyboard"}` into the code
> window answered `key 1 of 6 was sent to TextEditor and raised 'HexIDE' (a message box: "This action will reset your
> project, proceed anyway?", OK/Cancel) over the window; that key may not have landed. Nothing after it was sent.`
> The IDE had the focus, so the prompt opened over its own window. Cancel left the line blank.
