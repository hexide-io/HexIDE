---
name: demo-hexide-mcp
description: Live showcase that builds a small, non-interactive VB6 "demoscene" graphics intro (algorithmic, trig-driven, self-running) end-to-end by driving the RUNNING HexIDE entirely through the MCP tools, with a dramatic slow-build -> "needs something more advanced" -> zap-the-class pacing arc narrated organically into the IDE's own AI Chat addin, then compiles & launches it with the real VB6 compiler and verifies it by screenshot + resize. Use when asked to demo or showcase HexIDE's MCP automation, or when invoked as /demo-hexide-mcp.
---

# Demo: drive HexIDE to build a VB6 demoscene intro via MCP

Build a small, **non-interactive** VB6 graphics program — an Amiga-demoscene-style intro: self-running,
algorithmic, **far too much trigonometry**, resizable — **entirely by operating the live HexIDE UI through
the MCP tools**. The point is to *show off MCP control of the IDE*, with a deliberate dramatic arc to how the
code appears. The story is told **inside HexIDE** — narration *teleports* into the AI Chat addin (see
**Narration** below) — while this Claude conversation stays terse: state the action you're taking, not the tale.

**Why non-interactive?** MCP drives HexIDE's chrome, but a *compiled* `.exe` is an external process MCP can't
reach. A self-running effect sidesteps that: it needs no clicking, and verification is just a screenshot (and
a resize). No buttons, no textboxes — the math is the show.

## The one hard rule

**Every edit goes through the IDE UI via MCP. Never write project source any other way.**
- ✅ Designer: `view_designer`, `add_control` (just a `Timer`), `set_control_property`.
- ✅ Code: `open_file` then **`type_text`** (and `press_key` for Enter/Tab/select-all/delete). Add the class
  module via the Project menu / `invoke_command` (`AddClassModuleCommand` on the `MainView` VM).
- ✅ Commands: `invoke_menu_item` / `interact` (toolbar by AutomationId, dialogs).
- ❌ **Never** `Write`/`set_file_content` the `.frm`/`.cls`/`.bas`. (Reading files to *verify* a build is fine.)
- ❌ Never ask the user to click/type/drag. (Screenshotting the external compiled window via PowerShell is the
  one allowed out-of-IDE step — it's verification, not authoring.)

## What to build

A **single, fresh, self-running effect each run** (don't reuse last time's), in the Amiga-intro spirit: small,
clever, procedural, trig-heavy, and **resize-adaptive** (reflows to the window). Default Form chrome
(`BorderStyle = 2 Sizable`) gives normal min/max/restore/close for free. No raster *assets* — everything is
computed each frame (that IS the ethos); if you want a lookup (sine LUT, palette) build it in `Form_Load`.

**Effect palette** (pick one, vary across runs):
- **Vector (default — primitives only, reliable):** rotating 3D wireframe (cube / star / torus, with rotation
  matrices + perspective), harmonograph / Lissajous (decaying multi-pendulum curves), spirograph
  (hypotrochoid), 3D perspective starfield, vector balls (projected dot-spheres), copper/sine colour bars.
- **Raster (occasional "advanced" — Win32 GDI pixel buffer):** plasma (sin-of-sin palette), tunnel, rotozoom.
  Needs `CreateDIBSection` + `CopyMemory` (`RtlMoveMemory`) + `BitBlt` `Declare`s: build a `Long` colour buffer
  per frame, `CopyMemory` it into the DIB's bits, `BitBlt` to the form `hDC`. Heavier/fragile to type — use
  only when you want a true per-pixel run; **the compile+screenshot will catch a broken one.**

## VB6 technique (what the code uses)

- **Loop:** a `Timer` (Interval ~25–40 ms) → `Timer1_Timer` advances a phase counter, `Cls`, redraws. (Timer
  keeps the message pump alive so chrome stays responsive and resizable.)
- **Flicker-free:** `Me.AutoRedraw = True` (VB6 buffers to an off-screen bitmap + blits).
- **Pixels + resize:** `Me.ScaleMode = vbPixels`; read `Me.ScaleWidth` / `Me.ScaleHeight` **each frame** and
  scale the effect to them, so resizing recomputes automatically.
- **Maths (real VB6 — full library, unlike the stub interpreter):** `Sin/Cos/Atn/Sqr/Tan`, `Const PI =
  3.14159265358979`, `RGB(r,g,b)`, `Randomize`/`Rnd`. Draw with `Line (x1,y1)-(x2,y2), RGB(...)` (`,,BF` for
  filled bars), `Circle`, `PSet`.
- Set the window title in code: `Me.Caption = "..."` in `Form_Load`.

## Narration — teleport it into the AI Chat addin

The story lives in the **IDE's AI Chat panel**, not this chat. To make a line *appear* there instantly — no
API key, no LLM call — set the addin's `Say` property by reflection on a node whose DataContext is
`ChatViewModel`:

- `interact set_property` with `value: "Say=<your line>"`, targeting the AI Chat panel node — find it once
  with `dump_visual_tree` (a node under `…/Pane[AI Chat]/…` reporting `dataContextType: ChatViewModel`, e.g.
  its `Pane[Scroller]`). Each call appends one assistant bubble — a clean "teleport".
- The addin's **Send** button is a *real* LLM call (needs an API key); `Say` deliberately bypasses it, so the
  narration is scripted, instant, and free.
- **No dead air.** During any wait the viewer can see — a design workflow, a build, a rebuild — keep the panel
  alive with a short cagey holding line (`Say=Designing the next move…`, `Say=Letting the compiler chew on it…`).
  Never leave the AI Chat static for long stretches; dead air is hard to narrate over.

**Voice — make it feel like the idea just occurred to you.** First-person, in-the-moment, a touch wry, and
*spontaneous*. **Never** expose the machinery: no "Beat 2", no "the zap", no "warp speed", no skill-step
language anywhere — not in the AI Chat, not in this conversation. The moment you decide the tame version
isn't good enough should read as a genuine thought, not a stage cue — e.g. a beat of *"…hm. a bouncing line.
that's not it, is it."* then later *"there — proper 3-D, far too much trig."* Keep this Claude conversation
terse (just the action you're taking); let the personality live in the teleported lines.

## The arc (your internal guide — the *output* voice stays organic per Narration; never announce these steps)

Cadence is free: per-line `type_text` appears progressively (**slow**); one bulk `type_text` appears instantly
(**zap**). Nothing slow-typed is wasted — the scaffold persists; only the tame effect *body* is superseded.
Teleport an organic AI-Chat line at each turn.

1. **Open + pitch.** **Christen it first — never ship "Project1".** Open Project Properties
   (`invoke_command ProjectPropertiesCommand` on `MainView`), set the **Project Name** field (the enabled
   `Edit` on the General tab — its VM member is `ProjectName`) to a fresh, random, jaunty name each run
   (e.g. *Wild Ecstasy* → identifier `WildEcstasy`; names must be valid VB6 identifiers — no spaces), then
   `invoke` `Window/Custom/Button[_OK]`. A unique project name = a unique build, so a stale exe from a prior
   run can't lock or impersonate this one. Then `view_designer("Form1")`; `add_control` a `Timer` (use the
   name it returns, e.g. `Timer0`); teleport a short opener.
2. **Slow build.** `open_file("Form1")`, clear the pre-seeded `Form_Load` stub (`press_key` Ctrl+A → Delete),
   **then `get_file_content` to confirm it's actually empty** (a Ctrl+A right after a tab switch can miss
   focus and leave the old text — your new lines then splice into the middle; re-clear until empty). Type
   **one line per call**: `Option Explicit`, `Const PI`, module vars, `Form_Load` (`AutoRedraw=True`,
   `ScaleMode=vbPixels`, `Me.Caption="<the jaunty name>"` (match the project name for swagger),
   `<Timer>.Interval=30`, `<Timer>.Enabled=True`, `Randomize`), and a
   **deliberately tame** `<Timer>_Timer` (a bouncing `Line` / a few `PSet` stars). It runs, but it's plain.
   (Type-then-supersede — do **not** compile it.)
