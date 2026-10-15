# Battleship on forms

The [console Battleship](../battleship/)'s engine, played on forms, written the way a VB6 developer would write it:
- `Sub Main` shows a setup dialog `vbModal`, asking for your name and the board size;
- the board loads its grid of Labels with `Load` at run time, positions each in twips, and sizes the form to fit;
- the board holds `Private WithEvents mGame As Game`, and the game raises `Result` for each shot, which the form paints;
- `MouseDown` on a cell fires at it;
- a win, or running out of shots, asks `MsgBox "Play again?", vbYesNo`. No unloads the board with `Unload Me`, and the
  run ends with the last form.

![The board mid-game](screenshot.png)

## Files

| File | What it is |
|---|---|
| `BattleshipForms.vbp` | The project. It starts at `Sub Main`. |
| `modMain.bas` | `Sub Main`, and what the setup dialog asked for. |
| `frmSetup.frm` | The setup dialog: a name, and 5 x 5, 7 x 7 or 10 x 10. OK hides it, so Main can read it. Cancel or the close box unloads it, and the run ends there. |
| `frmBoard.frm` | The board. |
| `Game.cls`, `Grid.cls`, `Ship.cls` | The console demo's engine. The grid takes any size, and the fleet is placed at random, two ships on 5 x 5 and the classic five on 10 x 10. |

You get half as many shots as the board has cells. A miss is pale blue and a hit red, a ship sunk turns dark red, and
when the shots run out the ships you missed are shown in grey.

## Running it

- **In HexIDE:** open `BattleshipForms.vbp` and press F5.
- **In VB6:** `VB6.EXE /make BattleshipForms.vbp`. It builds cleanly, native and p-code.

## What it took

Writing it the natural way found one gap HexIDE had: a call written with a `$`, `Trim$(txtName.Text)` in the setup
dialog, stopped the run. The call form is fixed here, from a measurement of which library functions have a `$` form
and what the `$` changes. The rest of the type-character work is #742.
