# An integrated terminal

## Why

HexIDE has nowhere for a text-mode program to draw. Three needs meet here:

- **Any shell.** A developer expects an IDE to run pwsh, bash, zsh or cmd beside the code, with scrollback,
  selection, links, IME input and several terminals at once. Jen asked for "a kickass terminal" (2026-10-28).
- **An external backend's interactive shell.** #2482 measured what one writes: 24-bit SGR foregrounds, faint,
  reverse and underline, default colours set by OSC 10/11 and restored by OSC 110/111, OSC 8 links, a line
  editor's cursor movement and erasure, and colour emoji two cells wide. Jen wants it rendered in full fidelity.
- **VB6 programs that use the Console library** (#1106, #1108) need somewhere to draw under F5.

#1109 began as a sixteen-colour cell grid for the third. A grid that only the third could use would be rebuilt
for the other two, so this change designs one terminal, a real VT emulator hosting processes through the
platform's pseudo-terminal, of which the backend's shell and a VB6 console run are two clients.

The supervisor asked for this design before any code. This change records the contracts and the design; it
adds no executable behaviour.

## What Changes

- Introduce the `integrated-terminal` capability: terminal sessions started from profiles, hosted through a
  pseudo-terminal, drawn from a VT engine's screen model, with scrollback, selection and copy and paste, links,
  IME and Unicode input, keyboard routing, theme colours, several sessions as tabs, resizing and teardown.
- Add a `read_terminal` tool to the automation server, so a run can be checked as text and attributes without a
  screenshot. Typing into a terminal needs no tool: `type_text` and `press_key` reach any focusable control (#2460).
- Cut a first delivery (v1) from the full list. The rest is recorded in `design.md` under "Later", not as
  requirements, so the specs describe only what v1 will do.

## Capabilities

### New Capabilities

- `integrated-terminal`: terminal sessions in the IDE, from profiles, through a pseudo-terminal.

### Modified Capabilities

- `hexide-mcp-server`: `read_terminal`, which reads a terminal's screen and scrollback as text and attributes.

## Impact

This is a design-only change. **No package is added or pinned here.** `design.md` records the two packages it
recommends, a VT engine and a pseudo-terminal host, each with its licence as the NuGet registration API gave it,
and the decision is Jen's before either is referenced. Recording a package in `scripts/package-licences.tsv` and
pinning it in `Directory.Packages.props` are the first task of the first phase.

Implementation would touch a new tool window and its view model, process hosting in `HexIDE.Core`, the theme
packs (a terminal palette), settings (terminal profiles), keyboard routing, localisation (every shipped pack),
the Windows installer (a console host the pseudo-terminal needs), and the automation server. No interpreter
semantics, source files or project formats change. The VB6 Console library (#1108) is a separate change that
plugs into the session seam this one defines.

Implementation and platform verification remain unchecked in numbered phases in `tasks.md`. Keep this change
unarchived until they are complete. `MISSING_FEATURES.md` is unchanged: a contract does not implement a terminal.
