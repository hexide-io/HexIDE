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
cd IDE && dotnet test HexIDE.Tests/                      # IDE ViewModel tests
cd IDE && dotnet test HexIDE.Integration.Tests/          # Headless Avalonia UI tests (xunit v3/MTP)

# Run a single test
cd IDE && dotnet test HexIDE.Runtime.Tests/ --filter "FullyQualifiedName~MiscTests.DebugPrint_WithStringLiteral_ShouldOutputCorrectString"
cd LspServer && dotnet test HexIDE.VbLspServer.Tests/ --filter "FullyQualifiedName~WireContractTests"

# Publish (both needed for the "Make EXE" IDE feature)
cd IDE && dotnet publish HexIDE.Desktop -f net10.0 -o bin/
cd IDE && dotnet publish HexIDE.Standalone -f net10.0 -o bin/standalone/

# Release publish (AOT/trimmed removed — add-in system uses Assembly.Load which is incompatible with AOT)
cd IDE && dotnet publish HexIDE.Desktop -f net10.0 -o bin/

# LSP server
cd LspServer && dotnet build HexIDE.VbLspServer/
```

CI runs two parallel jobs in `.github/workflows/build.yml`: `build-ide` (working dir `IDE/`) and `build-lsp-server` (working dir `LspServer/`).

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

**MCP schema changes require a session restart.** If new tools were implemented mid-session, you MUST stop and ask for a session resume — do not attempt raw HTTP workarounds.

HexIDE exposes an embedded MCP server (opt-in via `--server-port <port>`). **The MCP server is a dev/automation tool and is `#if DEBUG`-compiled out of Release builds** — the `Server/` folder and the AspNetCore framework dependency are excluded from Release, so a distributed binary opens no port and `--server-port` is inert. The dev loop below uses Debug builds, so this does not affect it. With HexIDE running (Debug), Claude Code connects automatically via `.mcp.json` at the repo root and has access to these tools:

| Tool | Description |
|------|-------------|
| `get_project_info` | Current project name, path, forms, modules |
| `get_open_editors` | Open editor windows and active window |
| `get_document_tabs` | List all open editor/designer tabs with title, type (`code`/`designer`), and active flag |
| `activate_document_tab(title)` | Make a tab active by title |
| `close_document_tab(title)` | Close a tab by title |
| `get_diagnostics` | LSP errors and warnings |
| `open_file(name)` | Open a form or module in the code editor (creates or activates tab) |
| `view_designer(name)` | Open a form or UserControl in the visual designer (creates or activates tab; use before `take_snapshot`) |
| `run_project` | Start the VB6 runtime |
| `stop_project` | Stop the running project |
| `shutdown_ide` | Clean shutdown (triggers all shutdown handlers) |
| `take_snapshot` | Capture the IDE window as PNG; returns temp file path — read it with the `Read` tool to view |
| `dump_visual_tree(root?, maxDepth?, interactiveOnly?)` | Walk the active window's **control-view** tree (structural wrappers collapsed; a visible modal dialog is preferred). Each node carries an addressable `path`, automation ControlType, Name/AutomationId, DataContext VM type, and supported interaction providers. The discovery entry point. |
| `inspect_element(target)` | Deep-inspect one control by `path`: supported providers, bounds, current value/selection/toggle state, and the DataContext VM's command/property members (the surface `interact`'s reflection actions target). |
| `interact(target, action, value?)` | Drive a control. Provider actions: `invoke`/`select`/`set_value`/`toggle`/`expand`/`collapse`. Reflection actions (DataContext VM): `invoke_command`/`set_property`. The generic substitute for per-interaction tools. |

**CLI flags** (both `--` and `/` prefixes accepted, aligning with VB6 convention):
- `--server-port <port>` — enable the MCP server on the given port (all launch profiles use 5123)
- `--newproject` — skip the startup dialog and create a default Standard EXE project
- Positional `.vbp` path — skip the startup dialog and open that project

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

**MCP session note:** MCP tools are discovered at session start. If HexIDE is not running when a Claude Code session starts, the tools will not appear. After relaunching HexIDE mid-session, existing tool schemas remain usable (Streamable HTTP is stateless — each call is a fresh POST). Starting a new session with HexIDE already running picks up any newly added tools.

## Architecture

### Monorepo structure

