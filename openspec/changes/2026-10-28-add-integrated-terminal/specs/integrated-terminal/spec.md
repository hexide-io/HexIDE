## Purpose

The integrated terminal runs a shell or another text-mode program inside the IDE: a VT terminal emulator in a
tool window, hosting its process through the platform's pseudo-terminal, with scrollback, selection, links, IME
input and several sessions at once. An external backend's interactive shell and a VB6 program that uses the
Console library are two of its clients. This spec covers the first delivery; `design.md` in the change that
introduced it lists what comes later.

## ADDED Requirements

### Requirement: A terminal session SHALL run a profile's process through a pseudo-terminal
The IDE SHALL start a terminal session from a profile, running the profile's command with its arguments, working
directory and environment through the platform's pseudo-terminal: ConPTY on Windows 10 1809 and later, a pty on
Linux and macOS. It SHALL set `TERM` to `xterm-256color` and `COLORTERM` to `truecolor` unless the profile sets
them.

#### Scenario: Opening a shell
- **WHEN** the user opens a terminal from the default profile
- **THEN** the profile's shell starts attached to a pseudo-terminal, and its prompt appears in a new session

### Requirement: Profiles SHALL come from detected shells, the user's settings and provider suites
The IDE SHALL offer a profile for each shell it detects: PowerShell, Windows PowerShell, Command Prompt, Git Bash
and installed WSL distributions on Windows, and the login shell and the shells listed in `/etc/shells` elsewhere.
It SHALL add the profiles in `terminal-profiles.json` in the user data directory and those a provider suite
contributes, and SHALL let one be the default.

#### Scenario: A profile from the settings file
- **WHEN** `terminal-profiles.json` names a profile with a command and arguments
- **THEN** the New Terminal menu lists it, and choosing it starts that command

### Requirement: The terminal SHALL render the VT sequences a shell and its programs write
The terminal SHALL draw SGR colours in 16, 256 and 24-bit forms, bold, faint, italic, underline, reverse,
strikethrough and invisible text; cursor movement and shape; erasure, insertion and deletion; scroll regions; and
the alternate screen. It SHALL take OSC 0 and 2 as the session's title, and SHALL answer device attributes and
cursor position queries.

#### Scenario: A line editor redrawing its line
- **WHEN** a program writes `CSI 5 G`, then text, then `CSI K`
- **THEN** the text starts at column 5, and the rest of the line after the cursor is cleared

#### Scenario: A full-screen program
- **WHEN** a program switches to the alternate screen, draws, then switches back
- **THEN** the shell's screen and scrollback are as they were before it switched

### Requirement: A program SHALL be able to set the terminal's default colours over the theme's
The terminal SHALL draw with the theme's default foreground and background and its sixteen-colour palette. OSC 10
and OSC 11 SHALL set a session's default foreground and background, which text reset by `CSI 0m` then uses, and
OSC 110 and OSC 111 SHALL restore the theme's.

#### Scenario: A shell that paints its own background
- **WHEN** a program writes `OSC 11;#252626 BEL`, then text, then `CSI 0m`
- **THEN** the session's background is #252626, including after the reset, until `OSC 111` restores the theme's

### Requirement: Each grapheme cluster SHALL take the cells its width gives it
The terminal SHALL place each grapheme cluster in one cell or two: two for wide East Asian characters, for emoji
presentation, and for a character followed by U+FE0F, and none for a combining mark. A fallback font's own advance
SHALL NOT change where later cells fall. Right-to-left text SHALL be drawn in logical order, one cluster per cell.

#### Scenario: An emoji in a status line
- **WHEN** a program writes `✅ READY.`
- **THEN** ✅ takes two cells, and `R` is drawn in the third

### Requirement: Links SHALL open only on a modified click, after their target is shown
The terminal SHALL treat OSC 8 hyperlinks and detected `http` and `https` URLs as links, SHALL show a link's target
when the pointer is over it, and SHALL open it only on Ctrl+click, or Cmd+click on macOS. It SHALL NOT open a link
with any other scheme.

#### Scenario: A help link
- **WHEN** a program writes an OSC 8 link to an `https` address, and the user Ctrl+clicks it
- **THEN** the address opens in the default browser, and a plain click selects text instead

### Requirement: A session SHALL keep scrollback
A session SHALL keep the lines that scroll off its screen, 10,000 by default and as many as a setting gives, and
SHALL let the user scroll back through them. Output arriving while the user is scrolled back SHALL NOT move the
view.

