## ADDED Requirements

### Requirement: The client SHALL declare whole-line folding while it reads folds by line
While the client reads a server's fold by its lines alone, it SHALL declare `lineFoldingOnly`. The change that
makes it read a fold's characters SHALL remove the declaration.

A server told nothing may end a fold part-way along its closing line, and a fold read by whole lines then hides
that line (hexide-io/HexIDE#1363).

#### Scenario: A block's fold from a server that honours the declaration
- **WHEN** a server that honours `lineFoldingOnly` is asked for the folds of a block whose closing line stands
  alone
- **THEN** the block's fold ends on the line before, so the closing line stays visible when the fold is collapsed
