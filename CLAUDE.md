# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

**HexIDE** — a cross-platform recreation of the Visual Basic 6 IDE in C#/Avalonia UI. Originally derived from [AvaloniaVisualBasic6](https://github.com/BAndysc/AvaloniaVisualBasic6) (MIT). Monorepo with two halves — `IDE/` and `LspServer/` — both **MIT**. The LSP server runs as a separate process (stdio) for crash isolation and a replaceable backend, not for any licensing reason.

## Build & Test

Requirements: .NET 10 SDK. No Java needed — `Antlr4BuildTasks` bundles the ANTLR tool.

```sh
# Build & run (from repo root)
cd IDE && dotnet build HexIDE.Desktop/HexIDE.Desktop.csproj
cd IDE && dotnet run --project HexIDE.Desktop/

# Tests
cd IDE && dotnet test HexIDE.Runtime.Tests/              # ~1000 VB6 interpreter tests
cd LspServer && dotnet test HexIDE.VbLspServer.Tests/    # ~115 LSP server tests
cd IDE && dotnet test HexIDE.Tests/                      # IDE ViewModel tests (fetches a foreign LSP server once — see below)
cd IDE && dotnet test HexIDE.Integration.Tests/          # Headless Avalonia UI tests

# Run a single test — NOTE the `--`, and that this is NOT VSTest's --filter (see below)
cd IDE && dotnet test HexIDE.Runtime.Tests/ -- --filter-method "*DebugPrint_WithStringLiteral_ShouldOutputCorrectString"

# One class, or an arbitrary slice
cd IDE && dotnet test HexIDE.Tests/ -- --filter-class "*CodeEditorViewModelTests"
cd IDE && dotnet test HexIDE.Tests/ -- --filter-query "/*/*/CodeEditorViewModelTests/*"
cd LspServer && dotnet test HexIDE.VbLspServer.Tests/ --filter "FullyQualifiedName~WireContractTests"

# Publish (both needed for the "Make EXE" IDE feature)
cd IDE && dotnet publish HexIDE.Desktop -f net10.0 -o bin/
cd IDE && dotnet publish HexIDE.Standalone -f net10.0 -o bin/standalone/

# Release publish (AOT/trimmed removed — add-in system uses Assembly.Load which is incompatible with AOT)
cd IDE && dotnet publish HexIDE.Desktop -f net10.0 -o bin/

# LSP server
cd LspServer && dotnet build HexIDE.VbLspServer/
```

CI runs two parallel jobs in `.github/workflows/build.yml`: `build-ide` (working dir `IDE/`) and `build-lsp-server` (working dir `LspServer/`), alongside two guard jobs.

**Run `bash scripts/check-tree-hygiene.sh` before pushing anything that touches docs or config.** It takes
seconds locally and a full CI cycle otherwise, and it fails on things that look entirely innocuous while
you are writing them: an illustrative Windows user path in a troubleshooting example is, to a scanner,
indistinguishable from a real one — and that scanner is what stops a machine-specific path reaching a
public repository. Do not reason about whether yours is "obviously" generic; just run it.

### The foreign-server tests fetch real third-party language servers

`HexIDE.Tests` includes sixteen tests that drive language servers **HexIDE did not write** — the only
check that the client speaks LSP to something that does not accommodate it. A client and server by one hand
agree with each other rather than with the specification, which is how three defects hid until a foreign
server was pointed at (`ForeignServerFixture.cs` has the history).

**They do not cover teardown, and a fourth defect hid there.** Both end-to-end tests dispose the
registry as best-effort (`ForeignServerIntegrationTests.cs`, `TwoForeignServersTests.cs`), and
`VBLspClient.StopAsync` swallows its own exceptions — so a `shutdown` that **two of the three** reject
(rumdl, and a second server on another framework; texlab happens to accept it) was invisible to all of
them. Worse, two tests in
`HexIDE.VbLspServer.Tests` sent the same non-conformant request themselves, and the bundled server accepts
it — so the suite agreed with the bug. It was found by driving a *fourth*, externally-authored server and
reading its **exit code**: LSP has a server exit 0 when a shutdown preceded exit and 1 otherwise, which
made HexIDE's every clean exit look like a crash to any supervisor (hexide-io/HexIDE#312).

The lesson generalises past the one bug: **an assertion that a request did not throw is not an
assertion that the server accepted it**, and teardown is where this suite is weakest. When adding a
protocol test, prefer asserting the wire frame or an observable server-side consequence over asserting
that our own call returned. `ShutdownWireShapeTests` is the worked example — it reads the bytes,
because a `[JsonRpcMethod]` handler cannot tell you whether `params` arrived as `[]`, `{}`, or not at
all, and those three are not interchangeable to a real server.

Four servers, chosen for **framework** diversity rather than language diversity — interop bugs come from
the server's LSP library, not from the language being analysed:

| Server | Tests | Framework | Obtained as |
|---|---|---|---|
| rumdl (Markdown) | 6 | `tower-lsp` | pinned binary download |
| texlab (LaTeX) | 5 | `lsp-server` | pinned binary download |
| vscode-json-language-server | 3 | `vscode-languageserver-node` | `npm ci` against a committed lockfile |
| clangd (C/C++) | 2 | LLVM's own | pinned binary download |

**Read [`docs/foreign-language-servers.md`](docs/foreign-language-servers.md) before adding a fifth** — it
carries the full reasoning, including why a GPL-licensed server is consistent with a 100%-MIT tree, and the
bar for a new one (a protocol *shape* nothing else exercises, not simply another server).

Everything is fetched **on demand**, once, at a pinned version with a SHA-256 verified before anything
executes, into the gitignored **`artifacts/foreign-lsp/`**. Not committed: several megabytes per platform
against a repository whose whole history is a fraction of that, and the same again on every version bump.
Linux uses musl builds where offered, so one binary runs on any distribution.

**The cache path matters and is not arbitrary.** It used to sit under `IDE/HexIDE.Tests/tools/`, which
collides with the tracked `Tools/` directory holding real source — a collision invisible on Windows purely
because its filesystem is case-insensitive. Downloaded binaries live under `artifacts/`; nothing fetched
ever lands beside tracked files.

- **Use your own build instead**: set `HEXIDE_MARKDOWN_LSP`, `HEXIDE_LATEX_LSP`, `HEXIDE_JSON_LSP` or
  `HEXIDE_CPP_LSP` to an executable, or put `rumdl` / `texlab` / `clangd` on `PATH`. All are checked before the download, so an explicit
  choice is never silently overridden.
- **Stay off the network**: `HEXIDE_FOREIGN_LSP_DOWNLOAD=0`. The affected tests then skip, visibly.
- **Forbid skipping**: `HEXIDE_REQUIRE_FOREIGN_LSP=1` turns "no server available" into a failure. CI sets
  this, because a silently skipped proof is the failure mode this whole fixture exists to avoid.

**The three JSON tests need Node on `PATH`** — it is a harness dependency only, and nothing in HexIDE
requires it. A machine without Node skips them, except under `HEXIDE_REQUIRE_FOREIGN_LSP=1`, where they
fail. **WSL has no Node by default, so a WSL run fails those three under that variable** until
`sudo apt-get install -y nodejs npm` is run there; CI installs it explicitly.

Bumping a version means editing the version and digests in `ForeignServerAcquisition.cs`. **Digest
provenance differs per server and the code records which** — take a digest from the publisher's own
checksum file where one exists (that attests the bytes are the ones the publisher intended); where the
publisher offers none, compute it and mark the provenance as computed here, which pins what was tested
against but attests nothing about origin. Never take a digest from a file you downloaded and call it
publisher-attested — that verifies only that the download completed.

### The LSP coverage table is guarded against the specification

`docs/lsp-client.md` carries a table of all 93 messages LSP 3.17 defines and which of them HexIDE
implements. It is **generated from the specification's own `metaModel.json`**, and
`ProtocolCoverageDocTests` fails the build if it drifts — a missing message, an invented method name, a
duplicated row, a wrong direction, or a stale headline count.

The model is fetched once into the gitignored `artifacts/lsp-metamodel/`, pinned to a **commit** rather than
a branch and verified by SHA-256. `HEXIDE_LSP_METAMODEL` points at a local copy; without it and offline the
tests skip visibly, unless `HEXIDE_REQUIRE_FOREIGN_LSP=1` is set, which is CI.

**Regenerate the table, do not hand-edit it.** That guard exists because the document these replaced drifted
into fiction unnoticed for months — six architecturally-forbidden features listed as planned, a compiled-out
check described as working, three wrong counts. A ninety-row table is the most drift-prone thing in the
repository, and it is the one artefact here that cannot rot silently.

### Verify on Linux before pushing — `build-ide` runs on `ubuntu-latest`

The IDE parses and writes Windows-native formats (`.vbp`, `.vbg`, `.frm`), so a whole class of defect is
**unreachable on a Windows dev box and only fails on CI** (see the two `System.IO.Path` entries under
Gotchas). Run the suites under WSL rather than discovering it a push later:

```sh
# Ubuntu + dotnet-sdk-10.0 from Ubuntu's own repos (no Microsoft feed needed on 26.04+)
wsl -e bash -lc 'sudo apt-get install -y dotnet-sdk-10.0'

# Run against the SAME working tree — no second clone. .NET on Linux uses '/' regardless of the mount.
wsl -e bash -lc 'cd /mnt/c/Repos/GitHub/HexIDE/HexIDE/IDE && \
  dotnet test HexIDE.Runtime.Tests/ --artifacts-path /mnt/c/Repos/GitHub/HexIDE/HexIDE/artifacts/linux'
```

