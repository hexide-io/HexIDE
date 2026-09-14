# Command line

HexIDE runs with no arguments and opens its startup dialog. Everything below is optional, and exists to
skip a step you would otherwise take by hand, or to turn on something that has to be on before the IDE
finishes starting.

```text
HexIDE.Desktop [options] [<project>.vbp]
```

**Both prefixes work.** `--newproject` and `/newproject` are the same flag, and both are matched
case-insensitively. The `/` form is VB6's own convention and is kept for the muscle memory; `--` is what
most people will type today.

## Options

| Option | Argument | What it does |
|---|---|---|
| `--help` | — | Prints usage and exits without starting the IDE. Also `-h`, `/?` and `-?`. |
| `--newproject` | — | Creates a Standard EXE and skips the startup dialog. |
| `--capture-lsp` | — | Arms the [protocol capture](lsp-client.md#reading-the-conversation) for every language-server connection before the first one is made. |
| `--personality` | `vb6`, `vbaode`, `vba` | Selects the IDE personality for the session. |
| `--server-port` | a port number | Starts the automation server on that port. **Debug builds only** — see below. |
| `--developer-mode` | — | Turns on session developer mode. **Debug builds only** — see below. |
| *(positional)* | a path ending `.vbp` | Opens that project instead of showing the startup dialog. |

## The two that behave differently in a distributed build

This asymmetry is deliberate and it is easy to read the wrong way round.

**`--server-port` and `--developer-mode` are inert in a release build.** The automation server is compiled
out entirely, and developer mode is hard-disabled, so a distributed binary opens no port and cannot be put
into developer mode however it is launched. Neither flag errors; it simply does nothing.

**`--capture-lsp` is not.** The protocol capture ships in release builds, because the people it is for are
writing a language server against a HexIDE they downloaded. A flag that was inert exactly there would put
the feature out of reach of its audience.

## Details worth knowing

**`--newproject` wins.** If you pass both `--newproject` and a project path, the new project is created and
the path is ignored.

**`--server-port` needs its value as the next argument**, and that value must parse as a number. `--server-port 5123`
works; `--server-port=5123` does not.

**`--personality` takes one of three names**, matched case-insensitively: `vb6`, `vbaode`, `vba`.

**The positional path must end in `.vbp`.** A `.vbg` project group cannot currently be opened from the
command line even though the IDE opens groups perfectly well from **File → Open Project** — the argument
is matched on the `.vbp` extension alone. Tracked as a gap rather than a decision.

**Nothing is rejected.** An argument HexIDE does not recognise — a misspelled flag, a value in the wrong
place, a `.vbg` path, an unknown personality — is skipped in silence and the IDE starts normally. So a flag
that appears to have done nothing has usually not been read at all. Worth checking the spelling before
looking for a deeper cause, and `--help` will tell you how a flag is spelled.

**`--help` is answered before anything starts.** No window, no project, no port — it prints and exits. On
Windows it writes to the console that launched it: HexIDE is a GUI program and owns no console of its own,
so run from a shortcut or Explorer there is nowhere for the text to go and nothing appears.

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
HexIDE.Desktop --newproject

# Record every language-server conversation from its first handshake
HexIDE.Desktop --capture-lsp C:\src\Battleship\Battleship.vbp

# The VBA personality
HexIDE.Desktop --personality vba

# Debug builds: drive the IDE from an automation client on port 5123
HexIDE.Desktop --server-port 5123 --newproject

# What all of this says, from the program itself
HexIDE.Desktop --help
```
