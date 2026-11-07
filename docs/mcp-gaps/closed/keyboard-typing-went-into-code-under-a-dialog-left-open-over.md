# Keyboard typing went into code under a dialog left open over the window (#1290)

With the Edit-and-Continue reset prompt open and unanswered, a second `type_text` in keyboard mode into the code window
returned `typed 3 key(s)`, and all three keys went into the code under the prompt, to be reverted with it if the prompt
was answered No. The prompt was shown over the active window, often the running form when a tool typed the key, so the
code window stayed usable under it; and its focus manager still reported the code as focused, so the typing's check on
focus passed. #1257 stops the typing for a dialog that opens during it, not one already open.

> **Fixed.** The reset prompt is shown over the code editor's own window, which it holds, as VB6's prompt holds the IDE
> (`IWindowManager.MessageBox` takes an owner), unless that window is minimized, which would hide it. Keyboard
> `type_text` and `press_key` are refused before a key is sent, once they have found the control the keys would reach
> and found it showing, while a modal dialog is open
> over the window they act in, directly or through dialogs between; a control in a native popup counts as in the window
> the popup opened from. The refusal names the dialog on top, which takes the keys: "'Reset?', a modal dialog
> over 'Project1 - HexIDE', is open and waits for an answer, so a person's keys would go to it, not to TextArea; nothing
> was typed. Answer it first: …". The reply's note of open dialogs leaves it out. A dialog over another window is left
> to that note, as a person could click past it. The `type_text` and `press_key` descriptions say so. Guarded by
> `TypeKeysTests` and `ResetPromptTests`; the prompt's owner in a running IDE, and the native popup case, were read from
> the code, not run.

> **Measured since (#1329).** "As VB6's prompt holds the IDE" was a claim, not a measurement. VB6's prompt is owned by
> the running form, and it holds the IDE and the program together. HexIDE's holds the one window that owns it. The
> oracle's section "The reset prompt: which edits raise it, and which windows it holds" has both.
>
> **Live (#1330, 2026-10-25), the keyboard half.** Windows, a Debug build of main at beada72, from a Claude Code
> session's hexide tools, with Form1 running and its code window in front
> (`TE` is its `TextEditor`):
>
> ```
> type_text {"target":TE,"text":"Rem ","mode":"keyboard","window":"ide"}
> → typed 1 of 4 key(s) into TextEditor, then stopped: 'HexIDE' opened over the window, and a person's next key would
>    go to it. Answer it, then send the rest
> type_text {"target":TE,"text":"XYZ","mode":"keyboard","window":"ide"}
> → 'HexIDE', a modal dialog over 'DialogProbe - HexIDE [run]', is open and waits for an answer, so a person's keys
>    would go to it, not to TextEditor; nothing was typed. …
> ```
>
> - **After the refusal**, `get_file_content` held no `XYZ`. The first key, `R`, had landed, as the edit that raises
>   the prompt does, and Cancel took it out.
> - **The native popup branch is reachable this way.** A completion list opened with Ctrl+Space stays open when a
>   keyboard `M` raises the prompt. Then
>   `press_key {"target":"TE/List[PART_ListBox]/Pane[PART_ScrollViewer]/ListItem[Dim]","key":"Enter","window":"ide"}`
>   was refused the same way: "… not to ListBoxItem; nothing was pressed."
> - **Both refusals name the prompt by its title alone:** [#1975](https://github.com/hexide-io/HexIDE/issues/1975).
> - **The list closed** when the prompt was cancelled.
