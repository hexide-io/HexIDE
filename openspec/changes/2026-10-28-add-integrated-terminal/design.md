# Integrated terminal design (#1109)

#1109 began as a sixteen-colour grid for VB6 Console programs (#1108). The supervisor widened it twice on
2026-10-28: first to render an external backend's interactive shell in full fidelity, using the facts #2482
measured, then, from Jen ("we really need a kickass terminal"), to a general integrated terminal that runs any
shell, with the backend's shell and VB6 Console programs as two of its clients. This note designs that terminal
and cuts a first delivery from it. Nothing here is implemented, and no package is added. The decisions that are
Jen's are collected in [Decisions](#decisions-for-jen), each with a recommendation.

## Recommended path

- **Adopt a VT engine; don't write one.** XTerm.NET (MIT, no dependencies) already parses everything #2482
  measured, keeps scrollback, reflows, searches, selects and measures Unicode 17 widths by grapheme cluster. HexIDE
  wraps it behind its own interface and draws the cells itself in Avalonia.
- **Host the process through the platform's pseudo-terminal.** On Windows, use HexIDE's own small host over the
  ConPTY redistributable (Microsoft, MIT): it ships the console host whose output passes through unmodified, and
  owning the host lets a profile start its process suspended and put it in a job. On Linux and macOS, use Porta.Pty
  (MIT), whose native shim forks in C rather than from managed code.
- **One session model, two kinds of feed.** A pseudo-terminal session reads a child process. An in-process session
  is fed by HexIDE itself; that is how a VB6 program run under F5 draws (#1108). Both drive the same engine, so the
  two clients get the same rendering, selection, scrollback and automation.
- **Each session is a dock tool,** so tabs, splits and floating come from the dock framework rather than a second
  tab system inside one pane.
- **Read it from automation with one new tool,** `read_terminal`. Typing needs none.

## The full list, and the v1 cut

The supervisor asked for the whole list, then a v1 cut from it. The cut is a decision for Jen (D2).

| Area | v1 | Later |
|---|---|---|
| VT rendering | SGR 16, 256 and 24-bit colours, bold, faint, italic, underline, reverse, strikethrough, invisible; cursor movement and shape; erase; insert and delete; scroll regions; the alternate screen; OSC 0/2 title, OSC 8, OSC 10/11/110/111; replies to DA and DSR; bracketed paste and focus reporting | Sixel and Kitty graphics, which the engine decodes but HexIDE would have to draw; OSC 52 clipboard writes (off: see Security); desktop notifications |
| Width and text | Grapheme clusters, two-cell emoji including VS16 sequences, combining marks, wide East Asian characters | Bidirectional text (see Text) |
| Hosting | ConPTY on Windows 10 1809 and later, a pty on Linux and macOS; resize | Remote hosts (SSH, WSL as a target rather than a shell profile) |
| Profiles | Detected shells, user profiles in a settings file, a default profile, a profile per provider suite (#1208) | An Options page for profiles; per-profile fonts and colour schemes |
| Sessions | Several sessions as dock tool tabs; dragging one beside another splits them, as any dock tool does | Explicit split commands and keys; sessions restored when the IDE restarts |
| Scrollback | 10,000 lines by default, a setting | Unlimited, spilled to disk |
| Selection | Drag, double-click a word, triple-click a line, Shift+click to extend; copy and paste by keyboard and context menu | Block (rectangular) selection; copy as HTML with colours |
| Search | | Find in the terminal: next, previous, case, regular expressions, all matches highlighted |
| Links | OSC 8 links and detected `http`/`https` URLs, opened with Ctrl+click (Cmd+click on macOS) after the target is shown | `file:line` links opening the editor; links to other schemes after a confirmation |
| Input | Keys, IME composition at the cursor, Unicode text, mouse reporting for programs that ask, paste | A warning before pasting several lines |
| Shell integration | | OSC 133 prompt marks (jump between commands), OSC 7 working directory |
| Automation | `read_terminal`, with a wait for text; typing through `type_text` and `press_key` | Reading Sixel and Kitty images |
| Accessibility | An automation name and the visible text as a value | A text pattern for screen readers, which Avalonia 12.0.4 does not offer (see Accessibility) |
| Clients | Any shell; an external backend's shell as a profile | The VB6 Console library, which plugs into the in-process session (#1108's own change) |

The v1 line keeps everything #2482 needs, everything a general terminal is judged on in its first minute (a
shell that works, colour, scrollback, selection, links, IME, tabs), and the automation readout, because a surface
nothing can verify is not done here. Search, `file:line` links, shell integration and an Options page are the
first things after it. Each costs a UI and its strings in all 29 shipped packs, and none blocks a shell.

