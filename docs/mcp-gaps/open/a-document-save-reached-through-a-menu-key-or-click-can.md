# A document save reached through a menu, key or click can reply before its picker opens

**Symptom.** Narrowed by headless measurements for [#1335](https://github.com/hexide-io/HexIDE/issues/1335),
2026-10-23 (`UiSaveAnswerTimingTests`; no MCP calls were made). After arming a cancel with
`ScriptedFileDialogs.AnswerNextWith(null)`, these tests read `ScriptedFileDialogs.Pending` immediately after the
same action code the tools use returns:

- `invoke_menu_item(path: "File/Save Project As...")`: resolving the real MainView menu and executing its routed
  command takes the answer before returning: **0 pending**.
- `interact(target: "Window/#Standard.SaveProject", action: "click", window: "ide")`: the real toolbar button's
  press and release take the answer before returning: **0 pending**. The count is read after `FinishAsync`, as in
  the tool, not after the press alone.
- `press_key`: the exact headless call is
  `UiAutomationDriver.PressKey(editor, gesture.Key.ToString(), gesture.KeyModifiers.ToString())`, with the real
  CodeEditorView's TextEditor and `gesture = ApplicationCommands.SaveCommand.Gestures.Single()`: **0 pending**
  without Format on Save; **1 pending** while a formatting response is held, then **0** when the response completes.
  The test reads the platform binding (Ctrl on Windows/Linux, Cmd on macOS), rather than hard-coding Ctrl.

Project-save menu and button replies now reuse #810's pending-answer sentence when no picker took an answer,
with the reason "this save opened no picker" (a project command can save several documents). The tests also
exercise an empty project set, where **1 remains pending**, and assert the reply, not just the count.

The remaining gap is the focused document's save. `CodeEditorViewModel.SaveForm` / `SaveModule` discard the
`SaveWithFormattingAsync` task through `ListenErrors`; `RoutedCommand.Execute`, the command binding, and key /
pointer delivery expose no save task. Awaiting the action or draining the dispatcher cannot await an arbitrary
language-server response. A pending count at that point does not justify saying the save opened no picker.
The same document commands can be reached by a menu or `interact`; they are deliberately excluded from the
project-command feedback.

**Workaround.** `answer_next_file_dialog` warns about document-save timing. `clear_file_dialog_answers` reports
and discards answers still pending, but if formatting is still running, clearing an answer can remove it before
the eventual picker takes it. Prefer the tools that await their save and report the answer's state where applicable.

**Suggested fix.** Expose the focused editor's save task through routed-command execution before adding
pending-answer feedback to document saves. Waiting for dispatcher idle or adding a fixed delay would not establish
completion. The remaining scope is [#1335](https://github.com/hexide-io/HexIDE/issues/1335).
