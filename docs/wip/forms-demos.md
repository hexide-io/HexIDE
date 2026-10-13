# The next forms demos: Battleship, then Minesweeper

> **Work in progress.** Captured 2026-10-13. Neither demo exists yet. The work that blocks them is tracked in
> issues, linked below; this document is the reason those issues are ordered as they are.

## Why demos

The demos in [`demo/`](../../demo) are visual intros, plus a console Battleship that checks the interpreter
against real VB6 output. None is the program a VB6 developer would recognise as theirs: two or three forms, a
grid of controls, a class raising events to a form, a dialog that asks a question and closes.

A forms demo that runs under F5 is the most direct evidence that HexIDE runs real VB6 programs, and building one
is a reliable way to find out what still stops them.

## Battleship, on forms

The program as a VB6 developer would naturally write it:

- a setup dialog form, shown modally, that asks for the player's name and the board size;
- a board form holding `Private WithEvents mGame As Game`, so the game class raises events and the form reacts;
- a grid of Labels in a control array, created at run time with `Load`, positioned in twips, and coloured for
  a hit or a miss;
- `MouseDown` on a cell to fire at it;
- `Unload Me` to close a form, ending the run when the last one goes, and `MsgBox "Play again?", vbYesNo`.

The engine already exists as the console demo in [`demo/battleship`](../../demo/battleship).

**Decided:** there is no shortcut version. A single-form Battleship, with `End` instead of `Unload Me` and Click
instead of `MouseDown`, would run sooner, but it would show off a subset of VB6 that real programs do not fit
into. Real programs need the full set, so the demo waits for it.

### What it needs

**Done on main:** a program that hits something HexIDE does not implement now stops with an error rather than
silently (#754); identifiers are case-insensitive (#811); `Class_Initialize` no longer ends the object it is
building (#850); subscripts and control-array indexes are converted as VB6 converts them (#849); and runtime
Label colours, a number assigned to a `Caption`, and `MsgBox` styles all work.

**The keystone, #822:** one interpreter per run, with each form an instance of its module and a real `Me`.
Today every form opens an interpreter of its own and `Me` is the window, so a second form has nowhere to run
and nothing is shared between forms. Most of what remains either comes with #822 or is small once it lands:

| Needed | Issue | Depends on #822 |
|---|---|---|
| Module-level declarations read from a form, such as the form's own `Const GRID = 10` | #256 | Mostly fixed by it |
| A form-level array sized by a `Const` | #760 | Mostly fixed by it |
| `WithEvents` in a form | #972 | An acceptance test for it |
| `Unload Me`, with QueryUnload, Unload and Cancel | #973 | The first slice does not |
| Default instances, `Load`, `Show`, `Hide`, `New frmX`, and `Show vbModal` | To be filed once #822 lands | Yes |
| `MouseDown`, `MouseUp` and `MouseMove` | #974 | No |
| Control positions in twips at run time | #933 | No |

## Then Minesweeper

Minesweeper is a good second demo because it leans on different parts of VB6 than Battleship does, while
needing almost nothing Battleship has not already brought:

- **Right-click to flag a cell:** `MouseDown` with `Button = vbRightButton`, so #974 again, with the button
  argument encoded as VB6 encodes it.
- **A clock:** the Timer control, which already runs.
- **Revealing an empty region:** a recursive flood fill over a 2-D array, which exercises recursion and bounds
  checking. Recursion already works.
- **A grid of buttons or Labels** created at run time with `Load`, as Battleship's is.
- **A face button that restarts the game,** and a `MsgBox` on a loss.

Things to leave out until the interpreter has them: a high-score table needs file I/O (`Open`, `Print #`,
`FreeFile`), which still stops the run, and `Static` locals do too.

## Related

- #822, the keystone, and #972, #973, #974, #933, #256 and #760 above.
- [`demo/README.md`](../../demo/README.md): how demos are kept.
