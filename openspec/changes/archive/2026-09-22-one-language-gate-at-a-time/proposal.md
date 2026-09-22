# One language change awaiting confirmation at a time

## Why

A language change is applied at once and then held behind a gate that reverts it unless it is kept. Each gate
reverts to the language that was active when *it* opened. Nothing stopped a second change while the first gate
was open, so the second gate recorded the first's unconfirmed language as the one to return to, and the order
the two resolved in decided which language the IDE ended in (hexide-io/HexIDE#588). Measured: two
`set_ide_language` calls a second apart opened two gates at once. The Options page cannot reach this, because
the gate is modal over it; automation can.

## What changes

- While a gate is open, another gated change is refused and nothing is applied.
- The service reports which language its open gate is for, and `set_ide_language` answers a refused call with
  that language and how to close the gate.