**`--artifacts-path` is required, and it must point INSIDE the repo.** Without it the Linux build stomps
the Windows `obj/`/`bin/`; pointed outside the repo (e.g. `/tmp`), `GrammarParityTests.RepoRoot()` fails —
it walks up from `AppContext.BaseDirectory` looking for the directory holding both `IDE/` and `LspServer/`,
and finds no such ancestor. `artifacts/` is already gitignored.

Expect the run to be slower than Windows (the `/mnt/c` 9p mount), and identical in result — 1423/1423 as of
this writing. To confirm the setup still has teeth, run a `git worktree` at a commit predating a known
cross-platform fix and check it fails there.

### And for the version gap WSL leaves — Podman, matching CI's distro

WSL closes the Windows-path class, but it is **not** the same Linux CI runs, and the difference is measured
rather than assumed:

| | Ubuntu | ICU | glibc |
|---|---|---|---|
| CI (`build-ide`) and the container below | **24.04** | **libicu74** | 2.39 |
| WSL | **26.04 LTS** | **libicu78** | 2.43 |

Four ICU major versions apart. .NET takes string comparison, casing and culture-sensitive formatting from
the platform's ICU, and this project compares VB6 identifiers case-insensitively everywhere, ships 27
language packs, and round-trips culture-sensitive values into Windows-native files. So a divergence there
would present exactly as the original bug class did: green locally, red on CI.

```sh
# One-time. Podman, not Docker Desktop — the latter needs a paid subscription at 250+ employees or
# $10M+ revenue, and this file is instructions a contributor follows. See hexide-io/HexIDE#253.
podman machine init && podman machine start

# Any suite, in a container on CI's own Ubuntu version.
podman run --rm \
  -v "C:\Repos\GitHub\HexIDE\HexIDE:/repo" \
  -v hexide-nuget:/root/.nuget \
  -w /repo/IDE \
  mcr.microsoft.com/dotnet/sdk:10.0-noble \
  dotnet test HexIDE.Tests/ --artifacts-path /repo/artifacts/container
```

- **Run it from PowerShell.** In Git Bash the same command fails with `workdir "C:/Program Files/Git/repo/IDE"
  does not exist` — MSYS rewrites the container-side `/repo/IDE` into a Windows path before podman sees it.
  `MSYS_NO_PATHCONV=1` fixes it if you want Git Bash; nothing is wrong with the command itself.
- **`--artifacts-path` is required and must point inside the repo**, for the same reason as under WSL: the
  mount is shared, so without it the container's build stomps the Windows `obj/`/`bin/`. Use a *different*
  path from the WSL one so the two Linux runs do not stomp each other either.
- **Mount the NuGet volume.** `--rm` discards the container's filesystem, so without it every run
  re-restores from scratch.
- **Expect 4 skips in `HexIDE.Tests`, not 1.** The SDK image has no Node, so the three
  reference-implementation tests skip on top of the usual `[WindowsOnlyFact]`. CI installs Node explicitly
  and runs them; do not set `HEXIDE_REQUIRE_FOREIGN_LSP=1` here unless you have added Node to the image.

**Measured on first setup (2026-09-06): no divergence.** `HexIDE.Tests` 961/965 and
`HexIDE.Runtime.Tests` 1432/1432 in the container, matching CI exactly, against 965 and 1432 under WSL. So
the ICU gap is real but **not currently live** — which is a statement about the tests that exist, not a
guarantee. Nothing here probes collation or culture-sensitive formatting directly, so absence of a
divergence is weak evidence of absence. Reach for the container when a CI failure will not reproduce under
WSL; that is the case it was set up for.

**The 282-failure run is a thread-affinity race in the test harness, not a build problem.** Every so often
`HexIDE.Tests` reports a large batch of failures — **282**, the same number every time, because it is
exactly the set of tests that touch Avalonia — and passes on the very next attempt.

Each of those failures carries a real message, and it is always this one:

```
System.InvalidOperationException : The calling thread cannot access this object because a different
thread owns it.
   at Avalonia.Threading.Dispatcher.VerifyAccess()
```

**The mechanism.** `AvaloniaTestSetup.EnsureInitialized` binds Avalonia's dispatcher to whichever thread
calls it first, and every later access must come from that same thread.
`TestParallelization.cs` disables parallelisation to protect this, which prevents *concurrency* but does
**not** guarantee *the same thread*: an async continuation may resume on a different pool thread, and when
that happens around the first initialisation, every Avalonia-dependent test afterwards is on the wrong
side of `VerifyAccess`. The suite has async-heavy tests (real stream I/O, JSON-RPC, timers) that make the
drift more likely, though the fault is the harness's, not theirs.

The `Setup was already called on one of AppBuilder instances` failures are the **same fragility wearing a
different face** — what you get when the first initialisation throws and the latch, which is set *after*
the call it guards, never latches. Both are tracked in
[#286](https://github.com/hexide-io/HexIDE/issues/286).

**What to do: re-run it, then believe the second answer.** A genuine failure reproduces and names
different tests; this one is always the same 282 with the same message.

**What this note used to say, and why it is worth recording that it was wrong.** It attributed the run to
MSBuild's up-to-date check loading a mismatched assembly set, told the reader to expect *no error message
against any of the failures*, and offered run duration as the tell. All three were wrong, and the middle
one is why it went unexamined for so long — a reader who has been told there is no error does not go
looking for one. The duration correlation was real but incidental: a run that fails 282 tests in their
constructors finishes sooner because it does less work. Diagnosed properly on 2026-09-06 by reading a CI
log instead of the note.

## MCP Dev Loop

## Visual Verification — MANDATORY

HexIDE is a visual tool used by humans. Visual verification is not optional.

**MUST:**
- Treat every UI feature as incomplete until verified against the running IDE: `take_snapshot` for rendering/layout, and `dump_visual_tree` / `inspect_element` for **structured** assertions (a control exists, is enabled, has the expected value / selection / toggle state / providers) — which often replace a pixel snapshot.
- Discover and drive UI with the generic trio — `dump_visual_tree` to find a control, `interact` to navigate and act (select a page, click a button, toggle, type, open a dialog), `inspect_element` to confirm — plus `add_control` / `activate_document_tab` / `view_designer` for designer/tab setup. Set up required state autonomously; never ask the user to do something a tool can do.
- Build a **new** dedicated MCP tool only when the generic trio genuinely can't reach a surface **and** the tool-authoring policy in `openspec/specs/hexide-mcp-server/spec.md` justifies it (reads a model the tree can't see, persists/transacts, or beats path addressing). Otherwise, `interact` is the tool.
- **Record every MCP dev-server shortcoming you hit** — a surface it can't observe (e.g. a runtime modal layered over a running form), a property it can't set (e.g. an enum/colour), a lifecycle gotcha (e.g. tools dropping on `shutdown_ide`) — in [`docs/mcp-server-gaps.md`](docs/mcp-server-gaps.md), with symptom → workaround → suggested fix, so the tooling is improved deliberately instead of re-worked around each session.

**MUST NOT:**
- Assume AXAML renders as expected — Avalonia has rendering quirks invisible to tests
- Ask the user to draw controls, drag windows, navigate dialogs, or perform any action a tool can handle (`interact` clicks/selects/types/toggles and navigates modal dialogs)
- Use raw HTTP calls, PowerShell, or any bypass mechanism to work around missing MCP tool schemas
- Claim "correct by construction" as a substitute for verification against the running IDE
- Consider a UI feature complete without confirming it on the running IDE (a `take_snapshot` for rendering, and/or `inspect_element`/`dump_visual_tree` assertions)

**If you cannot verify a feature without user interaction, you are not done — drive it with `interact`, or (only if the authoring policy justifies it) build a tool.**

### The automation surface is a shipped UX, and is judged like one

**This surface is not internal scaffolding for whoever is building HexIDE.** It is exposed to every
developer who wants it, driven by models nobody here chooses. A suboptimal AI surface bites exactly as a
bad UI/UX surface bites a human user, and it gets the same treatment: issues, follow-ups, and a bar.

**"I found a way around it" is not the test.** Whoever found the way around it had context a first-time
caller does not. Judge a tool by what a model that has never seen it would do on first contact.

The worked example is in [`docs/mcp-server-gaps.md`](docs/mcp-server-gaps.md) and it was ours: the first
call to `list_lsp_messages` in a fresh session returned `{"messages":[],"matched":0}` and nothing else. The
author knew why — a language server starts on the first document of a language it claims, and nothing was
open. A caller who did not know that cannot tell *nothing happened* from *nothing was configured* from *the
tool is broken*, **which is the exact ambiguity the protocol inspector exists to destroy**, reintroduced
inside the tool built to destroy it.

So, for any tool added here:

- **An empty or surprising reply explains itself**, and says what to do next where there is an obvious
  next step. A count of zero that could mean four things must say which.
- **Check the generated schema's `required` array, not the C# signature.** A nullable parameter with no
  default value is still *required* on the wire, so the simplest question can cost five arguments while
  the C# reads as optional.
