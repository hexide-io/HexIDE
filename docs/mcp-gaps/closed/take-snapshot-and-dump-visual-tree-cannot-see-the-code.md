# `take_snapshot` and `dump_visual_tree` cannot see the code editor's completion list or parameter-info popup — **CLOSED** (#942, 2026-10-20)

**Symptom.** (Linux, openSUSE Leap 16 under XWayland, Debug build of main at b15c7cf, 2026-10-13.)

```
press_key {"target":"Window/Custom[MainView]/Pane[DockControl]/Pane[Root dock]/List[PART_ItemsControl]/List[#0]/Pane[#0]/Custom[Root]/None[TextEditor]","key":"Space","modifiers":"Ctrl"}
take_snapshot {}                     → the code window, with no list
dump_visual_tree {"window":"auto"}   → MainWindow only
```

The run concluded that no list opens, and that was wrong: a Tab sent straight after was swallowed with no
didChange, which needs an open list, and a headless reproduction confirms the list is open there (#941). Both
tools answer the same whether or not a list is open. AvaloniaEdit's completion and insight windows are Popups
parented only logically to the TopLevel, and the routes the tools use find visual-child popups plus open
ContextMenus and flyouts. Cross-platform by code reading.

**Workaround.** Do not read an empty snapshot as "no list". Infer the list from its effect: `press_key` Down then
Tab, then read `get_file_content`, or the next `textDocument/didChange` in `list_lsp_messages`. Until #941 is
fixed, Tab appends the item instead of replacing the prefix.

**Suggested fix.** Expose CodeEditorView's completion and insight windows through an internal accessor, and return
the open one from `AttachedPopupOf` for a CodeEditorView owner, so the snapshot and the tree both reach it.
[#942](https://github.com/hexide-io/HexIDE/issues/942).

> **Fixed.** The code window's open completion list and parameter info are the editor's attached popups:
> `dump_visual_tree` and `find` list them under the `TextEditor` that opened them, both at once when typing an
> argument has opened a list under the call's parameter info, and `take_snapshot` composes them into the image, by
> the same route that reaches a context menu or a flyout. A list's items are named by their text, so a path reads
> `ListItem[MsgBox]` and `interact` `select` with a value picks one by name. A closed list is not reported. Guarded
> by `CodeEditorPopupTests`. The snapshot's composition follows from that route and is not exercised headless, where
> a popup is drawn in the window's own overlay; there the walk also meets each popup a second time under `Window`,
> as it does a context menu. Still out of reach: the description tooltip beside a list item, and committing an item
> by `double_click`, which the list takes on a press; select it, then `press_key` Tab or Enter, which is #941's.
>
> **Live (#1330, 2026-10-25).** Windows, a Debug build of main at beada72, from a Claude Code session's hexide tools,
> after keyboard `type_text` of `Private Sub Probe()` and a line break into Form1's code (auto-indent and the automatic
> `End Sub` followed), `open_file` at line 19, column 5, and keyboard `type_text` of `MsgBox(`:
>
> - `take_snapshot {}` showed the parameter info beside the caret: `MsgBox([Prompt], [Buttons As VbMsgBoxStyle =
>   vbOKOnly], [Title], [HelpFile], [Context]) As VbMsgBoxResult`.
> - **The signature text needs `interactiveOnly: false`.** Rooted at the `TextEditor`, that dump lists it as
>   `Text[#1]` and `Text[Prompt]`. The default lists only the popup's hidden overload arrows, `Button[PART_UP]` and
>   `Button[PART_DOWN]`.
> - After `press_key` Ctrl+Space, `take_snapshot {}` showed the list at the caret, with the parameter info over its top
>   rows. The default dump rooted at the `TextEditor` listed its rows as
>   `…/None[TextEditor]/List[PART_ListBox]/Pane[PART_ScrollViewer]/ListItem[Dim]` and on, each with `selectionItem`.
> - `interact` `select` with value `MsgBox` on `List[PART_ListBox]` replied "selected 'MsgBox' (it was scrolled out
>   of view, so the list was scrolled to it first)", and inserted nothing.
> - **Not met through the client: the default whole-window dump.** `dump_visual_tree {}` was 75,575 to 83,944
>   characters each time, which Claude Code would not take inline and saved to a file, so the dumps above were rooted
>   at the `TextEditor`: [#1982](https://github.com/hexide-io/HexIDE/issues/1982).
