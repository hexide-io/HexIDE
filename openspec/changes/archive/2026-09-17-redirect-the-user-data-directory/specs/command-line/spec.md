## ADDED Requirements

### Requirement: A relative path on the command line SHALL mean relative to where HexIDE was started
A relative path given on the command line — a project to open, or a user data directory — SHALL be resolved
against the working directory HexIDE was started in, as it would be for any other program run from that
shell.

HexIDE moves its working directory to its own folder as it starts. Paths SHALL be resolved against the
directory it was started in, not the one it moved to; resolved after the move, a relative project path named
a file beside the executable, and the IDE opened nothing and reported nothing.

#### Scenario: A relative project path from another directory
- **WHEN** HexIDE is started from a directory `D` with the argument `sub/Project.vbp`
- **THEN** the project opened is `D/sub/Project.vbp`

#### Scenario: A relative user data directory
- **WHEN** HexIDE is started from a directory `D` with `--user-data-dir profile`
- **THEN** per-user files are kept in `D/profile`, not beside the executable