| Folder | License | Purpose |
|--------|---------|---------|
| `IDE/` | MIT | IDE application (14 projects) |
| `LspServer/` | MIT | Out-of-process VB6/VBA LSP server (EmmyLua shell + proleap grammar) |
| `HexIDE.slnx` | — | Master solution (Visual Studio 2022+) |
| `.github/` | — | CI workflows |
| `docs/` | — | Engineering docs — MISSING_FEATURES.md, LSP_FEATURES.md, the fidelity oracle, the gap catalogues. **Ships publicly.** |
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
- **MVVM**: `ViewLocator` resolves View from ViewModel by convention (strips `ViewModel` suffix).

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

- **Avalonia 12.0.4 / Dock 12.0.0.2** — the project is on Avalonia 12. `Classic.Avalonia.Theme 12.0.1-beta1` is kept as a **controls-only** dependency (provides `ClassicBorderDecorator`, `ClassicBorderStyle`). **Do not pin it back to 11.3.0.3**: that build was compiled against Avalonia 11, so `ClassicBorderDecorator.DrawRadioButtonBorder` called a `StreamGeometryContext.ArcTo` overload Avalonia 12 replaced, and **every VB6 option button killed the process on render** (`MissingMethodException`, thrown on the render thread — uncatchable, and nothing reaches the Serilog log; the stack only exists in the Windows Application event log). It is a prerelease because it is the only Avalonia-12 build published. `<ClassicTheme />` remains **NOT loaded** and this change does not re-open that question — do not add it back. The `Classic.Avalonia.Theme.Dock`, `.ColorPicker`, and `.DataGrid` sub-packages have been removed. `Avalonia.Themes.Simple 12.0.4` is the base theme (`<SimpleTheme />` in App.axaml).
- **`Classic.CommonControls.Avalonia 12.0.1-beta1` controls** (`ToolBar`, `ToolBarButton`, `RebarHandle`, `ListView`, `ListViewItem`) are **NOT used** — the `TypeLoadException: PseudolassesExtensions` applied to the 11.x build, but they stay unused by design (VB6 chrome removal is on the Evolution path). Use standard Avalonia `StackPanel`/`Button`/`ToggleButton`/`ListBox` instead. The version follows `Classic.Avalonia.Theme` transitively; no project references it directly. `SystemColors` static resource keys from that assembly are still used for color lookups and do not crash.
- **Anything compiled against Avalonia 11 fails only at render time.** That whole class of bug builds cleanly, passes view-model tests, and then kills the process the first time the control is painted. `HexIDE.Integration.Tests/Controls/ClassicRenderTests.cs` is the guard — it renders the affected controls for real under Skia (`UseHeadlessDrawing = false`) and asserts a frame came back. Add a case there before trusting any new `Classic.*` surface.
- **`Classic.CommonControls.Dialogs` is fully removed** — `MessageBoxResult`, `MessageBoxButtons`, `MessageBoxIcon` are now in `HexIDE.Core/IDE/MessageBoxEnums.cs` (namespace `HexIDE.IDE`). `FontDialogResult`/`AboutDialogOptions` are in `HexIDE/IDE/`. Managed dialog controls (`MessageBox`, `InputBox`) are in `HexIDE.Runtime/Dialogs/`. `AboutDialog`/`FontDialog` views and their ViewModels are in `HexIDE/Forms/Views/` and `HexIDE/Forms/ViewModels/`. `WindowManager` routes all paths (SingleView + desktop) through these managed controls.
- **All projects**: `<Nullable>enable</Nullable>` + `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`. Do not introduce nullable warnings.
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
3. **Message types** in `IDE/HexIDE.Lsp/Messages/LspMessages.cs` — `record` types with `[JsonPropertyName]` on every property.
4. **AOT registration** in `IDE/HexIDE.Lsp/Messages/LspJsonContext.cs` — `[JsonSerializable(typeof(YourResponseType))]`. Forgetting this causes silent failures under AOT.
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
   `ar, cs, da, de, el, es, fa, fi, fr, he, hi, id, it, ja, ko, nb, nl, pl, pt, ru, sv, tr, uk, ur, vi,
   zh-Hans, zh-Hant`) so non-English IDEs never show English fall-through. A missing key *inherits* English
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
- **`ServerCapabilities.TextDocumentSync`** is `JsonElement?`, not `int?` — the server returns it as an object `{"openClose":true,"change":2}`. Deserializing as `int?` fails silently under AOT.
- **`CS0108` suppressed** in `HexIDE.VbLspServer.csproj` — expected artifact from the generated `VisualBasic6Parser.cs` (the ANTLR parser hides an inherited member).
- **ANTLR generated-code naming**: `Antlr4BuildTasks` generates into the **global namespace** (no `namespace` in generated files; no `using` alias needed) and does **not** escape C# keywords the way other ANTLR targets do — e.g. the grammar rule `type` generates a C# method `type()`, not `type_()`. Check the generated code under `obj/` if unsure of a method name.
- **Two-stage parse**: `VbDiagnosticsProvider` parses SLL-first with `BailErrorStrategy`, then falls back to LL on `ParseCanceledException` (ANTLR's official perf pattern). SLL-only mispredicts VB6's call-vs-array ambiguity (`Foo(1)`) on valid code — never ship SLL-only. A wall-clock backstop (`TryGetDiagnosticsAndTreeWithin`, `ParseBudget` = 2s) abandons pathological parses and keeps prior diagnostics.
- **`IReadOnlyList<T>`** has no `.Find()`. Use `Array.Find(array, predicate)` or a `foreach` loop.
- **MVVM**: use `[Notify]` (PropertyChanged.SourceGenerator) for `INotifyPropertyChanged` properties.
- **`DrawingContext.DrawImage(image, destRect)` samples the source's *device-independent* extent, not its pixels.** Composing a `RenderTargetBitmap` rendered at `96 * scaling` dpi into another therefore reads only the top-left `1/scaling` of it and stretches that to fill — on a 150% display, correctly placed and sized output with magnified, clipped contents inside. Use the three-argument overload with an explicit **pixel** source rect. Related: render a visual with `RenderTargetBitmap.Render`, which handles scaling correctly, rather than routing it through a drawing context. Both traps are invisible at 100% scaling and invisible headlessly. See `SnapshotComposer`.
- **Avalonia 12 breaking changes** (already migrated, for reference): `GotFocusEventArgs` → `FocusChangedEventArgs`; `CaptionButtons` (chrome control) removed — replaced with custom `MDICaptionButtons : TemplatedControl`; `GetVisualRoot()` → `TopLevel.GetTopLevel(this)`; `RenderOptions.SetTextRenderingMode` → `TextOptions.SetTextRenderingMode`; `RenderOptions.TextRenderingMode="Alias"` in AXAML → `TextOptions.TextRenderingMode="Alias"`; `<CompiledBinding Path="X" />` inside `MultiBinding` → `<Binding Path="X" />`.
- **Integration test project** (`HexIDE.Integration.Tests`) uses xunit v3 (`xunit.v3 3.2.0`) with MTP runner (`TestingPlatformDotnetTestSupport=true`). Run with `dotnet test` or `dotnet run`. No `Microsoft.NET.Test.Sdk` needed.

## Living Documents

> **Two of these are maintainers' documents held outside the public repository** (`docs/private/`, pruned
> from the public copy). If your clone has no `docs/private/`, that is expected — skip the
> two entries below and ignore any instruction to update them. Nothing an outside contributor needs is in
> there: the design record they work against is `openspec/`, which ships in full.

