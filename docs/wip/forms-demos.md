# The next forms demos: Battleship, then Minesweeper

> **Work in progress.** Captured 2026-10-13. **Both exist:** Battleship since 2026-10-15, as
> [`demo/battleship-forms`](../../demo/battleship-forms) (#1071), and Minesweeper since 2026-10-17, as
> [`demo/minesweeper-forms`](../../demo/minesweeper-forms) (#1123). The work that blocked them is tracked in issues,
> linked below; this document is the reason those issues were ordered as they were.

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

### What it needed

All of it has landed, and the demo runs under F5 (#1071).

| Needed | Issue | Landed in |
|---|---|---|
| Module-level declarations read from a form, such as the form's own `Const GRID = 10` | #256 | #822's first slice |
| A form-level array sized by a `Const` | #760 | #822's first slice |
| `WithEvents` in a form | #972 | #822's first slice |
| Implicit `Me`: `Caption = "x"` in a form sets the form's caption | #995 | its own fix |
| A control used in `Form_Initialize` loads the form there | #1009 | #1056, #822's slice 2a.1 |
| `Unload Me`, with QueryUnload, Unload and Cancel | #973 | #1046 |
| Default instances, `Load`, `Show` and `Hide` | #822 | #1056, slice 2a.1 |
| `Show vbModal`, and `Sub Main` showing forms | #822 | #1063, slice 2a.2 |
| `MouseDown`, `MouseUp` and `MouseMove` | #974 | #1032 |
| Control positions in twips at run time | #933 | its own fix |
| `Trim$(s)`: a library call written with a `$` | #742 | #1071, which found it. The rest of #742 is still open |

Still to come from #822, and not needed by the demo: `New frmX`, several instances of one form, and the `Forms`
collection (slice 2b).

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

### What it found

As planned, it needed nothing new: it ran under F5 the first time, a game won and a game lost (#1123).

So it grew the Game menu a real Minesweeper has, with the level as a menu control array, and that found three
bugs, each measured on vb6.exe:

| Found | Issue |
|---|---|
| A form with a menu bar loses the bar's height off its client area, and reading `ScaleHeight` on it stops the run | #1127 |
| No menu can be used from code at run time: `mnuLevel(i).Checked` is "Variable not defined", and a menu array's Click gets no Index | #1126 |
| The automation tree names a running form's menu `&Game` | #1128 |

The demo ships without the menu until those land, and the version with it is commit caab5bdd.

## Related

- #822, the keystone, and #972, #973, #974, #933, #256 and #760 above.
- [`demo/README.md`](../../demo/README.md): how demos are kept.
