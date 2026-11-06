# Four texts named a waiting dialog only by its title, which two of the IDE's dialogs share — **CLOSED** (#1975, 2026-10-26)

**Symptom (2026-10-22, #1329, Windows and the openSUSE VM).** During a run, both the reset prompt and the run-time error
dialog are titled `'HexIDE'`. One held a `MessageBox` with Yes and No (OK and Cancel since #1360, measured 2026-10-25),
the other a `RuntimeErrorView` with Continue, End, Debug and Help. The other half of #1365, that only a call naming a
window that doesn't exist lists the open ones, is done: #1624 says so in the `window` description.

**Narrowed (#1624, measured under #1330 on 2026-10-25).** The `modalDialog` note now says what the dialog is:
`'HexIDE' (the run-time error dialog: "Compile error:…", Continue/End/Debug/Help)`, or
`'HexIDE' (a message box: "This action will reset your project, proceed anyway?", OK/Cancel)`.

Three texts still give the title alone:

- the program wait's "has a window open over its form, 'HexIDE'";
- keyboard `type_text`'s stop when a dialog opens part way;
- the refusal from `type_text` and `press_key` while a modal dialog is open.

The calls are in [#1975](https://github.com/hexide-io/HexIDE/issues/1975).

**Workaround.** Read the `modalDialog` note of the next reply that has one, or dump the dialog before answering it.

**Suggested fix.** Use the note's description in the three texts.
[#1975](https://github.com/hexide-io/HexIDE/issues/1975).

**Fixed (#1975).** The program wait, keyboard `type_text`'s stop, the refusal from `type_text` and `press_key`, and
`interact` `close`'s refusal for a window with a dialog over it now describe the dialog as the `modalDialog` note does,
through the same `OpenDialogs.DescriptionOf`. That is its title, then its kind, first line and buttons where it has them.
A dialog of a kind the describer doesn't know, an InputBox among them, gives its title and buttons. One with no title is
"one with no title" in all five texts. The wait describes a MsgBox once it has built its text and buttons, which it does
after it shows. Guarded by `ProgramWaitTests`, `TypeKeysTests` and `CloseWindowTests`.

**Live (#1975, 2026-10-26).** Windows, a Debug build of the fix, from a Claude Code session's hexide tools, on a form with
a 40 ms Timer, a button calling an undefined Sub and a button showing `MsgBox "Hello"`:

- The wait, for the MsgBox: `has a window open over its form, 'DialogProbe' (a message box: "Hello", OK); a MsgBox holds
  its handler`.
- The wait, for the compile error, on 3 of 10 clicks, with the note naming it on the other 7: `has a window open over its
  form, 'HexIDE' (the run-time error dialog: "Compile error:…", Continue/End/Debug/Help)`.
- Keyboard `type_text` into the code window while running: `typed 1 of 3 key(s) into TextEditor, then stopped: 'HexIDE'
  (a message box: "This action will reset your project, proceed anyway?", OK/Cancel) opened over the window`.
- With that prompt open, `type_text` and `press_key` were refused with replies starting `'HexIDE' (a message box: "This
  action will reset your project, proceed anyway?", OK/Cancel), a modal dialog over 'DialogProbe - HexIDE [run]'`.
