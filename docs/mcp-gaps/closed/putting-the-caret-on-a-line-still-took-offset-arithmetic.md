# Putting the caret on a line still took offset arithmetic (#700)

Once `get_file_content` returned the whole file (#273 task 3.18), a caller could at least count out the buffer
offset `interact set_property CaretOffset=N` takes, but it still had to count: there was no way to name a line and
column, as every tool that takes a line does. Reaching a member's `Attribute` line meant `press_key` Ctrl+End, Up three
times, then Home.

> **Fixed.** `open_file` takes `line` and `column`, 1-based and counted as the code window numbers them, header
> included, so a line from `get_diagnostics`, a breakpoint or `get_file_content` can be passed straight on. Columns
> count characters, as the status bar does, so a tab is one; one past a line's last character is its end. A place off
> the text, or between the halves of one character, is refused before anything opens, rather than clamped, since a
> caret quietly put elsewhere would take the next `type_text` there; so are `column` without `line` and a line for a
> carried file. The note reads the caret back once focus and layout have run, rather than echoing the request, and says
> so if the editor moved it.
>
> The review found three ways the caret was not where it could be seen, and the first two reached any navigation, as
> go-to-definition into a file not yet open places the caret the same way. A view made for a caret already placed did
> not scroll to it, as `ScrollToLine` does nothing before layout; it now scrolls once laid out. A caret placed in a
> header before its fold existed was folded away under it; a new header fold is now left open over the caret. A caret
> put at a folded member run's end was moved to the declaration's end, while the reply said it was where asked; a caret
> navigated into a fold now opens it. Measured on Windows through the server's HTTP endpoint, on a copy of the
> battleship demo with an Attribute run added to `Game.Init`: a line past the end opened nothing; `frmBoard` opened at
> line 150 showed it; `Game` at the run's end opened the run with the caret there; and `type_text` wrote where
> `open_file(frmSetup, line 94, column 5)` put the caret. Guarded by `CaretPlacementTests` and `OpenFileCaretTests`.
>
> **Live (#1330, 2026-10-25).** Windows, a Debug build of main at beada72, from a Claude Code session's hexide tools, on
> a copy of the battleship demo with
> `Attribute Init.VB_Description` and `Attribute Init.VB_HelpID` added under `Game.Init`:
>
> ```
> open_file {"name":"frmBoard","line":999}
> → Nothing was opened: in BattleshipForms/frmBoard, line 999 is not one of its lines, numbered 1 to 153
> open_file {"name":"frmBoard","line":150}
> → … The caret is at line 150, column 1: offset 4942 in the text get_file_content returns.
> open_file {"name":"Game","line":25,"column":29}
> → … The caret is at line 25, column 29: offset 707 in the text get_file_content returns.
> open_file {"name":"frmSetup","line":94,"column":5}, then type_text "Rem " into its TextEditor
> → get_file_content: line 94 reads "    Rem If optSize(2).Value Then"
> ```
>
> `get_document_tabs` after the first call listed no frmBoard tab. `take_snapshot` after the second showed line 150
> in view, with "Ln 150, Col 1" in the status bar. After the third, it showed the two Attribute lines unfolded under
> `Init`, with the caret after the second's `1` and "Ln 25, Col 29".