- **Enumerate the vocabulary a reply uses.** A `kind` of `Unconsumed` means nothing to somebody who was
  never told the set.
- **Explain anything that looks like a defect and is not.** Sequence gaps beside a field called
  `framesDropped` read as data loss.
- **A reply that mutates reports the new state**, and reports it even when that state is empty.

**Record every shortcoming in `docs/mcp-server-gaps.md` AND file it**, exactly as a human-facing defect
would be. An entry that lives only in that file is a note to self; the surface ships either way.

**A new tool needs a session restart only if the MCP server was NOT attached when the session started.**
Measured, both ways, in one afternoon:

- Four tools were added while **no IDE was running**. Building and launching did not surface them; a
  session restart was needed. The server had never attached, so there was nothing to re-list from.
- A fifth was added with the IDE **running and attached since session start**. `shutdown_ide`, rebuild,
  relaunch — and it arrived as a deferred tool and worked on the first call, with no restart.

So the condition is the state of the attachment when the session began, not whether the process has
restarted since. **If the IDE was up and answering at session start, do the rebuild cycle and try the tool
before asking.** If it was not, stop and ask for a resume.

**Never reach for raw HTTP either way.** That rule is unchanged and is not about schemas: a bypass proves
nothing about the surface a real caller uses.

This entry used to say a restart was always required. That is the cautious direction and it is not free —
it costs a round trip through the user every time a tool is added, and the whole point of stopping to ask
is that it is reserved for when it is genuinely needed.

HexIDE exposes an embedded MCP server (opt-in via `--server-port <port>`). **The MCP server is a dev/automation tool and is `#if DEBUG`-compiled out of Release builds** — the `Server/` folder and the AspNetCore framework dependency are excluded from Release, so a distributed binary opens no port and `--server-port` is inert. The dev loop below uses Debug builds, so this does not affect it. With HexIDE running (Debug), Claude Code connects automatically via `.mcp.json` at the repo root and has access to these tools:

| Tool | Description |
|------|-------------|
| `get_project_info` | Current project name, path, forms, modules, carried files |
| `get_open_editors` | Open editor windows and active window |
| `get_document_tabs` | List all open editor/designer tabs with title, type (`code`/`designer`), and active flag |
| `activate_document_tab(title)` | Make a tab active by title |
| `close_document_tab(title)` | Close a tab by title |
| `get_diagnostics` | LSP errors and warnings |
| `open_file(name)` | Open a form, module or carried file in the code editor (creates or activates tab) |
| `view_designer(name)` | Open a form or UserControl in the visual designer (creates or activates tab; use before `take_snapshot`) |
| `run_project` | Start the VB6 runtime |
| `stop_project` | Stop the running project |
| `shutdown_ide` | Clean shutdown (triggers all shutdown handlers) |
| `take_snapshot` | Capture the IDE window as PNG; returns temp file path — read it with the `Read` tool to view |
| `dump_visual_tree(root?, maxDepth?, interactiveOnly?)` | Walk the active window's **control-view** tree (structural wrappers collapsed; a visible modal dialog is preferred). Each node carries an addressable `path`, automation ControlType, Name/AutomationId, DataContext VM type, and supported interaction providers. The discovery entry point. |
| `inspect_element(target)` | Deep-inspect one control by `path`: supported providers, bounds, current value/selection/toggle state, and the DataContext VM's command/property members (the surface `interact`'s reflection actions target). |
| `list_lsp_messages(connectionId?, method?, failuresOnly?, afterSequence?, limit?)` | Recorded language-server envelopes — time, direction, method, id, size, outcome, latency — with no content. Answers "was it even sent" and "what came back", which diagnostics cannot. Works unarmed. |
| `get_lsp_message(connectionId, sequence)` | One message's body, as the bytes that crossed the wire. Needs the connection armed, except for a connection's opening, which is always kept. |
| `arm_lsp_capture(connectionId?, armed)` | Arms or disarms retention of message **bodies**. Session-scoped; use `--capture-lsp` to arm before the first connection exists. |
| `clear_lsp_capture(connectionId?)` | Discards the record and keeps the arming, so the next thing exercised is the only thing in it. |
| `export_lsp_conversation(connectionId?)` | Writes the conversation as JSON-lines plus a manifest and returns both paths. **Always pseudonymised** — this is the shareable form; `get_lsp_message` is the raw one and is not. |
| `get_lsp_capture_state()` | What is being recorded: every known connection, whether its bodies are kept, and what it has discarded. Read-only — ask this rather than arming something to find out what is armed. |
| `interact(target, action, value?)` | Drive a control. Provider actions: `invoke`/`select`/`set_value`/`toggle`/`expand`/`collapse`. Reflection actions (DataContext VM): `invoke_command`/`set_property`. The generic substitute for per-interaction tools. |

**CLI flags** (both `--` and `/` prefixes accepted, aligning with VB6 convention):
- `--server-port <port>` — enable the MCP server on the given port (all launch profiles use 5123)
- `--newproject` — skip the startup dialog and create a default Standard EXE project
- `--capture-lsp` — arm the protocol capture for every language-server connection **before any is made**, so a conversation is recorded in full from its first handshake. Arming is otherwise session-scoped and the documented rebuild cycle restarts the IDE every iteration, which is what this exists for. **Unlike `--server-port`, this is not DEBUG-only**: the capture ships and the automation server does not
- Positional `.vbp` path — skip the startup dialog and open that project

