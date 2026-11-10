# command-line Specification

## Purpose
How HexIDE interprets the arguments it is started with. The option list itself is documented for users in
`docs/command-line.md` and checked against the parser by `CommandLineDocumentationTests`; this capability holds
the behaviour that list cannot express, starting with what a relative path on the command line is relative
to.

## Requirements

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

### Requirement: Desktop startup does not depend on window activation
The desktop main window SHALL initialize its startup project hook, dock layout-save handler and add-in
menus and commands once when it opens. Activation SHALL continue to refresh command availability without
repeating initialization.

#### Scenario: An X display without a window manager
- **WHEN** the desktop main window opens without being activated, with `--newproject` specified
- **THEN** the startup hook creates the default project
- **AND** later activation or reopening the same window does not run initialization again

### Requirement: Absolute project paths are accepted alongside slash-prefixed flags
HexIDE SHALL accept an absolute positional path ending in `.vbp`, `.vbg` or `.twinproj`, including a Unix
path beginning `/`. Recognized slash-prefixed flags SHALL continue to take precedence over positional paths.

#### Scenario: An absolute Unix project path
- **WHEN** HexIDE is started with `/capture-lsp /tmp/example/Project.vbp`
- **THEN** capture is armed and `/tmp/example/Project.vbp` is selected as the project to open

### Requirement: Native Wayland is opt-in on Linux
Desktop SHALL accept `--platform <name>` in Debug and Release, overriding `HEXIDE_PLATFORM`.
Desktop and Standalone SHALL read that variable and use the same backend selection: `x11` (default)
uses platform detection, `auto` tries Wayland with the detected backend as fallback, and `wayland`
requires Wayland without fallback. Selection SHALL leave Windows and macOS platform detection unchanged.
Values SHALL be case-insensitive. An absent Desktop selection SHALL use the environment variable;
an absent environment selection or an unrecognized value SHALL select `x11`. A flag without a value
SHALL be ignored.

#### Scenario: A made program opts in
- **WHEN** a Standalone program starts on Linux with `HEXIDE_PLATFORM=auto`
- **THEN** it tries native Wayland and falls back to X11 if Wayland initialization fails

#### Scenario: Desktop overrides the environment
- **WHEN** Desktop starts with `--platform x11` and `HEXIDE_PLATFORM=wayland`
- **THEN** it uses the existing platform detection path

#### Scenario: Diagnosis requires Wayland
- **WHEN** Desktop starts on Linux with `--platform wayland` and Wayland cannot initialize
- **THEN** startup fails rather than silently using X11