3. **The turn.** Let the realisation land *organically* in the AI Chat (the tame version isn't enough — read
   like it just occurred to you), and type a matching wry code comment, e.g.
   `' --- a bouncing line. not exactly Second Reality. ---`
4. **The zap.** Add a **real class module** (`AddClassModuleCommand`; use the name it returns, e.g. `Class1`),
   clear its editor, bulk-insert the **whole advanced effect in one `type_text`** (fully formed), then rewire
   `<Timer>_Timer` to drive it (`Private fx As New <Class>` at module scope; `fx.Render Me` per tick).
   Teleport a short "there we go".
5. **The reveal.** Save (toolbar `#Standard.SaveProject`), Make/Run-with-VB6, screenshot + resize (below).
   Teleport the closing line.

## Preconditions

- HexIDE running with the MCP server + a project (`get_project_info` works).
- **Exactly one instance, and you're driving the right one.** A second launch on an already-bound
  `--server-port` starts a silent, MCP-less window — you'd then unknowingly drive the *stale* instance. After
  any (re)launch: kill **all** `HexIDE.Desktop` and **wait for the port to free** before launching one; then
  confirm `get_project_info` is blank (no leftover forms/modules) and the control names start fresh
  (`Timer0`, `Class1`) — proof you're on the new instance, not a previous run's.
- **VB6 installed & findable** (`$VB6_EXE` or default `…\VB98\VB6.EXE`); else `MakeWithVb6Command.CanExecute`
  is false. Do **not** fall back to `run_project` (its interpreter is a stub missing most builtins).
- Open with a `take_snapshot` of the clean IDE.

## Compile + verify (the proof)

1. **Save to disk first** — `interact invoke` `Window/Custom[MainView]/None[StandardToolbar]/Button[Standard.SaveProject]`
   (synthetic Ctrl+S does NOT save; the compiler reads the `.frm`/`.cls` from disk).
