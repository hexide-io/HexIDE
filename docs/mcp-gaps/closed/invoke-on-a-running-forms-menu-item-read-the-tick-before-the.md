# `invoke` on a running form's menu item read the tick before the program's Click ran (#1255)

`interact invoke` on a menu item adds "(now ticked)" or "(now unticked)" when the tick moved, read straight after
raising Click. On a running form, `VBMenu` puts the tick back to the program's `Checked` before Click, and the
program's handler runs later, through its queue, yielding at the debugger's time slice before its first statement.
So the reply was built before `mnuLevel(i).Checked = (i = Index)` ran: Game > Intermediate replied with no tick note,
and Intermediate ended up ticked. The `toggle` refusal on a menu item tells a caller that invoke's reply says whether
the tick moved, so a reply with none read as "it did not move".

> **Fixed.** A running form's menu item names no tick when it is clicked. The ticks of every item in its menu are read
> once the wait for the program finds it idle, through the `Settled` read #1280 added, so a level menu's change is named
> whole: "invoked MenuItem 'Intermediate' …; the program ticked 'Intermediate' and unticked 'Beginner'". When the wait
> comes back early, at a breakpoint, a dialog or the time limit, the reply says the form may still change and names no
> tick. `inspect_element` now reads any checkable menu item's tick as `toggleState`, which it did not before, so the tick
> can be read back then too. The IDE's own checkable items, such as View > Minimap, keep the immediate read, which is
> right for them. On a running form's item, `toggle` no longer points a caller at `invoke` for the tick: an item such as
> Exit would run. It says the program sets the tick in the item's Click, and where to read it. Guarded by
> `ProgramWaitTests`, with handlers that tick, untick, do both and do neither, and the click alone on an item whose Click
> ticks it at once.

> **Live, through the hexide tools (2026-10-27, Windows at 150%, a Debug build of main at f9b5649c, headless
> `claude -p`, #1330).** The review of #1289 said it was checked live without saying how, so it was checked again, on a running
> form whose Game menu holds a control array, Beginner (ticked), Intermediate and Expert, and whose Click ticks the one
> clicked. After `expand` on `Window/Menu/MenuItem[mnuGame]`, `invoke` on `…/MenuItem[mnuLevel(1)]` answered `invoked
> MenuItem 'Intermediate' and closed the menu, as a click does; the program ticked 'Intermediate' and unticked
> 'Beginner'`. `inspect_element` then gave Intermediate `"toggleState":true` and Beginner `"toggleState":false`.