## The VT engine

### Write one, or adopt one

A terminal good enough for vim, htop and a line editor's redraws is a large surface: the escape-sequence state
machine, the screen model, scrollback, reflow when the width changes, Unicode width tables kept current, selection
across wrapped lines, and a long tail of modes. Writing it is months, and the tail is where a home-grown parser
loses to one that many terminals have already exercised. Adopting one is right, provided the licence is clean,
the parser can be separated from any UI, and it can be replaced.

### Candidates

Licences are the registration API's `licenseExpression` at the version named. Activity is from GitHub, read on
2026-10-28.

| Candidate | Licence (NuGet at version) | Latest | Last commit | TFM, dependencies | Findings |
|---|---|---|---|---|---|
| **XTerm.NET** (tomlm/XTerm.NET) | `MIT` at 3.0.0 | 3.0.0, 2026-10-14 | 2026-10-26 | net10.0; none | SGR 2/22, 4/24, 7/27, 38;2 and 48;2; OSC 8, 10, 11, 110 and 111 (`Common/OscCommand.cs`); widths from python wcwidth 0.8.2 at Unicode 17.0.0 (`Common/WidthTables.Generated.cs`), replayed codepoint by codepoint in its tests; grapheme clusters per cell and mode 2027; emoji plus VS16 is two cells; scrollback, reflow (`Buffer/BufferReflow.cs`), search (`Search/BufferSearch.cs`), selection, the alternate screen, mouse and Kitty keyboard encodings. Headless: a renderer reads `Buffer.Lines[y][x]`. |
| RoyalApps.RoyalTerminal.Terminal.Vt.Managed | `MIT` at 0.6.3 | 0.6.3, 2026-10-10 | 2026-10-27 | net10.0; its sibling packages and `System.Security.Cryptography.ProtectedData` | Faint, OSC 8, OSC 10–12 and 110–112; Unicode 18 width by grapheme cluster with VS15/VS16; scrollback, reflow, search. Pre-1.0; its contracts package carries SSH and DPAPI helpers HexIDE does not need. |
| libghostty-vt (Ghostty's core, C ABI) | GitHub MIT; on NuGet only through third-party bindings | no release of the C library | 2026-10-28 | native, built with Zig 0.17 | The most complete engine, but its header says the API "is not yet stable and is definitely going to change", upstream publishes no binaries, and the .NET bindings either miss three of six runtime identifiers or carry their own patches. |
| libvterm | GitHub MIT | 0.3.3, 2023 | Launchpad branch; Neovim's fork is archived | native, libtool | From the survey, not re-checked here: no faint; OSC 8 and 10/11 reach only a fallback callback; widths from Unicode 5 and a wide table to about Unicode 12; VS16 does not widen. |
| alacritty_terminal, termwiz | Apache-2.0 (crates.io), MIT | | | Rust, no C ABI | From the survey: we would write and build the C ABI ourselves; alacritty gives U+FE0F width 0. |
| XtermSharp, VtNetCore | `''`, licenseUrl only | 2020, 2022 | | | Unresolvable licences, unmaintained. Refused. |

Five ready-made Avalonia terminal controls were found. Four need Avalonia 12.1.1 or later, which HexIDE is not on
(#868 tracks the move): Devolutions.Terminal.Control (`[12.1.1, )`), Terminal.Avalonia (`[12.1.2, )`),
RoyalApps.RoyalTerminal.Avalonia (`[12.1.3, )`) and SvcSystems.UI.Terminal (`[12.1.4, )`). The fifth,
Iciclecreek.Avalonia.Terminal 4.1.1, accepts 12.0.4 and is built from
XTerm.NET and Porta.Pty, but depends on Unicode.net 2.0.0, whose `licenseExpression` is `''` (a licence file
only). An unresolved licence is refused as firmly as a copyleft one, so it is out, and a control would in any case
own the rendering, input and automation that HexIDE needs to own. This design needs no Avalonia upgrade.

### Recommendation: XTerm.NET, behind HexIDE's own interface

XTerm.NET is the only .NET engine where every v1 capability is confirmed in source, with no dependencies, at a
1.0-plus version. Its risks are real and are designed around:

- **One maintainer, and three major versions in ten months** (1.0.0 on 2025-12-23, 3.0.0 on 2026-10-14). The
  package is pinned to an exact version. HexIDE's renderer, input and automation talk to an `ITerminalEngine`
  interface of HexIDE's own, and never to the package's types, so an upgrade or a replacement is one adapter.
- **The same maintainer publishes Porta.Pty.** One person is the bus factor for both seams. The adapters are what
  make that tolerable: either can be replaced alone.
- **Conformance tests are written against the interface,** not the package: a stream in, cells and attributes
  out. They pin what HexIDE depends on, so an engine upgrade that changes behaviour fails a test rather than a
  user. The streams are **synthetic**, written to the shapes #2482 measured. They are never captured output of the
  backend's shell: #2482 records facts, and none of that program's text.
- **Plan B** is RoyalTerminal's managed engine (MIT, Unicode 18, actively developed by a commercial vendor). It
  would replace the adapter, not the renderer.

## Hosting the process

### Windows: HexIDE's own host over the ConPTY redistributable

`Microsoft.Windows.Console.ConPTY` (`MIT` at 1.25.260930003, 2026-10-02, published by Microsoft, no dependencies)
ships `conpty.dll` for x86, x64 and arm64, the matching `OpenConsole.exe`, and a `conpty.h` that adds
`PSEUDOCONSOLE_GLYPH_WIDTH_GRAPHEMES`, `_WCSWIDTH` and `_CONSOLE`. Using it, rather than the console host built
into Windows, matters for two reasons:

- **Passthrough.** The bundled console host passes a program's output through unmodified (microsoft/terminal
  #17510, merged 2024-08-01). The inbox host on older builds re-renders it, and a re-render is where OSC sequences
  can be lost. Whether the inbox host on this machine's build (26100) keeps OSC 8 and OSC 10/11 is unmeasured;
  it is the first spike, because it decides whether falling back to the inbox host is honest.
- **Width agreement.** The console host keeps a buffer and decides how many cells a character takes. If it and
  the engine disagree, every cursor movement after an emoji lands in the wrong column, which is #2482's case
  exactly. The glyph-width flag that matches XTerm.NET's Unicode 17 wcwidth tables is to be measured, not assumed:
  the same stream of emoji, VS16 sequences and wide characters through each flag, compared cell by cell.

HexIDE writes its own host (`CreatePseudoConsole`, `ResizePseudoConsole`, `ClosePseudoConsole` and `CreateProcessW`
with `PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE`) rather than using Porta.Pty's Windows path, for three reasons: it
chooses the glyph-width flag, it can start a process suspended (see Lifetime), and it is a few hundred lines over
Microsoft's own binaries. The installer gains `conpty.dll` and `OpenConsole.exe` for each architecture it ships.

Two duties belong to the host. Output is drained continuously, including while `ClosePseudoConsole` runs, which
otherwise can block on a full pipe. And the host answers what the console host asks at start, such as a device
attributes query, which the engine's reply mechanism covers.

### Linux and macOS: Porta.Pty

Porta.Pty (`MIT` at 2.2.3, 2026-10-01; depends on `Microsoft.Windows.Console.ConPTY [1.24.260710001, )`) starts the
child through a small C shim, `libporta_pty`, that calls `forkpty` and `execvp` in native code. Forking from managed
code is unsafe on current .NET, and the shim avoids it. It ships the shim for linux-x64, linux-arm64, osx-x64 and
osx-arm64. The shim is built from `src/Porta.Pty.Native/porta_pty.c` in the project's public GitHub Actions
workflows (Linux in a glibc 2.28 container, both macOS architectures on a macOS runner), and the publish workflow
packs those builds. That provenance is auditable from the logs, but not reproducible from HexIDE's side.

The alternative is HexIDE's own `posix_spawn` with `POSIX_SPAWN_SETSID` and the pty's slave opened as the child's
standard handles, which needs no native code. On Linux, a session leader that opens a terminal acquires it as its
controlling terminal. On macOS that is unverified, and the BSDs need an explicit `TIOCSCTTY`, which `posix_spawn`
cannot issue. So Porta.Pty is recommended now, behind an `IPseudoTerminal` interface, with the spawn path as a
spike that could retire the native shim later.

### The child's environment

`TERM=xterm-256color` and `COLORTERM=truecolor`, so programs choose 24-bit colour as they did in #2482's ConPTY
run, and `TERM_PROGRAM` naming the IDE. A white-label build sets that from its brand, not a literal (#2042). The
profile's own environment is applied over these.

## Lifetime

**Each session ends its process when it closes, and every session ends when the IDE ends, however it ends.**

- **Closing a session** closes the pseudo-terminal. On Windows, the console host then signals every process attached
  to it to close. On Linux and macOS, closing the master sends SIGHUP to the session's process group. After a grace
  period, two seconds, the host ends the direct child and its process group.
- **When the IDE ends,** the operating system closes the same handles, with the same effect, even after a crash.
- **An exited session stays readable.** Its last screen and scrollback remain, its tab shows the exit code, and it
  offers to restart. A shell profile can choose to close its tab on a clean exit instead.

**The job object, and a challenge to the brief.** The brief asks for the existing teardown helper,
`ChildProcessJob` (#406). That helper's job is created with kill-on-close, so everything a child starts ends with
it. That is right for a language server, and wrong as the default for a shell: a person who runs `start notepad`
or `code .` from a terminal expects that program to outlive the tab, as it does in Windows Terminal, where closing a
tab signals only the processes attached to the console. So:

- **Shell profiles** don't use a job. They rely on the hangup and close signals above, which end the shell and
  whatever is still attached to its terminal, and leave alone what the user deliberately detached.
- **Tool profiles,** such as an external backend's shell that starts its own language server, set `"lifetime":
  "job"`. On Windows the host starts the process suspended, assigns it to a `ChildProcessJob`, then resumes it. That
  needs a new `Assign(SafeProcessHandle)` overload, because the existing `Assign(Process)` can only run after start,
  a gap the class's own documentation records. Starting suspended closes that gap. On Linux and macOS, where the
  helper has no job, the process group is the unit.

This is decision D3.

## Rendering in Avalonia

**A control of HexIDE's own** draws the visible rows from the engine's buffer in `Render`, with no templated text
controls per cell.

- **Cell metrics.** The cell's width is the font's advance for `0` and its height the font's line height, each
  rounded up to whole device pixels at the window's render scaling, then converted back to device-independent
  units. Rows and columns then land on device pixels at 125%, 150% and 175% alike. Measuring in device-independent
  units alone leaves sub-pixel seams that only show above 100%.
- **Glyphs keep to the grid.** Each run of cells with one style is drawn as a glyph run whose advances are set to
  the cell width, never the font's own. A fallback font with different advances, or a proportional fallback for a
  rare script, then cannot push later columns sideways.
- **Emoji and fallback.** A cluster the primary font lacks is matched through `FontManager.TryMatchCharacter`
  (Segoe UI Emoji, Apple Color Emoji or Noto Color Emoji, as the platform has), shaped as a whole cluster, and drawn
  centred in its one or two cells and clipped to them. The engine decides the width. The font never does, so a
  font that draws ✅ narrower or wider than two cells cannot move the cursor.
- **Fonts.** The default is the platform's monospace font (Cascadia Mono, Menlo, or the first of DejaVu Sans Mono
  and Noto Sans Mono that is installed), with a setting. No font is bundled: the common terminal fonts are under the
  SIL Open Font Licence, which is outside the permitted set.
- **Box drawing** is drawn from font glyphs in v1. Drawing it as geometry, so that lines join across cells at any
  scale, is later.
- **Colours.** The sixteen ANSI colours, the default foreground and background, the cursor and the selection are
  new keys in `Themes/Classic.axaml`, mapped by `ThemePack` like every other chrome colour, so a theme pack sets
  its own and no colour is a literal. OSC 10/11 sets a session's defaults over the theme's, and OSC 110/111 restores
  them. That is the layer #2482's shell uses to paint its background without background SGR.
- **The DPI traps CLAUDE.md lists.** Rows are drawn as glyph runs every frame, not cached as bitmaps, so
  `DrawImage`'s device-independent source rectangle and `RenderTargetBitmap`'s scaling never enter. If profiling
  later asks for row caching, the cache uses explicit pixel source rectangles (`SnapshotComposer` is the worked
  example). Each change is checked at 150%, where those traps show and the headless tests cannot see them.
- **Throughput.** The engine parses on a reading thread. The control draws on the next frame, at most once per
  frame however much output arrived, from a snapshot of the dirty rows. When parsing falls behind, the host stops
  reading the pseudo-terminal, so the child blocks rather than the IDE growing a queue. A `cat` of a large file
  must not freeze the IDE; a test pins that with a stream of tens of megabytes.

## Input

- **Keys** are encoded by the engine (xterm encodings, and the Kitty protocol for programs that ask) and written
  to the pseudo-terminal.
- **Keys the IDE keeps.** While a terminal has focus, every key goes to it except a short reserved set, so F5 still
  runs, and the IDE's own commands for focus and panes still work. The set is per platform: Cmd chords on macOS,
  never a hard-coded Ctrl. Copy is Ctrl+C when text is selected and an interrupt when not, as in Windows Terminal,
  with Ctrl+Shift+C and Ctrl+Shift+V on Linux, where Ctrl+C must stay an interrupt.
- **Paste** goes through bracketed paste when the program has asked for it. A warning before pasting several lines
  is later.
- **IME and Unicode.** The control answers Avalonia's `TextInputMethodClientRequested` with a
  `TextInputMethodClient` (present in 12.0.4, with `SetPreeditText`). The candidate window then sits at the cursor,
  and the uncommitted composition is drawn in place, underlined, without being sent until it is committed. Committed
  text is sent as UTF-8, and so is any text that arrives without a key.
- **Mouse** reporting is sent when a program enables it (1000, 1002, 1003 and 1006), with Shift held to select
  instead, as terminals do. Focus in and out (1004) is reported when asked.

## Text

- **Width** is the engine's, by grapheme cluster: wide East Asian characters and emoji presentation take two
  cells, VS16 makes a text-presentation emoji two cells, and combining marks take none.
- **Right-to-left text** is drawn in logical order, one cluster per cell, without bidirectional reordering. That is
  what most terminals do, because reordering inside a grid breaks every program that positions its cursor. #1109
  asked how right-to-left text sits in the grid. This is the answer, and is recorded as a deliberate divergence from
  a text editor.

## Sessions, tabs, splits and profiles

- **Each session is a dock tool** in a Terminal tool dock at the bottom of the IDE, opened from View → Terminal and
  a New Terminal menu listing the profiles. Tabs, dragging one beside another to split, and floating are the dock
  framework's, as for every tool window.
- **Profiles** have a name, a command, arguments, a working directory, an environment, a lifetime (`session` or
  `job`) and, later, a font and colour scheme. HexIDE detects the shells present: PowerShell, Windows PowerShell,
  Command Prompt, Git Bash and installed WSL distributions on Windows, and the login shell and `/etc/shells` on
  Linux and macOS. User profiles live in `terminal-profiles.json` in the user data directory, beside
  `lsp-servers.json`, so `--user-data-dir` keeps a session's own. One profile is the default.
- **A provider suite contributes profiles** (#1208). An external backend's interactive shell reaches the IDE that
  way, so nothing in HexIDE names a backend (#2042): it is a profile like any other, with `"lifetime": "job"`.

## The clients

- **Shells** need nothing beyond the above.
- **An external backend's shell (#2482).** Everything it writes is in v1's rendering list: 24-bit SGR runs ended by
  `CSI 0m`; faint, reverse and underline; OSC 10/11 at start and 110/111 at exit; OSC 8 on help links; `CSI n G`,
  `CSI n A`, `CSI n B`, `CSI K` and `CSI 2K` from its line editor; ▽, ▲ and 💡 at two cells for the emoji, and
  `✅ READY.` drawn as written. Its folds and lightbulbs stay drawn, not interactive, as they are in its own terminal
  (#2482, point 3), and the data behind them is LSP that HexIDE already speaks.
- **VB6 programs that use the Console library (#1108)** draw into an **in-process session**. HexIDE writes VT for
  each Console call (colours as SGR 30–37 and 90–97, `SetCursorPosition` as CUP, `Clear` as ED and CUP) into the
  same engine, and the session's keys go to the program's key queue rather than to a pseudo-terminal. The session is
  named for the project, opens when the program first touches the Console, is reused by the next run, and keeps its
  content after the run ends, so the output can be read. How the grid's size maps to `WindowWidth` and
  `WindowHeight`, and whether a program can set them, is behaviour the twinBASIC DLL defines for #1108, not a
  terminal decision. A made program (HexIDE.Standalone) writes to a real terminal, which can be this one through a
  profile.

## Automation: `read_terminal`

The automation surface is a shipped UX, and this tool is judged the way a person would judge the pane.

**Why a tool, against the authoring bar.** The generic trio can find the terminal control and type into it
(`type_text` and `press_key` reach any focusable control since #2460), so input needs no tool. Reading does: a
cell's colours, faint and link, the cursor, and the defaults OSC 10/11 set are state the accessibility tree does not
expose. A wait for text is materially more reliable than polling snapshots. Both are exceptions the bar allows.

**Parameters.** `terminal` (a session's name or id, defaulting to the active one), `rows` (`screen`, the default;
`all`; or `last:N` for the last N lines of scrollback and screen), `attributes` (`runs`, the default, or `none` for
text only), `waitFor` (text, or a regular expression written `/…/`, that must appear before the reply) and
`timeoutMs` (default 5000, at most 30000). Each is optional, with a default value as well as a nullable type, so the
generated schema's `required` array is empty: CLAUDE.md's trap.

**The reply.**

- `sessions`: every session, each with its name, id, profile, state (`Running` or `Exited`), exit code, process
  name and id, and whether it is active.
- For the session read: `size` (columns and rows), `cursor` (row, column, visible), `title`, and `defaultColors`
  (foreground and background, and whether the program set them with OSC 10/11 or they are the theme's).
- `lines`: each with its row (negative for scrollback), its `text`, and with `attributes` its `runs`. A run has a
  start column and a length in columns, foreground, background, bold, faint, italic, underline, reverse,
  strikethrough and link.
- `sequence`: a counter that rises with every change, so a caller can tell an unchanged screen from a coincidence.
- `waited`: what `waitFor` found, and where, or that it timed out with the screen as it stood.

**Wide cells, said in the reply,** because they look like a defect. A wide character takes two columns but one
place in `text`, so a run's columns and the text's indices differ after one. The reply's note says so whenever the
lines read contain one.

**Empty and surprising states explain themselves.**

- With no session open, the reply names the menu item that opens one and the profiles available.
- An exited session says so, with its exit code, and that its screen is as it was when the process ended.
- A wait that timed out returns the screen it last saw, not an error with nothing in it.

**Vocabulary.** Every enum the reply renders, the session's state and the underline style, carries
`[DescribesEnum]`, so `ToolDescriptionEnumTests` fails the build if the description leaves a member out. The
description stays under the 2048-character cap: the reply's own notes carry the detail.

**Snapshots** draw the terminal like any control, so `take_snapshot` keeps working. A new tool needs `/mcp` in a
session that is already attached.

## Accessibility

Avalonia 12.0.4 offers no text provider, the automation pattern a screen reader uses to read a document by line and
word. Its providers stop at value, range, selection, toggle, expand and collapse, invoke, scroll and embedded root.
v1 gives the control an automation name (the session's) and its visible text as a read-only value. That is less
than Windows Terminal gives Narrator, and the gap is filed as an issue against this change, not left as a footnote.

## Security

- **Links** open only after Ctrl+click (Cmd+click on macOS), with the target shown first. v1 opens `http` and
  `https`. Any other scheme, and `file` links into the editor, are later and will confirm first.
- **OSC 52** (a program writing the clipboard) is off in v1. It lets any program that can print to the terminal,
  including one reached over SSH, replace the clipboard.
- **No reports that echo text back.** HexIDE answers device attributes and cursor position, which carry no text a
  program supplied. It does not answer title reports (CSI 21t), which are a known way to inject keystrokes through
  a terminal.
- **Paths from links** that later open files resolve through the same checks every tool argument naming a path
  gets, before anything is opened.

## Testing

- **Engine conformance** through `ITerminalEngine`: synthetic streams in, cells out, for every row of the v1
  rendering list, including the exact shapes #2482 measured.
- **Width agreement** on Windows: the same emoji and wide-character stream through the console host and the engine,
  compared cell by cell. This is the spike that chooses the glyph-width flag.
- **Pseudo-terminal integration** per platform: start a shell, run a command, read its output, resize, close, and
  check that no process is left. On Linux and Windows in CI; macOS by hand until a runner exists.
- **Rendering** under the headless platform for layout, and live at 150% for the DPI traps.
- **Automation**: `read_terminal` against a session whose stream the test controls, and live from a headless
  session told to work from the descriptions alone, as `docs/mcp-first-contact.md` describes.

## Decisions for Jen

**D1. Two new dependencies.** Recommended: adopt both, pinned exactly, each behind HexIDE's own interface.

- `XTerm.NET` 3.0.0, `MIT`, net10.0, no dependencies: the VT engine. Plan B: RoyalTerminal's managed engine (`MIT`).
- `Porta.Pty` 2.2.3, `MIT`, for Linux and macOS: it adds a prebuilt native shim per platform, built in public CI.
  Plan B: HexIDE's own `posix_spawn` path, if the macOS spike succeeds.
- Through Porta.Pty, and directly for HexIDE's own Windows host: `Microsoft.Windows.Console.ConPTY` 1.25.260930003,
  `MIT`, Microsoft's. It adds `conpty.dll` and `OpenConsole.exe` for each architecture to the Windows installer.
- One maintainer publishes both of the first two. The adapters, the exact pins and the conformance tests are the
  mitigation.

**D2. The v1 cut** in the table above. Recommended as drawn. If it must shrink, mouse reporting and the alternate
screen are the last to go, because without them vim and htop do not work and the terminal is not general.

**D3. Lifetime.** Recommended: shells end through the terminal's own hangup and close signals, so what the user
detached survives, and only tool profiles use the kill-on-close job. That departs from the brief, which named
`ChildProcessJob` for every child, for the reason given under Lifetime.

## Alternatives considered

- **Writing HexIDE's own VT parser.** Rejected: months of work to reach what the adopted engine already passes, and
  the long tail is where it would stay behind.
- **A ready-made Avalonia terminal control.** Rejected: four need Avalonia 12.1, the fifth carries a dependency
  with an unresolved licence, and every one owns the rendering, input and automation HexIDE needs to own.
- **A native engine through a C ABI** (libghostty-vt, libvterm). Rejected for v1: libghostty-vt's API is declared
  unstable and has no published binaries, and libvterm lacks faint, OSC 8 and current widths. libghostty-vt is the
  engine to watch: once it is stable and binaries are published, the adapter lets it replace XTerm.NET.
- **A web terminal (xterm.js) in a web view.** Rejected: Avalonia has no cross-platform web view in-box, and a
  browser engine in the IDE for one pane is out of proportion.
- **A sixteen-colour grid for VB6 Console programs only,** #1109's original scope. Superseded: it would be rebuilt
  for the other two clients.

## Risks and spikes, in order

1. Whether the inbox console host on build 26100 keeps OSC 8 and OSC 10/11. This decides whether a fallback to it
   is honest. It is measured on the oracle VM and this machine.
2. Which ConPTY glyph-width flag agrees with the engine's widths.
3. Whether `posix_spawn` with a setsid flag gives a controlling terminal on macOS, which could retire the native
   shim.
4. Throughput: how much a frame can draw at 150% with emoji fallback before it drops frames.
5. The engine's API churn: an upgrade is a deliberate change, with the conformance suite as its gate.
