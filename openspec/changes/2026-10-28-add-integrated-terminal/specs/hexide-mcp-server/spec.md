## ADDED Requirements

### Requirement: read_terminal SHALL read a terminal's text, attributes and state
`read_terminal` SHALL return every terminal session with its state, and for the session asked for, its size,
cursor, title, default colours and lines: the screen by default, or the scrollback asked for. Each line SHALL give
its text, and unless the caller asks for text only, runs of columns with their colours and attributes. A reply
whose lines hold a wide character SHALL say that columns and text positions differ after one.

#### Scenario: Checking a program's colours
- **WHEN** a session shows a word written in 24-bit blue and faint, and `read_terminal` reads it
- **THEN** the word's run gives its columns, the blue as a colour, and faint

### Requirement: read_terminal SHALL wait for text and explain an empty or ended session
Given text or a regular expression to wait for, `read_terminal` SHALL reply once it appears or its timeout passes,
saying which, with the screen as it stood. With no session open, it SHALL name how to open one and the profiles
available; for a session whose process has ended, it SHALL say so and give the exit code.

#### Scenario: Waiting for a prompt
- **WHEN** `read_terminal` waits for `READY.` and the program prints it after a second
- **THEN** the reply arrives then, says it found the text, and gives where

#### Scenario: Nothing open
- **WHEN** `read_terminal` is called and no session is open
- **THEN** the reply says none is open, how to open one, and which profiles exist