**The server answers loopback only, and now checks that rather than assuming it.** A request whose `Host`
header does not name a loopback address on the bound port is refused with **403** before it reaches any
endpoint, health included, and so is one carrying an `Origin` that is not loopback. If a client of yours
starts getting 403s it is sending the wrong `Host`, not talking to a broken IDE. Binding to loopback alone
did not close this: DNS rebinding lets a page the developer happens to visit reach a local port with no
preflight, and the `Host` header is the one thing that trick cannot forge. There is still **no
authentication**, so anything able to open a socket to the port can drive the IDE. That is a separate
control and a separate decision ([#352](https://github.com/hexide-io/HexIDE/issues/352)).

### Rebuild cycle (no user interaction required)

When you need to rebuild while HexIDE is running, always follow this cycle — do NOT ask the user:

1. **Shut down**: call `shutdown_ide` MCP tool (clean shutdown, releases all file locks)
2. **Build**: `cd IDE && dotnet build HexIDE.Desktop/HexIDE.Desktop.csproj -c Debug`
3. **Relaunch**: `Start-Process "$PWD\IDE\HexIDE.Desktop\bin\Debug\net10.0\HexIDE.Desktop.exe" "--server-port 5123 --newproject"`
4. **Wait for ready**: poll `http://localhost:5123/health` until HTTP 200 (use a loop with 1 s sleep, up to 30 s)
5. **Continue**: MCP tools are immediately usable once `/health` returns 200

If `shutdown_ide` is unavailable (MCP disconnected), use PowerShell: `Stop-Process -Name HexIDE.Desktop -ErrorAction SilentlyContinue` then proceed from step 2.

**File locks from Visual Studio debugger:** If a build still fails with lock errors after shutting down via MCP/CLI (i.e. Visual Studio also has the project loaded and attached), pause (not stop) the VS debugger and retry the build. Only ask the user if you cannot resolve the lock yourself.

### Initial dev loop (HexIDE not yet running)

1. Build: `cd IDE && dotnet build HexIDE.Desktop/HexIDE.Desktop.csproj -c Debug`
2. Launch: `Start-Process "$PWD\IDE\HexIDE.Desktop\bin\Debug\net10.0\HexIDE.Desktop.exe" "--server-port 5123 --newproject"`
3. Wait for ready: poll `http://localhost:5123/health` until HTTP 200
4. Use MCP tools to inspect and interact with the running IDE

**MCP session note:** MCP tools are discovered at session start. If HexIDE is not running when a Claude
Code session starts, the tools will not appear **and relaunching it will not make them appear** — that
session has no attachment, and only a resume creates one. If HexIDE *was* running at session start, the
attachment survives a relaunch: existing schemas stay usable (Streamable HTTP is stateless — each call is
a fresh POST) **and newly added tools are picked up**, which is measured rather than assumed. See the
restart note above.

## Architecture

### Monorepo structure

| Folder | License | Purpose |
|--------|---------|---------|
| `IDE/` | MIT | IDE application (14 projects) |
| `LspServer/` | MIT | Out-of-process VB6/VBA LSP server (EmmyLua shell + proleap grammar) |
| `HexIDE.slnx` | — | Master solution (Visual Studio 2022+) |
| `.github/` | — | CI workflows |
| `docs/` | — | Engineering docs — MISSING_FEATURES.md, the LSP pair, the fidelity oracle, the gap catalogues. **Ships publicly.** |
| `docs/private/` | — | Strategy and ops — ROADMAP.md, EVOLUTION.md, the neighbour assessments, launch readiness, the signing runbook. **The only pruned part of `docs/`** — absent from a public clone by design; never link to it from a shipping file |

### Key IDE projects (`IDE/`)

Package versions are centralized in `IDE/Directory.Build.props` (Avalonia, Dock, .NET TFMs, Serilog).

| Project | Role |
|---------|------|
| `HexIDE` | IDE shell — MVVM, form designer, toolboxes, MDI, DI setup |
| `HexIDE.Lsp` | LSP client (`VBLspClient` via StreamJsonRpc, `LspServerLocator`) |
| `HexIDE.LspProxy` | Debug proxy — set `VB6_LSP_DEBUG_PROXY=1` to log LSP frames to stderr |
| `HexIDE.Runtime` | VB6 interpreter, built-in controls, component model, serialization |
| `HexIDE.Runtime.Tests` | xUnit interpreter tests |
| `HexIDE.Tests` | IDE ViewModel unit tests |
| `HexIDE.Integration.Tests` | Headless Avalonia UI tests (`Avalonia.Headless.XUnit`) |
| `HexIDE.Desktop` | Desktop entry point; conditionally copies LspServer exe to output |
| `HexIDE.Standalone` | Headless VB6 runner (no IDE) |
| `HexIDE.Browser` | WebAssembly entry point (future aspiration) |
| `HexIDE.Core` | Framework-agnostic abstractions |

### LSP architecture

- **Server** (`LspServer/HexIDE.VbLspServer`): out-of-process console app built on **EmmyLua.LanguageServer.Framework** (MIT); handlers wired in `LspServerHost.cs` (stdio, `SingleThreadScheduler`, `AddRequestHandler`/`AddNotificationHandler`). Parses with the **proleap / grammars-v4** VB6 grammar (`VisualBasic6Lexer.g4` + `VisualBasic6Parser.g4`) via a two-stage SLL→LL strategy + a wall-clock parse backstop (`VbDiagnosticsProvider`). Diagnostics = collecting ANTLR error listener + `Option Explicit` undeclared-variable checks (`VbScopeAnalyzer`), messages via `VbErrorMessages.Prettify()`. Run standalone with `dotnet run --project HexIDE.VbLspServer/`.
- **Client** (`HexIDE.Lsp`): `VBLspClient` uses `StreamJsonRpc` + `SystemTextJsonFormatter`. Auto-started by `App.axaml.cs`, stopped on `ShutdownRequested`. Desktop-only.
- **Diagnostics flow**: server → `publishDiagnostics` → `VBLspClient.DiagnosticsPublished` → `CodeEditorViewModel.OnDiagnosticsPublished` (on `Dispatcher.UIThread.Post`) → AvaloniaEdit offsets → `LspTextMarkerService.SetMarkers()` → wavy underlines.
- **AOT**: `StreamJsonRpc` IL warnings suppressed in Desktop. `LspJsonContext` (source-gen `JsonSerializerContext`) covers all LSP types.
- **Desktop.csproj** uses `Exists()` condition on the LspServer reference — the IDE builds fine without it (LSP diagnostics disabled).

### Runtime internals

- **Two grammar copies, one lineage**: `IDE/HexIDE.Runtime/Interpreter/Grammar/VB6.g4` (proleap) for the interpreter; `LspServer/HexIDE.VbLspServer/Grammar/VisualBasic6Lexer.g4` + `VisualBasic6Parser.g4` (proleap/grammars-v4, with HexIDE clean-room fixes) for the LSP server. Both MIT; separate copies, different generated parsers.
- **Interpreter**: `BasicInterpreter.cs` → `StatementExecutor` + `ExpressionExecutor`. All values are `Vb6Value` (readonly struct: `ValueType` + `object? Value`).
- **Component model**: `ComponentBaseClass` subclasses in `Components/`. Declare properties (`PropertyClass<T>`), events (`EventClass`). All VB6 properties are static `PropertyClass<T>` instances in `VBProperties.cs`.
- **Built-in controls**: `BuiltinControls/` — Avalonia `Control` subclasses (e.g., `VBTextBox : TextBox`). Override `StyleKeyOverride` to base Avalonia type. Events wired via `AttachedEvents` static helpers.
- **Form runtime**: `VBLoader` spawns controls from `FormDefinition` onto Avalonia `Canvas` (absolute pixel positioning). `VBFormRuntime` is the running window.
- **Serialization**: `Serialization/` reads/writes native VB6 `.frm`/`.vbp` format.

### IDE internals

- **DI**: `Pure.DI` (source-generated, zero reflection — required for `PublishAot=true`). All singletons registered in `DISetup.cs`. Roots: `MainViewViewModel`, `ILspClient`.
- **Services** in `IDE/HexIDE/IDE/`: `ProjectManager`, `EditorService`, `WindowManager`, `EventBus`, `FindReplaceService`, `SettingsService`.
- **Visual designer**: `VisualDesigner/` — works with `ComponentInstance` objects (not live controls).
- **MVVM**: `ViewLocator` resolves View from ViewModel through an **explicit registration table** in its
  static constructor — `Register<TViewModel, TView>()` — not by naming convention. A new editor or tool
  window whose view is not registered there resolves to nothing and renders as a **blank pane with no
  error**, which is a slow thing to diagnose because the tab, its title and its docking all work.

## Platform Scope

**Android and iOS are not supported** — the projects have been deleted. The only non-desktop platform to consider is Browser (WASM), which is a future aspiration only — it is not a current target and requires no active work.

See [OUT_OF_SCOPE.md](docs/OUT_OF_SCOPE.md) for the full list of VB6 features that are excluded by design (SDI mode, User Documents, Data Environment, etc.). COM/OLE is **not** excluded — it is in scope but Windows-gated (foundational to real-world VB6; see the COM/OLE section of the maintainers’ Evolution catalog).

## Fidelity Principle

**Fidelity means reproducing VB6's intended behaviour, not its bugs.** Where VB6 had a known defect, HexIDE should do the right thing even in Classic mode. The canonical example: in VB6 the Object Browser lost its MDI chrome (title bar, close button) when maximised, because it was an MDI child window — that was a Windows MDI system limitation, not an intended design. In HexIDE the Object Browser is a `Document` tab in the `DocumentDock`, so the Dock framework always owns its tab header and close button; maximising an MDI child inside the host cannot affect it. Whenever a feature diverges from VB6 behaviour, document the reason here or in the relevant spec.

**Verify actual VB6 behaviour against the real compiler — never guess.** Any doubt about what VB6 *actually does* at runtime (a numeric result type, an overflow/error code, a rounding rule, a coercion, a literal's type, a `Format`/intrinsic edge) **must be tested against real `vb6.exe`** — the fidelity oracle — before you pin an interpreter test expectation. Documentation and memory are repeatedly wrong here; the oracle has overturned "obvious" assumptions many times. Record every verified fact (and the reusable `On Error Resume Next` `/make` harness) in [`docs/vb6-fidelity-oracle.md`](docs/vb6-fidelity-oracle.md). `vb6.exe` is at `C:\Program Files (x86)\Microsoft Visual Studio\VB98\VB6.EXE` (or `$VB6_EXE`) — the same toolchain as the "Make/Run with VB6" feature. This is a Windows dev-time check; it never becomes a runtime dependency (HexIDE re-implements VB6's behaviour, it does not call `MSVBVM60`).

**`vb6-fidelity-oracle.md` is a PRIMARY OUTPUT of this project, not a scratchpad.** It ranks with the code,
and a change that measures something new is not finished until the measurement is written down there.

VB6 is end-of-life. Its documentation is archived and, on the details that matter here, repeatedly wrong.
The people who held these rules in their heads have dispersed, and — as this project keeps demonstrating —
what they remember is often a rule from a *neighbouring* product. So a file of behaviours obtained by
running the real compiler and reading what came back is very likely the most complete systematic record of
VB6's actual runtime semantics that exists anywhere. Treat it accordingly:

- **Record the measurement, not just the conclusion.** The probe, the value, the type. A later reader must
  be able to see what was asked as well as what was answered.
- **Record what was overturned, and by whom.** "The obvious guess was X; it is Y" is worth more than "it is
  Y", because the obvious guess is what the next person will also make. Several entries record *our own*
  wrong expectations for exactly this reason.
- **Record what resisted explanation.** An honest "measured, but no rule I would defend" is a result. Do not
  tidy it into a plausible rule — that is how a wrong generalisation gets laundered into a fact, which has
  already happened once here and cost a silent bug.
- **Never delete a row because it looks odd.** Odd is the signal. Most oddities in this file dissolved once
  a storage width or a subsystem boundary was understood; the ones that did not are the valuable ones.

**Secondary reference (subordinate to the oracle): the VBA documentation** — a local clone of `MicrosoftDocs/VBA-Docs` (the VBA language + object-model reference), located via `$VBA_DOCS`. Licence-vetted MIT-clean: docs are **CC BY 4.0** (attribution-only, *no* copyleft — cannot infect HexIDE's MIT), code samples are **MIT** (© Microsoft). Use it as a **reference for the object model, intrinsic surface, and general semantics** — never as the fidelity authority: it describes *VBA* (VBA7), which diverges from *VB6* at the edges, so `vb6.exe` **always wins** on any behavioural conflict, and every pinned test expectation still comes from the oracle, not the docs. **Two rules to keep clean-room status: (1) never copy the doc prose verbatim** into HexIDE code/comments/docs (facts/APIs/semantics aren't copyrightable, so learning-then-implementing is fine; copying *expression* is not — if you ever quote prose, attribute per CC BY; copied code samples are MIT, keep the notice); **(2) fidelity stays oracle-driven** — this is a lookup aid, not a spec to implement from (that spec-driven lane belongs to a real language engine; HexIDE stays independent). NB this is the docs repo, *not* the formal `[MS-VBAL]` Open Specification (a separate artifact under Microsoft's Open Specifications programme).

## Language-Analysis Boundary — CST, not AST (HARD LIMIT)

HexIDE's own in-process language tooling operates at the **syntactic (CST) level only**. It parses VB6/VBA
to a concrete syntax tree and ships CST-level services — syntax diagnostics, structural document symbols,
keyword/declared-name completion — plus a **tree-walking interpreter** (`BasicInterpreter` walks the parse
tree directly) that demonstrates understanding by **executing** code. The interpreter's runtime scope table
and runtime type semantics are **execution machinery, not static analysis** — that is within bounds, and its
path to fidelity is **runtime-execution fidelity** (running VB6 correctly), never static-analysis fidelity.

**HexIDE does NOT, and by design never will, build a semantic (bound) AST or perform static semantic
analysis** — cross-file / whole-program name binding, type inference, semantic diagnostics beyond syntax,
semantic rename, find-all-references, workspace symbol resolution, or a code-fix / refactoring engine.
Producing and analysing a proper **AST is the exclusive job of a real language engine**, delivered over the
replaceable LSP/backend seam.

**Why + consequences:** this keeps HexIDE a shell + demonstrator, not a half-built compiler frontend — no
farm-bet re-implementation of semantic analysis, and a genuinely replaceable backend. It is *why* the
Option-Explicit undeclared-variable check is **default-off** (it needs a symbol table = semantic analysis;
see `VbDiagnosticsProvider.EnableUndeclaredVariableCheck`) and why the Evolution catalog's "Language
intelligence" rows are marked as belonging to that engine rather than to HexIDE. If a task needs a bound
AST / semantic model, it belongs in the backend engine, not in HexIDE. Decided 2026-07 (user, external
advisor concurring).

**Where the pre-pass sits, because it looks like a violation and is not.** `PrePass.cs` walks a module
before execution and builds tables — procedures, properties, events, `WithEvents` names, UDTs, enums,
`Option Base`, `Option Explicit`. That is sanctioned, and the rule that decides it is:

> **Collecting symbols up front is fine, including where it is the only way. Analysing the *relationships
> between* symbols up front is not.**

Collection is *"here is every `Sub` in this module"*, *"here is every `Public` variable in the project"*,
*"here is every `DefInt` letter range and its type"*. Relating is *"this identifier refers to **that**
declaration"*, *"this expression has type T"* — which is **binding**, and binding is the AST.

A weaker test — *"could the walk do this itself, just slower?"* — is tempting and wrong: it forbids
order-independent `Const` evaluation, which no single walk can do and which is plainly not AST-building.
Necessity is not the line. Relationships are.

The rule also decides **how** to build what it permits. `Const A = B + 1` looks like a symbol relationship,
so do not topologically sort the constants up front — collect the *expressions* and evaluate lazily and
memoised, letting the walk resolve the dependency when it reaches it. Same behaviour, and the pre-pass
stays pure collection.

What it still refuses, for the same reason rather than a different one: the linearized CFG that `GoSub` /
`Return` / `On expr GoTo` and nested-granular `Resume` are deferred pending. A control-flow graph relates
statements to each other; it is a map of the program, not a lookup for the walk.

**The interpreter is an APPROXIMATION, not a reimplementation.** If a program is valid VB6 and can be made
to run, running it beats refusing it — even at a different evaluation point, even with an error raised later
than VB6 would raise it, even where a diagnostic VB6 gives at compile time can only be given here as the
statement executes. `interpreter-core:40-42` already prescribes that translation: same error number, at run
time rather than before it.

So a construct is walled only when it cannot be **executed** — never merely because it cannot be
**validated the way VB6 validates it**. That distinction is what nine entries in `interpreter-gaps.md` got
wrong: a compile-time check that genuinely needs binding was taken to wall the whole feature, when the
feature itself was a runtime lookup. Interface conformance is the clearest case — the check needs binding
only if you insist on making it *before* the program runs; after a class is instantiated, comparing two
already-collected member tables is ordinary execution. VB6's own COM substrate asks this question at
runtime, via `QueryInterface`.

**The guardrail, and it is not negotiable:** this licenses approximating *when and how an error surfaces*.
It never licenses approximating a **result**. A wrong answer that runs is worse than a clean refusal — that
is the whole of `docs/serialization-outcomes.md`, and it applies here identically. Late error: acceptable.
Missing error on a path never taken: acceptable, and documented. Wrong value: never.

**Three limits, not one — do not conflate them.** The CST/AST line is only one of the things that bounds
this interpreter, and filing everything under it is how `interpreter-gaps.md` came to have nine deferrals
sitting in its "Walled off (by design)" section, `GoSub` and `Implements` justified identically when
neither shares the other's limit.

**No VB6 construct is incomprehensible to the CST.** The grammar parses everything VB6 accepts; the tree
holds it. What is out of reach is:

1. **Binding — permanent.** Questions answerable only by relating two symbols *without executing either*:
   does this `Public` signature expose a `Private Type`, does `Property Let` agree with `Property Get`, does
   this class satisfy `Implements IFoo`. These cost **diagnostics, not constructs** — the construct is
   always buildable, and `interpreter-core:40-42` prescribes the in-bounds translation: raise VB6's error
   number at run time rather than before it. A second-order consequence worth stating: VB6 compiles the
   whole program and HexIDE only sees executed statements, so an error on a never-taken path is invisible
   here and fatal there.
2. **Tree-walking — an execution-strategy limit, NOT the AST limit.** `GoSub`/`Return`, `On expr
   GoTo`/`GoSub`, and nested-granular `Resume` need "jump to label L, then resume where I was" — a position
   in a linear statement sequence. A tree-walk's position is a stack of visitor frames, not an index, so
   there is nothing to save and return to. The CST has the labels and the statements; a linearized CFG or
   bytecode would lift this without touching the binding question. **This is the only limit that costs
   actual constructs.**
3. **Missing input — platform.** Type libraries: an OCX's default member, `New Excel.Application`, COM
   instancing. Not a comprehension failure at all. COM/OLE is *in scope and Windows-gated* per
   OUT_OF_SCOPE.md, so filing it as a language-boundary limit both misdescribes it and makes it look
   permanent.

Before recording anything as walled, say which of the three it is. If the answer is "the diagnostic is
walled but the construct is not", record exactly that — the distinction is what nine mis-filings turned on.

## Currency & Best Practice

**This project always aims for current best practice.** If you spot anything that looks even slightly
rusty — a superseded API, an outdated pattern, a package with a known vulnerability or a stale version
pin, a deprecated flag, or a convention that has since moved on — **flag it immediately** rather than
quietly working around it. If it bears on a decision in flight, **pause and ask** before proceeding; if
it's incidental, note it so it can be tracked. Staying current is a first-class quality bar here, not a
nice-to-have.

## Critical Constraints

- **The tree stays 100% MIT, and a dependency's licence must be RECORDED before it is used.** Every
  centrally-managed package needs a line in `scripts/package-licences.tsv` giving its SPDX id, and that id
  must be permissive (MIT, Apache-2.0, BSD-2/3-Clause, ISC, 0BSD, Unlicense, MS-PL). `scripts/check-licences.sh`
  fails CI otherwise. Read the licence from the NuGet registration API at the pinned version — not from
  memory, and not from the package's README.
  **An unknown licence is refused as firmly as a copyleft one.** `NOASSERTION` is not a mild finding: GPL
  terms are at least knowable, whereas an unresolved licence is unbounded and cannot be undone once shipped.
  The guard used to check only for GPL, which caught the last problem rather than the next one.
- **Code travels OUT to licence-ambiguous neighbours, never back in.** Contributing to a project whose CLA
  permits relicensing is a decision the maintainer is free to make; importing from a repository whose licence
  no tool can resolve is irreversible and breaks a promise made to everyone downstream. So anything wanted in
  both places is **authored in this tree first**, under MIT, and a copy is contributed outward — never written
  there and copied back. `check-licences.sh` enforces the inbound half by refusing references to such an
  origin's namespaces.
  Facts are not code: a protocol's method names, wire shapes and semantics are free to read and implement.
  It is *expression* that must not cross — the same line already drawn for the VBA documentation.

- **Avalonia 12.0.4 / Dock 12.0.0.2** — the project is on Avalonia 12. `Classic.Avalonia.Theme 12.0.1-beta1` is kept as a **controls-only** dependency (provides `ClassicBorderDecorator`, `ClassicBorderStyle`). **Do not pin it back to 11.3.0.3**: that build was compiled against Avalonia 11, so `ClassicBorderDecorator.DrawRadioButtonBorder` called a `StreamGeometryContext.ArcTo` overload Avalonia 12 replaced, and **every VB6 option button killed the process on render** (`MissingMethodException`, thrown on the render thread — uncatchable, and nothing reaches the Serilog log; the stack only exists in the Windows Application event log). It is a prerelease because it is the only Avalonia-12 build published. `<ClassicTheme />` remains **NOT loaded** and this change does not re-open that question — do not add it back. The `Classic.Avalonia.Theme.Dock`, `.ColorPicker`, and `.DataGrid` sub-packages have been removed. `Avalonia.Themes.Simple 12.0.4` is the base theme (`<SimpleTheme />` in App.axaml).
- **`Classic.CommonControls.Avalonia 12.0.1-beta1` controls** (`ToolBar`, `ToolBarButton`, `RebarHandle`, `ListView`, `ListViewItem`) are **NOT used** — the `TypeLoadException: PseudolassesExtensions` applied to the 11.x build, but they stay unused by design (VB6 chrome removal is on the Evolution path). Use standard Avalonia `StackPanel`/`Button`/`ToggleButton`/`ListBox` instead. The version follows `Classic.Avalonia.Theme` transitively; no project references it directly. `SystemColors` static resource keys from that assembly are still used for color lookups and do not crash.
- **Anything compiled against Avalonia 11 fails only at render time.** That whole class of bug builds cleanly, passes view-model tests, and then kills the process the first time the control is painted. `HexIDE.Integration.Tests/Controls/ClassicRenderTests.cs` is the guard — it renders the affected controls for real under Skia (`UseHeadlessDrawing = false`) and asserts a frame came back. Add a case there before trusting any new `Classic.*` surface.
- **`Classic.CommonControls.Dialogs` is fully removed** — `MessageBoxResult`, `MessageBoxButtons`, `MessageBoxIcon` are now in `HexIDE.Core/IDE/MessageBoxEnums.cs` (namespace `HexIDE.IDE`). `FontDialogResult`/`AboutDialogOptions` are in `HexIDE/IDE/`. Managed dialog controls (`MessageBox`, `InputBox`) are in `HexIDE.Runtime/Dialogs/`. `AboutDialog`/`FontDialog` views and their ViewModels are in `HexIDE/Forms/Views/` and `HexIDE/Forms/ViewModels/`. `WindowManager` routes all paths (SingleView + desktop) through these managed controls.
- **All projects, tests included**: `<Nullable>enable</Nullable>` + `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`. Do not introduce nullable warnings.
  This line claimed "all projects" from the first commit while no **test** project actually set it, and the gap had a cost: 22 `xUnit1051` warnings sat unread in `HexIDE.VbLspServer.Tests` because nothing failed on them. The four test projects were brought to zero warnings and the setting turned on, so the statement is now true rather than aspirational. To keep a warning deliberately, suppress it **at the site with a written reason** (see the two `Task.Delay` timeout arms in `SilentServerTests` and `DynamicRegistrationProbe`) rather than adding a project-wide `NoWarn`.
- `ThemeVariantScope` on the form canvas (`FormEditView.axaml`) **must never be removed** — it locks the form designer canvas to `Light` theme so form preview colours are stable regardless of OS dark/light mode.

## Global Usings

`IDE/Directory.Build.props` provides `System.ComponentModel` to all projects. Per-project `GlobalUsings.cs` files add frequently-used internal namespaces.

**Good candidates** (add these without asking): `System.*` namespaces not already covered by `ImplicitUsings=enable`, `Microsoft.Extensions.*`, `HexIDE.*` namespaces used across many files in a project.

**Bad candidates** (do not add as global usings): third-party namespaces that only apply in limited contexts — e.g. `Avalonia.*`, `Serilog`, `CommunityToolkit.Mvvm.*`, `PropertyChanged.SourceGenerator`, `Dock.*`. These should remain explicit at the use site.

When a new `HexIDE.*` namespace becomes widely used in a project, add it to that project's `GlobalUsings.cs`. When adding a new service namespace across the whole solution, consider `Directory.Build.props` instead.

## Key Conventions

### AXAML

- **Always use compiled bindings** — `AvaloniaUseCompiledBindingsByDefault=true` is set project-wide. Set `x:DataType` on every view root. Use `{CompiledBinding}`, not `{Binding}`.
- **Never use literal colour values** in IDE chrome AXAML. Use `{DynamicResource KeyName}` from `Themes/Classic.axaml` or `{DynamicResource {x:Static commonControls:SystemColors.SomeBrushKey}}` (xmlns prefix: `Classic.CommonControls` assembly). Only exception: `Transparent`.
- `ClassicBorderDecorator` (from `Classic.Avalonia.Theme` namespace, `Classic.Avalonia.Theme` assembly) for VB6-style borders. Keyboard commands via `AvaloniaLabs.CommandManager`.
- Control themes: `<ControlTheme x:Key="{x:Type MyControl}" TargetType="MyControl">` inside a `<ResourceDictionary>`.

### Theming

- `Themes/Classic.axaml` is the single source of all IDE-specific colour keys. New IDE chrome AXAML must reference keys from there.
- IDE chrome uses `SimpleTheme` (`Avalonia.Themes.Simple`). The Win98 chrome layer has been removed. `ThemeService` sets the app's `RequestedThemeVariant` **deterministically** — it must never be `ThemeVariant.Default`, which follows the host OS and leaves chrome stuck dark on a dark-mode OS (menu, toolbox, dock headers, status bar) while the pinned `Classic.axaml` keys stay light. Classic → `ThemeVariant.Light`; theme packs → `Dark` if the pack declares `themeVariant: "Dark"`, otherwise `Light`.

### LSP JSON serialization

- Always use `SystemTextJsonFormatter` + `LspJsonContext`. **Never use `new {}` anonymous types** in `NotifyAsync`/`InvokeAsync` — use `EmptyParams.Instance` instead. AOT has no metadata for anonymous types.
- All `TextDocument` and marker access must be on the UI thread (`Dispatcher.UIThread.Post`).

### Adding a new LSP method (5-step recipe)

1. **Server handler** in `LspServer/HexIDE.VbLspServer/LspServer.cs` — add `case "textDocument/yourMethod":` and `HandleYourMethod(idNode, paramsNode)`.
2. **Server capability** in `BuildInitializeResult()` in the same file.
3. **Message types** in `IDE/HexIDE.Core/Lsp/Messages/LspMessages.cs` — `record` types with `[JsonPropertyName]` on every property. (`HexIDE.Lsp/Messages/` holds only the serializer context; the records are in Core.)
4. **Serializer registration** in `IDE/HexIDE.Lsp/Messages/LspJsonContext.cs` — `[JsonSerializable(typeof(YourResponseType))]`. **Not optional, and not only an AOT concern** — `VBLspClient` builds its `JsonSerializerOptions` *from* this context, whose generated resolver ends in `return null` with nothing chained behind it, so an unregistered type throws under plain JIT too. The throw lands in a debug-level catch, which turns the omission into a server that connects, initializes and then answers nothing — indistinguishable from a broken server, and it has cost real time twice.
5. **Client** in `IDE/HexIDE.Lsp/ILspClient.cs` + `VBLspClient.cs` — add interface method, implement with `_rpc.InvokeWithParameterObjectAsync<T>(...)`.

### Adding a new VB6 control

1. `VBXxx : SomeAvaloniaControl` in `BuiltinControls/`. Override `StyleKeyOverride`. Wire events via `AttachedEvents`.
2. `XxxComponentClass : ComponentBaseClass` in `Components/`. Declare props/events, implement `InstantiateInternal`, singleton `Instance`, set `Name` (toolbox) and `VBTypeName` (e.g. `"VB.TextBox"`). Per-component default overrides via `PropertyClass<T>.OverrideDefault<TComponent>()`.
3. Register in toolbox and deserializer mappings.

### Localization (user-facing strings)

The IDE chrome is fully localized (system spec: `openspec/specs/localization/spec.md`; the shipped language
packs: `openspec/specs/language-packs/spec.md`; catalog: `docs/localization-regions.md`). **Any new
user-facing string is a localization key, never a hardcoded literal.**

1. **Add the key at the use site:** AXAML → `{DynamicResource Str.Area.Element}`; user-facing C# →
   `ILocalizationService.GetString("Str.Area.Element")` (use a `{0}` placeholder + `string.Format` for
   interpolation). Keys are `Str.{Area}.{Element}` (PascalCase, dot-separated).
2. **Add the key + English value to the canonical pack** `IDE/HexIDE/Localization/Packs/en.json` — the
   single source of truth (US-spelled neutral English; renamed from `en-US.json` in P17). `LocalizationCoverageTests`
   **fails the build** if an AXAML `Str.*` key (or a VB6 property's `Str.PropDesc.*`) is missing from `en`.
   **C# `GetString` keys are NOT auto-checked — add them to `en` by hand**, or the call renders the raw key.
   New VB6 property ⇒ add its `Str.PropDesc.{name}`.
3. **Translate every new key into all shipped packs in the same change — don't defer.** The moment you add a
   `Str.*` key to `en`, add its translation to each shipped full-translation pack (the supported set:
   `ar, cs, da, de, el, eo, es, fa, fi, fr, he, hi, id, it, ja, ko, la, nb, nl, pl, pt, ru, sv, tr, uk, ur,
   vi, zh-Hans, zh-Hant` — **29**) so non-English IDEs never show English fall-through. A missing key *inherits* English
   (no blank control), but that drift must not ship — close it at the point of creation. For more than a
   couple of keys, use the language-packs workflow (one agent per pack: translate the new keys,
   **preserving `{0}`/`{1}` placeholders and each pack's mnemonic convention** — `_` kept for Latin scripts,
   omitted for non-Latin). `en-GB` is a thin variant — add a key there only where British English differs
   from `en`.
4. **Confirm zero drift before committing** with the coverage tool (lists any full translation still missing
   keys; a clean run = every pack at parity with `en`):
   ```sh
   cd tools/TranslationCoverage && dotnet run
   ```
**The shipped set is closed, and `LanguagePack.cs` is its single source of truth.** This list, that file and
the coverage tool must agree; they did not for a while, which is how `la` and `eo` came to be translated in
every pass without anyone having decided they were shipped.

**No more languages "for fun" — the bar is whether a real person would pick it, not whether it is a real
language.** `la` (Latin) and `eo` (Esperanto) stay, as a recorded decision rather than an accident: Latin is
the Holy See's official language, Esperanto has a genuine localisation community, and both are already
complete. Nothing further of that kind is added — Klingon, Na'vi, Tolkien's languages and their relatives are
refused on request, and this line is the maintainer's own standing instruction to refuse them.

The reason is cost, not taste. Every pack is a permanent tax on every new key: adding two keys today cost 58
translations, and the guarantee that makes this system worth anything is that **every shipped pack is 100%
complete, enforced at build**. A pack nobody selects still has to be kept complete forever, or the guarantee
weakens for the packs that people do use. A legitimacy test ("is it a real language of a real state") gets
this backwards — it admits Latin, which nobody will select, and excludes Esperanto, which someone might.

5. **Verify** nothing was missed: switch to **Pseudo (LTR)** in Options → Language — any plain-English
   (un-`⟦bracketed⟧`) chrome is a string you forgot to key.

### Testing (assertions & mocks — all projects)

- **Always assert with AwesomeAssertions** (`value.Should().Be(...)`, `.Should().BeNull()`,
  `.Should().BeTrue()`, `.Should().Contain(...)`, `.Should().Throw<T>()`, etc.). **Never use xUnit
  `Assert.*`** (`Assert.Equal`/`Assert.True`/`Assert.Null`/…). xUnit provides the test framework
  (`[Fact]`/`[Theory]`) and runner only — the assertions are AwesomeAssertions. `AwesomeAssertions` and
  `Xunit` are global `<Using>`s in every test `.csproj`, so no per-file `using` is needed.
- **Always mock with NSubstitute** (`Substitute.For<IFoo>()`, `.Returns(...)`, `.Received()`). Do not add
  Moq or any other mocking library. NSubstitute is referenced + globally imported in `HexIDE.Tests` and
  `HexIDE.Integration.Tests` (the projects that need mocks); add the `PackageReference` + `<Using>` if a
  new test project needs it.

### Testing (Runtime)

Inherit `BaseVBTestFixture`. Call `await Run("VB6 code")`. Assert via `AssertDebugLog(expectedValues)`. Type suffixes: `42!` = Single, `42#` = Double.

**Testing internals:** both `HexIDE` and `HexIDE.Runtime` expose their `internal` members to the test projects
(`HexIDE.Runtime.Tests`, `HexIDE.Tests`, `HexIDE.Integration.Tests`) via `<InternalsVisibleTo>` in each `.csproj`.
So a test may call an `internal` type/method directly (e.g. `RuntimeExtensions.ExecuteSub`) — prefer that over
widening visibility to `public` just for a test. When a new test project needs runtime internals, add it to the
`InternalsVisibleTo` ItemGroup in the relevant `.csproj`.

### Testing (LSP server)

- Unit tests: call `VbDiagnosticsProvider.GetDiagnostics(code)` directly.
- Protocol tests: use `System.IO.Pipelines.Pipe` pairs to drive `LspServer` in-process.
- Test inputs must be **module-level VBA** (e.g., `Sub Foo()\nEnd Sub`) — bare statements are invalid at `startRule`.

### Gotchas

- **`Vb6Value`** is a value type. Always check `Type` before accessing `Value` (it's `object?`).
- **`ICSharpProxy`**: controls exposed to VB6 implement this; interpreter calls `proxy.Call(methodName, args)`.
- **LanguageExt** (in `HexIDE.Runtime`): shadows `System.Linq` — `Where`, `Select`, `Map`. Prefer explicit `foreach` over LINQ when `using LanguageExt;` is in scope.
- **Grammar instability**: the VB6 grammar can produce degenerate parse trees on malformed input. ANTLR visitors traversing expressions need a depth guard (`MaxDepth = 500` in an overridden `Visit()`) and must return early from leaf-node overrides (never recurse into children from a leaf-node visit).
- **Avoid double-parse**: use `VbDiagnosticsProvider.GetDiagnosticsAndTree(source)` when you need both diagnostics and the parse tree.
- **Server capabilities are `JsonElement?`, never `bool?` or `int?`.** Most are `boolean | XxxOptions` in the protocol and a conformant server may send either; modelling one narrowly threw during `initialize`, and the swallowing catch turned that into a total silent LSP blackout (#238). `TextDocumentSync` needs the same treatment for a different reason — the bundled server returns it as an object, `{"openClose":true,"change":1}` (Full) — and it is the one field whose two shapes mean *different* things, so it has its own reader rather than going through `IsEnabled`.
- **`CS0108` suppressed** in `HexIDE.VbLspServer.csproj` — expected artifact from the generated `VisualBasic6Parser.cs` (the ANTLR parser hides an inherited member).
- **ANTLR generated-code naming**: `Antlr4BuildTasks` generates into the **global namespace** (no `namespace` in generated files; no `using` alias needed) and does **not** escape C# keywords the way other ANTLR targets do — e.g. the grammar rule `type` generates a C# method `type()`, not `type_()`. Check the generated code under `obj/` if unsure of a method name.
- **Two-stage parse**: `VbDiagnosticsProvider` parses SLL-first with `BailErrorStrategy`, then falls back to LL on `ParseCanceledException` (ANTLR's official perf pattern). SLL-only mispredicts VB6's call-vs-array ambiguity (`Foo(1)`) on valid code — never ship SLL-only. A wall-clock backstop (`TryGetDiagnosticsAndTreeWithin`, `ParseBudget` = 2s) abandons pathological parses and keeps prior diagnostics.
- **`IReadOnlyList<T>`** has no `.Find()`. Use `Array.Find(array, predicate)` or a `foreach` loop.
- **Never use `System.IO.Path` on a path that came out of, or is going into, a VB6 file.** `.vbp`/`.vbg`/`.frm` are Windows-native formats: their paths are backslash-separated on every host, while `System.IO.Path` answers about the *host* filesystem. **`build-ide` runs on `ubuntu-latest`**, where a backslash is an ordinary filename character — so `Path.GetFileName(@"docs\README.md")` returns the whole string, and `Path.GetRelativePath` emits `docs/README.md` into a file that must say `docs\README.md`. Both are silent (each yields something that still looks like a path) and **both are invisible on a Windows dev box**. Use `SerializedProject.FileNameOf` / `.ToHostPath` / `.ToProjectFilePath`; convert to host separators only where a path is resolved against the filesystem, never for what gets written back.
- **A test expectation about VB6-file *content* must be a literal backslashed string, never composed with `Path.Combine`.** An expectation built from a host path API follows whichever machine runs it, so it passes on Windows by accident and on Linux *certifies the bug*. Three `SerializationRoundTripTests` cases did exactly this — one of them named `..._PreservedVerbatim` — and read as cover for years. Test **inputs** are different: those are real filesystem paths and are rightly built with `Path.Combine`. That distinction is the whole rule.
- **MVVM**: use `[Notify]` (PropertyChanged.SourceGenerator) for `INotifyPropertyChanged` properties.
- **`DrawingContext.DrawImage(image, destRect)` samples the source's *device-independent* extent, not its pixels.** Composing a `RenderTargetBitmap` rendered at `96 * scaling` dpi into another therefore reads only the top-left `1/scaling` of it and stretches that to fill — on a 150% display, correctly placed and sized output with magnified, clipped contents inside. Use the three-argument overload with an explicit **pixel** source rect. Related: render a visual with `RenderTargetBitmap.Render`, which handles scaling correctly, rather than routing it through a drawing context. Both traps are invisible at 100% scaling and invisible headlessly. See `SnapshotComposer`.
- **Avalonia 12 breaking changes** (already migrated, for reference): `GotFocusEventArgs` → `FocusChangedEventArgs`; `CaptionButtons` (chrome control) removed — replaced with custom `MDICaptionButtons : TemplatedControl`; `GetVisualRoot()` → `TopLevel.GetTopLevel(this)`; `RenderOptions.SetTextRenderingMode` → `TextOptions.SetTextRenderingMode`; `RenderOptions.TextRenderingMode="Alias"` in AXAML → `TextOptions.TextRenderingMode="Alias"`; `<CompiledBinding Path="X" />` inside `MultiBinding` → `<Binding Path="X" />`.
- **Test frameworks are mid-migration** ([#297](https://github.com/hexide-io/HexIDE/issues/297)).
  All four test projects are on **xunit v3** and the **MTP** runner — the VSTest/MTP split is closed.
  `Microsoft.NET.Test.Sdk`, `xunit.runner.visualstudio` and `coverlet.collector` are gone from all of
  them; a v3 project under MTP needs `<OutputType>Exe</OutputType>` and
  `<TestingPlatformDotnetTestSupport>true</TestingPlatformDotnetTestSupport>` and nothing else. (While
  `Microsoft.NET.Test.Sdk` was still present it supplied the executable output itself, so the explicit
  `OutputType` only became necessary when that package was removed.)
- **A custom `FactAttribute` must never throw from its constructor.** xunit v2 reported that as a test
  failure; **v3 discards the test at discovery**, so a guard written that way fails *open*. This bit
  `HEXIDE_REQUIRE_FOREIGN_LSP`, whose entire purpose is to stop the foreign-server proof disappearing:
  with servers absent the suite went green at 978 instead of red, fourteen tests simply not there.
  Enforcement now lives in `RequiredServersTests` as ordinary `[Fact]`s, which are discovered whatever
  the environment. Put availability checks in a test, not in an attribute. A v3 project needs no
  `OutputType` change — `xunit.v3.core` makes it an executable itself.
- **`xunit.v3` is capped at `[3.2.2,4.0.0)` and that is deliberate.** 4.0.0 (still "Core Framework v3" — the
  package version and the framework generation are decoupled) breaks `Avalonia.Headless.XUnit` with a
  `MissingMethodException` at test **discovery**: no build error, no restore warning. The package's own
  dependency is an open `>= 3.2.2`, so a routine bump resolves it happily. See the comment in
  `Directory.Packages.props`.
- **`--filter` is silently ignored. All four projects are on MTP.** Passing VSTest's
  `--filter "FullyQualifiedName~Foo"` does not error — it runs **every** test in the project, so a run you
  believe was scoped to one class was actually the whole suite. Measured: 153 tests instead of 27.
  — Use `-- --filter-class "*Name"`, `-- --filter-method "*Name"`, or `-- --filter-query "/*/*/Class/*"`.
  The `--` matters: everything after it goes to the test executable rather than to `dotnet test`.
  — A filter matching **nothing** fails the run rather than passing vacuously, which is an improvement
  on VSTest and worth relying on: "0 tests, green" cannot happen by typo.

## Living Documents

> **Two of these are maintainers' documents held outside the public repository** (`docs/private/`, pruned
> from the public copy). If your clone has no `docs/private/`, that is expected — skip the
> two entries below and ignore any instruction to update them. Nothing an outside contributor needs is in
> there: the design record they work against is `openspec/`, which ships in full.

- **`docs/private/ROADMAP.md`** *(maintainers)* — completed phases, design decisions, accepted/rejected ideas. Keep updated when phases complete or architectural decisions are made.
- **`docs/private/EVOLUTION.md`** *(maintainers)* — Evolution-tier modernisation catalog: Remove/Keep/Change/Add tables with effort + persona-value ratings, the muscle-memory keep-list, and suggested waves. New Evolution work starts from this catalog; update rows as modernisation work lands.
- **`docs/lsp-client.md`** and **`docs/lsp-server-features.md`** — the LSP pair, split along the seam
  because the two halves have different bounds. The **client** doc is what HexIDE speaks to *any*
  server: wire contract, capability gating, sync model, routing, and the client's own gaps (#282,
  #284). The **server** doc is what the bundled VB6 server analyses, with every limitation marked ◐
  *depth* (real outstanding work) or ■ *boundary* (the CST-not-AST limit). **Keep that mark honest** —
  the single doc these replaced listed six binding-dependent features as Planned/Future, which the
  hard limit had already ruled out, and it shipped publicly that way for months.
- **`docs/foreign-language-servers.md`** — the third-party servers the suite drives, why each earns its
  place, how they are obtained, and why a GPL-licensed one is consistent with a 100%-MIT tree. Read it
  before adding a third: the bar is a protocol *shape* nothing else exercises, not another server.
- **`docs/language-servers.md`** — how a user attaches a language server: where `lsp-servers.json` lives,
  what its fields mean, and why `extensions` and `languageId` are not the same question. The only
  user-facing account of that file — the openspec specs describe the behaviour as contracts, which is not
  the same thing and is not where someone configuring the IDE will look.
- **The backlog lives in [GitHub Issues](https://github.com/hexide-io/HexIDE/issues)**, not in a file. `docs/TODO.md` was retired on 2026-08-17 and its actionable items opened as issues, so a contributor can find work without reading the repository. Note it down as an issue, not as a checklist entry.
- **`openspec/`** — design records in [OpenSpec](https://github.com/Fission-AI/OpenSpec) format (CLI: `openspec`). **There is no status field anywhere — position in the tree *is* the status.**
  - `specs/{capability}/spec.md` — how the system behaves **today**, as present-tense `### Requirement:` / `#### Scenario:` pairs. RFC 2119 keywords (SHALL/MUST) must appear in the requirement **body**, not only its heading, or `--strict` warns.
  - `changes/{change-id}/` — work in flight: `proposal.md`, optional `design.md`, `tasks.md`, and spec deltas under `specs/{capability}/spec.md` (delta files start with `## ADDED Requirements` and carry **no H1**).
  - `changes/archive/` — completed changes. **Get there via `openspec archive {id} -y`, never by hand** — the archive is not scanned for deltas, so a hand-placed change silently never merges into `specs/`. After archiving, **check `## Purpose` on each spec the archive touched, and rewrite it if the CLI replaced it with a `TBD` placeholder.** It has done that historically; on **openspec 1.11.0 it did not** (verified archiving `2026-09-04-carry-files-a-project-does-not-compile`, which touched two specs and left both Purposes intact, changing only blank lines around `## Requirements`). Diff the file rather than assuming either way — restoring a Purpose that was never lost is its own way to lose one.
  **This is now guarded, because it was the last drift-prone artefact here trusted to a habit.**
  `DesignRecordTests` fails the build when a change has every task ticked and is still sitting in
  `changes/`, and when an archived change's requirements are absent from the capability it targeted.
  It was written after five changes were found unarchived at once, three of them `MODIFIED` deltas
  whose requirement headings existed in `specs/` while their bodies were weeks out of date — which is
  why a survey by heading reported those three as tidying and archiving them rewrote sixty-eight lines.
  - Spec-authoring and change-workflow rules live in `openspec/config.yaml` under `rules:`. Notably: where the code contradicts a spec because the *code* is defective, write the **intended** behaviour as the requirement, open an issue, and link it from a note under `## Purpose` — a spec matched to a known bug enshrines the bug (`specs/object-browser` is the worked example).
  - `openspec/` **ships publicly**. The 35 pre-OpenSpec design documents that used to sit in `specs-pending/` were all migrated (2026-08-16) and the folder was deleted; migrating each one is also what sanitised it, since rewriting a plan as behaviour contracts drops the third-party names. Anything landing here now is in OpenSpec format from the start.
- **`docs/MISSING_FEATURES.md`** — full VB6 IDE fidelity catalog; status assessed against the codebase. **Update this file whenever a feature's status changes** — after any implementation phase, scan the relevant rows and update Status/Notes to reflect the new state.
- **`docs/debugger-vb6-divergences.md`** — living catalogue of where the interpreter debugger + Edit-and-Continue affordance knowingly diverge from real VB6. **Add a row the moment a divergence is found** — during implementation, review, or live verification — so it's tracked, not forgotten. (Sibling to `interpreter-gaps.md` = runtime language gaps, `vb6-fidelity-oracle.md` = verified runtime semantics, `MISSING_LANGUAGE.md` = the positive language-coverage inventory.)
- **`docs/MISSING_LANGUAGE.md`** — the full VB6 language surface (statements, functions, operators, keywords, literals, directives, constants, in-box objects) with HexIDE's support level for each, ordered by **F5 impact**: won't load / dies mid-run / runs-but-differs / faithful. **Update the row in the same change that changes the status.** A coverage document that drifts is worse than none, because it gets quoted rather than checked. It owns *what runs*; `interpreter-gaps.md` owns *why something is missing*.

## Git Workflow

**Commit and push unprompted** when a significant piece of work completes (a spec migration, a feature phase, a bug fix, a doc housekeeping pass, etc.), unless there is an open question that warrants a manual check first. If you still need the user to verify something before the work is considered stable, ask before committing. Do not wait to be asked when the work is clearly done and self-contained.

When in doubt about whether a piece of work is "complete enough", err on the side of committing — a WIP commit is easy to amend or squash, but uncommitted work can be lost.

## Planning Sessions

Every `/plan` session that results in an approved implementation plan **must** also add a new phase entry to `docs/private/ROADMAP.md` before implementation begins — *if you have it*. Working from a public clone, record the same content in the change's `proposal.md` under `openspec/changes/` instead; the roadmap entry is the maintainers' mirror of it, not a second source of truth. The roadmap entry should be written at the end of plan mode (before `ExitPlanMode`) and must include:

- A short phase title and one-sentence summary
- The motivation (which tier it serves — Fidelity / Evolution / Abstraction)
- Key implementation decisions made during planning
- Any approaches that were considered and rejected (with reason)

This keeps the roadmap as an accurate record of architectural decisions, not just a list of completed features.

## Implementing Phased Features

Where work is driven by a `tasks.md` under `openspec/changes/`, always implement **one phase (or one numbered task group) at a time**:

1. Before starting, identify the next incomplete phase — the first whose functionality is not yet in the codebase. Trust the code over the document where they disagree.
2. Implement only that phase — do not begin the next one even if it looks small.
3. After the phase is complete and the build is green, **stop and report** what was done, what remains, and what the next phase will involve. Do not proceed further until the user asks you to continue.
4. For a `changes/` item, tick its `tasks.md` boxes as you go. When every task is done, `openspec validate {id} --type change --strict`, then `openspec archive {id} -y`, then rewrite the merged spec's `## Purpose`.

If the user says "continue" or "begin the next phase", start step 1 again for the next incomplete phase.
