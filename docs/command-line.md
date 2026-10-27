# Command line

HexIDE runs with no arguments and opens its startup dialog. Desktop startup runs when the main window
opens, even on an X display without a window manager to activate it (#1964). Everything below is optional,
and exists to skip a step you would otherwise take by hand, or to turn on something that has to be on before
the IDE finishes starting.

```text
HexIDE.Desktop [options] [<project>.vbp | <group>.vbg | <project>.twinproj]
```

**Both prefixes work.** `--newproject` and `/newproject` are the same flag, and both are matched
case-insensitively. The `/` form is VB6's own convention and is kept for the muscle memory; `--` is what
most people will type today.

## Options

| Option | Argument | What it does |
|---|---|---|
| `--help` | — | Prints usage and exits without starting the IDE. Also `-h`, `/?` and `-?`. |
| `--newproject` | — | Creates a Standard EXE, saves it in a unique temporary directory and skips the startup dialog. |
| `--newproject-unsaved` | — | Creates a Standard EXE without writing project or component files and skips the startup dialog. Save Project asks where to put them. |
| `--capture-lsp` | — | Arms the [protocol capture](lsp-client.md#reading-the-conversation) for every language-server connection before the first one is made. |
| `--user-data-dir` | a directory | Keeps this session's settings and other per-user files there instead of the usual place. See below. |
| `--personality` | `vb6`, `vbaode`, `vba` | Selects the IDE personality for the session. |
| `--server-port` | a port number | Starts the automation server on that port. **Debug builds only** — see below. |
| `--session` | a name | Serves automation only to the agent session of that name. **Debug builds only** — see below. |
| `--developer-mode` | — | Turns on session developer mode. **Debug builds only** — see below. |
| `--ansi-code-page` | a Windows code page number, such as `932` | Reads and writes VB6 sources as a machine whose ANSI code page is that one would. **Debug builds only** — see below. |
| *(positional)* | a path ending `.vbp`, `.vbg` or `.twinproj` | Opens that project or project group instead of showing the startup dialog; a `.twinproj` opens read-only. Relative to where you ran HexIDE from. |

## The ones that behave differently in a distributed build

This asymmetry is deliberate and it is easy to read the wrong way round.

**`--server-port`, `--session`, `--developer-mode` and `--ansi-code-page` are inert in a release build.** The automation
server is compiled out entirely, and developer mode is hard-disabled, so a distributed binary opens no port
and cannot be put into developer mode however it is launched. Nor can it be told to read files as another
machine would: what a release reads a file as is what the machine it runs on reads it as. None of the four
errors; each simply does nothing.

**`--capture-lsp` is not.** The protocol capture ships in release builds, because the people it is for are
writing a language server against a HexIDE they downloaded. A flag that was inert exactly there would put
the feature out of reach of its audience.

## Details worth knowing

**`--ansi-code-page` shows a file as another machine would read it.** VB6 source carries no declaration of
its encoding, so the same bytes read differently on a Japanese and a Western Windows. A file the machine's
code page cannot read is opened byte for byte instead, with a notice. On a Western machine every byte
decodes, so that path cannot otherwise be reached there. `--ansi-code-page 932` makes it reachable without
changing the machine's own settings. A value that is not a code page this platform has a table for stops
HexIDE starting, with exit code 2, rather than run as this machine and look as though it had worked. That
includes 65001: what VB6 does on a Windows set to UTF-8 is not settled.

**`--user-data-dir` keeps a session away from your real settings.** Everything HexIDE stores per user
lives in one directory — `settings.json`, the window layout, recent projects, [`lsp-servers.json`](language-servers.md),
add-in consent and revocations, your own translations. Point this flag at another directory and all of it is
read from and written to there instead, for that session only. Use it to set up a demo, an automation run or
an experiment without touching the configuration you work in:

```sh
HexIDE.Desktop --user-data-dir ./demo-profile
```

- **The directory is created on first use**, so a new one starts from HexIDE's defaults exactly as a first
  install would. That is also what a typo gets you: an empty profile, not an error.
- **A relative path is relative to where you ran HexIDE from**, as it is for any other program.
- **A program's `SaveSetting` settings move with it on Linux and macOS, but not on Windows.** There they
  are kept in the registry key VB6 itself uses, so a program run in HexIDE and the same program compiled by
  VB6 share them, and this flag does not move them. Elsewhere they are in `program-settings.json` in this
  directory.
- **Logs are not per-user files** and stay in their usual place. IDE log pruning keeps at least the seven
  most recent sessions and deletes an older session only when all its parts were last modified more than
  seven days ago. Frequent launches across profiles do not shorten that window.
- **Add-ins still load.** They live beside the executable, not in this directory; only the record of which
  ones you allowed moves, so a fresh profile asks about each third-party add-in again.
- **A malformed one refuses to start, as a malformed `--server-port` does.** Given with nothing after it —
  or with another flag where the directory should be — HexIDE prints why and exits with status 2. Most
  arguments are skipped when HexIDE cannot use them, but starting normally here would put the session on
  exactly the settings you asked it to stay away from.

**New-project flags win.** `--newproject-unsaved` takes precedence over `--newproject`, regardless of their
order. With either flag and a project path, the new project is created and the path is ignored.

**`--server-port` needs its value as the next argument**, and that value must be a port number from 1 to
65535. `--server-port 5123` works; `--server-port=5123` does not. In a Debug build, anything else stops
HexIDE before it opens a window, rather than letting it start without the server you asked for. A
distributed build has no automation server, so there it ignores the flag and its value alike.

**A port something else already holds stops HexIDE too.** Usually the something else is another HexIDE
started with the same port. HexIDE used to open anyway, with no server behind it, and a client went on
talking to the other instance with nothing to say it was the wrong one. Now it says the port is in use and
exits with code 3; the window may flash first, because the server starts once the window is open. To tell
which process a client is actually reaching, `/health` reports the answering process's `pid`.

**`--session` ties the automation server to one agent session.** Several agent sessions on one machine each run
their own IDE on their own port, and a session that reaches another's port, or a stale IDE left on its own, would
otherwise drive that IDE with nothing in any reply to say so. Given `--session <name>`, the server serves a tool call
only when the client names that session in its `X-HexIDE-Session` header, which the repository's `.mcp.json` sends
from `HEXIDE_SESSION`. Any other call is answered with an error saying whose IDE this is and what to check, and is not
run. Listing the tools still works, and so does `/health`, which reports the `session` beside the `pid`. Without the
flag, a client that names no session is served, as before, and one that names a session is refused: it was meant for an
IDE started for it. Two clients that both name none cannot be told apart, so this does not separate those.
A session's name is matched as plain ASCII: Claude Code sends the header in Latin-1, and the server refuses a header
byte outside ASCII as a malformed request, which the client counts towards dropping its tools. Like `--user-data-dir`, it refuses to start with exit code 2 when given
nothing after it, because starting would serve every session's client, which is what it was given to prevent. It is a
guard against mistakes between cooperating sessions, not access control: the header is not a secret. The
`scripts/dev-ide.ps1` launcher passes it from `HEXIDE_SESSION`; `CLAUDE.md` describes the setup under *Parallel
sessions*.

**`--personality` takes one of three names**, matched case-insensitively: `vb6`, `vbaode`, `vba`.

**Paths are relative to where you ran HexIDE from.** HexIDE moves its working directory to its own folder as
it starts, and used to resolve the project path *after* that move — so `HexIDE.Desktop ../demo/x.vbp` looked
beside the executable, found nothing, and opened nothing without saying so.

**The positional path must end in `.vbp`, `.vbg` or `.twinproj`**, matched case-insensitively. A `.vbg` opens
the whole project group, as **File → Open Project** does, and a `.twinproj` opens read-only, for browsing its
sources (#1189). Any other path is not taken as a project.

**Almost nothing is rejected.** An argument HexIDE does not recognise — a misspelled flag, a value in the wrong
place, a path that isn't a project, an unknown personality — is skipped in silence and the IDE starts normally. The
exceptions are the flags whose whole purpose would be defeated by carrying on without them: a
`--user-data-dir` with no directory, a `--session` with no name, a `--server-port` with no usable port, and an
`--ansi-code-page` HexIDE has no table for. So a flag
that appears to have done nothing has usually not been read at all. Worth checking the spelling before
looking for a deeper cause, and `--help` will tell you how a flag is spelled.

**`--help` is answered before anything starts.** No window, no project, no port — it prints and exits. On
Windows it writes to the console that launched it: HexIDE is a GUI program and owns no console of its own,
so run from a shortcut or Explorer there is nowhere for the text to go and nothing appears.
An inherited file or pipe is used directly, before trying to attach to a console. In particular, a
Windows batch file can capture help and refusals with `HexIDE.Desktop.exe --help > out.txt 2>&1`,
even when the batch file's cmd has no console window.

**The mark at the top is in colour where the terminal can show it.** That means output going to a
terminal rather than a file or pipe, `NO_COLOR` unset, `TERM` not set to `dumb`, and a terminal that takes
24-bit colour: any Windows console since Windows 10 1703, Windows Terminal (including one hosting a WSL
shell), and on other platforms a terminal declaring `COLORTERM=truecolor` or `COLORTERM=24bit`. Anywhere
else, `--help > file` included, the same mark is drawn in plain ASCII, so the captured text reads the same
in an editor.

The mark sits beside the text in a window at least 120 columns wide — the default for both Windows
consoles and Windows Terminal — and above it in anything narrower. Stacking costs a couple of rows, and
below about 94 columns the option lines wrap as well; the mark moves first because a wrapped row pushes the
next mark row down, which arrives as the mark sliced into bands with text between them.

## Exit codes

| Code | Meaning |
|---|---|
| 0 | HexIDE ran and closed normally, or `--help` answered. |
| 2 | The command line was refused before anything started: `--user-data-dir` without a directory, `--session` without a name, `--server-port` without a port from 1 to 65535, or `--ansi-code-page` without a code page this platform can read (the last three in Debug builds only). |
| 3 | The automation server's port was already in use. Debug builds only. |
| 4 | The automation server could not start for any other reason; the message gives the cause. Debug builds only. |

Each non-zero exit also prints why, to the console HexIDE was started from, the same way `--help` does.

## Keeping this page true

The options above are declared once, in `ServerOptions.Options`, and both the parser and `--help` read
that list — so the program cannot accept a flag it does not print. This page is the part a list cannot
enforce, so `CommandLineDocumentationTests` fails the build when a flag is missing from the table above,
and when the table names a flag the parser does not accept.

Adding an option is therefore three things in one place and one row here — plus a summary short enough
that the rendered row stays inside 94 columns, so the mark still fits beside it in a 120-column window.
`CommandLineDocumentationTests` measures that too, and names the offending option when it fails.

## Examples

```sh
# Open a project
HexIDE.Desktop C:\src\Battleship\Battleship.vbp

# Straight into an empty Standard EXE
HexIDE.Desktop --newproject-unsaved

# Record every language-server conversation from its first handshake
HexIDE.Desktop --capture-lsp C:\src\Battleship\Battleship.vbp

# The VBA personality
HexIDE.Desktop --personality vba

# Debug builds: drive the IDE from an automation client on port 5123
HexIDE.Desktop --server-port 5123 --newproject

# What all of this says, from the program itself
HexIDE.Desktop --help
```