#### Scenario: Reading earlier output
- **WHEN** a command prints more lines than the screen holds, and the user scrolls up
- **THEN** the earlier lines are shown, and the view stays there while further output arrives

### Requirement: The user SHALL be able to select, copy and paste
The terminal SHALL select text by dragging, a word by double-click and a line by triple-click, SHALL extend a
selection with Shift+click, and SHALL copy and paste from the keyboard and a context menu. A paste SHALL be
bracketed when the program has asked for bracketed paste.

#### Scenario: Copying a wrapped line
- **WHEN** the user selects a line the terminal wrapped across two rows, and copies it
- **THEN** the clipboard holds it as one line

### Requirement: Keys SHALL go to the session, except those the IDE keeps
While a session has focus, every key SHALL go to its process, except a reserved set of the IDE's own chords, which
SHALL be the platform's. Ctrl+C SHALL copy when text is selected and interrupt when none is; on Linux, Ctrl+Shift+C
and Ctrl+Shift+V SHALL copy and paste.

#### Scenario: Interrupting a command
- **WHEN** no text is selected and the user presses Ctrl+C in a session running a long command
- **THEN** the process receives an interrupt, and the clipboard is unchanged

### Requirement: Composed and Unicode text SHALL reach the session
The terminal SHALL take input from the platform's input method, showing the candidate window at the cursor and the
uncommitted composition in place, and SHALL send text to the process only once it is committed, as UTF-8.

#### Scenario: Typing Japanese
- **WHEN** the user composes a word with an input method and commits it
- **THEN** the composition is shown at the cursor while it is open, and the committed text alone reaches the process

### Requirement: Mouse and focus reports SHALL be sent only when a program asks for them
The terminal SHALL report mouse buttons, movement and wheel to the process in the encoding the program enabled, and
focus in and out when it enabled focus reporting. With Shift held, the mouse SHALL select text instead.

#### Scenario: A full-screen program that takes the mouse
- **WHEN** a program enables SGR mouse reporting and the user clicks a cell
- **THEN** the program receives the click's position, and no text is selected

### Requirement: Several sessions SHALL be open at once as tool windows
Each session SHALL be a tool window in the IDE's dock, so that sessions show as tabs, can be docked beside each
other, and can float, as every tool window can.

#### Scenario: Two shells side by side
- **WHEN** the user opens a second session and drags its tab beside the first
- **THEN** both are shown at once, each running its own process

### Requirement: A session's process SHALL be told when the terminal is resized
When a session's pane changes size, the terminal SHALL change its grid to the columns and rows that fit, and SHALL
tell the process the new size through the pseudo-terminal.

#### Scenario: Widening the pane
- **WHEN** the user widens a session's pane
- **THEN** the grid gains columns, and a program that asks for the terminal's width gets the new one

### Requirement: Closing a session or the IDE SHALL end the session's process
Closing a session SHALL close its pseudo-terminal, which signals the processes attached to it, and SHALL end the
session's process if it has not exited within two seconds. When the IDE ends, however it ends, every session's
process SHALL end. A profile whose lifetime is `job` SHALL also end everything its process started.

#### Scenario: Closing a running shell
- **WHEN** the user closes a session whose shell is running a command
- **THEN** the shell and the command end, and no process from the session is left running

### Requirement: An exited session SHALL stay readable
When a session's process exits, the session SHALL keep its screen and scrollback, SHALL show the exit code, and
SHALL offer to restart the profile, unless the profile closes its session on a clean exit.

#### Scenario: A command that failed
- **WHEN** a session's process exits with code 1
- **THEN** its output is still there to read and select, and its tab shows that it exited with code 1

### Requirement: A program SHALL NOT write the clipboard or have text echoed back to it
The terminal SHALL ignore OSC 52 clipboard writes, and SHALL NOT answer a query whose reply carries text a program
supplied, such as a title report.

#### Scenario: A program that tries to set the clipboard
- **WHEN** a program writes an OSC 52 sequence
- **THEN** the clipboard is unchanged

### Requirement: A session SHALL be named and readable by assistive technology
A session's control SHALL carry the session's name as its automation name, and its visible text as a read-only
value.

#### Scenario: A screen reader on the terminal
- **WHEN** an accessibility client reads the focused session
- **THEN** it gets the session's name, and the text on its screen
