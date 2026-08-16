# Battleship

A console analog of the **Rubberduck [Battleship](https://github.com/rubberduck-vba/Battleship)** game engine —
and, unlike the other demos here, a **fidelity cross-check** rather than a graphical intro.

The same VBA program runs **byte-for-byte identically** on two engines:

- HexIDE's in-process tree-walking interpreter (the `BattleshipChallenge` runtime regression test), and
- the real **VB6 compiler + runtime** (`Battleship.exe`, built with `VB98\VB6.EXE`).

Both produce exactly this `output.txt`:

```
Firing...
(0,0) HIT
(0,1) SUNK
(5,5) MISS
(2,0) HIT
(2,1) HIT
(2,2) SUNK
AllSunk=False
```

## What it exercises

One small program that leans on the whole object model at once:

- **Four class modules** — `Ship`, `Grid`, `Game`, `Announcer`.
- **`Property Get`/`Let`** (`Ship.Size`, `Game.AllSunk`) and **`Class_Initialize`** (`Game` builds its grid).
- A **2-D array grid** (`mState(0 To 9, 0 To 9)`, `mShipId(0 To 9, 0 To 9)`) initialised with nested `For` loops.
- An **object "fleet" array** — `Private mFleet(0 To 2) As Ship` with `Set mFleet(i) = New Ship` (object stored
  into an array element, a counted reference so a ship stays alive while the fleet holds it).
- **Custom events** — `Game` declares `Public Event Result(...)` and `RaiseEvent`s it; `Announcer` handles it
  via `Private WithEvents mGame As Game` + `mGame_Result`, with `ByVal` args, dispatched synchronously.

## Attribution & scope

Inspired by the **Rubberduck Battleship** (MIT, © Mathieu Guindon / the Rubberduck project). This is an
**original clean-room analog of the game *engine***, not a port — the code here was written from the concept, not
copied.

A *faithful* port of the real Rubberduck Battleship deliberately **cannot** run in HexIDE's interpreter, and that
is by design: the original is built on **`Implements`** (VBA interfaces — its whole point), **MSForms** UserForms
for the UI, and the **Excel object model** — all permanent walls, because they require a static/bound type model
(interface-vtable dispatch), which is the job of a real language engine behind the replaceable backend seam, not a CST-level
demonstrator. The object-oriented *engine* underneath, though — classes, properties, `Class_Initialize`, object
arrays, and `WithEvents` — runs identically to VB6, which is exactly what this demo shows.

## Running it

- **Pre-built:** run `Battleship.exe`; it writes `output.txt` next to itself.
- **Rebuild** (needs VB6 installed): `VB6.EXE /make Battleship.vbp`.
- **In HexIDE's interpreter:** the same program is the `BattleshipChallenge` test in
  `IDE/HexIDE.Runtime.Tests/` — `dotnet test HexIDE.Runtime.Tests/ --filter BattleshipChallenge`.