- **`docs/private/ROADMAP.md`** *(maintainers)* — completed phases, design decisions, accepted/rejected ideas. Keep updated when phases complete or architectural decisions are made.
- **`docs/private/EVOLUTION.md`** *(maintainers)* — Evolution-tier modernisation catalog: Remove/Keep/Change/Add tables with effort + persona-value ratings, the muscle-memory keep-list, and suggested waves. New Evolution work starts from this catalog; update rows as modernisation work lands.
- **`docs/LSP_FEATURES.md`** — LSP capability mapping.
- **The backlog lives in [GitHub Issues](https://github.com/hexide-io/HexIDE/issues)**, not in a file. `docs/TODO.md` was retired on 2026-08-17 and its actionable items opened as issues, so a contributor can find work without reading the repository. Note it down as an issue, not as a checklist entry.
- **`openspec/`** — design records in [OpenSpec](https://github.com/Fission-AI/OpenSpec) format (CLI: `openspec`). **There is no status field anywhere — position in the tree *is* the status.**
  - `specs/{capability}/spec.md` — how the system behaves **today**, as present-tense `### Requirement:` / `#### Scenario:` pairs. RFC 2119 keywords (SHALL/MUST) must appear in the requirement **body**, not only its heading, or `--strict` warns.
  - `changes/{change-id}/` — work in flight: `proposal.md`, optional `design.md`, `tasks.md`, and spec deltas under `specs/{capability}/spec.md` (delta files start with `## ADDED Requirements` and carry **no H1**).
  - `changes/archive/` — completed changes. **Get there via `openspec archive {id} -y`, never by hand** — the archive is not scanned for deltas, so a hand-placed change silently never merges into `specs/`. After archiving, **rewrite `## Purpose`** — the CLI overwrites it with a `TBD` placeholder every time.
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
