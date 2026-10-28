# `set_control_property` could not reach a control-array element, and wrote to the first when given the array's name (#1252)

`get_form_controls` takes an element as `name(index)` and marks its rows settable, but `set_control_property` matched
the name alone. `lblTitle(1)` was refused as no such control. `lblTitle` wrote to whichever element came first, saved
the file, and replied as though the caller's element had been set.

> **Fixed.** `set_control_property` resolves its control with `PropertyText.NamedOne`, as `get_form_controls` reads
> one. `name(index)` reaches that element. A bare name that matches several controls changes nothing, since the save
> would write a guess to disk, and the refusal lists the array's indexes. An index the array lacks is refused the same
> way, rather than as no such control. The reply names the element, as in `optSize(1).Caption is now 'Seven'`. An index
> too big for an int, or written in non-ASCII digits, names no element where it used to throw. Measured on Windows
> through the server's HTTP endpoint, on a copy of the battleship demo: `optSize` was refused with "Index 0, 1, 2", and
> `optSize(1)` set the second option's caption, as the saved `.frm` shows. Guarded by `PropertyTextTests`.
> `move_control` took the first match too, so no element after the first could be moved, and the next save wrote the
> guess to disk; it now resolves the same way. `get_form_controls` gives each element its `index`, so a caller can tell
> the rows apart before naming one. A name with an index on a control that is no array says so. Renaming one element
> still takes it out of its array with no way back, as the Properties window does; the reply says so, and #1276 holds the
> fix. A source test checks that both tools resolve with `NamedOne`.
>
> **Live (#1330, 2026-10-25).** Windows, a Debug build of main at beada72, from a Claude Code session's hexide tools, on
> a copy of the battleship demo:
>
> ```
> set_control_property {"formName":"frmSetup","controlName":"optSize","property":"Caption","value":"Seven"}
> → Nothing was changed: 'optSize' names a control array of 3, Index 0, 1, 2; name one element, such as optSize(0)
> move_control {"formName":"frmSetup","controlName":"optSize","left":20,"top":72}
> → Nothing was moved: 'optSize' names a control array of 3, Index 0, 1, 2; name one element, such as optSize(0)
> set_control_property {… "controlName":"optSize(1)","property":"Caption","value":"Seven"}
> → optSize(1).Caption is now 'Seven', and the file is saved.
> move_control {… "controlName":"optSize(2)","left":216,"top":72}
> → optSize(2) is at Left 216, Top 72, Width 73, Height 17, … It lies at least partly outside frmSetup, …
> set_control_property {… "controlName":"cmdOK(0)","property":"Caption","value":"Go"}
> → Nothing was changed: 'cmdOK' is not a control array, so it takes no index; name it as cmdOK
> ```
>
> `get_form_controls` then gave optSize(1) the caption Seven and optSize(2) left 216, and `take_snapshot` showed
> both. The saved `frmSetup.frm` has `Caption = "Seven"` in Index 1's block and `Left = 3240` in Index 2's. The
> move was saved by the next property save, since `move_control` does not save.