2. **Clear stale locks, then build.** Before building, **kill any stray `vb6.exe` and prior demo `*.exe`** —
   a previous `RunWithVb6` leaves a `vb6.exe` holding the `.vbp` open (build fails *"…is already open"*), and a
   still-running prior exe holds the output file locked (build silently keeps the old exe, so you screenshot the
   *wrong* program — you'll see the previous run's caption). `Stop-Process -Name VB6,<exe> -Force` first, then
   `interact invoke_command MakeWithVb6Command` on `Window/Custom[MainView]` (prefer **Make** over
   **Run** — Make produces the `.exe` and lets `vb6.exe` exit, instead of holding the project open in the IDE).
   Confirm via MCP-visible signals: `get_diagnostics` clean + a **freshly-timestamped** `.exe` on disk.
   *Note:* VB6 names the output exe after the **`.vbp` filename**, not the Project Name property — so a project
   named `WildEcstasy` saved as `Project1.vbp` still builds `Project1.exe`. That's fine: the jaunty name shows
   in the IDE title and the window **Caption** (set in `Form_Load`); match the running window by caption, not
   by exe filename. If HexIDE's Make is flaky after a lock, a direct `vb6.exe /make "<vbp>" /out "<freshlog>"`
   is the reliable fallback (it's compiler invocation, not source authoring) — read `<freshlog>` for the *true*
   error; the persistent `make.log`/`Form1.log` in the project dir are often **stale** and misleading.
3. **Screenshot the running effect** (external window — `take_snapshot` can't see it). Use PowerShell +
   `PrintWindow` (captures even if occluded), finding the form by its caption among the process's visible
   windows (a VB6 exe also has a hidden 0×0 `ThunderRT6Main` window — skip it; pick the sized one):
   `EnumWindows` → match PID + `IsWindowVisible` + non-zero rect → `PrintWindow(hwnd, hdc, 2)` → save PNG →
   `Read` it.
4. **Prove resize-recompute** — `MoveWindow` the form to a different size, wait a tick (let the Timer redraw),
   `PrintWindow` again, `Read` it. The content must have reflowed to the new size.

## Archive every verified demo (do this, don't ask)

Working demos are **not** ephemeral — they are kept under `demo/` in the repo. Once a demo is verified
(running screenshot + resize confirmed), preserve it **without further interaction**:

1. **Copy into `demo/<jaunty-slug>/`** (slug = the jaunty project name, kebab-cased) — the full VB6 source
   (`<Name>.vbp` + `.frm`/`.cls`/`.bas`; rename the `.vbp` to `<Name>.vbp` so a rebuild yields `<Name>.exe`),
   the compiled `<Name>.exe`, the resize screenshot as `screenshot.png`, and a short `README.md` (what the
   effect is, how it works, build/run). Source lives in HexIDE's temp project dir
   (`%LocalAppData%\Temp\hexide_*\`); copy from there. Drop empty/orphan leftovers (e.g. an unused `Class2.cls`)
   and confirm the `.vbp` references only the files you copied.
2. **Verify the archived copy is self-contained** — run `vb6.exe /make "demo/<slug>/<Name>.vbp"` and confirm
   `Build … succeeded` + a fresh `<Name>.exe`. The committed source must build on its own.
3. **Add it to the gallery** — append a row to `demo/README.md`.
4. **Commit and push, unprompted** — `git add demo/<slug>` + the README, commit, push. (`.exe` is committed on
   purpose; there is no global `*.exe` ignore — double-check `git check-ignore` if unsure.)

## Gotchas (from real runs)

- **External windows are invisible to MCP** — verify builds via `.exe`-on-disk + clean diagnostics + the
  PowerShell `PrintWindow` capture; never via `take_snapshot`.
- **Save via the toolbar button**, not Ctrl+S. **Clear the pre-seeded `Form_Load` stub** before typing the
  form code. (A class module's editor is empty on creation and header-free, so clearing/typing it is safe.)
- **`Scale` is a reserved VB6 word** — never name a variable `Scale` (a scale-factor is tempting in graphics
  code); use `sc`. The LSP flags it and `vb6.exe` rejects it. Watch for other reserved names too.
- **`set_control_property` can't set enum/Name props** — fine here (Timer needs only `Interval`/`Enabled`,
  settable as numbers/bools; set the title in code).
- **Real VB6 has the full maths library** (the bundled `run_project` interpreter does not — it's a stub).
- **Multiple `*.exe` of the same name** from prior runs confuse `Get-Process` — match by window caption, or
  taskkill stray instances first.

## Done when

A real VB6-compiled, **self-running** demoscene `.exe` is running and confirmed by screenshot (matched by its
jaunty caption, **not** "Project1"/"Form1"), it **reflows on resize** (second screenshot), the project carries
a fresh random jaunty name, every edit went through the IDE via MCP, the build appeared with the
slow→turn→zap arc, and the story was narrated **organically into the AI Chat addin** (never naming the steps)
while this Claude chat stayed terse — under ~15 minutes — and finally the verified demo was **archived under
`demo/<slug>/` and committed + pushed** without asking (see *Archive every verified demo*).
