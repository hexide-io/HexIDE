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

**Nothing is rejected.** There is no `--help`, and an argument HexIDE does not recognise — a misspelled
flag, a value in the wrong place, a `.vbg` path, an unknown personality — is skipped in silence and the IDE
starts normally. So a flag that appears to have done nothing has usually not been read at all. Worth
checking the spelling before looking for a deeper cause.

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
```
