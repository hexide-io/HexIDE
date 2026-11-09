# Changelog

All notable changes to HexIDE are recorded here.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow
[Semantic Versioning](https://semver.org/spec/v2.0.0.html). The leading `0.` is the stability promise:
there isn't one yet. Anything may change between 0.x releases.

## [Unreleased]

New entries are written one file per change in [`changelog.d/`](changelog.d/README.md), and gathered here at
each release by `scripts/changelog-gather.sh` (#1375).

## [0.5.0] — 2026-11-09

### Added

- **Language server initialization options** (#3317): `lsp-servers.json` can supply server-specific
  `initializationOptions`, passed unchanged in the handshake and omitted when unset.

- **DAP client protocol coverage is generated and guarded** (#3302). The table in `docs/dap-client.md`
  lists every request, event and reverse request from a commit-pinned, SHA-256-checked DAP schema, with
  support read from the client code. Offline runs skip visibly; CI requires the schema.

- **Foreign debug adapters exercise the real DAP client** (#3302). Pinned, SHA-256-verified debugpy and
  netcoredbg sessions cover launch, a breakpoint, stack and locals, continue and clean disconnect, with
  wire replies and process exits checked. Offline runs skip visibly; CI requires both adapters.

- **Warnings for source that exceeds VB6's line limits** (#3203). The bundled language server warns
  beyond 24 continuations, counting long lines' 1,023-character pieces, and on physical lines over
  1,023 characters. HexIDE keeps running the whole source; the notices explain the VB6 compatibility risk.

- **Windows on Arm gets an advisory test job** (#3139). The desktop matrix now runs on
  `windows-11-arm`, including the made-program test and required foreign-server checks with deliberate
  x64 server emulation. Its result artifacts have a distinct name, and docs-only changes report the
  same advisory check.

- **Release add-in signing chains can be generated without development defaults** (#3136).
  `genkeys-release` accepts publisher and intermediate identities, separates the three private keys
  from the public records, refuses overwrites, and prints public-key fingerprints. The packer README
  documents a self-contained Windows publish and the files and CI secret to replace on root rotation.
  Release generation and publisher issuance share display-name validation that refuses invisible
  Unicode formatting and separators; an end-to-end test covers issuance and both package identities.

- **`--dpi-unaware` runs a Debug build at scale 1 on a scaled Windows display** (#3083). A layout check
  at 100% no longer needs a registry change, and `take_snapshot` of an 860 × 640 window is 860 × 640.
  `get_window_state` says why the scale is 1, and its `frame` is now in the same pixels as the rest of
  the reply in that mode. Avalonia offers Windows no other scale, so the flag gives 1 and nothing else.

- **Call Hierarchy** (#3080) shows every advertising server’s answers in a reusable pane beside
  Immediate. Calls To and Calls From expand lazily; Enter or double-click opens a node or call site.
  The command appears beside Find References and in the code context menu, with a keymap entry and
  no default shortcut. HexIDE preserves opaque server data and computes no call relationships.

- **Expand and Shrink Selection** (#3076): code-window context-menu and keymap commands climb
  the language server's enclosing selection ranges and step back down. Editing or moving the caret
  resets the chain. Both commands require an advertised provider and have no default shortcut.

- **Format Selection** (#3075) asks a language server that advertises range formatting to format the
  selected code, using the editor's Tab Width. Accepted edits form one undo step and protect IDE-only
  lines. The localized command sits beside Format Document and has an unassigned keymap entry.

- **Go to Implementation and Go to Type Definition** (#3074) in the View and code context menus, enabled
  when the document’s language server offers them. A single result opens directly; multiple results fill
  the Find References pane. Both commands can be assigned in a keymap and have no default shortcut.

- **Breakpoint conditions** (#2914): choose Condition… from a breakpoint's gutter menu to set or remove
  an expression, with a dialog prompt explaining when the breakpoint stops. Conditional breakpoints have
  a distinct glyph and expression tooltip, persist with IDE
  breakpoints, and send conditions to capable external adapters. Incapable adapters and interpreter runs
  stop unconditionally, with a localized notice once per session. Launch-file refusals direct users to
  the gutter menu; adapter verification and messages remain visible in tooltips.

- **Set Next Statement for external debug sessions** (#2914): adapters advertising goto targets receive
  the caret source and line, then the first target on the stopped thread. Refused moves retain inspection
  and report the adapter's message in Immediate, without moving the caret or scrolling the editor.
  Refused continue and step commands also keep the caret and scroll position.

- **External debug sessions refresh on adapter invalidation** (#2914). Call Stack, scopes, Locals and
  Watches reload while stopped, retaining the selected frame when available and discarding stale replies.

- **Language servers can apply checked edits during a command the user started** (#2908).
  Unsolicited or late edits are refused with a reason; accepted edits share rename's all-or-nothing
  checks and one undo step per module, preserving the active tab. External document requests ask every
  time, showing the full URI in a localized question, and launch only after Yes; only web and mail links
  are offered, and anything else is refused.

- **Language servers can open a loaded project member at a selection** (#2908). `window/showDocument`
  honors `takeFocus` and answers whether the URI resolved; external and unknown URIs open nothing.

- **Language-server questions use queued IDE message boxes** (#2908). The server's name and actions
  appear in a box owned by the main window. Questions wait out running programs and other modals,
  allow diagnostics through while waiting, and close when the server cancels them. The box shows
  the message type's icon and can be dismissed with Escape.

- **Find References** (#2907): Shift+F12 and the Edit and code context menus gather references, including the declaration, from offering language servers. The bottom results window groups locations by module, emphasizes each name's range and opens a location on Enter or double-click. Accessible row names read the module, line number and line text. Failed servers appear alongside successful results; callers with locations already in hand can fill the same list without another request.

- **Language-server hovers render Markdown** (#2905): paragraphs, emphasis, inline code, lists, links
  and fenced code blocks. VB/VBA fences share the editor's font and colours. HTML stays literal,
  command links stay inert, and links open only when clicked: a browser link in the browser, a file link only
  when it names a document of the project. The renderer supplies the
  advertised Markdown/plain-text formats; plain-text replies remain literal.

- **Recovery sessions identify themselves in the window title** (#2888). Launching with `--profile none`
  shows a persistent localized notice that settings changes will not be saved, including while a project runs.

- **OpenSpec design for local operational diagnostics and an optional OTLP exporter add-in** (#2823),
  for content-free traces and metrics. This records the proposed behavior and phased tasks; no telemetry
  implementation or exporter is shipped by this change.

- **In-app translation correction design for review** (#2819): proposes local editing, explicit submission
  consent, offline queuing and accepted-correction delivery, with receiving endpoint options left for Jen.

- **A translation problem issue form for people already on GitHub** (#2818). Report the language,
  where the text appears, its current wording and a suggested correction; the `Str.*` key is optional.

- **Regional language packs for Canada, Mexico and Brazil** (#2777). Canadian French, Mexican Spanish and Brazilian Portuguese override their regional terminology and inherit the remaining strings from their neutral language packs. Portuguese (Portugal) continues to use the neutral Portuguese pack.

- **Thai UI language pack** (#2776). A complete Thai translation ships in the language picker,
  with left-to-right text, no access-key markers, and Thailand regional fallback.

- **Serbian in Latin script** (`sr-Latn`) is available in Options → Language, with a complete
  translation, left-to-right layout and Latin access keys (#2775).

- **Galician UI language pack** (#2774). Choose Galician in Options → Environment → Language for a complete
  translation, with Latin-script access keys and a Spain region that inherits the neutral pack. The
  remaining English interface text is translated and Galician is registered in the language picker.

- **Catalan UI language pack** (#2773). Choose Catalan in Options → Environment → Language for a complete translation, including menus, dialogs and property descriptions.

- **Basque UI language pack** (#2772). Euskara (`eu`) is selectable in Options → Environment → Language,
  with every canonical string translated, left-to-right text and Latin-script access keys.

- **Estonian UI language pack** (#2769). All IDE strings are translated, with access keys and format placeholders preserved; Estonia resolves through the same pack. Select Estonian in Options → Environment → Language.

- **Latvian UI language pack** (#2768). All interface strings are translated, with access keys preserved; select Latvian and the Latvia region in Options → Environment → Language.

- **Lithuanian UI language** (#2767). A complete translation is selectable in Options → Environment → Language, including the Lithuania region.

- **Slovenian UI language pack** (#2766). Options → Environment → Language offers Slovenian, with all UI strings translated and a Slovenia region that inherits the neutral pack.

- **Slovak UI language pack** (#2765) — a complete translation, selectable in Options → Environment → Language, with Slovakia as a region.

- **Croatian UI translation** (#2764). Select Croatian in Options → Environment → Language for a complete
  translation, including menus, dialogs, tool windows and property descriptions.

- **Bulgarian UI language pack** (#2763). All interface strings are translated, with Bulgarian available
  in Options → Environment → Language and Bulgaria in the Region list.

- **Hungarian UI translation** (#2762). Select Hungarian in Options → Environment → Language; all
  interface strings are translated, with access keys and formatting placeholders preserved.

- **Romanian UI language pack** (#2761). Options → Language offers Romanian, with Romania and Moldova
  regions sharing a complete translation, including property descriptions and access keys.

- **An add-in can contribute a language server** (#2760, part of #457). `IHexIdeHost.RegisterLanguageServer` takes what
  an `lsp-servers.json` entry holds, checks it as it checks one, and lists the server in the Language Servers window
  under the add-in's name; an entry of the user's file with the same id still wins. A registration can carry a gate
  that runs before every launch and fails the start if it throws or outlasts its limit (a minute unless the add-in
  sets one), and a message prefix of its own: the add-in sends
  requests and notifications under it over HexIDE's connection, captured as any others are, and answers the
  server-to-client methods it claims. A server registered after documents are open is opened on the ones it claims,
  and disposing the registration stops it and removes its row.

- **About thanks HexIDE's human contributors, the Claude Code sessions and the Codex lanes** (#2746). A Credits
  section, open when About opens, names Claude by Anthropic and Codex by OpenAI and the work each does, in every
  shipped language. Brands can append their own credits through the About options; HexIDE's thanks always remain
  first and can't be removed.

- **Completion expands numbered snippet placeholders** (#2670): defaults and repeated placeholders
  edit together, Tab visits stops in numeric order, and `$0` sets the final caret position. Snippet
  support remains undeclared until nested placeholders and variables are implemented.

- **Completion snippets offer choices** (#2670): choice placeholders show their values in the
  completion picker, including escaped commas and pipes, and the selected value updates linked
  occurrences. Unterminated choices refuse the completion.

- **Completion lists use server ranking, preselection and filter text** (#2670). Descriptions show
  documentation alongside detail as plain text, and kind glyphs match the Object Browser. Server items
  remain available when none matches the opening word, including snippet replacements.

- **Language servers can read the editor’s Tab Width** (#2669). The client advertises workspace
  configuration support, answers `editor.tabSize`, returns null for unknown settings, and notifies
  running servers when Tab Width changes.

- **Profile catalog foundations** (#2621): immutable versioned declarations, explicitly indexed embedded
  built-in manifests and installed-directory discovery. This component is not yet connected to the IDE;
  manifest declarations are validated with file-and-field diagnostics. Portable path spelling and
  directory-manifest link checks refuse unsafe references. A standalone byte reader protects actual
  Linux and Windows opens against link replacement after preflight. A standalone assembler captures
  referenced JSON, validates pack record shapes and settings fields, and attributes file errors. Name resolution reserves built-in ids while explicit directories remain
  selectable. Atomic bundle loading and pack/default adapters remain pending; the assembler must not yet be connected to consumers.

- **`interact` turns the mouse wheel: `wheel`** (#2576). It sends real wheel input through the window, notch by notch,
  at a point on the target or the centre of what shows of it. Whatever the control does with a wheel then happens, its
  step per notch and its stop at an end included. The reply says what scrolled, and how far.

- **FormatNumber, FormatCurrency and FormatPercent** now format figures with the measured rounding, optional flags and decimal widths, using the VB6 locale for separators and currency symbols (#2515).

- **Language-server output is readable in the IDE** (#2388). Each server's card in Language & Debug
  Servers shows live log messages with their protocol type, import notes and stderr, with its protocol
  inspector one click away. A bounded session history keeps startup output without arming capture.

- **Restart a language server when its workspace artefact changes, opt-in** (#2072). Set
  `workspaceArtifact.restartOnChange` for a server that reads its file only at startup; debounced
  regeneration restarts that server and reopens its documents only when the file changes. Requests
  wait for those documents to reopen, including when a workspace-root restart overlaps it. The
  spring-tide RDCore profile enables it until RDCore can reread its own project file.

- **Named panel layouts in the Window menu** (#2006). Save the current arrangement under a name, switch,
  rename or delete saved layouts, or use Full Editor to give documents the whole frame. Layouts restore
  add-in windows through the same keys as the current layout; unavailable windows are deferred and reported.
  The submenu opens on first use and exposes localized accessible names. Legacy add-in keys restore their
  saved placement and are removed from subsequent layout saves when the add-in loads.

- **Block-end hints name the block being closed** (#1998). The editor displays inlay-hint labels from
  offering language servers after the line without moving caret columns. The bundled VB6 server names
  closed blocks from its syntax tree, joining continued opening expressions with one space.
  Options → Editor → General can hide the hints. The option is
  translated into all 39 shipped languages.

- **Outline window** (#1992): View → Outline shows the active document's language-server symbol tree,
  follows the caret, navigates on click or Enter, filters by symbol kind and refreshes after edits.
  Symbol refreshes flush only unsent edits, keeping diagnostics from triggering a change/request loop.
  Saving without edits sends only the save notification, without resending unchanged text.
  Flat symbol replies work too, including in carried files served by other language servers.
  Outline labels are translated into all 39 shipped language packs.

- **Diagnostics panel** (#1991). Tools → Diagnostics lists current diagnostics across documents and
  attached servers, with severity and current-document filters. Double-click a row to open its document
  at the reported position; the list follows diagnostic replacements and withdrawals while open.
  Document cells show member or file names with full paths in tooltips; severity captions are singular,
  and columns accommodate full server names and remeasure localized headers after a language switch.

- **32-bit Arm Linux release builds** (#1225). The release pipeline publishes `linux-arm` archives
  in framework-dependent and self-contained flavours, including the matching standalone VB6 runner.

- **Composite workspace design draft** (#1207): records the proposed discovery, descriptor cascade,
  ownership, roots and trust boundaries, with open questions, a provisional JSON Schema and three examples
  for review. No workspace behaviour changes.

- **Era-skin design draft for review** (#1138): records the proposed three layers, skin parameters,
  layered icons, clean-room drawing process and font-policy options, with open decisions left for review.

- **FlatScrollBar runtime control** (#1097, parts A–C): Integer range, value and change sizes, synchronous Change, silent range clamping, Orientation size swaps, Appearance and Arrows. Part B adds arrow/track clicks, hold repeat, thumb Scroll/Change events and flat/3D/hover drawing without taking focus. Part C loads VB6-saved properties without Orientation size swaps and adds the measured toolbox size and placement; saved blocks keep their high words, ordering and omitted lines verbatim and remain read-only in the designer.

- **Animation runtime playback** (#1096 part A) loads preserved Animation blocks and adds
  Open, Play, Stop, Close, AutoPlay and the measured defaults and errors. The managed AVI
  reader decodes uncompressed 4/8/16/24/32-bit and RLE8 video without a codec dependency,
  ignoring audio streams. Playback follows the clip header rate. Editable form-file integration remains pending.

- **Animation pointer events** (#1096 part B, input slice) raise MouseDown, move focus,
  then raise MouseUp and Click. Double-click adds DblClick without a second pair of
  mouse events; coordinates are client twips.

- **Animation frame drawing** (#1096 part B) follows the clip's size with Center off,
  centres and clips it with Center on, draws transparent backgrounds in BackColor and
  opaque backgrounds in the clip's own colours, and keeps earlier controls above their
  neighbours. Hit bounds follow the window; Width and Height retain their values until Close.

- **MonthView’s date and selection state and saved-block runtime loading** (#1095 A): measured
  date bounds, selection limits, date parts, month layout and scalar settings, with Value as the
  default member. CurrentDate serials and StartOfWeek’s low word load; the designer keeps the
  original block view-only and verbatim. Unimplemented members and unmeasured conversions refuse.
  Week reads and writes follow the measured week-year rule; selection and date-bound edges raise the
  measured errors. VisibleDays supports one month and twelve months with rounded indexes.
  Other month spans refuse pending the first-month rule. Calendar grids now draw month titles,
  weekday headings, trailing dates, selection shading and optional week numbers for one or twelve
  months, without pinning pixel metrics. Right-click raises MouseDown, MouseUp and Click without
  changing selection; Tab enters and leaves the calendar with GotFocus/LostFocus events. Single-month,
  single-selection date and trailing-date clicks, double-clicks and month buttons now raise their
  measured event sequences, including GetDayBold when navigating. Right, Down, Page Down,
  Ctrl+Page Up, Home and End now move the selection
  between KeyDown and KeyUp, with GetDayBold on month changes; k and Esc raise only key events,
  and MultiSelect Shift+Right leaves the selection alone. MultiSelect plain date clicks select at the press; a ByRef SelChange Cancel
  restores the previous selection before DateClick. Today, Shift+click and drags, other input layouts, today’s mark and footer, remaining own members and file
  writing remain pending.

- **ImageCombo's internal object model** (#1092, part A): ComboItems supports insertion,
  index and key lookup, removal, clearing, held-item renumbering, default Text and Variant Tag.
  Forms carrying MSComctlLib.ImageCombo refuse at F5 until drawing and input land (#1092);
  the designer preserves their blocks. Measured Count/Index getters return Long and item Indentation returns Integer. Keys compare
  case-sensitively; a new or Empty Tag is an empty String, and Tag can retain Nothing.
  The internal owner model adds Text, SelectedItem, Locked and default Indentation; selection updates
  Text, selected-item edits follow through, and code Change is posted through the event pump until a yield.

- **ListView ImageList images** (#1085): Icons, SmallIcons and ColumnHeaderIcons bind from code and saved
  forms, with item icons in all four views and Report cell and header images. Numeric and key selectors
  retain their types; bound lists reject removal, and clearing Icons resets only the large-icon selectors.
  Unmeasured image sizes and binding edges remain refused.

- **Offline third-party publisher issuance** (#890). The add-in packer's `sign-publisher` command
  validates the supplied root and intermediate chain, signs a publisher's public key without receiving
  their private key, preserves existing output files and verifies the written chain. TRUST.md and
  CONTRIBUTING.md describe requesting a record and packing with it.

- **Profile bundle loader** (#504): validate versioned manifests and snapshot their JSON payloads through
  the installed-profile reader, which refuses links inside a bundle and files over 1 MiB. Resolve embedded
  or installed names and explicit directories, and refuse escaping file references with localized,
  attributed errors naming the refused file.

- **Profile settings defaults** (#504): layer validated profile defaults below explicitly saved user
  values, using existing value validation and preserving settings-file presence when saving edits.

- **Profile theme adapter** (#504): apply snapshot theme payloads above profile defaults and below
  explicit saved theme choices. Reject invalid known colours before applying a bundle, retaining the
  active theme; keep attributed warnings for unknown colour keys.

- **Profile keymap adapter** (#504): apply snapshot bindings above profile defaults and below explicit
  saved or edited keymap choices. Validate known gestures before applying a bundle and attribute
  unknown-command warnings to its source file.

- **Profile language adapter** (#504): layer snapshot translations over shipped locale fallback and
  below user overrides, reusing placeholder validation and preserving the shipped locale inventory.
  Clearing a user override reveals its profile fallback.

- **Real-server close and shutdown coverage** (#491). The foreign-server suite and bundled-server
  tests read diagnostics from recorded wire frames after closing a document and pin the process exit
  code observed after normal HexIDE teardown.

- **Signed add-in capability declarations** (#459): schema 2 manifests carry a validated
  `capabilities` array; the packer accepts repeated `--capability` arguments and emits a stable,
  explicit declaration. Bundled add-ins declare their current host use. Host capability enforcement
  and consent presentation follow in later tasks.

- **Add-in declaration verification coverage** (#459): signed-package regression tests cover
  declaration tampering and reordering, exact-byte hashes, re-signing, invalid signed declarations
  and declaration-free legacy package compatibility. Host denial remains pending security review.

- **Add-in consent isolation coverage** (#459): signed-package tests verify that adding, removing or
  reordering declarations, or repacking a legacy package, requests fresh consent even when the add-in
  id, publisher and version are unchanged. Earlier build decisions remain independent.

- **Verified add-in declaration metadata** (#459): successful verification and registry discovery expose
  an immutable capability declaration tied to the signed build hash, with legacy absence distinguished
  from an explicit empty list. This is declaration data for consent and disclosure, not a capability
  grant or a record of host use.

- **Diagnostic tags and related locations** (#453). Keep unnecessary and deprecated tags and valid
  related locations from push and pull replies, ignoring unknown tags and malformed related entries.
  Fade unnecessary text and strike through deprecated text in the editor and diagnostics list, alongside
  the severity squiggle. Diagnostic tooltips show related messages with clickable locations and stay
  open while the pointer moves onto them. Advertise support for both fields.
- **Server Quick Fixes** (#453). Push and pull diagnostics preserve the server's token,
  including its JSON spelling, after the reply is disposed. The code window’s Quick Fixes context menu requests
  server actions on demand, grouped by server, with disabled reasons and lazy resolution. Picking a fix
  applies its workspace edit atomically with one undo step per module, protecting IDE-only lines, then
  flushes edited documents before running its command. Only commands declared by the originating server
  are sent back; rename and show-references client commands use HexIDE’s shared lens handling, and other
  unlisted commands show a localized refusal. Rename client commands share the menu and keymap’s
  prepareRename checks and server-provided prompt defaults. Each server receives only its own diagnostics and data,
  and list headers show server display names while keeping distinct servers separate. Refusals explain invalid or protected edits and
  changed or unsaved targets in the IDE language. Advertise literal actions, diagnostic data, disabled
  reasons and edit resolution. HexIDE computes no fixes itself.

- **Unix socket pipe configuration documented** (#381). Absolute `pipeName` paths reach Unix domain
  sockets on Linux and macOS; bare names use .NET’s pipe endpoint. A raw-socket round-trip test pins
  the existing behaviour.

- **Stop an AI Chat response** (#58). The panel can cancel streaming and history summarization,
  finish the partial reply and accept another message.

- **Project Explorer’s Add submenu offers Module, Class Module and User Control** (#5). Each adds to
  the selected node’s project, opening code for modules and the designer for UserControls.

- **Filter the Properties window as you type** (#4). A localized filter above the tabs narrows both the Alphabetic and Categorized lists by property name, hides empty categories, and restores the full list when cleared.

- **An add-in can contribute a debug launch for the External Debug Session.** `IHexIdeHost.RegisterDebugLaunch` takes a
  title, which projects it runs and what makes its launch configuration, asked for only when a run starts. **Run > F5
  Runs** chooses, per project and per user, whether F5 runs the project in the IDE or with the add-in's launch; the IDE
  stays the default, and the choice is kept in the `.user.hexproj` sidecar. **Run > Start Once With** runs a launch
  once.
  The session gets the project's saved breakpoints. A chosen launch that is gone leaves F5 to the IDE, with a status
  message. Turning the add-in off or revoking its consent takes its launches out, and a run already starting is refused
  before its adapter is started.

- **An add-in can contribute a project type.** `IHexIdeHost.RegisterProjectType` names the files a type reads, such as
  an RDCore `.rdproj`, and what reads them. Open Project, the recent list and a project group then open one as a
  project: its modules, named by their `VB_Name`, its references and its folders, an empty folder included, with code
  editing. The project file stays the add-in's program's to write: saving writes edited modules only, and HexIDE
  neither adds nor removes members, edits the project's properties or references, makes it nor runs it. The add-in
  reports a member added, removed or moved, or new references, in order, and the project follows as a .vbp project
  would. Turning the add-in off or revoking its consent takes its project types out.

### Changed

- **`activate_document_tab` and `close_document_tab` take a document's name passed as `title`** (#3338). A title
  that matches no tab is tried as a document name among the open tabs. When exactly one tab shows that document, the
  tool acts on it, and its note says so. When several do, such as a document's code and designer, the refusal names
  them and says whether `project` or `type` picks one. A first-time caller had passed "Var1" for the tab
  "Probe2 - Var1 (Code)" and been refused.

- **VB6 parity adapter settings** (#3317, #3188): the parity runner selects RDCore's VB6 language
  and keeps `Erl` after `Err.Clear`, overriding inherited adapter settings.

- **VB code gets a 64 MiB stack on Windows and Linux** (#3228). The IDE and a made EXE start their UI, where VB
  code runs, on a thread of their own instead of the process's first thread. On Windows that thread had 1.5 MiB, so
  a recursive Function stopped with Error 28 about 31 levels deep in a Debug build. It now reaches about 1,490,
  and vb6.exe's 7,227 nested If blocks fit. On Linux the stack no longer follows `ulimit -s`: under
  `ulimit -s unlimited`, runaway recursion took memory instead of raising Error 28. macOS keeps its first thread,
  which its UI needs, and its 8 MiB.
- **A recursion also stops with Error 28 at 5,000 calls deep** (#3250). Under F5 and in a made EXE the debugger
  yields every 15 ms, which hides the stack's depth from the check that raises Error 28, so on the bigger stack a
  runaway recursion would have taken memory without end. vb6.exe goes deeper, to 258,825 calls.

- **CI tests the pinned foreign language servers** (#3211). A runner image's own servers on PATH are skipped,
  so Windows CI drives the pinned, digest-verified clangd 22.1.6 rather than the image's 20.1.8, and each job log
  names the server, source and version it used. `HEXIDE_FOREIGN_LSP_NO_PATH=1` does the same locally.

- **Contributed language servers respect session trust changes** (#3177). Disabling an add-in or revoking its consent unregisters its servers immediately. Launch checks also refuse disabled, unconsented or revoked add-ins, naming the add-in in the connection attempt.

- **Automation double clicks stay double clicks on busy machines** (#3167). `interact` with `click` and
  `double` controls the input timestamps, so slow handlers or frame waits cannot turn the second press
  into another single click. Plain clicks keep real timestamps.

- **TreeView label-edit timing tests use a controlled scheduler** (#3143), checking the host's double-click
  interval without wall-clock waits. An internal scheduling seam retains the normal dispatcher timer.

- **Diagnostics delivery keeps draining when a subscriber throws** (#2985), so queued reports
  and rename clears still arrive. Pull diagnostics for a retired document name are ignored until
  it is reopened.

- **External run-control tests use the per-test composition owner** (#2968), ensuring their language servers are disposed after each case.

- **`hover` says how long a tip took to open** (#2949). When a tip opens, the reply gives how long after the move it
  was seen. When none opens and the control declares a tip, the reply quotes its `ToolTip.ShowDelay` and, when that is
  longer than the watch, says to pass a longer `dwellMs`, so a tip's delay no longer has to be found by trying several.

- **Code lens discovery no longer resolves every lens at once** (#2906, transport and scheduling slices).
  Callers can resolve individual lenses on their issuing connection with cancellation. A document lens
  session resolves only its supplied viewport, cancels work outside it and refetches on server refresh.
  Code windows reserve space above member text using the editor font metrics, drawing resolved lenses
  without changing text or line numbers. Clicking runs declared server commands or fills the existing
  Find References pane from supplied locations.
  Edits refresh on the diagnostics debounce, keeping existing labels until the answer arrives.
  A default-on, localized Options toggle persists visibility and stops lens requests while off.
  The assignable Run code lens command provides keyboard access, with a choice for multiple lenses on
  the caret line. Lens rows retain the editor background under selection and current-line highlighting.

- **In-box languages follow Windows 11 tier 1** (#2857): the combined language-pack stack ships
  39 languages and four regional variants, with English pack names and complete key coverage.
  Hindi, Persian and Urdu move to HexIDE.Localisation; Latin and Esperanto are removed.

- **Profile bundle reads allow symlinked ancestor directories** (#2829). The standalone reader resolves
  the profile directory before its protected walk, while continuing to refuse links inside the profile
  and retaining Windows directory handles for the whole bundle read.

- **Combine the reviewed ImageCombo, MonthView, Animation and FlatScrollBar controls** (#2827),
  preserving their runtime registrations, dispatch, explicit refusals and designer/file support in one stack.
  ImageCombo retains its internal object model, but carried forms refuse at F5 with #1092 until
  drawing and input land; ImageList image binding remains unfinished.

- **Make arrow-hold input tests deterministic** (#2827): drive FlatScrollBar and UpDown repeat
  handlers directly, checking timer intervals and release instead of counting wall-clock ticks.

- **In-box languages follow Windows 11 tier 1** (#2817). Hindi, Persian and Urdu move to separate
  packs in HexIDE.Localisation; Latin and Esperanto are removed. Saved selections of these languages
  fall back to English without an error.

- **Completion now declares its supported client capabilities** (#2670): all 25 completion kinds,
  preselection, plaintext documentation and insertion with whitespace preserved as sent. Snippet and
  insert/replace support remain undeclared until their editor implementations are available.

- **`interact`, `press_key` and `type_text` say what a running program printed** (#2657). On a running program's form,
  their replies now give `printed` and a note, as the run and stop replies have since #2531. So a click's
  `Debug.Print` lines arrive with the click, rather than having to be read from the Immediate window and diffed by
  hand. Actions in design mode reply as before.

- **`list_lsp_messages` explains its rows beside them** (#2564). A new `rowsNote`, present with any rows, says a request's
  reply is on its own row and why the sequence numbers have gaps that are not lost frames. The description used to say it,
  and is now within its budget.

- **CommonDialog uses the shared in-box library mechanism** (#1962). Removed its legacy constant lookup and carried-block warning exception; `TypeOf` takes its library from the registered component class.

- **Tool-window reopening has bounds coverage for Toolbox and Properties** (#895). Headless Dock tests
  check that default and resized panes return to their column at their previous size over repeated
  close/show cycles, including at 150% scaling. The existing reopening fixes are unchanged.

- **Form Layout starts closed** on first run and after Reset Layout (#531). Open it from View → Form Layout when needed; existing saved layouts keep their arrangement.

- **Profiles replace personalities** (#504): select embedded or installed bundles and explicit directories
  with `--profile`. Keep the three built-ins' References placement and New Project offerings, and apply
  profile defaults, themes, keymaps and translations below explicit user choices. The removed personality
  flag now explains its replacement and exits before opening a window. `--profile none` provides a
  recovery launch with Classic, Default and neutral English, bypassing saved settings/layout, user
  keymaps/translations and add-ins. Settings, layout and translation edits stay in memory; explicit
  project paths and new-project flags still work. Language confirmation and automation replies explain
  that recovery language changes last only for the session. Automation replies stay in English
  after switching the chrome language. Profile messages are translated in every shipped language pack.

- **New Project and Add Project agree on profile templates** (#504, #418). The toolbar flyout uses
  the profile's ordered offerings and disables unsupported entries. Empty profiles explain how to
  continue by opening an existing project.

- **Unsupported Help entry points are no longer offered** (#46). Removed the inert Contents, Index, Search and Technical Support menu items and the disabled Help buttons in New Project and References.

### Fixed

- **TreeView label edits stay visible on PageDown and PageUp** (#3352). The single-line editor no longer
  scrolls its text vertically out of sight; Escape still cancels the edit.

- **Public constants, fixed-length strings, UDT variables and Declare statements in classes and forms are refused** (#3350), with VB6’s object-module Public-member message. Constants, fixed-length strings and Declare statements stop at load; UDT members stop when instance storage is allocated. This extends the Public-array check using [#3349’s measurements](docs/vb6-fidelity-oracle/a-public-array-in-an-object-module.md).

- **Default debug shortcuts now match their documented bindings** (#3343). Step Over, Step Out,
  Run to Cursor, Quick Watch and Set Next Statement have their factory gestures. The Default pack
  also correctly documents Edit Watch as unbound.

- **The Properties window gives descriptions more room by default** (#3342). The bottom pane fits the property name and three description lines; its splitter still allows more room for longer text.

- **IDE dialogs no longer offer minimize or add a taskbar button** (#3341), including IDE message/input boxes and language-server prompts. A modal dialog can no longer be minimized while leaving the IDE disabled behind it.

- **Completion asks at the start of the word** (#3340). Servers that filter by the text before the
  request position can return the full list of names in scope; the editor selects the typed prefix
  itself. Complete Word uses the same position and still inserts a unique prefix match immediately.

- **The licence guard accepts Testing Platform source filenames** (#3337). Its filename check now
  requires a licence-file shape and a GPL-family token, while still rejecting GPL and LGPL licence files.

- **A Public array in a class or a form** (#3335): a program declaring one is now refused as it loads, with vb6.exe's
  compile error, "Constants, fixed-length strings, arrays, user-defined types and Declare statements not allowed as
  Public members of object modules". It used to run.

- **New Project templates wrap into rows** (#3294). The startup dialog scrolls vertically instead of
  cutting off templates in a single horizontal row; every template remains reachable by keyboard.

- **A running form without menus no longer lists an empty hidden menu bar in `dump_visual_tree`** (#3276), with either value of `interactiveOnly`. Forms with menus keep their bar.

- **Hovering a running form now reports what its MouseMove handler printed** (#3274). The reply waits
  for the program as other actions do, and reports Debug.Print output once, with a note saying how much
  was printed or that nothing was. Design-mode hovers keep their existing replies.

- **Dragging a text selection in a short Immediate or code window no longer overflows the stack**
  (#3273). A guard stops AvaloniaEdit's caret scrolling from recursively extending the selection while
  the pointer remains held.

- **The long-concatenation parser regression no longer depends on CI timing** (#3259). It checks that
  module startup completes in SLL without falling back to LL, so a runner pause cannot fail it.

- **The Integration suite no longer leaves language servers running, and fails if a test does** (#3256).
  Tests that build the application's real composition now dispose its language-server client, so the
  bundled VB6 server it starts for a test's VB6 document ends with that test. A suite-level guard fails
  the run on any server still running at the end, and names the test that started it. A leaked
  client's background reads were one way for another thread to take the UI dispatcher between tests,
  which ends the headless session (#2530).

- **Deep recursive call stacks fit in automation replies** (#3252). `get_call_stack` collapses consecutive
  identical frames with a count, reports the full depth as `totalFrames`, and returns at most 200 rows by
  default. Optional `maxFrames` changes the limit; a truncation note explains how to ask for more.

- **Adjacent output items work in `Print #`, `Write #` and `Debug.Print`** (#3248). A sign after `Spc` or
  `Tab` starts a new item; measured `&` and `=` forms instead report a syntax error at load and in the
  language server. Calls such as `F (2)` remain calls, and long `Debug.Print` concatenations keep the fast
  SLL startup parse. Member expressions such as `tab.Caption` stay together when the receiver
  shares a positioning keyword.

- **An error raised deep in a recursion reaches its handler in linear time** (#3247, #3250). An error raised N calls
  deep cost N² to surface, for three reasons. Each procedure threw it on with the whole .NET stack trace gathered
  below; each frame asking whether the error halts scanned down to its handler again; and under a debugger, waiting
  frames' continuations nested inside one another as it unwound. That was 18 s at 1,200 calls in a Debug build, and
  over a minute for Error 28 at 5,000 calls in a made EXE. They now take 0.9 s and about 1 s. Stop and End deep in a
  recursion unwind the same way, where a Stop 113,000 calls deep had taken gigabytes. Err, Erl and every On Error
  handler see the error as before.

- **Slow-shutdown tests keep their server alive past the exit wait** (#3244). Slow mode drains the
  connection after answering shutdown, then waits one minute after disconnect, so Windows pipe
  flushes complete and a disconnect cannot bypass the exit delay. Test writes and flushes time out.
  A missing timeout kill now reports the stop duration before checking the exit-handler lock.

- **The Error 28 expression-chain tests give the same answer on every runner** (#3243). Each runs its program
  on a 1 MiB thread of its own, where a 10,000-term chain must overflow. They ran on the test runner's own
  thread, whose stack is whatever the host gives it. On Linux CI that is 16 MiB, where the chain sometimes fit
  and ran to the end without raising Error 28.

- **Process shutdown tests tolerate timer precision differences** (#3231). Their elapsed-time lower
  bound follows the configured exit timeout with a 50 ms tolerance, avoiding a spurious Windows ARM
  failure when the runtime timer fires just before the high-resolution counter reaches the timeout.

- **`run_project` keeps output printed while the startup form loads** (#3229). The output buffer now
  starts afresh when the runner claims a run, before any code can execute, so a fast `Form_Load` no
  longer loses its `Debug.Print` lines when the run becomes active.

- **A language server still exiting after an answered shutdown is reported as stopped** (#3217).
  When the IDE ends a server that answered `shutdown` and was sent `exit` but was still running after
  the exit wait, the connection now says it stopped, not that it crashed with a killed process's exit
  code. A server that exits on its own with an error, or never answers `shutdown`, is still reported
  as a fault.

- **For Each rejects invalid collection expressions** (#3213). Both parsers require a reference and
  reject the bare literal keywords Nothing, True, False, Null and Empty. Executed scalar declarations raise VB6's compile-error text; a raised late-bound
  class collection under Resume Next empties the loop element, as measured on VB6.

- **Deep nesting no longer stops a module loading** (#3210). The interpreter refused a module whose `If`
  blocks nested more than 58 deep, or whose parentheses nested more than 291 deep, as "nesting too deep",
  although vb6.exe builds 7,227 and 7,643. Parsing now runs on its own 64 MiB stack and accepts both, and a
  module nested that deep parses in well under a second. Running code nested several hundred levels deep can
  still raise Error 28, Out of stack space, before VB6 would (#3228). A syntax error's message now quotes only
  the line the error is on, not the module's text before it.

- **A detached code window stays suspended** (#3205). A folding timer whose UI continuation was
  already queued when the view detached no longer resumes code lenses or sends another lens request.

- **Opening a project an add-in reads again with different path casing is refused on Windows too** (#3201).
  It was loaded a second time; it is now refused as already open, as a `.vbp` already is.

- **Opening an already loaded project with different path casing is refused on Windows** (#3201).
  Project opening, archive container identity and user sidecars share the host's path case rule;
  project paths remain case-sensitive on other platforms.

- **EditorRedockTests makes its three edits in one dispatcher turn** (#3197). On a loaded runner, one of its
  50 ms waits took 646 ms, past the client's 400 ms quiet period, so the edits went out as two syncs with a lens
  request each and the test saw 3 requests where it expects 2. The exact assertions are unchanged.

- **Code edits during a run explain when they take effect** (#3191). `set_file_content` and document-mode
  `type_text` keep editing without a prompt, and report that the run keeps its starting code until the
  next run. Debug-state and breakpoint replies name documents edited since the run started and explain
  why their line numbers may differ from the new text.

- **A server cancelling a language request no longer spends its failure warning** (#3190).
  `ServerCancelled` replies are logged at debug, leaving the once-per-method warning available for
  a later genuine failure on the same connection. Diagnostic pull retry rules are unchanged.

- **The C++ shutdown test no longer races clangd's standard-library index** (#3187). clangd builds that index in the
  background after the first file opens and won't exit until it's done, so on a loaded Windows runner it was still
  indexing past HexIDE's 2 s wait and was killed. The test now turns the index off in its own workspace and still
  requires exit code 0. HexIDE's wait itself is #3217.

- **Refused language-server edits explain why in the status bar** (#3175). Edits requested by a code
  lens, a code-action command or another command, and unsolicited edits, show the refusal reason
  instead of leaving the user with no feedback. HexIDE’s own refusal messages use the selected IDE
  language and identify the edit as not applied, rather than crediting the refusal to the server.
  Server messages also use localized prefixes; their text stays as sent. The server’s command-required
  and unavailable-applier replies remain English.

- **Mouse-opened context menus start with nothing selected** (#3164). Enter does nothing until an item
  is selected; Down or Up selects the first or last enabled item. Keyboard opening still selects the
  first enabled item, and routed commands keep their editor target while navigating or scrolling.

- **The conversation trace test passes in every local time zone** (#3156). Its expected timestamp now
  follows the fixture's local-time conversion, including on the Pacific-time Windows on Arm runner.
  The trace continues to display local time.

- **Scrolling context menus open on their first enabled command** (#3155). Menu scroll buttons no longer
  take opening focus or replace the code editor as the routed-command target, so applicable commands stay
  enabled while the menu scrolls. Run code lens and Quick Fixes remain in the code window's menu.

- **Consecutive server edits can update an unsaved open module without saving between them** (#3154).
  Unversioned edits check the request’s buffer snapshot instead of refusing unsaved changes alone;
  edits to a target changed since that snapshot still refuse the whole operation, even after the
  typing reaches the server. Overlapping commands use the oldest unanswered command's snapshot;
  unsolicited edits remain refused.

- **The DAP parity corpus instructions pin the test culture to `en-US`** (#3153). Step 1 gives the direct
  test-executable command with xUnit's `-culture en-US`, so a different machine locale does not hide
  comparisons behind `locale-differs`.

- **Popup bounds use the inspected window's coordinates** (#3149). `inspect_element` places context
  menus and their items in the same coordinate space as other controls, so snapshot scale and offsets
  place them correctly. If that position cannot be read, `boundingRect` is unavailable and `boundingRectNote`
  explains why.

- **`press_key` names the context menu that received Escape from a Watches row** (#3146), using
  the menu's window-rooted path. An addressing walk that cannot reach its receiver no longer reports
  an unrelated ancestor's path, while folded controls retain their nearest addressable ancestor's path.

- **The DAP parity runner compares Erl using line labels** (#3133). It starts the RDCore adapter with
  `Configuration__Environment__ErlLineNumbering=LineLabel` in the child's environment, records the setting
  and its reason in the results header, and accepts `--erl-line-numbering DocumentLine` for a document-line comparison.

- **Project Explorer hides “Show hidden archive entries” for ordinary projects** (#3131). The toggle
  appears only for an archive root or node selection, letting the menu focus its first applicable entry.

- **Right-click menus opened by `interact` appear at the click** (#3130). Click arrival updates
  Avalonia's pointer placement position, so context menus and context flyouts no longer open at the screen origin.
  Press and release also use window input, cancelling the tooltip timer started by arrival.

- **Escape sent to a tree node closes its ancestor's focused menu** (#3129). `press_key` keeps
  focus in an open attached menu or flyout placed on an ancestor of the target for unmodified Escape,
  and names the popup receiver's path in the reply.

- **Fresh clones build in Visual Studio as well as with `dotnet build`** (#3124). The IDE tests now
  use a pinned Roslyn package rather than the .NET SDK's internal compiler folder, and Windows CI
  builds the solution with Visual Studio's MSBuild to guard both layouts.

- **Server workspace edits keep keyboard focus in the originating code window** (#3123). Running a
  code lens command that edits another member preserves the active tab and caret, applying edits
  without activating the target tabs.

- **View and code-window context menus use distinct access keys** (#3121). Latin-script packs keep
  Call Hierarchy's assigned key and the first other entry's key when labels clash. Later entries use
  their first free letter, preferring word starts, or lose their marker when every letter is taken.
  Re-keyed labels include Color Palette, Bookmarks, Toggle Breakpoint and Expand Selection; previously
  unmarked entries receive a key where available. Tool titles and the no-fix message have separate menu labels so tabs
  and messages do not show mnemonic underscores. Already unique keys stay unchanged, and disabled entries count.
  VB6's own mnemonics were not measured: this follows HexIDE's menu-order policy rather than claiming
  VB6 mnemonic fidelity.

  Across both menus, 189 entries have no key because all their letters are taken (regional variants
  count separately): `ca` 7, `cs` 2, `da` 7, `de` 7, `en` 5, `en-GB` 4, `es` 7, `es-MX` 7, `et` 7,
  `eu` 12, `fi` 12, `fr` 11, `fr-CA` 11, `gl` 9, `hr` 5, `id` 9, `it` 10, `lt` 2, `lv` 4, `nb` 7,
  `nl` 6, `pl` 3, `pt` 4, `pt-BR` 4, `ro` 4, `sl` 8, `sr-Latn` 6, `sv` 6, `tr` 3.

- **Quick Fixes sends each server its own diagnostic range** (#3120). When several servers diagnose
  the caret, each receives the range of its own first diagnostic and only its diagnostics. Servers
  without a diagnostic there are not asked, matching the lone-server behaviour.

- **Dialog buttons show what the keyboard will activate** (#3117). IDE message boxes, server prompts
  and all dialogs hosted by the IDE window manager show a dotted keyboard-focus rectangle. Declared
  default buttons have a heavier border, using the active theme's colours. Shared desktop dialog hosts give an
  unfocused dialog keyboard focus on its default button or first input/tab stop. Save Changes Yes is the
  default; Find/Replace opens on Find What and Add Watch on Expression.

- **Call Hierarchy has menu access keys and usable results height** (#3114). Its Edit and code context
  entries have separate localized labels, with access keys distinct from the navigation commands.
  Both results panes target six rows in the current font, taking only spare document height above
  the 120 DIP floor and accepting fewer rows in short windows. Each show posts one adjustment;
  sibling borrowing was dropped after live checks exposed infinite layout loops.

- **Implementation and Type Definition use free menu access keys where available** (#3110). Latin-script
  packs avoid collisions with every sibling entry, including disabled commands, in View and the code
  context menu, while keeping keys that were already unique.

- **Opening a context menu or flyout keeps its owner's automation paths stable** (#3109). Attached
  popup content has its own `ContextMenu` or `Flyout` segment in `dump_visual_tree`, addressable by
  `inspect_element`, `interact` and `press_key`, rather than renumbering the owner's children.

- **Navigation menu labels form one translated family** (#3096). Implementation and Type Definition
  now match the older go-to commands' phrasing, have distinct navigation access keys in Latin-script
  packs, and use bare nouns in the View menu.

- **Find References grows a short results slot to show six rows** (#3094). Opening or bringing results
  forward borrows available height from the document area without crossing its 120 DIP minimum, saves the new
  layout, and leaves an already tall enough slot unchanged. This also covers navigation and code-lens
  results shown in the same pane. Short windows keep the document tabs and code visible even when
  fewer results fit; collapsed saved layouts are repaired and layouts below the minimum are not saved.

- **Window geometry survives a forced stop after restoring, moving or resizing** (#3088). Saves wait
  for a 250 ms pause in changes, and maximized sessions retain their normal restore bounds instead of
  saving the screen-sized geometry as a normal window. Native maximize event ordering preserves the
  earlier normal bounds, and a maximized relaunch applies the saved normal size before maximizing.
  Normal restore bounds, including the first launch's default size, fit the destination screen's working area while
  respecting the window's minimum size, so restoring on a smaller screen keeps the window visible.
  On Windows, fitting uses the drawn frame when DWM is available, excluding invisible resize borders
  so a saved size that visibly fits is preserved and clamped windows reach the working-area edge.

- **Opening a tool pane no longer asks language servers for unchanged code again** (#3082). Code windows
  retain their lenses, semantic tokens, inlay hints and folds across re-docking. Edits still send changes
  and refresh analysis, scrolling still resolves visible lenses, and server code-lens refresh still works.

- **Cross-file go-to navigation lands at the server’s requested column** (#3074), converting the protocol’s
  zero-based column to the editor service’s one-based column.

- **Rename asks supporting language servers before opening the prompt** (#3073). A returned range or
  placeholder supplies the initial name; a refusal prevents the prompt and shows the server's reason.
  Servers without preparation support keep the existing behavior.

- **JSON configuration errors name the right line and column** (#3065). Language Servers reports
  1-based editor positions, converting UTF-8 byte offsets to character columns for non-ASCII text,
  with localized labels and the original JSON path. Logs keep English.

- **Closing an unsaved code window keeps its edited text on language servers** (#3064), so rename and
  Find References can still use the project's current code. Reopening preserves the server document and
  its versions; save, discard or removal releases it. Closing a clean window still closes its server document.

- **Diagnostic tips show the server hover when it arrives** (#3057). An already open code-editor tip updates to show diagnostic messages first, followed by Markdown quick info, including rich diagnostic content with related locations.

- **Completion keys follow VB6's List Members and Complete Word** (#3056). Enter accepts the selected
  item before starting a new line, Tab accepts without a break, and Space accepts before typing a space.
  An unmatched or empty word leaves no item selected; Tab still accepts the first item. Complete Word
  inserts a unique prefix match immediately. Enter in a snippet choice picker accepts without a line break.

- **External debug stops can open Locals and Call Stack without an error in Immediate** (#3048).
  Re-docking the code editor invalidates callbacks from its previous attachment, so they cannot access
  detached state or interfere with the re-attached editor. Inspection failures now log their exception
  and stack at Debug level.

- **Rename keeps the source window active** (#3047). A successful rename returns to the name in the
  window where it started. Refusals open no windows, and stale versions or unsaved changes name the
  member and explain whether to try again or save first, in every shipped language.

- **Markdown hover list markers align with linked text** (#3043). Bullets and numbers share a link caption's measured baseline across font families and sizes.

- **Editor hover tips close while typing or opening completion or signature help** (#3042), so they no longer cover the editing popups. Moving into a tip still keeps its links reachable.

- **Inspecting the Immediate window keeps its newest output** (#3035). Its editor's `value` keeps the
  last 4,000 characters and its owner's `Document` member the last 200. Truncation suffixes say which
  end was dropped, how much, and how to read more; ordinary text fields still keep their start.

- **Language-server configuration problems follow the IDE language** (#3031). Translated explanations
  preserve JSON keys, values, paths and diagnostic details, update when the language changes, and keep
  English messages in logs. Selectable problem lines wrap within the tab, single unknown fields use
  singular headings, and quoted option values follow each language’s quotation style.

- **The Language Servers window scrolls to its last line** (#3030). Configuration problems and the
  restart note beneath the server cards can be brought fully into view at 100% and 150% scaling.

- **Data-bound text keeps a stable automation path** (#3028). `dump_visual_tree` addresses a
  TextBlock or SelectableTextBlock bound to data by position unless it has an AutomationId or x:Name.
  Resource labels keep their text paths, and current text still resolves as an alternative address.
  A stale text address now explains positional addressing and asks the caller to dump again.

- **`inspect_element` bounds long values without burying a control's layout** (#3027). Member values
  are cut at 200 characters, a control's own value at 4,000, with full lengths stated. A note counts
  cut members and points to a control showing the value to read more.

- **Language server stage times follow the IDE language** (#3026). The millisecond unit is translated
  in every shipped language and updates when the language changes while the card is open.

- **A caught VB6 language-server crash exits with code 2** (#3023), so supervisors can distinguish
  it from a clean stop (0) and an `exit` without an answered `shutdown` (1).

- **Language & Debug Servers follows live language switches** (#3012). Status, connect-chain stages,
  age and empty-output text refresh immediately, and the label column shrinks again when switching
  to shorter labels. Age units are translated in every shipped language pack.

- **The bundled VB6 language server exits with code 1 when `exit` arrives without an answered
  `shutdown`** (#3007), as LSP requires. A normal shutdown still exits with code 0.

- **External debug state stays consistent through adapter cleanup** (#2992). `get_debug_state`
  reports Ended with `running: false`, retains the ended session after cleanup, and maps known stop
  reasons to its documented vocabulary with the raw adapter reason alongside. Its description explains
  external source paths; `stop_project` directs callers to Immediate for external output instead of
  claiming Debug.Print wrote nothing.

- **End explains when an unresponsive debug adapter had to be stopped** (#2980). Immediate shows one
  localized sentence with the adapter name and wait, followed only by nonempty standard error. Raw exit
  details remain in the log and Protocol Inspector instead of appearing beside a cancelled-task message.

- **`get_diagnostics` forgets renamed document URIs** (#2975). Saving an untitled module
  and Save As no longer leave the old URI in `analysed`; clean open
  documents still appear there. Late server diagnostics after a rename close cannot restore the old
  entry; reopening the URI allows diagnostics again.
  Ordinary editor closes retain analysis and continue accepting server diagnostics.

- **`list_lsp_messages` no longer tells a filtered poll that nothing was recorded** (#2974). When a filter such as
  `failuresOnly` matches nothing after `afterSequence` but other envelopes did arrive, the note now says how many came
  and that none matched, rather than suggesting the connection went quiet.

- **MCP steps, Continue, Break and Set Next Statement drive external debug sessions** (#2968). They use the Debug menu's adapter routes and report the next stop's source file and line, a running program or its end. Adapter refusals carry the adapter's message and say when stopped inspection was kept. Set Next Statement requires the adapter's goto capability and a saved source file. External Run To Cursor still refuses, explaining that its route lacks one-shot breakpoint support.
- **External debug MCP guidance describes the supported commands consistently** (#2968). Run To Cursor leads with its purpose before explaining the external limitation; Set Next Statement clarifies that external targets still take module names.

- **Unsupported external breakpoint options explain their refusal** (#2967): `hitCondition` and
  `logMessage` name the launch file and breakpoint entry in the selected language before the adapter starts.

- **`set_next_statement` no longer says no project is running during an external debug session** (#2966). It
  refuses as `get_locals`, `get_call_stack` and `evaluate` already did, saying the command isn't available for an
  external session, rather than contradicting `get_debug_state`'s "Paused".

- **External debug stops show break mode in the title bar** (#2965). The status bar shows Ready at a stop,
  matching interpreter breaks, and returns to the running message on resume. Ending the session restores
  the design title and Ready immediately, even while adapter cleanup is pending.

- **The bundled VB6 language server exits quietly after a clean stop** (#2956). Restarting a
  connection or closing the IDE no longer adds an exception stack to the protocol inspector's stderr
  rows. Unexpected input closure still reports the fault.

- **Trailing omitted arguments and plain ReDim element-type changes are refused** (#2938).
  Calls ending in a comma raise "Syntax error"; `ReDim Arr(10) As String` after `Dim Arr()`
  raises "Can't change data types of array elements". Interior omissions, omitted optional tails
  without a comma, and ReDim of a scalar Variant keep working.

- **Corpus smoke checks give the test host its own startup budget** (#2934). The worker's 60-second
  preparation watchdog starts at worker entry, with a separate five-minute limit for the host to get
  there. The three-second project run limit is unchanged.

- **The held-button keep-alive test no longer depends on how promptly a loaded runner wakes** (#2931). Its calls
  now come at exact 100 ms steps of a clock the test moves, where a delay that overran the 300 ms limit on Windows CI
  once abandoned a hold the test was still keeping. The abandonment half still runs on the real clock.

- **Workspace restarts send only `exit` after `shutdown`** (#2924). Debounced folder changes and project
  imports are dropped for the retiring connection; the replacement receives the current workspace and
  import. The protocol inspector marks requests still unanswered when their connection ends as Abandoned.

- **Document requests wait for their open after a language-server reconnect** (#2920). A hover,
  folding or semantic-token request made while tracked documents are being replayed reaches the new
  connection only after its document's `didOpen` has been written.

- **Servers declaring both project import and a workspace artefact receive file-operation notifications**
  (#2915). Import replaces generation, so matching create, rename and delete notifications are no longer
  suppressed for those entries.

- **External launch breakpoint conditions explain their refusal** (#2914), naming the launch file and
  breakpoint entry in the selected language before starting the adapter.

- **Retry semantic tokens after Content Modified** (#2913). A transient server reply now retries with
  backoff so lexical colours can be replaced without an edit. Cancellation, document changes and closes,
  and connection changes stop obsolete retries.

- **A refused external debug continue or step keeps the stopped inspection** (#2912). Call Stack, the
  selected frame's Locals and the current-line mark remain visible when the adapter refuses to resume;
  an accepted resume still clears inspection.

- **A language server is asked about a document only after it has been told the document is open** (#2909).
  A code window asked for folds and semantic tokens while its server was still importing the project, or while a
  second server claiming the same document started, so the request reached the server before the document's
  `didOpen`. Such a request now waits for the open and is still answered. Each server is now opened on the document
  as soon as it has started, so a server no longer waits for a slower one's start before it hears about it.

- **Language-server questions focus their first action when opened** (#2908, #3095). Enter chooses
  that action without first clicking or tabbing to a button; Escape still dismisses the question.
  External-link consent focuses No instead, so Enter declines.

- **The licence guard finds a working Python 3** (#2897). It tries `python3`, `python` and `py -3`,
  skipping failed or silent aliases such as the Windows Store stub, and reports what it looked for
  if none works.

- **A staying hover ends when the next hover lands in another window** (#2886). Two windows could both read as under the
  pointer at once. The reply now says which window the pointer left.

- **`get_form_controls` includes controls the designer carries view-only** (#2882). Each row gives its
  name, type, position and size, with `carried: true` and a note explaining that it cannot be moved, sized
  or set. Naming one with `controlName` returns that row too.

- **File conflicts deferred in the background appear when returning to the IDE** (#2881). Activation now
  waits for the window's active state before showing the consolidated prompt, without needing another disk change.

- **Export Picture asks about replacing one file** (#2880), showing its full path and a dialog title without an ellipsis, with dedicated translations.

- **`interact` `expand` and `collapse` on a Properties row reach the row's context menu** (#2877). The menu sits on an
  unnamed wrapper directly under the row, and aiming at the row was refused with no hint. When a node has no menu of
  its own and exactly one unnamed child has one, the verb now acts on that child, and the reply says so.

- **About and the shared desktop dialogs follow right-to-left languages** (#2874). About, InputBox,
  MessageBox, Font, Color and Print opened by the IDE now take the active language's reading direction,
  so their content mirrors under Arabic and Hebrew. A running program’s MsgBox and InputBox retain their left-to-right
  layout regardless of the IDE language. About’s Latin title, subtitle, copyright and signing-key
  fingerprints keep their left-to-right reading order within the mirrored dialog, with wrapped
  fingerprint characters kept inside their bounds.

- **Language server cards keep labels and values readable** (#2865). Label columns now fit their
  contents and align values with a gap, including longer translations. State ages advance while
  the view is shown, with the timer stopped when hidden or closed (#2866).

- **External file changes preserve unsaved code after it has been applied to the model** (#2862).
  Open module and form code windows still raise a conflict after a cancelled close or another operation
  copies their edits into the model; saving those edits restores silent reloads.

- **A language server that exits immediately is reported as having exited** (#2752), with its stderr
  and exit code, even when it exits before Windows can assign it to its process job.

- **Corpus tests find the repository root by both project trees** (#2735), rather than mistaking a test
  project's `Corpus` source folder for the repository's corpus on a case-insensitive filesystem. The
  affected test projects share one root helper per project.

- **A ListView with Appearance 1 and no border runs** (#2730): every Appearance and BorderStyle combination
  uses VB6's measured client edge (2 pixels for Appearance 1, plus 1 for BorderStyle 1) for drawing, hit
  testing and mouse coordinates, so both saved corpus ListView forms run instead of refusing at F5.

- **ListView label editing uses the measured Report-view heights** (#2729): 17 pixels on opening and
  21 after typing, keeping the existing width calculation. Opening notifications with unchanged label
  text preserve the initial height. Zero padding, a thin border and vertically centred text keep the
  label readable inside both heights. The editor shares the label drawing font (Arial 11 DIP),
  avoiding clipping from a larger theme font.

- **Profile bundle reads retain their directory** (#2714). Replacing a bundle directory between reads
  no longer combines generations. A changed manifest refuses assembly with an internal retry code;
  the standalone loader is still not connected to IDE consumers.
  Windows now opens retained directories with list-directory access so their sharing restrictions
  prevent renaming the bundle or any ancestor while it loads.

- **New-project write replies identify the temporary working copy** (#2707). `set_control_property`,
  `add_control` and `set_file_content` explain what was written and that Save Project still asks where
  it belongs. Their descriptions distinguish a working copy from a pathless document, and
  `get_project_info` names projects with no chosen home.

- **Save Changes and File Conflict selected file names stay readable** (#2693). Both dialogs use the
  theme's highlight background and text colours whether or not the selected row has focus.

- **Completion applies server text edits and additional edits together** (#2670): accepting an item
  honours its replacement range and commits related edits as one undo step. A protected or invalid
  edit refuses the entire completion.

- **Profile containment test cleanup** (#2621): remove links and junctions before recursively deleting
  fixtures, and verify that their outside targets survive bundle cleanup.

- **Profile reader hardening** (#2621): cap each manifest and payload at 1 MiB, including files that
  grow during reading, refuse non-regular Linux files and non-disk Windows handles, and exclude
  Windows device names from bundle ids on every platform.

- **Profile reader Linux compatibility** (#2621): fall back to seekability checks when libc does not
  export `fstat` or the native stat layout is unsupported, preserving bounded reads and pipe refusal.

- **Profile reader macOS compatibility** (#2621): share the handle-relative, no-follow Unix walk with
  Linux using Darwin's open flags, preserve bounded reads and the seekability fallback, and report
  unsupported platforms as internal data errors rather than unhandled platform exceptions.

- **Composition tests stop their language servers after each test** (#2588). Recently closed document,
  workspace wiring and dock wiring tests now own and dispose their language registry rather than leaving
  bundled servers running until the test host exits. A source guard keeps new compositions under that owner.

- **A form with no focusable control raises its own focus events** (#2579). GotFocus follows
  Activate when shown; LostFocus precedes Deactivate when another form becomes active.

- **`scroll` no longer says a control has nothing to scroll when it draws its own scroll bar** (#2576). A running form's
  TreeView with rows out of view was answered as "nothing taller than its viewport". The refusal now says what `scroll`
  looked for. It names the control's own bar and its position, and the two routes that move it: `set_range_value` on
  the bar, and `wheel`.

- **`hover` says why a code window opened no tip** (#2574). "no tip opened" used to read the same for no language
  server, an empty answer, a slow one and a missed word. The reply now names the line, column and word at the point
  (or that it is below the text) and what the editor asked for there: each `textDocument/hover` request the capture
  recorded during the watch, with its server, sequence and outcome, or why none was asked, such as no server
  registered for the file's extension, a watch shorter than the editor's 400ms delay, Auto Quick Info off, or a paused
  program asking for an Auto Data Tip instead.

- **`CommonDialogFileResultTests` run on the headless session, like every other test that builds a control**
  (#2530). They were plain xunit tests, so they ran on xunit's own threads alongside the Avalonia tests.
  Building a `VBCommonDialog` there gave that thread a dispatcher of its own, and if that happened while the
  session was resetting the UI dispatcher for another test, that thread took the UI thread. The session's
  setup then failed and the Integration run hung. A Linux CI run showed this, with no language server
  involved.

- **The Integration suite runs its test classes one at a time** (#2530). Avalonia's headless session resets
  the UI dispatcher as each Avalonia test is set up. Any plain test that built an Avalonia object on another
  thread at that moment took the UI thread, the session's setup failed, and the run hung. With one class at
  a time, no other test runs during a setup. Full runs on Windows and WSL took no longer.

- **A code editor's language-server session posts to the UI dispatcher it was built on** (#2530). Its
  debounced change and its diagnostics arrive on pool threads. Each post used to ask `Dispatcher.UIThread`
  there, at the moment of posting. In a headless test run the UI dispatcher is cleared as each test is set
  up. So a session that one test never disposed could post during the next test's setup, make its pool
  thread the UI thread, and hang the Integration run. The session now takes the dispatcher when it's built.

- **The bundled VB6 server reports startup on its Language & Debug Servers card** (#2509). An
  informational message includes its name and version even with tracing off; file logging is unchanged.

- **Add-in signing outputs preserve existing files** (#2504). Key/trust-chain generation and
  revocation signing refuse to replace any existing output, including private keys. Failed signing
  and parity-harness writes remove only the output files created by that invocation.

- **Parity runs preserve existing output files** (#2504). The developer harness creates results,
  summaries and case files exclusively, refusing collisions instead of replacing earlier output.

- **Picture exports ask before replacing an existing file** (#2504). The question names the destination;
  declining or dismissing it preserves the existing bytes, including with scripted file-picker answers.

- **Partial-write tests cover project/group saves, package encoding and carried-file retries** (#2503).
  The tests check that a refused write preserves the destination and removes its staging file;
  carried-file saves retain their dirty state until a successful retry.

- **Language & Debug Servers refreshes its document counts after opens and closes** (#2485). A server's
  first document no longer leaves its card showing zero, including for Incremental sync servers such as
  ruff and texlab; closing a document or renaming it refreshes the count too.

- **A failed control preview no longer closes the form designer** (#2405, first slice). A control whose designer instance throws, while it is created, laid out or drawn, is logged and shown at its saved bounds with its name, failure reason and a localized view-only label; its canvas item is disabled. Converting the failed control to an inert carried block, including exclusion from property selection and verbatim saves after edits elsewhere, remains outstanding.

- **Running TreeViews scroll overflowing nodes** (#2359). Overflow bars, keyboard and wheel navigation, code selection and EnsureVisible now follow the measured VB6 viewport rules; Scroll False suppresses scrolling.
- **TreeView drawing leaves scrollbar layout alone** (#2359). Changing to a flat edge and repainting an overflowing tree no longer changes scrollbar layout during the render pass. Scrollbar arrangement also preserves the label editor host’s layout.

- **Stdio log identity tests keep their server alive through startup** (#2347). A test server now
  waits for an exit frame before writing stderr and exiting, avoiding a race with Windows job
  assignment. Each server's expected log callback is awaited explicitly, with received logs included
  in timeout diagnostics.

- **Toolbar dropdown arrows respond across their full strip** (#2328). Clicking anywhere in the
  full-height 13 px arrow strip raises MouseDown then ButtonDropDown, including beside the glyph. The strip is measured from the laid-out button’s right edge;
  a glyph extending onto the face no longer enlarges the dropdown hit target.

- **More TextBox keys reach KeyDown and KeyUp** (#2325). Letters, digits, Space,
  navigation/editing keys, F1–F9/F11/F12 and numeric keypad keys now carry the
  measured VB6 codes through the existing event queue and KeyPreview path.

- **Stepping off the end of an event handler leaves break mode** (#2320). The current-line highlight and inspection windows clear, the title returns to running mode, and Break is available again.

- **Missing project members share one localized load warning** (#2294). Forms, modules, classes, UserControls and property pages name the file as the `.vbp` does, without the host directory in the message. Their project entries remain preserved on save.

- **Moving a designer control preserves its size above 100% display scaling** (#2180). A drag no longer
  writes layout-rounded dimensions back into Width and Height; resizing starts from the declared size.

- **A control's Container and Parent can be read at run time** (#2064). Container returns the Frame or
  PictureBox holding it, or the form; Parent returns the form, including for a nested control. Both are
  objects, so their members can be read through the result. Moving a control by assigning Container is
  still unimplemented; the unexplained Timer and Data Container reads are unchanged.

- **A form keeps its saved MousePointer at run time** (#1970). `Me.MousePointer` reads the value loaded from
  the `.frm`, including `1 - Arrow` and `11 - Hourglass`, and the window shows that cursor, as assigning the
  property in code does.

- **MousePointer defaults to `0 - Default` in the designer** (#1970), matching the runtime on forms and
  supported intrinsic controls. Saving omits a default-valued MousePointer and retains `1 - Arrow`.
  Reading MousePointer on a Shape or Line raises error 438.

- **The Object Browser fills its VBA library when a language server becomes ready** (#1957). A browser
  opened before the server starts replaces its status row with built-ins without requiring VBA or All
  Libraries to be selected again.

- **Unsaved projects give language servers no invented workspace root** (#1382). Servers requiring a
  workspace launch argument wait with a reason and start when the first project save completes, without
  reopening a document. Scratch file storage never becomes a workspace or artifact root, including on
  subsequent opens and later unsaved projects.

- **Saved FlatScrollBars open without an unsupported-control warning** (#1097), while their designer
  placeholders remain view-only. FlatScrollBar and UpDown share the MSComCtl2 component library.

- **Saved FlatScrollBars are selectable in the designer** (#1097): blocks with supported properties load
  as real controls, keep their saved values and round-trip byte for byte. Unmodelled blocks remain carried;
  own property fields stay unavailable, as for RichTextBox.

- **Animation projects open without an unsupported-control warning** (#1096) for the
  top-level blocks the runtime supports. Their designer placeholders remain view-only.
  Animation shares the MSComCtl2 library with UpDown and the other Common Controls-2 controls.

- **Splitting a described declaration's trailing comment keeps its attributes** (#1037). Enter, Shift+Enter,
  paste, Insert File and `type_text` move the remaining comment after the attribute run, leaving the caret
  before it. Add-in range edits that would separate the declaration from its attributes are refused.

- **Rename can update several modules together** (#1024). Every edited member opens in a code window,
  with one undo step per module and a status message naming the module count. The whole rename is
  refused if any target is unresolved, read-only, protected or stale; edits against disk also require
  no unsaved changes. Versioned document edits are now declared and accepted from language servers.

- **VB6 build timeout message** (#979): a build stopped after 180 seconds now says so and explains that
  vb6.exe may have been waiting on a dialog, instead of blaming the project file for an empty log.

- **A Property Get, Let and Set are reached by their name** (#977). A standard module's `x = PG` read Empty with no error,
  and inside a class or form its own `Full = First & "!"` gave `"!"`. Reads, calls and assignments now reach the accessors:
  a standard module's from anywhere, bare, with parentheses or through the module's name, with parameters and array
  results, and a class's or form's own inside it, before a standard module's of the same name. A property with a Let and
  no Get is assigned to. The invalid uses raise vb6.exe's errors when they run; a Let into a property whose Get returns a
  live object, with no Let, is not measured and is refused. Measured native and p-code. With Codex.

- **A Font changed through a retained reference keeps VB6's size and stays shared** (#934). Setting an Arial
  font's Size to Integer 14 through `Set f = Label1.Font` reads back Currency 14.25, as vb6.exe gives it, and
  reaches every control sharing that font.

- **PictureBox AutoSize fits bitmap pictures in the designer** (#922, first slice). Assigning a bitmap resizes the box
  to its pixels plus its border in the same undo step: a 40 by 30 bitmap gives 44 by 34 pixels with the default border
  and 40 by 30 with none, saved as 660 by 510 and 600 by 450 twips as VB6 writes them. Loading a form, clearing its
  picture and assigning Width keep the saved or assigned size. A flat Fixed Single border or a non-bitmap picture,
  whose sizes aren't measured, refuses once as not implemented. Label and runtime AutoSize remain pending.

- **Control captions use grayscale antialiasing** (#917). Designer previews and running CheckBox and
  OptionButton captions no longer render outline fonts with jagged edges and broken-looking spacing.

- **Debug commands grey out when their handlers cannot act** (#733). Toggle Breakpoint and Run To Cursor require an active code window, and Edit Watch requires a selected watch. With the interpreter, Set Next Statement requires a code window and a paused statement, and Show Next Statement requires a paused statement. Enablement refreshes on tab, debugger and watch-selection changes; external-debugger command paths keep their existing enablement.

- **Save pickers for new forms and modules start in their saved project's folder** (#498). In an
  unsaved project, each picker after the first starts in the folder chosen by the previous picker in
  that save. The first picker's platform default is unchanged.

- **Form Editor is listed in the toolbar band's context menu** (#417). Its checkbox toggles the same persisted visibility setting as View > Toolbars.

- **User themes load without rebuilding** (#413). Place a JSON pack in the active user-data
  directory’s `themes` folder and select its filename in Options → Environment → Theme after launch.
  Shipped themes keep precedence; unreadable or invalid packs revert to Classic.
  Recovery mode (`--profile none`) skips user themes.

- **Polish tool-window translations retain their diacritics** (#350). Restored accents in 22 early
  entries, including Object Browser tooltips, project commands, properties and debugger columns.

- **Greek captions no longer show stray ampersands** (#347). Language and translation labels and
  designer and code-editor context menus omit access-key markers, following the non-Latin script
  convention. A pack guard preserves only the three keys that use literal ampersands.

- **Windows paths in language-server configuration explain their JSON escapes** (#263). Invalid escapes
  keep the parse location and suggest forward slashes or doubled backslashes; legal escapes that produce
  control characters in path fields, array arguments, and environment values warn with the field, index or
  variable name and likely intended path, leaving the value unchanged.

- **Save and Save As in a module code window write the module** (#244), including UserControls with a designer half. Both commands retain format-on-save instead of silently taking the form-only save path.

- **Numeric comparisons compare in VB6's measured pair domains** (#229), including Single/Double, Single/Long, Currency/Double and Decimal/Double. Boolean ordering treats True as -1 and False as 0.

- **Find and Replace remember the search text and options when reopened** (#81). The dialog reuses its view model, preserving replacement text, search scope and direction too.

- **Reopening a project restores its open editors and designers** (#51). Tab order and the active
  document are remembered on project close or clean IDE exit, in the user's configuration directory.
  Missing members are skipped; recovery mode leaves the saved session untouched.

- **Japanese and Chinese access keys** (#41): menu, dialog and editor labels now carry the English
  accelerator in parentheses, so Alt access keys remain available in Japanese, Simplified Chinese
  and Traditional Chinese.

- **Object Browser kind icons** (#11) now use cached, theme-tinted Fluent vector images in the
  classes, members and workspace search lists. The description and Copy Signature show the plain
  member signature without a placeholder glyph.

- **Toggle Breakpoint in the code window's context menu is enabled** (#8). It uses the same command and
  keymap gesture as the Debug menu, toggling a breakpoint on the active editor's caret line.

- **Shift+F2 opens View Definition** instead of Previous Bookmark in the default keymap (#3).
  Previous Bookmark has no default shortcut; F12 still goes to the definition.

## [0.4.0] — 2026-11-02

### Added

- **Read-only project resource reader** (#2625). Runs load the project's `ResFile32` file relative to
  its folder. The Win32 RES reader handles named and ordinal entries, counted string blocks and raw
  data, custom and bitmap resources. Multiple-language lookups are explicitly refused pending
  measurement; the `LoadRes*` intrinsic calls remain #2626.

- **`ObjPtr` supplies object identities** (#2622, first slice): Nothing returns zero, aliases share a
  non-zero Long, and different objects have different identities. These numbers support identity keys;
  they are not native addresses.

- **`StrPtr` and `VarPtr` supply String buffer and variable identities** (#2622): null and assigned
  empty Strings remain distinct, copies and ByVal parameters have separate identities, ByRef aliases
  share them, and Long, Integer, Currency and Variant array elements retain their measured steps.
  These Long numbers are identities for comparisons, not native addresses.

- **`get_process_windows` reads the windows of a program a VB6 program started with Shell** (#2591). Pass the process ID
  `Shell` returned to get each top-level window's title, Normal, Minimized, Maximized or Hidden state, bounds and
  whether it has the foreground. The processes it started are included, so a launcher that hands off still resolves.
  Only processes `Shell` started can be read, and nothing is sent to any window. Omit the ID to list what was started.

- **A hover can stay, so a snapshot shows what is drawn only under the pointer** (#2562). `hover` takes `stay`, which
  leaves the pointer on the control. Its pointer-over look, such as a flat toolbar button's hot image, and any tip it
  opened then appear in `take_snapshot`, which now draws an open tip with the window. The reply says whether the control
  reads as under the pointer, and `inspect_element` reports `isPointerOver`. The next click, drag or hover in that
  window ends a staying hover first.

- **`Width #` controls `Print #` line wrapping** (#2554). Widths from 0 to 255 use
  VB6's measured rounding and validation; items stay whole, commas wrap at the limit,
  and trailing semicolons retain the column. `Write #` remains unwrapped.

- **The tools that run, step, break and stop a program say what it printed** (#2531). `run_project`, `stop_project`,
  `continue_program`, `break_program`, the steps and `run_to_cursor` give in `printed` what the program wrote with
  `Debug.Print` since the run began, or since the last reply that reported its output. Their note says how much, or that
  it wrote nothing. Reading a program's output used to take finding the Immediate window's editor and inspecting it.

- **Partition buckets values into padded ranges** (#2526), including CLng argument conversion, a Null Number or Start giving Null, and the measured invalid-argument errors.

- **SLN and SYD depreciation functions** (#2524) return Double results matching every measured
  VB6 row to the bit, including negative SLN life and fractional SYD period. Zero SLN life and
  out-of-range SYD periods raise error 5. Named calls remain explicitly unimplemented; DDB stays
  refused because its measured precision is not yet reproduced.

- **`NPer`, `IPmt`, `PPmt` and `Rate` run measured financial calculations** (#2523), preserving
  VB6's exact Double results, signed zero and measured errors. Unmeasured argument edges refuse explicitly.

- **`Pmt`, `FV` and `PV` run measured loan and savings calculations** (#2523), retaining precision
  between the rounded base and the Double result. Unmeasured argument edges refuse explicitly.

- **`Shell` starts a program and returns its process ID as a Double** (#2522), as a function or a
  statement with an explicit measured window style. Window styles set the new process's show state on Windows; Linux and macOS accept the
  styles without changing window state. Missing programs, empty paths, folders and Null arguments
  report the measured VB6 errors.
  Unmeasured styles, omitted-style launches, non-string path conversions and other launch failures
  remain explicitly not implemented.

- **CVErr creates Error Variants** (#2520), with measured argument rounding and range checks,
  type inspection, explicit conversions and ordered Error comparisons. Print, Write, Debug.Print
  and MsgBox display Error values. Comparing an Error selector with a number raises Type mismatch
  and Resume Next runs that Case's body. Implicit numeric and string uses raise Type mismatch;
  unmeasured conversions remain explicitly unimplemented.

- **CallByName** (#2519) calls class methods and reads or writes fields and properties, including object
  properties, ByRef writeback, Optional and ParamArray arguments. Expressions and statements, including
  `VBA.CallByName`, also support Collection `Add`/`Item` and `Err.Number`. Unmeasured combinations refuse
  explicitly rather than returning a guessed value.

- **`FormatDateTime` formats dates and times in the program’s locale** (#2516), with all five named formats, date serials and strings, Empty and Null, and measured argument conversions and errors.

- **Corpus project open/save smoke checks** (#2505, open/save lane): a headless test opens each
  in-tree corpus project and projects under `HEXIDE_ROUNDTRIP_CORPUS`, saves a scratch copy through the
  real project service, and reports crashes, refusals and byte differences, including companion files.
  It retains exact before/after evidence for differing files and runs in the normal suite and CI.
  Five hand-authored demo forms now use VB6's recorded trailing spaces on block openers, resolving
  their 66 cosmetic line differences.

- **Corpus project startup and Make smoke checks** (#2505): the sweep runs bounded headless startup
  through the Standalone runner entry point and checks Make in the scratch copy, reporting completion,
  time limits, refusals and crashes per project. `VB6_TEMPLATES` joins the optional corpus roots.

- **`inspect_element` reports the mouse pointer a control shows** (#2453). Nothing did, so a program's MousePointer and
  `Screen.MousePointer` over it could not be checked in the running IDE. `pointer` gives the MousePointer by number and
  name, the pointer drawn, where it is set, and whether it is the control's own, an override above it, or a style's.

- **The status bar shows that the automation server is listening** (#2451). A Debug build started with `--server-port`
  shows `MCP :5140`, with the session after it when started with `--session`, so several sessions' IDEs can be told
  apart. Hovering shows the endpoint, the process and build directory, that anything on the machine able to open the
  port can drive the IDE, the last call, and calls refused from other sessions. A dot lights while a call runs. A
  release build has no server and no section.

- **`inspect_element` gives the text of every item a list holds** (#2450). A list makes rows only for what shows, so a
  dump saw ten of Options' 31 languages, and nothing said what the rest were called. A list, open dropdown or tab
  control now reports `items`, each one's text as `select` matches it, rows out of view included.

- **`answer_next_file_dialog` can answer a multi-select Open with several files** (#2408). `paths` names every file of
  one answer, in order, and only an Open that allows several takes them all, so Add File, and a running program's
  multi-select Open, can be driven with several. A save, or an Open that takes one file, answers it as cancelled rather
  than with its first file, and a reply says so.

- **Language servers can import the current project through a declared command** (#2363). Imports run
  after initialization and on debounced project changes, replacing generated workspace files. Reply
  notes appear in server output, failures appear as configuration problems, and open-document diagnostics
  are refreshed until the server starts requesting refreshes itself.

- **TreeView labels can be edited with a click or from code** (#2360). A click on the selected label
  opens the shared editor after the host double-click interval; double-clicks and manual-mode clicks
  do not edit. StartLabelEdit works in either mode. The editor selects the original text, grows with
  typing, and supports cancellable BeforeLabelEdit and AfterLabelEdit events and a mutable NewString.

- **`get_window_state` and `set_window_state` reach any window** (#2357). Both take the optional `window` the tree
  tools take, and act on the IDE's own window when it is omitted, as before. A running form is minimized, maximized,
  restored, moved or resized as its caption buttons and frame would do it, so its Resize and Activate run, and the reply
  waits for the program. Anything a person couldn't do is refused: a state the form has no button for, resizing a fixed
  border, or changing a disabled window or one under a modal dialog. The reply's `name` says which window was read.

- **Toolbar buttons can select images from a bound ImageList at run time** (#2334). Index and key selectors
  retain their measured types and reject missing images without changing the previous value. Unbinding clears
  button images, and bound lists reject removal and clearing. Saved Toolbars now bind their named ImageList at form load, including a list later in the file, while
  preserving the form and companion byte for byte. Empty lists raise the measured error; replacing a bound list
  clears the image selectors and resizes the buttons for 16 or 32 px images, keeping the caption-width rule.
  Bound images now draw at the measured offsets, with paint-time masking, flat hot images and disabled images.
  An index absent from the selected alternate list draws nothing.

- **Design controls on SSTab pages** (#2301). Click a caption to switch pages, draw a control onto the
  current page, or move one between pages by cut and paste. Saving maintains page lists and the measured
  75000-twip offset for hidden controls. Page switching does not add an undo entry; VB6's designer undo
  behavior has not been measured.

- **CommonDialog.ShowColor uses the IDE’s managed RGB chooser** (#2299). Flags 0 starts at black;
  cdlCCRGBInit starts at Color. OK writes the selected colour back, and Cancel preserves it, raising
  32755 under CancelError. Other colour flags and system-colour initialization remain explicitly
  unimplemented. Action 3 uses the same chooser.
- **CommonDialog.ShowPrinter uses a managed dialog** for the measured Flags 0 configuration, including
  through Action 5. OK returns Copies 1, Orientation 1 and hDC 0; Cancel preserves values and honours
  CancelError. Actual printing, native printer DCs and unmeasured configurations remain unimplemented.

- **`set_lock_key` sets Caps Lock, Num Lock, Insert or Scroll Lock on or off as programs running in HexIDE read it**
  (#2296). It exists so a StatusBar key panel's on state can be live-checked. On Windows a StatusBar reads the toggle
  the operating system keeps, which `press_key` can't change. The machine's own keyboard is untouched, and `Keyboard`
  returns a key to it. Every reply lists all four keys with their state and its source (`Set`, `Keyboard` or `Window`).
  A StatusBar on screen repaints at once. DEBUG-only, like the automation server.

- **File creation notifications reach matching language servers** (#2248). First saves and Save As to
  new files report creation after writing succeeds. Create, rename and delete senders respect server
  filters, the registry routes them across language claims, and initialize advertises support.
  Disk rename and delete hooks remain outstanding; explorer name and membership changes leave files in place.

- **A drag can hold its button between calls** (#2231). `interact click "20,20 to 100,60 hold"` stops after its held
  moves with the button still down, so `take_snapshot`, `dump_visual_tree` and `inspect_element` see what shows only
  mid-drag: the designer's draw preview, Dock's drop targets, a control's outline as it moves. A new `release` action
  lets it go, at an optional point it moves to first, with an ordinary drag's reply. While a button is held, every
  reply carries a `heldButton` note, and `click`, `double_click` and `hover` are refused. With no call for 120 s the
  hold is abandoned: Escape, then a release where it was pressed. A designer draw still places a control then, and a
  resize snaps to the grid; the note names any undo step the designer recorded.

- **View conditional branches as twinBASIC 32-bit or 64-bit** in the code window (#2154). The preview uses measured compiler constants and the known VB6 project type, leaving unknown build information ungreyed; F5 and language-server diagnostics are unchanged.

- **Code editors use a language server's semantic colours** (#2054). Full semantic tokens from the highest-priority provider override lexical colours on their ranges, with lexical colouring retained elsewhere and after an empty or failed response.

- **Preview a form without running the project** (#2003). The designer's Preview Form command opens a
  live window for checking layout, resizing and tab order without executing VB code. Closing the preview
  leaves the design unchanged.

- **Invisible controls are dimmed in the form designer** (#2002). Controls with Visible=False use a
  theme-supplied dimming mask while remaining selectable and editable.

- **The form designer can show tab order** (#2001). Enable the tab-order toggle to see each modelled
  control's TabIndex over it; a crossed-out index marks TabStop off. The overlay is read-only.

- **Reopen closed windows** (#2000). The Window menu lists the last 20 closed code, designer and tool
  document tabs for this session. Reopen Closed Window (Ctrl+Shift+T, Cmd+Shift+T on macOS; rebindable)
  restores the newest tab and its code caret. Removing a document or closing its project prunes its history.

- **Recent Edits** (#1999): a View menu panel lists the latest edited code locations with their module,
  line and preview. Select a row to return to it. Nearby edits within 10 lines replace the newest entry;
  the session keeps up to 50 locations, follows text moving above them, and drops deleted lines or removed
  documents. Closing and reopening a code tab keeps its history.

- **Parentheses coloured by nesting depth** (#1996). The VB6 code editor uses the theme's three-colour palette for matching pairs and marks unmatched parentheses. Editor → General can turn the colouring off.

- **Code-window indent guides** (#1995). Faint theme-coloured guides follow indentation, with the caret's innermost block emphasised. Turn them off in Options → Editor → General.

- **Sticky scroll in code windows** (#1994). Enclosing block headers supplied by the language server stay
  above the code while scrolling; clicking a header navigates to it. Editor → General offers an off switch
  and a maximum depth, defaulting to three headers.

- **Paste as Comment** in the code window's Edit menu (#1993). Paste clipboard lines prefixed with an
  apostrophe at the paste's indentation, as one undo step. Keymap packs can bind `PasteAsCommentCommand`.

- **An archive project's `.twin` sources grey their inactive `#If` branches** (#1691), as their compiler
  compiles them at 32-bit: its built-ins as measured on beta 1024, a call to a VB function reading 0, and the build
  type from the project's settings. A branch on something the archive does not record, such as the build number,
  stays ungreyed rather than guessed.

- **Window frame inspection and snapshots** (#1595, #2182). `inspect_element` reads native Windows style masks,
  and `take_snapshot` accepts `includeFrame: true` to capture the whole window, caption and border included, as
  Windows renders it, so a window covering it is not in the image, with its open popups drawn over it. Should
  Windows render nothing, the rectangle is copied from the screen and the note says how much of it other windows
  cover. Unsupported captures explain why instead of returning a client-only image: a minimized window is told to
  restore it with `set_window_state`, and a refusal no longer describes an image it never took.

- **`Controls.Add` and `Controls.Remove` run** (#1556), as vb6.exe runs them: a control made at run time from its class
  name, at its class's size, hidden, in the form's font, into the form or a Frame or PictureBox, and taken out again
  with everything it holds. Every refusal is vb6.exe's, measured.
- **A control held in a form's `WithEvents` variable raises the variable's handlers** (#1556), after its own and the
  one set last first. That holds for a design-time control too, where the variable heard nothing before.

- **Token scanning before commits and in CI** (#1224). A pinned, checksum-verified gitleaks binary
  scans pull-request commits with redacted output. Its publisher checksum is pinned in CI, and
  new-branch pushes scan only commits since their merge base with main. Contributors can enable a
  staged-change hook that blocks commits on findings or a missing scanner; default rules remain enabled
  without broad exclusions.

- **Inactive conditional branches are greyed in the code window** (#1113). A selector after the Procedure box
  previews VB6, VBA 32-bit or VBA 64-bit constants. It appears only in modules with `#If`, defaults to VB6,
  and stays in that window for the session. It changes neither F5 nor language-server diagnostics.

- **Managed class libraries named by a project's references** (#1107). The shared library model now
  supplies class factories and enums as well as controls. Referenced GlobalMultiUse classes are created
  on first use, shared until Sub Main returns, and resolved in reference order behind project procedures
  and ahead of VBA. `New` and `CreateObject` make separate managed instances; unknown libraries keep
  their existing behaviour.

- **The Components dialog adds an in-box control library to a project** (#1103). It lists MSCOMCTL.OCX and
  COMCTL32.OCX under VB6's own descriptions, and writes or drops their `Object=` lines where VB6 does. The toolbox then
  offers the library's ProgressBar, and placing one declares the library in its form's header. Unchecking a library a
  form uses is refused with VB6's message.

- **RichTextBox RTF groundwork** (#1100): an internal document reader and writer reproduces
  the measured empty, plain-text and formatted Arial/Courier New RTF shapes, with character formatting,
  font/colour tables, ANSI escapes and tabs. Paragraph formatting retains alignment, indents, tab stops and the measured bullet groups.
  CR LF text writes paragraph breaks; selection output reproduces the measured empty and single-format
  documents with reduced tables. Unsupported RTF and unmeasured selection output are refused rather than flattened. A minimal RichTextBox form now runs: code can load RTF, edit its text and selection, apply character formatting and paragraph alignment, and read TextRTF or SelRTF back. The default member is TextRTF. The measured RichTextBox form and RTF companion now round-trip byte for byte. Inline TextRTF moves to the companion, single-line boxes keep their first paragraph, and toolbox placement seeds the control name. Saved own properties are carried without editable designer fields; unsupported inline RTF keeps the designer read-only. A first input/drawing slice accepts typing, Enter and Backspace with the measured key/selection/change order, respects saved ReadOnly/MaxLength and protected text, and draws character formatting with inverse selection. Click/double-click selection, the measured Select All/tab/alignment shortcuts, plain-text copy/paste, Undo and caret scrolling now work. Saved background, disabled text/background and flat/inset borders now draw. Enabled works at runtime; Appearance and BorderStyle read their saved values and reject writes with error 383. Scalar SelColor reads, other runtime members, unmeasured mouse-selection edges, formatted clipboard transfer and paragraph drawing remain unimplemented.

- **RichTextBox keyboard selection** (#1100): Shift navigation includes paragraph breaks and the hidden final break; command arrows follow the measured punctuation word stops, and plain arrows collapse a selection to the pressed side. Wrapped vertical and non-ASCII word endpoints remain refused.

- **RichTextBox runtime editing limits** (#1100): Locked and SelProtected now control real input
  while allowing code edits. MultiLine and ScrollBars read saved values and reject runtime writes.
  MaxLength supports the recorded limits, truncates assigned Text and drops SelText appended at a full
  limit; unmeasured RTF truncation and other limit edges remain refused.

- **MaskedEdBox runtime foundation** (#1098): MSMask's measured library identity and 66 type-library
  constants, plus a runtime component for code-assigned mask text, prompt-free readings, default Text
  reads, selection reads and Change dispatch. Keyboard input now inserts, removes and navigates across
  mask literals, with measured key event order and ValidationError dispatch. The measured `##-##` SelText
  writes insert, truncate overflow and reset the caret to zero. Clipboard shortcuts now copy/cut with
  ClipMode and paste with measured insertion, skipped characters, truncated overflow and validation.
  Saved forms now retain VB6’s property order and duplicate OLE modes, always write PromptChar, omit Text,
  and preserve an unedited save. Formatting, toolbox placement, designer support and runtime loading
  of saved own properties remain unfinished.

- **DTPicker's date object model** (#1094, first slice): preserved minimal MSComCtl2.DTPicker blocks
  can run with Value, date parts, ranges, checkbox Null state and format settings. Input, drawing
  and editable file persistence remain pending.

- **DTPicker calendar colours** (#1094, part A continuation): calendar colours now read their measured system defaults as Longs. Recorded MousePointer
  and OLEDropMode values, numeric dates, default Value access and part-conversion errors are supported;
  minimal carried blocks are labelled view-only in the designer.

- **MSCOMCT2.OCX is recognised as an in-box control library** (#1093 prerequisite). Project > Components
  can select its reference under Microsoft Windows Common Controls-2 6.0 (SP6). UpDown, DTPicker,
  MonthView, Animation and FlatScrollBar are catalogued; their implementations are tracked separately.

- **UpDown's runtime object model** (#1093, part A): an unbound `MSComCtl2.UpDown` on a form runs,
  and code can set its range and value and bind a TextBox or Label buddy. Value assignments raise Change
  even when the value stays the same; reversed ranges, buddy synchronization and binding geometry follow
  the VB6 measurements. Editable file persistence remains for part C.

- **UpDown mouse input and drawing** (#1093, part B): spin buttons step on press, repeat while held,
  and raise UpClick or DownClick after MouseUp, including a release outside the control. Increment
  snapping, reversed ranges, wrapping and synchronized buddy text follow the measured VB6 behaviour.
  Vertical and horizontal buttons render with disabled arrows; Up moves focus to the preceding control.

- **UpDown saved forms** (#1093, part C): saved range, orientation and buddy bindings load without
  changing the file's geometry, including a buddy declared later in the form. The original control
  blocks survive saving and form edits verbatim and remain read-only in the designer. The toolbox
  offers the measured 255 × 750 twip default.

- **SSTab runtime pages** (#1090): minimal SSTab blocks run as containers, and programs can switch tabs,
  edit indexed captions and flags, and observe page positions, TabStop changes and Click(PreviousTab).
  The remaining object-model members are still outstanding; unsupported members
  refuse explicitly.

- **SSTab saved pages** (#1090): VB6-designed forms load their page lists, captions and implemented
  properties, including HIMETRIC dimensions and the tab-zero load quirk. Both measured saved forms
  round-trip with their companion pictures; extender edits retain the measured property order and
  saved page lists and flags, and TabHeight is always written. Designer edits remain read-only until
  page membership can be maintained.

- **SSTab tab strip** (#1090): running forms draw captions and tab rows, bringing the active row
  forward. Style controls caption widths and active bolding; TabHeight, TabMaxWidth, hidden-tab gaps,
  horizontal clipped side captions, wrapping and the focus rectangle follow the measured behaviour.
  Appearance/picture binding and the saved-state designer preview remain for later slices.

- **SSTab input** (#1090): caption clicks switch pages between MouseDown and MouseUp, with
  Click reporting the previous tab. Arrow keys and Ctrl+Tab switch while the SSTab has focus;
  Alt caption access keys switch from page controls and focus the SSTab. Disabled captions take
  focus without changing pages, and page clicks do not raise Click.

- **TabStrip collection groundwork** (#1089): an internal Tabs/Tab model reproduces the measured
  case-sensitive keys, rounded indexes, insertion and removal selection rules, and code-driven
  BeforeClick/Click cancellation.
- **TabStrip interpreter binding** (#1089): a form holding an MSComctlLib.TabStrip can run,
  add tabs and switch selection from code. Tab properties, scalar tags and
  integer/Boolean strip properties are reachable; code BeforeClick cancellation still raises Click.
  Unsupported members remain refused.
- **TabStrip keyboard selection** (#1089): Left, Right, Home and End select tabs through BeforeClick
  and Click; cancelling an arrow-key selection keeps the old selection and suppresses Click. Alt plus a caption's
  mnemonic selects its tab and takes focus.
- **TabStrip mouse selection** (#1089): MouseUp sees the new selection, and cancelling BeforeClick
  still raises Click. Selected-tab and right clicks skip BeforeClick; Style 1 MultiSelect supports plain
  and Ctrl clicks. Double-clicks raise a second MouseUp and Click without MouseDown, and client-area
  clicks raise nothing.
- **TabStrip top-row drawing** (#1089): selected tabs appear raised, selected buttons pressed, and flat
  buttons support separators. Caption mnemonics are underlined; disabled tabs draw like enabled ones.
  Other layouts and TabStyle 1 drawing remain refused.
- **TabStrip saved files** (#1089): measured property bags round-trip with VB6's property order,
  typed tab images and the mandatory Tabs block. A missing ImageKey for ImageVarType 8 refuses the
  whole form. Saved scalar properties and plain tabs load at run time, with tab sizes converted from
  HIMETRIC; saved ImageList/Font runtime loading remains refused.

- **The Toolbar's runtime model** (#1088): MSComctlLib.Toolbar now loads with its measured scalar defaults,
  Style and TextAlignment validation, and size assignments that leave its size unchanged. Runtime Buttons and
  ButtonMenus now support collection operations, scalar properties and measured errors. Measured single-row
  geometry supports the probed Alpha caption, separators and placeholders, repositioning
  buttons after collection, visibility, style and placeholder-width changes. Arbitrary caption sizing,
  ImageList binding remains unimplemented. Runtime buttons now draw their captions, check/group
  values, separators, placeholders and dropdown arrows without images.
  Toolbar clicks now raise the measured mouse/Click/ButtonClick sequence, update check/group values,
  suppress ButtonClick for an already pressed group button, and open dropdown menus with ButtonDropDown.
  Menu activation raises ButtonMenuClick alone; double-click's second press raises ButtonClick alone.
  The VB6-saved Toolbar form and ImageList companion now have byte-for-byte preservation coverage,
  including a save after editing the form's caption. Configured Toolbar blocks remain carried verbatim;
  designed top-level bars now load the measured button/menu properties at run time, including numeric
  image indexes, tooltips and twips placeholder widths. ImageList binding and designer property-bag editing
  remain unfinished.

- **StatusBar drawing and events** (#1087, part B): panels draw their text and borders, key indicators grey out while off, and Simple style displays SimpleText. PanelClick and PanelDblClick follow VB6's measured mouse-event order, for either button, with no panel events in Simple style and no events on the size grip. Native file persistence and panel pictures remain for part C.

- **The StatusBar object model** (#1087, part A): Microsoft Windows Common Controls 6.0 offers a StatusBar in the toolbox, with Panels, a panel's default Text member, measured collection errors and fixed Width/MinWidth rules. Drawing, events, layout-derived values and native file persistence follow in later parts; unmeasured members refuse explicitly.

- **StatusBar spring layout values** (#1087, first part B slice): panels share spare width above their minimums, reserve the size grip, and keep fixed widths when they overflow. Count and panel Index now return the measured Integer type. Drawing, pointer events and nonempty Contents sizing remain for the rest of part B.

- **StatusBar saved panels** (#1087, first part C slice): saved panel values load with HIMETRIC widths, and the measured forms and picture companions save byte for byte. TextSave snapshots and panel picture records are preserved.

- **StatusBar panel pictures at run time** (#1087): saved bitmap and icon pictures can be read, assigned between panels or loaded from a file with LoadPicture, drawn beside the text, and cleared with Nothing. Saved panel blocks retain their original bytes; unmeasured picture members refuse explicitly.

- **TreeView programs can build and walk a tree on a form** (#1086). MSComctlLib.TreeView owns its
  Nodes collection, with measured placement, navigation, subtree moves/removal, Integer count/index
  wrapping, expansion, selection, checkbox resets and independent root/child sorting. Code can read and
  write Style, LineStyle, Indentation, LabelEdit, HideSelection, Checkboxes, Sorted, SelectedItem and
  PathSeparator. Runtime trees draw their visible labels, lines, expansion buttons, checkboxes and
  selection, following Style, LineStyle and Indentation. Mouse and measured keyboard input now
  raise node, expansion and click events, including SingleSel ordering and code-driven expansion.
  Native TreeView forms now save their own properties in VB6's order, preserving HIMETRIC Indentation,
  ImageList references, Font and OLE settings, with the saved form and companion reproduced byte for byte.
  Owned values remain unavailable for designer editing. ImageList binding, label editing and remaining
  runtime members follow later.

- **ListView begins its object model** (#1085): MSComctlLib ListViews now expose ListItems and item text, keys,
  selection and checkbox state, including VB6's first-item selection and retained SelectedItem pointer. ImageList binding,
  mouse input, label editing and file persistence remain for later slices.

- **ListView begins keyboard interaction** (#1085): Down and Home select items, Ctrl+Down moves only
  the focus, and Space toggles a focused checkbox before raising ItemCheck and ItemClick.
  Mouse input, label editing and file persistence remain for later slices.

- **ListView upward keyboard navigation** (#1085): Up selects the previous item and raises ItemClick;
  Ctrl+Up moves focus without changing the selection or raising ItemClick.

- **ListView columns, sub-items and sorting** (#1085): the measured object model now includes ColumnHeaders,
  ListSubItems, SubItems, control properties and FindItem. Sorting preserves case-only ties and defers newly
  added sorted items until the dispatcher turns at DoEvents. ImageList binding and files follow separately.

- **ListView draws its four views** (#1085): item text, Report headers and subitems, selection and focus,
  and checkboxes now render. Report columns support alignment, grid lines and full-row selection. ImageList
  icons await #1083; mouse input, label editing and saved files follow separately.

- **ListView mouse interaction** (#1085): labels select items with ItemClick, checkboxes raise ItemCheck,
  and headers raise ColumnClick alone. Right-click selects and raises Click; Ctrl/Shift selection and
  double-click event order follow the measured VB6 behavior. Label editing and saved files follow separately.

- **ListView label editing** (#1085): StartLabelEdit and automatic selected-label clicks open an inline editor.
  BeforeLabelEdit and AfterLabelEdit can cancel; changed Enter commits after the handler, while Escape and
  unchanged Enter leave the label alone. Navigation now raises KeyDown before item events and KeyUp afterwards.

- **ListView begins loading designed properties** (#1085): text-only saved controls load their scalar state,
  font name and size, and flat columns with HIMETRIC widths. Missing lines use the measured file defaults,
  and renamed properties follow VB6's file names. ImageList bindings and file writing remain refused.

- **ListView saves preserve designed controls** (#1085): saved property runs and columns round-trip with their
  binary companions, and designer geometry, name, tag and visibility edits keep the carried data intact.
  The designer shows the control name and optional checkbox. Own-property and column editing remains unavailable.

- **MSComctlLib ImageList** (#1083): the picture collection supports indexed and keyed lookup, insertion, removal, tags and enumeration. VB6's saved BMP/ICO images and their `.frx` records survive a save byte for byte. The toolbox places the invisible control at its measured size; native handles, drawing, overlays and icon extraction remain explicitly not implemented.

- **Tests and a spec for selection at an unterminated folded member attribute run** (#1019).
  Pin the retained behavior: Select All includes the run but leaves the caret at the declaration's end;
  select-to-document-end stops there and leaves the run out.

- **Named-pipe language-server stdout is captured** (#724) as its own `StandardOutput` kind, alongside
  stderr and exit codes in the Protocol Inspector and `list_lsp_messages`, including startup failures.

- **Unsaved work survives a crash** (#499). While the IDE runs it keeps a private copy of every unsaved document,
  outside the project and its sidecar. After a crash, a kill or a power cut, the next start offers it back. A project
  that was never saved is offered at start; a saved one when it is opened, matched by its path or, once the project
  has moved, by its project file. Restored work comes back unsaved, with the files on disk untouched, and the dialog
  names any file changed since the copy was taken. File → Recover Unsaved Work lists whatever is still waiting.
  [`docs/crash-recovery.md`](docs/crash-recovery.md) has the details.

- **Add-in capability declaration proposal** (#459): draft vocabulary, signed manifest field,
  legacy-manifest alternatives and contracts for consent and disclosure, awaiting security review.
  No add-in behaviour changes yet.

- **Language servers can request generated workspace files** (#455). Per-server, per-workspace consent
  is resettable in Options; existing files are protected by recorded content hashes and paths are checked
  through links. The first provider generates clangd compilation databases from carried C/C++ files and
  compiler arguments. Material project changes regenerate the file; watched-file notifications await #456.

- **Find searches the current project by default** (#372), including closed files and unsaved edits in
  open editors. Current Procedure and Selected Text join Current Module and All Open Documents;
  All Projects appears for a loaded project group. The dialog names the current project, structural
  scopes use the document's language server with a whole-document fallback, and Replace All asks
  before changing matches across files.

- **Preserved project items are visible** (#44). Project Explorer shows missing files and unsupported
  UserDocuments with distinct overlays and localized labels, retaining their paths and project-file entries.

### Changed

- **A `dump_visual_tree` reply with an empty tree says why** (#2725): the window holds no controls, with a pointer to
  `take_snapshot` for what is drawn on it, or `interactiveOnly` left out so many parts that show, with how to see them.

- **Replies around a running program's waits say three things a caller used to guess** (#2720):
  - a handler found running, at a MsgBox or paused at `Stop`, that the action or `continue_program` let finish: `(Command1_Click finished)`;
  - a form the program opened, by its name, with its caption where that differs: `the program opened Form2 ("Second")`;
  - which `window` reaches a window that holds a handler: `window "Question" reaches it`.

- **A Make run through `invoke_menu_item` says what it made, or why it made nothing** (#2671). The reply used to say
  only "Invoked". It now waits for Make to finish:
  - when Make wrote a program, it names the program and the project beside it, with their sizes, and says whether the
    program wrote over a file that was already there;
  - when it wrote nothing, it says why, such as a missing standalone runner, a cancelled dialog or a form HexIDE can't
    write as it was read, and whether a message box saying so is open;
  - a Make waiting at a save dialog that no answer was armed for is reported as that, at once.

- **`open_file` says when its caret is where `type_text` would be refused** (#2578). A caret put in a form's header or a
  member's Attribute lines now comes with a note naming that part and the nearest place that can be edited, by line,
  column and offset. The refusal from the next `type_text` used to be the first a caller heard of it.

- **`take_snapshot`'s reply gives the coordinate mapping with its own numbers** (#2565). A new `units` field maps
  `inspect_element`'s `boundingRect` into the image and the inline copy, and `pathNote` says each call writes a new file
  that nothing deletes. The description gave the formulas for a caller to fill in, and is now within its budget.

- **ParamArray forwarding with empty parentheses is covered by interpreter tests** (#2532).
  `T a()` already passes one element holding a copy of the array; the tests pin that shape and
  show that writing through the callee's element leaves the caller unchanged. The language
  inventory and interpreter spec now describe this supported case.

- **Absent language-server hover results explain their origin at debug level** (#2302): a missing
  connection, incomplete initialization, unavailable capability, or the server returning null. The
  notification-flood test includes client logs and server receipt in its failure diagnostics.

- **CI keeps evidence when the macOS runner runs out of memory.** A watchdog records free memory and the largest
  processes during the macOS test steps, and ends a run that is about to starve the runner, so its log survives. The
  ViewModel tests also name any test still running after two minutes (#2128).

- **`hover` reports where an opened tip is** (#1303). A tip closes when the hover ends, so no snapshot could show where it
  had opened. The reply now gives `tipBounds`, the tip's x, y, width and height in the window's device-independent units,
  as `inspect_element`'s `boundingRect`, and its offset from the point hovered. `hover`'s move through the window's input
  is now measured live on Windows and Linux: toolbar tips, quick info and Auto Data Tips, a disabled button, and the real
  mouse afterwards.

- **Toolbar designer persistence coverage** (#1088): a headless regression verifies that carried Toolbars
  offer no Properties-window edits, and that a surrounding form edit survives save and reload with the
  Toolbar blocks and companion bytes intact. Toolbar scalar properties and Buttons remain view-only
  until their property bags can be written faithfully.

- **Image Click coverage now pins VB6’s measured hit area** (#861): real pointer input tests cover an empty Image’s centre and far corner, a point past its edge, and Image versus Frame dispatch inside a Frame. The existing runtime behaviour already matches these measurements.

- **Documented the Linux program-locale limitation** (#798): separate `LC_TIME` and `LC_NUMERIC`
  overrides are not applied to date and number parsing and formatting. Runtime behaviour is unchanged.

- **CI cancels a pull request's superseded run when a newer push arrives.** Each push used to start a full run while the
  older one kept going, and superseded runs held the few macOS runners the whole queue shares. Pushes to a branch are
  never cancelled, so every commit on main is still built.

- **A hung test in CI fails its step after 30 minutes, and keeps its log.** Every test step in `build.yml` now
  has a timeout. A hang used to hold its job for GitHub's six-hour default, and cancelling the job to free the
  merge queue threw its log away, so nothing said which test hung. `WorkflowActionTests` checks that every
  test step keeps one.

- **twinBASIC and RDCore may be named anywhere in the tree, with attribution.** The hygiene check no longer limits
  either to a list of agreed files. The README carries their attribution, and the check fails if it goes missing.
  Every other third-party project is still named a file at a time (#2071).

- **Two PRs that each add a line to the fidelity oracle's index now merge without a conflict.** The index takes one line
  per subject at its foot, so any two PRs that both measured something collided there, though keeping both lines was
  always the answer. `.gitattributes` now merges that one file with git's built-in `union` driver.

### Fixed

- **Shell's quoted-program test retries temporary-directory cleanup** (#2741). A copied helper executable
  briefly held open after exit no longer fails the test; persistent access or I/O failures leave the
  directory for the OS temp cleaner.

- **Hover tests wait for the tooltip state they assert** (#2740), with a five-second bound and timeout
  diagnostics naming the condition, elapsed time, open state and tooltip content, instead of assuming
  the runner finishes within a fixed delay.

- **What `type_text` writes into a TextBox is drawn at once** (#2737). In its default mode, a write into a focused box,
  such as the Find dialog's, left the box drawing its previous text, though it held the new one and the app acted on
  it, so a snapshot showed the box empty. The write is now committed in the order the TextBox's own typing uses.

- **Make's question before replacing an existing archive is reported** (#2718). While Make asks, the reply to
  `invoke_menu_item` says so within about a second, naming the archive, where it used to wait 60 s and say Make was
  still writing. The `interact` that answers the box reports what Make then did. A No is reported as declining to
  replace the archive, where it read as a cancelled save dialog.

- **The Find dialog's boxes are named by their captions** (#2717): Find What, Replace With and the Search scope list now carry
  their localized caption, without the access-key underscore, as their accessible name. A screen reader announces which box
  it is, and automation reaches the Find What box as `Edit[Find What:]` rather than by position.

- **A `window` name shared by a dialog and the message box over it reaches the box** (#2712). The Find dialog's
  message box has the dialog's own title, so `window: "Find"` was refused with advice to name one by a title only it
  has, which neither has. The name now reaches the window on top, as a person can use only that one, and the dialog
  again once the box closes. Two windows of one name side by side are still refused, and the refusal now says which
  of them `auto` reaches.

- **Code-window selections now reach Find and add-ins** (#2711). Selecting text with the keyboard or mouse keeps the view-model in step, so Selected Text searches use the selected span and the add-in API reports it.

- **The Locals window and `get_locals` type a row as VB6's Locals window does** (#2700), from hexide-int's measurements of
  `vb6.exe`:
  - **A live object** reads as its declared type, then its class: `Object/Class1`, `Class1/Class1`, or
    `Variant/Object/Class1` in a Variant. A control under `Me` reads `Timer/Timer`, a Timer's `Parent` `Form/Form1`, and
    an `Item` row `Variant/Object/Timer`.
  - **`Nothing`** reads as the declared type alone, and a Variant's value as `Variant/String` or `Variant/Empty`.
  - **An array** is named without parentheses and typed by its element type and bounds, as `Class1(1 to 2)`.
  - **A class instance's variables** are sorted by name, Private included.
  - **A standard module** is listed by its name, typed `Module1/Module1`, with a `<No Variables>` row when it has none.

- **Options accessible names omit label colons** (#2680). The sticky-scroll depth spinner now has its
  row's localized name, and spinner buttons use numeric increase/decrease wording in Czech, Danish,
  Dutch, Finnish, German and Indonesian.

- **Saving a new project asks where to save its scratch forms and modules before the project file**
  (#2678). Save Project As and the first Save Project now write members at the chosen destinations,
  rather than leaving the project pointing into TEMP. Cancelling any picker writes nothing, and
  scratch files no longer read as saved.

- **Automation edits to a new project persist to its scratch files without opening a save picker**
  (#2678). Explicit Save commands still ask for a permanent destination.

- **Formatting and auto-close follow the editor’s Tab Width** (#2669). Formatting requests, including
  format-on-save, send the configured width and use spaces like the editor. Enter after a block opener
  indents the body by that width and keeps the closer aligned with the opener.

- **Unset optional LSP members are omitted from JSON** (#2668), including formatting's
  `trimTrailingWhitespace` and initialize's `foldingRange` capability. Members whose protocol types
  allow null retain it, and explicit values are preserved.

- **Picture properties can be loaded in the designer** (#2653). The Picture, Icon, DragIcon and MouseIcon
  rows now have an ellipsis button opening the IDE file picker. Pictures accepted by the existing reader
  replace the old picture as one undo step and save in the form’s companion file; unreadable files are
  refused without changing the old picture.

- **Unedited files brought in with Add File no longer ask to save when removed** (#2644). Forms,
  modules, classes, UserControls and property pages now establish the same unchanged baseline as
  members loaded with a project.

- **Signature help marks the active parameter in bold** (#2632), preserving the label's optional-parameter brackets instead of adding misleading brackets of its own.

- **ErrObject assignments and creation match VB6** (#2627). Assigning a Collection to `As ErrObject`
  raises 13 and preserves its reference; `New ErrObject` raises trappable -2147221164,
  "Class not registered", instead of a compile error.

- **`Mid$`, `MidB` and `MidB$` statements run** (#2623). Byte writes preserve untouched UTF-16 bytes,
  including writes across characters. `Mid` and `Mid$` raise error 94 for a Null target or replacement.

- **Pointer identities no longer enlarge every interpreter value** (#2622): String buffer identities
  share the value's existing payload slot, restoring the stack footprint used before `StrPtr` and
  `VarPtr` were added while preserving their identity relations.

- **Signature help keeps a trailing list highlighted for every argument past it** (#2599), as in `Array`,
  `Choose` and `Switch`. An extra argument to any other function still marks no parameter, since VB6 refuses it.

- **Serialization requirements are visible to OpenSpec** (#2587). Move the trailing requirements above
  Out of Scope without changing their content, and guard every live spec against requirements outside
  its Requirements section, reporting the file and line.

- **InputBox buttons follow the IDE language** (#2586), using the same OK and Cancel translations as the other runtime dialogs.

- **A line number or label on `End Sub`, `End Function` or `End Property` works** (#2561). `90 End Sub` stopped the module
  loading, and `Done: End Sub` read `Done` as a call. A `GoTo` to one now returns from the procedure, with the value a
  Function had, as vb6.exe does, measured native and p-code.

- **TreeView SetFocus, Move and ZOrder reach the shared control implementations** (#2557).
  Remaining method refusals name the member and its tracking issue, #1086.

- **Classes with `VB_PredeclaredId = True` have a default instance** (#2555). The class name creates it
  on first use, recreates it after `Set Class1 = Nothing`, and holds it until the program ends.

- **Lock and Unlock run instead of stopping the program** (#2553). Whole-file and record ranges
  prevent another file number in the program from reading or writing locked records; Close releases
  the locks. Both parsers now accept an omitted range start, such as `Lock #1, To 4`.

- **App file logging runs** (#2552). `StartLogging`, `LogEvent`, `LogMode` and `LogPath` follow the
  measured VB6 file output and mode values, including mode 18 disabling logging without overwriting.
  Default and NT logging deliberately write no Windows event, avoiding administrator-only event-source
  registration on the developer's machine.

- **App.HelpFile, App.StartMode, App.TaskVisible and IMEStatus are available** (#2551). The application
  properties retain measured types and writable values; IMEStatus reports Long zero without an IME.
  Unmeasured project help files and assignment conversions are refused explicitly.

- **Conditional-compilation greying tests wait for evaluation to finish** (#2533), with a ten-second
  upper bound instead of assuming the debounce has completed after 350 ms on a loaded machine.

- **A line number or label on a closing line works** (#2529). `30 Next`, `30 End If`, `40 Case Else` and the rest
  were syntax errors, so a module numbered on every line did not load, and `Skip: Next`, the usual way to go round a loop
  early, read `Skip` as a call. Now both parse, and a `GoTo` to such a line does what vb6.exe does, measured native and p-code:
  from inside, the block ends as if it had run to its end; from before, an If, Select Case or With is skipped and a Loop or
  Wend tests its condition. A `GoTo` from outside a For loop to its `Next` is refused, since vb6.exe hangs on it.

- **`Erl` runs** (#2528). An error handler that reported the line number, as numbered production code does in nearly
  every handler, stopped the run, so a handled error became a fatal one. `Erl` now answers as vb6.exe does, measured
  native and p-code: the line number at or above where the error was raised, in source order, in the procedure that
  trapped it, cleared where VB6 clears `Err` itself. A number on a closing line such as `30 Next` still stops the module loading (#2529).

- **Error handlers can read Err.HelpFile, Err.HelpContext and Err.LastDllError** (#2527).
  Raise accepts help arguments and supplies the measured default help topic; Clear resets the help fields.
  LastDllError is a read-only Long fixed at zero while Declare calls remain unsupported.

- **Reset closes every open file and flushes buffered writes** (#2521). `FileAttr` now returns the
  measured Output, Append, Random and Binary modes as Long values, with the measured argument and
  closed-file errors. Input mode remains explicitly unimplemented pending a VB6 measurement.

- **`LSet` and `RSet` justify String variables and Variants holding text or an Integer** (#2517).
  They preserve the target's current length, pad with spaces and keep the leftmost characters when
  truncating. A Null source raises error 94 and leaves the target unchanged. Fixed-length String
  declarations and UDT copying remain unimplemented.

- **A held button is abandoned after its idle limit even when the timer fires early** (#2514). A timer could fire a
  few microseconds before the clock said the limit had passed. The check then kept the hold, and nothing armed the
  timer again, so `click … hold` stayed held until the next tool call. The check now arms the timer again for the time
  left. This was also the intermittent timeout in the held-button idle test.

- **`match: "TextEditor"` finds the Immediate window's editor, and `inspect_element` reads its text** (#2513).
  `dump_visual_tree`'s description promised the first, but the editor was folded out of the tree, so a caller reading a
  program's `Debug.Print` output found nothing and could not tell an empty window from an unreachable one. The editor
  is now the node `…/None[Immediate.Editor]`, labelled with the window's name, and its `value` is the window's text,
  with `isReadOnly` beside it. A code window's editor still leaves its file to `get_file_content`.

- **Corpus smoke checks locate the repository on Windows** (#2505): root discovery now requires both
  `IDE/` and `LspServer/`, so the test source folder `Corpus/` cannot be mistaken for the in-tree corpus.

- **Corpus smoke saves account for measured reference-path rewrites** (#2505): expected bytes make
  same-drive reference paths relative, with each applied change listed in the report. Companion file
  paths compare using the host's case rules, avoiding false differences for VB6 template member names
  on Windows and macOS. All other byte differences still fail.

- **Make asks before replacing its derived project archive** (#2504). The question names the existing
  `.dll`; declining leaves both the archive and executable untouched. Saves use unique staging names,
  preserving user files at the former `.tmp` and `.hexide-tmp` paths.

- **Conversation exports preserve earlier files** (#2504). The Protocol Inspector asks before replacing
  either the messages or its derived manifest, and MCP exports use a fresh temporary directory per call.
  The file-safety audit records the remaining command risks and VB6 prompt measurements still needed.

- **Project and carried-file saves, Make outputs, exports and project sidecars flush before replacing
  each destination** (#2503). They share one writer that stages bytes in a unique sibling file and
  attempts cleanup after a refused write. Make keeps the runner's Unix permissions. External VB6
  compiler output still needs a measured staging contract.

- **Save-failure regression tests use portable failure injection** (#2503). Sidecar migration
  retries are tested through the atomic writer, and replacement-refusal assertions accept
  both I/O and access-denied errors while checking destination preservation and cleanup.

- **Forms terminate in project order at a run's end** (#2497), with the newest instance first within each form, as measured on VB6, instead of following instance creation order.

- **The `Forms` collection runs** (#2496). `For Each f In Forms: Unload f: Next`, the usual way a program with several
  forms closes them all from its main form's Unload, stopped the run as the program ended. `Forms`, `Forms.Count` and
  `Forms(i)` now answer as vb6.exe does, measured native and p-code: the loaded forms in the order they loaded, a For Each
  that walks the forms held when it began, and `Unload Forms(i)` by index.

- **A declared array passed as `arr()` passes the whole array** (#2474). Calls and expressions
  retain ByRef sharing and ByVal copying; `v()` on a Variant holding an array is refused with VB6's
  "Expected Sub, Function, or Property" error when the statement runs.

- **A procedure declared with a `ParamArray` runs.** Every call to one stopped the program with "ParamArray parameters are
  not yet supported", so a `Sub Log(ParamArray args())` or a `Function Max(ParamArray v())` never ran. The procedure now
  gets its remaining arguments as a Variant array from 0, and each element is its argument ByRef, so a store through one
  reaches the caller's variable, converted to its type, as in VB6.

- **The stdio process lifecycle test tolerates a server exiting during its Linux process check** (#2461).
  A disappearing `/proc` entry now counts as the process being gone rather than failing the test.

- **`type_text` types into a control that takes typing itself** (#2460). In keyboard mode, a running form's RichTextBox
  was refused as having no text surface, because the check went by the control's class. A control that takes focus is
  now sent the keys as a person's keyboard sends them, and the reply says if it took none of their text.

- **Saving an ActiveX EXE adds a missing `Retained=0` in VB6's measured place** (#2459), between adjacent `Unattended=0` and `ThreadPerObject=0` lines. Existing values, other project types and unmeasured layouts are preserved.

- **`get_window_state` says what its numbers measure** (#2454). It gave `x` and `y` in screen pixels and `width` and
  `height` in device-independent pixels, with nothing to say so or which edge `x` and `y` were. The reply now adds
  `scale`, the window's `outer`, `frame` and `client` rectangles in device-independent pixels, and a `units` line
  saying which is which.

- **Options lists expose their localized labels to accessibility and automation** (#2449). Language,
  Region, Theme and Keymap remain named even when disabled or empty; the rendered-surface guard now
  catches unnamed combo boxes and visits Options pages. The sticky-scroll depth spinner buttons also
  have localized Increase/Decrease names.

- **An array sent as text by a client with a stale schema is refused naming the cause** (#2446). A session that loaded a
  tool before it took an array parameter sends that array as a string, and was told only that it sent "a long string",
  and to read the schema it held, which was the stale thing. The refusal now says a client holding an older schema sends
  that, and that a new session is needed; and a long string is shown by its length and start.

- **A form’s own `ActiveControl` remembers its last focused control** (#2437, first slice), including
  while inactive or hidden. A form loaded but never shown, or loaded again after unloading, reads Nothing.
  Restoring focus and raising GotFocus on reactivation remain outstanding.

- **A shown form focuses its first eligible control in tab order** (#2436), after `Form_Activate`:
  disabled, hidden and non-tab-stop controls and Labels are passed over. Startup forms and modal
  forms follow the same rule. `Me.Show` inside `Form_Load` raises Activate and the initial GotFocus
  there, before a subsequent `SetFocus` moves the focus.

- **Bundled-server completion no longer waits behind document analysis** (#2434). It answers from the
  last parsed declarations, or static keywords and built-ins during the first parse, while diagnostics
  keep their existing scheduling. Debug logs separate queue
  waits, SLL/LL parse stages, diagnostics and completion time.

- **The Screen object runs** (#2431). `Screen.Width`, `Height`, `TwipsPerPixelX` and `Y`, `MousePointer`, `ActiveForm`,
  `ActiveControl`, `FontCount` and `Fonts` stopped every program that used them; they now answer as vb6.exe does, measured
  native and p-code. `Screen.MousePointer = vbHourglass` shows the hourglass over every form. `MouseIcon` is still refused.

- **`press_key` types the character a key types** (#2428). It sent the key's events alone, so Shift+K on a running
  TextBox raised no KeyPress and typed nothing, where a person's press types K. The character now goes between the
  key's KeyDown and KeyUp, except with Ctrl, Alt or Meta held and for control characters, and the reply says what was
  typed. In a code window a protected line refuses it, as it refuses typing.

- **End after Select All in a running single-line TextBox leaves the caret at the end** (#2423), so Backspace deletes the last character. Home collapses the selection to the start for Delete.

- **`press_key` and `type_text` press each modifier's own key around the key** (#2422). A person's Shift+K is Shift
  down, K down, K up, Shift up; the tools sent K alone with Shift as a flag, so a program never saw Shift's own
  KeyDown 16 or its KeyUp with mask 0. Each modifier now goes down before the key and up after it, and the reply
  names them.

- **A left click ends with MouseMove, as in VB6** (#2417). Forms, CommandButtons, Labels and Images raise one Button 0 move at the release point, on the control or form beneath the pointer. A Form, Label or Image double click raises none after its second release.

- **`take_snapshot` refuses a minimized window instead of returning its last frame** (#2416). The default capture draws
  through the compositor, which still held the frame from before the window was minimized, and returned it as if
  current. It is now refused, as the frame capture already was, with the `set_window_state` call that restores it.

- **`App.Title` uses the project file's name when `Title=` is absent** (#2413), matching VB6 under F5 even when `Name=` and `ExeName32=` differ.

- **A declaration with a type character takes that type** (#2409). `Dim s$` stopped its whole module loading, a
  local one stopped the run, and so did `Property Get P$()`, while `Sub F(n%)` ignored the character and took a
  Variant, so `F 2.6` gave `n` 2.6 instead of 3. Each now declares the type its As clause would: on `Dim`,
  `Private`, `Public` and `Static`, arrays included, on parameters, and on a Property Get's return. A character
  beside an As clause is VB6's compile error, as vb6.exe reports it.

- **`interact`'s `click` moves the pointer there before it presses** (#2407). A person's pointer arrives before it
  presses, so a running form's MouseMove comes before its MouseDown; a click raised none, so no program's MouseMove ran
  from one. A click where the last one left the pointer moves nothing first, as a still mouse sends no move, and the
  reply says which. Checked against real X input on Linux: the same events, in the same order.

- **`open_file` puts the caret at a line in a carried file** (#2403). It refused a line for a carried file and opened
  nothing, so a caller walked the caret there a key at a time. It now places it as in a code window, against the text
  `get_file_content` returns, opens a fold over it, and reads it back in the note.

- **One Next may close several loops, `Next j, i`** (#2393). It stopped its whole module loading, and the editor
  underlined it. It now runs as `Next j: Next i`, which is how vb6.exe runs it, for For and For Each loops alike and for
  any number of names. A list with a name missing, `Next j,` or `Next , i`, is still a syntax error, as it is in vb6.exe.

- **Running forms raise one Resize per window change** (#2389). Maximizing, restoring and dragging a
  corner report the settled client size once. Paint follows a native resize when the client grows;
  restoring from minimized does not reactivate the already active form.

- **Closed split tools keep their place across a restart** (#2387). Showing one again restores its own dock and saved split proportions, including late-registering add-in tools.

- **Docs-only pull requests report the Windows and macOS build checks** (#2382). Lightweight Ubuntu
  jobs report the same check names while the real builds stay skipped, so branch protection can finish.

- **`hover` says what took the tip** (#2376). The real mouse, resting over the IDE's window, gets moves from the platform
  as windows change around it, and the tip goes with them, as it does for a person. The reply offered that as one of two
  possible causes, and none when no tip was declared. It now reads the window's input during the watch and says whether
  the real mouse or another hover running at the same time moved the pointer, where to and over what, and what to do.

- **Absolute project paths work on Linux and macOS** (#2375). A path beginning `/` now opens the project
  instead of being silently ignored; recognized slash-prefixed flags still work.

- **Toolbar buttons size to their captions** (#2373). Buttons share the widest caption's width in the host font, update when captions change, and retain their dimensions after Clear. Narrow bars wrap when Wrappable is enabled; tabs and line breaks take the measured space without drawing on separate lines.

- **The held-button idle-limit test waits for abandonment to finish** (#2368), allowing up to 30 seconds
  for the timer and dispatcher under load instead of failing after two seconds.

- **`press_key` refuses a disabled control** (#2366). A disabled control can't take focus, so the tool raised the key
  on it as a routed event, which no person's keyboard could do, and reported `"success": true`. It now gives the refusal
  `interact` gives a disabled target (#2223), naming a container that disables it. `inspect_element`
  reports `isKeyboardFocusable` false for such a control, where it read true.

- **Edits cancel superseded language-server analysis before sending the changed text** (#2365).
  Diagnostic pulls, semantic tokens and folding requests for that document are asked to stop
  before the changed text is sent; completion and hover remain outstanding.

- **Language servers see existing source folders outside a project's directory at startup** (#2364).
  The project's own folder stays first; missing sources add no folder and shared folders are listed once.
  Servers opting into folder changes receive additions and removals before any project import,
  on the same debounced project-change trigger.

- **Project import notes reach the server log as readable text** (#2363). Diagnostic refresh support is
  declared during initialization; a server's refresh stops import fallback pulls only on its connection.

- **`set_tool_window_visible` hides only the window it names** (#2362). Hiding a tool window closed the whole dock it
  was in, so every tool tabbed with it closed too, while the reply named only the one asked for. It now closes that
  window alone, as its tab's close button does, and an add-in's tool window that unregisters does the same. The reply
  names the window as `get_tool_windows` does, says when nothing changed, and names any other window that opened or
  closed with it.

- **AI Chat widens a narrow dock on first display** (#2361). Its usable width comes from measuring
  the prompt, Send and New row. The document keeps its minimum width, and later user resizing is retained.

- **Read-only greying tests preserve their execution context while draining dispatcher jobs** (#2346),
  preventing a previously queued job from leaving a disposed test context behind for later delays.
  Pre-debounce assertions also run before dispatcher pumping can fire the timer.

- **`hover` reports a tooltip that is a control by the text it shows** (#2336). It read a tip by its `ToString`, so the
  designer's box for a carried control, whose tip is a StackPanel of three text blocks, answered
  `tip: Avalonia.Controls.StackPanel`. A tip that is a control now reads as its visible text blocks, in order, joined
  with ` / `, for an open tip and for a declared one. A tip with no text to read says so, rather than give a type's name.

- **Toolbar caption widths use GDI on Windows** (#2334), matching the measured whole-string
  extent of the control's font. Other hosts retain rounded per-glyph advances; ampersands still count.
  Exact Segoe UI regression widths are now gated on the oracle font build; other Windows
  builds are checked against their installed font’s GDI metrics.

- **Disabled Toolbar images keep their original colours** (#2334). A DisabledImageList image
  now paints at full opacity, while the disabled caption keeps its existing appearance.

- **The Locals window and `get_locals` list a Timer, a control array and a form's `Me` as VB6's Locals window does**
  (#2329), from hexide-int's measurements of `vb6.exe`:
  - **A Timer** lists VB6's seven rows: `[_Default]`, `Enabled`, `Index`, `Interval`, `Name`, `Parent` and `Tag`. Each
    has the member's declared type, so a Timer in no control array reads `Index` as `<Object not an array>`, with type
    `Integer`. Its `Parent` opens to the form.
  - **A control array** opens to `Item 1`, `Item 2` and on, counted from 1 whatever its elements' Index.
  - **A running form's `Me`** starts with `[_Default]`, then lists its properties, controls and own variables as one
    list sorted by name, then an `Item` row per control, in the order the form's file holds them.
  - **`get_locals`** marks a row that reopens an object already open above it with `repeats`, and leaves its children
    out.

- **A Toolbar no longer opens as unsupported** (#2327). Opening a project no longer says a top-level Toolbar "is not
  a supported control class". It runs, and a CommonDialog already loaded without the warning for the same reason. The
  designer's box for each now says "View-only in the designer" instead of "Not supported in HexIDE". A Toolbar
  holding controls, or one in a control array, still doesn't run, and keeps both the warning and the unsupported box.

- **Immediate-window output tests no longer race over the static Debug.Print sink** (#2326).
  The display and debug-console tests share a collection with parallelization disabled.

- **TextBox KeyDown and KeyUp handlers begin receiving input** (#2325, first slice).
  The measured k/K, Ctrl+A, Alt+X, Backspace, Delete, Left, F2 and modifier keys
  carry their VB6 key codes and Shift masks. Other controls remain pending.

- **TextBox KeyPress handlers can filter typing** (#2325). Setting KeyAscii to zero
  swallows a character; changing it changes the character typed. Form KeyPreview
  raises each form event before the control's, and Form KeyCode = 0 suppresses
  only the control's KeyDown.

- **`interact` `click` with `double` sends its second press when the first click replaced what it landed on** (#2324).
  A Toolbar rebuilds its buttons on each release. The second press was refused as "hidden or closed, or detached"
  because the element the first press landed on was gone, even when the addressed control was still there. So the
  Toolbar's measured double-click couldn't be checked live. Now the second press goes to whatever takes the pointer at
  that point by then, with a click count of 2, as a person's would. It is refused only when the window has gone or
  nothing is there, and the reply names the element when it differs from the first press's. It waits first for the
  window's next frame and for the render thread to apply it, as a person's second click comes after one, since hit-testing
  read the render thread's picture and missed a just-rebuilt button about one time in three.

- **An open flyout attached to a button is in `dump_visual_tree` and `take_snapshot`** (#2323). The dump and the
  snapshot reach a control's flyout from the control, and read `FlyoutBase.GetAttachedFlyout` only for controls
  other than buttons. A running Toolbar's dropdown is a MenuFlyout attached to its button, so the open menu was in
  neither: the snapshot left it out with no note, and `match: "First"` found nothing. The menu was open all the same.
  Now its items are under the button, addressable, and the snapshot shows the menu.

- **Integration tests name the test that leaves a culture behind** (#2322). The suite checks the current
  thread's culture and UI culture and both process defaults after each test against their startup values,
  so a culture leak fails the leaking test rather than only its next neighbour.

- **`invoke_menu_item` reaches an item whose header is a control, and a submenu filled when it opens** (#2321). The tool
  read a header by its `ToString`, which for a TextBlock is its type's name: such an item couldn't be reached, and a
  miss listed "Avalonia.Controls.TextBlock". It is now matched by its automation name, which `dump_visual_tree` shows
  it by, or else by its text. The tool also read a submenu's items without opening it, so one filled by its
  SubmenuOpened handler still held its placeholder, "It has no items", until something had opened it once. Each
  submenu on the path now gets its SubmenuOpened before its items are read, as opening it does, without a popup.

- **End remains available on both toolbars while a running form has focus** (#2319), so stopping a run no longer needs focus returned to the IDE first.

- **Workspace artefact debounce tests use controlled time** (#2305), awaiting regeneration instead of
  assuming a loaded runner finishes within a fixed wall-clock delay.

- **New SSTab page properties save at their block’s indentation** (#2301), including inside containers.

- **SSTab designer previews use their saved tab strip** (#2301, first slice). Saved captions, selected
  tab, counts, style, orientation and implemented layout properties now appear in the preview.

- **Desktop builds with `--artifacts-path` copy the bundled language server's dependency from its actual output** (#2298), so a fresh checkout no longer needs a previous build in the default output directory. The same path is used for publishing.

- **A language server's children are ended after a clean server exit on Linux and macOS** (#2297).
  The launch guard terminates the remaining process group, escalating after two seconds for children
  that ignore SIGTERM, while preserving the server's exit code.

- **Tool docks keep their size and split arrangement when shown again and after a restart** (#2295).
  Repeated close/show cycles no longer shrink a lone dock, and a closed member of a split returns to
  its own dock. Late-registering add-in tools restore their saved dock and ancestor proportions;
  tools in unrecognized arrangements still restore open in their home region.

- **`take_snapshot` shows text that a control draws after popping a clip, above 100% scaling** (#2293). It drew
  its image with a `RenderTargetBitmap` at 96 × scaling DPI, where Avalonia.Skia scales the first transform after a
  popped clip twice. So a running StatusBar's later panels came out blank while the screen showed them, and a live
  check reported a fault that wasn't there. The window and each popup are now the compositor's own snapshot, which is
  what the screen shows. If the compositor gives no picture within 5 s, a render target draws it, and the reply's
  `note` says so.

- **AI Chat fits a narrow dock pane** (#2291). The prompt box narrows with the pane, keeping Send and New visible when the panel is docked on the left.

- **Paused watches respect their context** (#2290). Module-wide watches exclude procedure locals,
  and procedure-scoped watches read a caller's frame while it remains on the stack. Undefined names
  and returned procedures display the measured VB6 messages with type `Empty`. Design-time and
  after-End resolution remain to be implemented.

- **Locals expands a control whose property can't be read** (#2289). A Timer's Left and Top raise 393 when read, as
  VB6's do, and that raise failed the whole expansion. So `get_locals` at its default depth failed on any form with a
  Timer, and the Locals window gave Timer1 no expander. Such a property is now listed with the raised error's own
  description in angle brackets, `<Get not supported at runtime>`, and no type, and the control's other properties are
  listed as before. The angle brackets are VB6's own convention for a member its Locals window lists whose read raises,
  as measured on #2289. VB6 lists no Left or Top for a Timer at all. Listing the members a control's type has, as VB6
  does, is #2329.

- **`inspect_element` reports what a tree has selected** (#2288). A TreeView is no `SelectingItemsControl`, and its
  peer offers no selection provider, so `selectionItems` came back `[]` whatever was selected. In the Watches, Locals,
  Project Explorer and Options trees, that read as nothing selected. The tree's own selection is now read, and each row
  is named as `remove_from_selection` names it.

- **Project saves write reference paths and component lines in VB6's order** (#2274).
  Libraries on the project's drive are named relative to the saved project on every
  host, including system libraries. Other-drive and UNC paths stay absolute; empty paths stay empty.
  Unedited saves preserve line order, including late component and reference lines.
  Accepting the References dialog, even unchanged, groups references before read
  components and members. The corpus check compares only reference paths by the file
  they name; templates retain the same strict line-order comparison as saved projects.

- **Project reference locales are read and written as hexadecimal** (#2273). `409` now means locale 1033,
  and references with letters in the locale, such as `c0a`, enter the project model instead of being kept
  only as unknown lines. Saves use lowercase hex without padding, as VB6 does.

- **In-box control corpus states are checked for merge leftovers** (#2272). Tests reject a control folder in both
  `pending/` and `saved/`, identical files at the same relative path, and README folder listings in the wrong state.
  State-prefixed README rows cover every current control folder; ImageList's original probe pictures live with
  the authored inputs.

- **The Properties window updates its descriptions and category headers on a live language switch** (#2270), preserving the selected property and collapsed categories.

- **Foreign-server stderr assertions wait for their expected lines** (#2269). The untitled-document
  tests drain and poll with a bounded deadline before reading clangd's URI refusal and ruff's dynamic
  registration warning, avoiding a race with the stderr reader.

- **`set_ide_language` saves the language when Keep is pressed** (#2268). Only the Options dialog's OK saved it, so
  a language kept through the tool lasted for the session and the next start came back in the one saved before. The
  reply now says Keep saves it and Revert saves nothing, and asking for the language already in use no longer claims
  a confirmation is open.

- **Avalonia-dependent IDE tests run on the UI thread** (#2267). A source guard rejects plain facts and
  theories that touch Avalonia directly or through helpers in the same class.
  Regression cases also check the three reported folding theories against the guard and require their
  UI-thread attributes.
- **Command requery listeners survive garbage collection while their owners are alive** (#2267).
  The Format selection regression now forces collection, reproducing the notification loss that only
  appeared intermittently in full test runs; unsubscribing also removes the original weak subscriber.

- **ImageList draws a raised designer icon instead of a hatched character** (#2266). The measured
  38 × 38 box contains HexIDE's own vector filmstrip glyph, with theme brushes for Classic and dark themes.

- **A bare `As ProgressBar` resolves when its control library is referenced** (#2265, first slice). The first
  `Object=` library in the project supplies the type; `TypeOf` follows that binding, and assigning the other
  library's ProgressBar raises error 13.

- **Local test runs enforce the same test-count floors as CI** (#2264). `scripts/test.ps1` reads the
  floors from the workflow, uses a floor of one for filtered runs unless the caller supplies one, and
  prints a final PASS/FAIL from the exit code even when the test host prints a misleading pass.

- **The DAP timeout test gives its follow-up request time to complete** (#2262). Requests can override
  the connection timeout individually, keeping the one-second bound on the request under test while
  allowing the follow-up the usual test patience on loaded runners.

- **Only one writer maintains a server's workspace artefact** (#2248). Servers with `workspaceArtifact`
  receive no file-operation notifications; servers without it still receive matching notifications.

- **Failed-save notification tests accept both host error types** (#2248). A directory used as a file
  destination can raise either an I/O or access-denied exception. A wire test confirms the failed save
  sends no file-creation notification.

- **A control inside a disabled Frame or PictureBox reads Enabled False** (#2245), including through nested
  containers and `Me.Controls`. Its own setting is kept for when its containers are enabled again.

- **Lost-connection tests wait for the warning to be logged** (#2244), as well as for the client to stop,
  avoiding a race when a server drops a reply or sends a malformed frame.

- **Tool windows stay open after restarting following a far-left dock drop** (#2239). A live tool in an unrecognized dock arrangement is saved as open in its home region instead of being recorded as closed.

- **The Toolbox reappears after closing and reopening it** (#2238). Runtime-created left docks are
  initialized after their tools are added, so Dock no longer treats them as empty.

- **A drag that could not have docked no longer says "nothing was docked"** (#2232). `interact`'s `click … to …`
  reports Dock's outcome only when the drag moved a tool or document, or was pressed on a tab or a tool window's
  title strip. A form designer drag used to end every reply with it, which read as the drag having failed.

- **The designer's draw preview, rubber band and grab handles are in `dump_visual_tree`** (#2231). The dump folded the
  first three away as unnamed wrappers, and a selected control's grab handles sat in an adorner layer far from the
  control. They now carry AutomationIds, and an adorner is listed under the element it adorns, so
  `…/ListItem[Command1]/None[ResizeAdorner]/None[PART_BottomRight]` addresses a handle and `inspect_element` reads
  the preview's bounds.

- **A hidden control's refusal names showing copies in the same window** (#2230). `interact` gives
  the path of a copy with the same type and label or automation ID, or the count and first three paths
  when several show, so callers can target one directly. With no showing copy, the existing guidance remains.

- **`interact` replies identify form controls and toolbar buttons** (#2229). Descriptions fall back to
  the automation id when there is no Avalonia name, keeping the type alongside the identifier used in
  `dump_visual_tree`, including a running control array's index. Click replies and modal refusals now
  say which control they concern.

- **The Properties window's category boxes show + and −** (#2228). In the Categorized view, the box beside each
  category drew as a plain grey square, filled while the category was open, because its theme named a resource that
  nothing defined. It now draws − while the category is open and + while it is closed, as VB6's does.

- **Completion sees the character just typed** (#2227). The editor sends its pending document change
  before requesting completion, so member lists no longer use the source from before the keystroke.

- **Language servers receive the IDE UI language at initialize** (#2210), including the resolved
  system language and English for pseudo-locales. Reconnect after changing Options → Language;
  switching language does not automatically restart servers.

- **Transient diagnostic pull failures are retried** (#2209). ContentModified and retriggerable
  ServerCancelled replies use a short, capped back-off so an opened document does not stay without
  diagnostics until edited. Closing or changing the document stops the old retries.

- **Language Servers reports replace plain local paths in failure details and configuration problems**
  (#2205). The shared export redactor also handles absolute drive-letter, UNC and Unix paths in server
  prose, using the same names as other redacted paths.
  Sensitive home, temp and report workspace roots are replaced before matching prose paths, including
  roots containing spaces, alternate separators and URI encoding.

- **Completions from servers returning a bare item array now reach the editor** (#2204).
  The client accepts both LSP completion reply shapes, preserving each item's detail and insertion text.

- **Automation can clear a single-selection list or tree** (#2203). `remove_from_selection` reports
  the resulting selection, including when empty, and says when the target was not selected.

- **The completion popup budget test advances time explicitly** (#2200). Its delay and late-answer
  check now share an injectable clock, avoiding wall-clock timeouts on loaded runners.

- **The Timer event-order test preserves its execution context while draining dispatcher work** (#2194).
  It captures cancellation before starting the form, avoiding reads of an earlier test's disposed
  `TestContext` during polling. The wider async headless-test lifetime question remains tracked in #883.

- **`press_key` takes a chord written as one key** (#2189). A `key` such as `Ctrl+H` used to be refused as an unknown
  key, which sent the caller to the key names. It is now read as key `H` with modifiers `Ctrl`, and the reply says so.
  A chord whose leading part is not a modifier, such as `A+B`, is refused naming that part. The reply now writes
  combined modifiers as `Control+Shift+End`, not `Control, Shift+End`, which read as two presses.

- **Adding a file whose project line was kept no longer moves the other kept lines** (#2187). Their
  original `.vbp` positions survive when the restored file's line is removed from the kept list.

- **Signature help accepts parameter labels given as offsets** (#2186). The popup slices the signature
  label and highlights that exact range. Replies that fail result deserialization appear as failed
  exchanges in the protocol inspector, with an explanation that the answer was dropped.

- **`set_window_state` says when no window manager is running** (#2185). On an X11 display with none, a minimize or
  maximize went unconfirmed with "the window manager has not confirmed" and advice to re-read, though nothing would
  ever apply it. The reply now says no window manager is running, and that Normal with a position and size still
  works. HexIDE reads this from the root window's `_NET_SUPPORTING_WM_CHECK`, only when a change goes unconfirmed.

- **`interact` explains why a disabled control cannot be clicked or invoked** (#2184). A blocking modal
  dialog is named in the refusal, with instructions to answer it first, instead of blaming the click's
  coordinates or reporting an invocation that a person could not make.

- **Layouts saved by a newer build no longer prevent startup** (#2183). Unknown tool-window keys are
  skipped and logged once per restore, while known windows keep their saved positions and sizes.

- **Designer Ctrl/Shift clicks across containers leave the selection unchanged** (#2181), matching measured VB6 behavior and preventing those clicks from creating a mixed selection that Align Lefts moves outside a Frame.

- **Break stays available while a running form has focus** (#2179). The Standard and Debug toolbar
  buttons can pause a handler looping with `DoEvents`, and follow pause/continue state.

- **Acting on a disabled control explains why it was refused** (#2178). Every `interact` action except
  the two reflection actions uses the same refusal, naming a disabling container or blocking modal
  dialog when present, and confirms nothing was done instead of reporting a peer exception.

- **Selecting a hidden property by its row path names the collapsed category** (#2177). The refusal tells
  the caller to toggle the Button in that category, using its displayed name, and no longer suggests
  another visible copy without evidence that one exists.

- **An overflowing tool argument names the storage limit it exceeds** (#2176), leaving the
  parameter's own bounds to its description instead of presenting its C# type's range as accepted input.

- **MCP: `interact`'s drag can dock a tool window, and says what Dock moved** (#2175). Dragging a tool window's
  title strip onto another dock did nothing, and the reply read as a drag that worked. The held moves now arrive
  one at a time with layout between them, as a real pointer's do. A drag in the dock layout names what moved and
  where, or says the layout is unchanged.

- **Procedure boundary breakpoints now pause the interpreter** (#2172). A Sub declaration breakpoint
  stops before the body; breakpoints on natural `End Sub`, `End Function` and Property Get's `End Property`
  stop before return. Stepping from a called Sub or Function's last statement also stops on its End line.
  Startup stepping still advances from Initialize's last statement into Load's first statement.

- **Opening a code window asks the language server for folds once** (#2154), avoiding a delayed duplicate request while choosing a conditional-compilation preview.

- **Launched language servers end after an unclean IDE exit on Linux and macOS** (#2147).
  A guard process bounds the lifetime of stdio and launched pipe servers and their process group,
  preserving protocol streams and server exit codes. Windows keeps its kill-on-close job object.

- **The Object Browser refuses remote and device type-library paths** (#2123). References load only
  from fully qualified local files, using the shared path check, and reading a library never registers it.

- **Reject three constructs that VB6 refuses to compile** (#2121): `Continue Do` and `With Debug`
  raise "Syntax error", and assigning through `Me.` to an explicitly Private scalar class field raises
  "Method or data member not found". The interpreter reports these as it reaches the statement.

- **Procedure calls no longer copy every name in their module or instance** (#2120). Parameters,
  locals and the return name live in the call's own scope, with module and instance names looked up
  through the enclosing scope. Small helpers in modules with many variables or controls avoid the
  per-call dictionary copy; the Locals window keeps its existing grouping.

- **Corpus parity compares Immediate output lines and records the program locale** (#2119). The dump preserves printed Null and item formatting; the runner distinguishes locale differences, recognises application errors, launches declaration-only modules through an empty entry procedure, names projects per case, and retries missing-entry failures alone after a parallel pass.

- **Format expands large numbers under fixed masks** (#2118). Values beyond Decimal's range now keep
  VB6's 15 significant digits and pad with zeros, including grouping, negative sections and Standard.

- **`Randomize n` preserves part of Rnd's current state** (#2117), matching measured VB6 sequences rather than restarting from the seed alone. Negative `Rnd` arguments now fold their Single bits as VB6 does, so `Rnd(-1)` followed by `Randomize n` repeats the measured sequence.

- **Dates and times use ordinary spaces before AM and PM on Linux** (#2102). The program locale
  replaces ICU's narrow and ordinary nonbreaking spaces in date/time patterns and AM/PM designators,
  so date text no longer contains an invisible difference from Windows and VB6.

- **Opening a refused `.twinproj` reports the file and the reader's reason** (#2101). The project picker keeps existing projects and documents open when opening an archive.

- **IDE dialogs stay separate from a running program in automation replies** (#2094). Owning a dialog
  does not make it part of the program; only the program's own MsgBox and InputBox calls opt into its wait.

- **The designer's tool drag preview matches the control placed on release** (#2092). It uses the starting
  container's grid and limits, snaps to the nearest grid line, and includes the far corner's extra pixel.

- **MCP: a target that is no path says where paths come from** (#2080). An empty target, or one not starting with
  `Window`, is now refused with the next step: `dump_visual_tree` gives every control its path, and `match` finds one
  by name. A guessed path also gets a call to send as it stands, matching its last segment in the window it
  was looked for in.

- **MCP: a waiting dialog's buttons no longer include its check boxes and radio buttons** (#2079). Tools > Options
  listed "Prompt for project" beside OK and Cancel, in the `modalDialog` note and every text that describes a dialog.

- **`take_snapshot` explains the missing title bar and names the captured window's title** (#2078).
  The description and reply identify the client-area capture; `title` reports the actual window title,
  including the IDE's. `activeDialog` remains for compatibility.

- **A Shape's BackColor can be read at run time** (#2070). Reading the stored colour through a control
  variable no longer stops the run with “Method or data member not found”.

- **A Timer's Left and Top reads raise run-time error 393** (#2069), as measured in VB6, instead of returning 0 for a position that belongs only to the designer.

- **A Timer with no Interval line stays quiet** (#2068). Its default is now 0, as in VB6,
  instead of 1000. Newly added designer Timers use the same default; saving omits 0 and retains 1000.

- **A form accepts bang reads of its controls** (#2066). `Me!Name` now reaches the same control as
  `Controls!Name`, so reading a member such as `Me!a2.Name` no longer stops the run.

- **A program can click its CommandButton** (#2065). `Value` reads False, and assigning True raises
  Click before the next statement, with the button's own handler followed by its WithEvents handlers.

- **A Frame's Caption can be read at run time** (#2063), returning its saved caption or the empty string instead of stopping with “Method or data member not found”.

- **Controls without a Font block inherit the form's font on load** (#2062), including inside nested Frames and PictureBoxes. A container's own font does not replace the form's, and changing the form's font later leaves its controls' copies unchanged.

- **A control drawn in the designer is the size VB6 makes it** (#2058). Its corners go to the nearest grid
  line, not the one below, and it ends one pixel past its far corner, with the grid on or off. Each drawn control
  used to be 15 twips smaller each way than VB6 would make it.

- **Selecting a property hidden by a collapsed category names the category and how to open it** (#2056).
  The refusal tells automation callers to `toggle` the Button in that category's row, using its displayed name.

- **MCP: `interact` `toggle` no longer reports a refused change as done** (#2050). It reads the state back once the
  work the toggle set off has run, so a box its dialog puts back is reported as put back. When a dialog opened during
  the call, the reply says the state may still change once it is answered.

- **Format's `AMPM` token uses the user's AM or PM designator** (#2040), in any letter case,
  and switches hours to the twelve-hour clock even when the designators are empty.

- **A control drawn inside a Frame or PictureBox goes into it** (#2039). It goes into the innermost container
  under the point the drag began from, and the drag is held to that container, as VB6's designer does. Before, it
  landed on the form, drawn over the container but not in it. `add_control` places the same way from its top-left.

- **String-building modules start faster** (#2038). The interpreter tries SLL parsing first and retries
  with LL when needed, including Immediate-window expressions and statements, preserving syntax errors
  and the nesting guard.

- **Keyboard `type_text` no longer counts the key that opens a dialog as an applied edit** (#2035).
  The reply identifies the key sent, warns that it may not have landed, and asks the caller to answer
  the dialog and check the text before deciding what to send next. No later keys are sent.

- **Large visual-tree replies spend their budget on showing controls first** (#2033). Hidden nodes keep
  their paths but lose their children first. Further depth cuts preserve the active document and its
  controls while shortening other branches; the note explains each cut and how to retrieve the rest.

- **Visual-tree searches explain which tab and Options page contents were not searched** (#2032).
  Match replies name unselected document tabs and Options pages and say how to bring them into the tree,
  including when some nodes matched.

- **MCP: focus in a flyout counts as in its window** (#2030). With a text box's Cut/Copy/Paste flyout open,
  `dump_visual_tree` reported no focus, and `press_key` moved the focus out before pressing, so Escape closed the
  dialog instead of the flyout.

- **Signature help uses the highest-priority non-empty answer from concurrent servers** (#2027).
  Typed triggers reach only servers advertising that character; explicit parameter info asks all
  claimants, within one shared one-second budget.

- **MCP: `inspect_element` names a selected item that has no row by its row's name, not its type** (#2026). A
  Properties list too short to show a row answered `HexIDE.VisualDesigner.PropertyViewModel`.

- **Stdio language servers get time to exit cleanly** (#2023). HexIDE waits up to two seconds before
  killing a remaining server's process tree, so wrapper children are terminated too. The conversation
  records the exit code during shutdown as well as after an unexpected exit; real-process tests cover
  clean exits, ignored exits, wrapper children and crashes.

- **Hover replies in legacy LSP forms now show their text** (#2022). Markdown strings, language-tagged
  code blocks and arrays of marked strings are accepted alongside markup content.

- **Watch context labels follow language changes** (#2021). Existing Watches rows and the Add/Edit Watch
  dialog refresh the all-procedures placeholder without changing named module/procedure contexts.

- **Controls inside hidden containers are dimmed in the form designer** (#2002). Changing a Frame's
  Visible property updates its children's previews while keeping them selectable and editable.

- **Tab-order badges clearly mark TabStop off even for 4** (#2001). A diagonal slash replaces the
  horizontal strike that could disappear into the digit’s crossbar.

- **Indent-guide tests no longer depend on Avalonia's dispatcher thread** (#1995). Guide calculations use plain text lines, so their unit tests run independently of UI thread ownership.

- **Paste as Comment keeps following code uncommented** (#1993). Pasting before existing code ends
  the comment with a line break, preserving the code on its own line at the paste's indentation.

- **A filtered grid's rows appear once in `dump_visual_tree`** (#1977). Recycled DataGrid rows and their
  stale cells are left out, as released list rows already are.

- **Running-form automation replies distinguish active and queued handlers** (#1976). Events with no
  handler are omitted, and queued handlers are named as waiting to run rather than still running.

- **SIGTERM shuts down a Debug IDE started with `--server-port`** (#1973). The embedded server no
  longer owns process signals; the IDE stops the running project, closes dialogs and closes its main
  window on the UI thread, using the same cleanup as `shutdown_ide`.

- **Properties category rows support expand and collapse through automation** (#1967). Inspection reports their expansion state, and each category toggle has the localized category name.

- **`dump_visual_tree` lists virtualized rows in item order** (#1966). Scrolling to a selection no longer
  lists rows in their containers' creation order; their paths still resolve to the corresponding rows.

- **A tool argument too large for its integer reports the whole-number range** (#1965), rather than a
  .NET serializer message. The refusal includes the parameter's declared guidance, such as hover's
  0 to 10000 ms dwell limit, and also explains values below the integer's minimum.

- **Desktop startup no longer waits for window activation** (#1964). On an X display without a window
  manager, `--newproject` now creates its project, and opening a project or the startup dialog works too.

- **Language-server progress names the server** (#1961). The status bar uses the registration’s display
  name and separates the task title from its message, with absent parts left out in every shipped language.

- **Object Browser, Locals and Watches follow language changes** (#1948). The all-libraries entry and
  members header are translated, and ready/out-of-context placeholders update immediately.

- **Completion from multiple language servers stays responsive and removes duplicates** (#1918).
  The popup waits at most one second, keeps the higher-priority item for each label and kind, and drops
  late answers with a debug log and protocol-inspector note. Advertised trigger characters are combined
  across claimants and ask only the servers that advertise them.

- **Formatting takes a deleted declaration's Attribute run with it** (#1890). After protected lines
  are trimmed from the formatter's answer, deleting a whole logical declaration removes its run in the
  same undo step, without disturbing the other formatting edits.

- **Choosing a caller in the built-in Call Stack window shows its calling statement** (#1866), with a green call-site triangle while the current execution statement stays in place.

- **A placed UserControl's run-time members follow its `.ctl` settings** (#1631, part A). CanGetFocus False
  removes focus members, InvisibleAtRuntime restricts the extender and makes Left/Top reads raise 393,
  and missing members distinguish early and late binding. Windowed hWnd is explicitly unimplemented;
  windowless controls expose no hWnd.

- **UserControls take part in tab order and access keys** (#1631, part B). CanGetFocus and ForwardFocus
  select the focus destination; forwarded keys raise AccessKeyPress on a receiving UserControl.
  DefaultCancel supports Default and Cancel, Enter and Esc, and Ambient.DisplayAsDefault notifications.

- **UserControl containers, bottom alignment and transparent backgrounds run as measured** (#1631, part C).
  Container assignment preserves coordinates and refuses ineligible controls with error 425. Focus and
  lifecycle events follow the measured order, including windowless and invisible controls.

- **UserControl instances save their measured property subset** (#1631, group 2A, first slice).
  Nonfocusable instances omit TabIndex, invisible instances retain only position and saved extents,
  and windowless siblings follow windowed controls on forms containing UserControls.

- **PropertyPage HelpContextID can be edited and saved** (#1631, group 2B storage slice).
  The designer lists the help context number, and `.pag` saves omit the invalid StandardSize line.

- **Single-view form runs start again** (#1631). Initial window visibility no longer starts UserControl
  lifecycle events before the form has code and has loaded.

- **Forms with invisible UserControls missing saved extents open read-only** (#1631). Dirty checks no
  longer throw while opening the designer; saving is refused because the size cannot be reproduced.

- **`TypeName` of a scroll bar is `HScrollBar` or `VScrollBar`** (#1556), where it was `ScrollBar`, so
  `TypeOf c Is HScrollBar` was False.

- **Err.Source reports the raising project's name** (#1483), including errors in classes, independently
  of the project title. A sourceless `Err.Raise` fills an empty source and keeps one already set;
  explicit sources survive trapping and propagation across managed library calls.

- **MCP can drag across a running form or the designer** (#1402). `interact`'s `click` accepts `to x,y` or
  `to <path>:x,y` after its press point, button and keys. It waits for the press's work, moves with the button
  held and releases to the pointer capturer, or what is under the release point. The reply names both points
  and the release recipient. A path can be pasted from `dump_visual_tree`, including spaces; `double` with
  `to` is refused with an explanation.

- **The reset prompt holds the IDE and running forms together** (#1366). Other desktop windows are disabled while
  it waits, then regain their previous enabled state, including when the IDE is minimized.

- **Selection verification tests preserve their execution context when draining dispatcher jobs** (#1296).
  Queued work can otherwise replace the test's context before its later awaits and cleanup. A deterministic
  regression checks that the caller's context survives the drain.

- **Referenced libraries retain the Components dialog metadata** (#1107, #1103). The shared runtime
  library model preserves control-library descriptions and project and form reference lines.

- **RichTextBox disabled-text rendering checks tolerate anti-aliasing** (#1100): the test verifies
  the text-run brush is GrayText and finds neutral grey glyph pixels darker than the button face,
  without requiring a pixel to match the brush exactly on Windows.

- **RichTextBox forms open visibly and run with saved decimal colours** (#1100): the designer
  shows labelled, view-only placeholders, and runtime BackColor accepts the decimal spelling VB6
  saves. Plain navigation without a selection follows the recorded Shift endpoints; unmeasured
  line and selection endpoints explicitly refuse.

- **UpDown input accepts the newly measured edge cases** (#1093): Increment 0 leaves Value unchanged
  and raises UpClick or DownClick without Change. Acceleration is stored without affecting stepping or
  repeat timing. Synchronized buddies read and write Label Caption and TextBox Tag as well as Text,
  with invalid strings treated as zero.

- **SSTab MouseDown sees the old tab** (#1090, #2358). Caption presses switch pages after the
  MouseDown handler finishes, preserving MouseDown, Click(PreviousTab), MouseUp order even when
  the interpreter yields.

- **TabStrip designer previews and focus events** (#1089): saved ImageList and Font properties no longer
  throw when the designer opens a form. Runtime refusal remains. GotFocus and LostFocus now fire,
  with mnemonic focus acquisition preceding selection events.

- **Picture function named arguments** (#1087): LoadPicture and SavePicture now declare their measured parameter names and required counts, with matching editor signatures.

- **TreeView rows use the measured 16-pixel height** (#1086), keeping drawing and pointer hits aligned.
  GetVisibleCount reports row capacity, including 12 for the oracle's control, rather than node count.

- **TreeView capacity uses its client area, including before layout and in Form_Load** (#1086).
  Saved Appearance and BorderStyle choose the measured edge; drawing and pointer hits begin inside it.

- **TreeView mouse coordinates start inside its edge** (#1086). MouseDown, MouseMove and MouseUp
  report client-area twips, matching the drawing and hit-testing origin.

- **ListView mouse tests follow the drawn label bounds** (#1085), avoiding host-font-dependent click
  coordinates. Drawing and mouse selection share the label rectangle, with coverage at its edges.

- **ListView mouse coordinates start inside its edge** (#1085): MouseDown, MouseMove and MouseUp
  report client-area twips, and drawing and hit testing share the default 3-D or flat inset.

- **ListView coexists with the newer in-box controls** (#1085): its runtime bindings, toolbox entry and
  saved forms are retained alongside ImageList, StatusBar, Toolbar, TreeView, TabStrip, SSTab, CommonDialog, RichTextBox,
  UpDown and MaskEdBox support from main. Duplicate pending corpus copies are removed after
  comparison with their saved counterparts.

- **ListView disabled drawing and label editor sizing** (#1085): saved background colors remain visible,
  disabled Report rows use button face without repainting the empty area, and the label editor measures
  its text and template without scrollbars. The unmeasured Appearance 1 / BorderStyle 0 edge refuses at
  F5 instead of reporting guessed mouse coordinates.

- **CommonDialog loads through its MSComDlg library and registered component** (#1084, #1962), using the same
  mechanism as the other in-box controls. Its saved scalar properties remain carried verbatim. Color reads now
  retain the measured unsigned Automation Long value and VarType 19, and assigning -1 raises overflow; unmeasured
  numeric conversions are refused as not implemented.

- **CommonDialog runtime property reads follow the measured object model** (#1084). Reading its write-only
  Action, including assigning the control as a value, raises 394. Left and Top raise 393 on reads and 382 on
  writes; absent extender properties raise 438 on reads. FontName ignores a missing face and preserves an
  accepted name's spelling.

- **CommonDialog scalar properties use their measured defaults and assignment checks** (#1084).
  Adds the page, printer, help and font-effect property storage, rejects invalid FilterIndex and MaxFileSize
  values, and reports read-only FileTitle and hDC assignments as error 383.

- **CommonDialog single-file results follow the measured Open/Save behavior** (#1084).
  Implied Flags survive Cancel, picks report ExtensionDifferent even with an empty DefaultExt,
  Save uses a simple selected filter extension when DefaultExt is empty, and an undersized Open file buffer raises 20476.
  Save extension completion refuses unavailable filter information rather than guessing.

- **CommonDialog Open preserves multiple selected files** (#1084). AllowMultiselect reaches the host picker,
  and FileName uses the measured Explorer NUL-separated or old space-separated format with an empty FileTitle.
  Unmeasured selections across folders and old-format paths containing spaces are refused.

- **CommonDialog Action = 1 opens the file picker** (#1084). The assignment waits for ShowOpen to finish,
  returning its file properties and CancelError result. Measured invalid Action values raise error 380;
  unmeasured dialog mappings and conversions remain explicitly refused.

- **CommonDialog saves omit default properties in VB6’s measured order** (#1084). The four-control VB6 save
  now participates in the byte-for-byte corpus checks, with separate font lines and the original control order preserved.

- **CommonDialog Action 2–6 dispatches to its Show methods** (#1084). Save and Font assignments await
  their dialogs and preserve CancelError behavior. ShowHelp returns without error for measured commands
  0, 2 and 3. Color and Print still report not implemented pending host dialogs and measured writeback.

- **A minus sign applies after a power** (#1073): `-2 ^ 2` now gives -4. Numeric literals
  take their type from the digits before negation, so `-32768` is a Long and `-2147483648`
  a Double. Subtraction without spaces (`x-1`) also parses correctly.

- **A running control keeps its measured VB6 minimum size** (#1029). Existing intrinsic controls apply a
  floor rather than snapping sizes; TextBox and CommandButton use the recorded font and border cases,
  and assigning a ComboBox's Height raises 383. The unmodelled file-list and Data controls remain outstanding.

- **Rename refuses edits reaching other documents** (#1024). The code window leaves the buffer unchanged
  and names the affected members (or their URIs), instead of silently applying only its own part.

- **Server folds honour character offsets** (#1022). The declaration stays visible, described procedures
  keep both their procedure and attribute folds, and folds sharing a start retain their own folded states
  when a server answers again or a neighbouring fold disappears.

- **Rename refusals explain why no edit was made** (#1015). The bundled server returns RequestFailed for
  keywords, protected lines, designer names and invalid new names; the editor shows remote refusal messages.

- **A Function returning an object array checks what is stored into its name** (#999). `As Object()`,
  `As Collection()` and class-array returns reject a Variant array with error 13, leaving the caller's
  target unchanged. Direct array assignments require the same declared element type; through a Variant,
  any object array is accepted, as measured on VB6.

- **Form Layout thumbnail follows the theme** (#958): its face, label and title strip use theme brushes,
  keeping the form name readable under Dark while preserving Classic colours.

- **Window-state automation waits for the window manager** (#946). `set_window_state` reads back confirmed
  state and geometry, reports an unconfirmed request with a recovery note, and checks off-screen positions
  before restoring.

- **A control's Font is an object** (#934). Font-bearing controls expose a stable Font reference,
  with Name as its default member and Size as Currency. Sharing a font with `Set` shares later
  changes; `Set Font = Nothing` raises 380. Controls without a Font raise 438.

- **Align and Make Same Size require two selected controls** (#930). The designer rechecks menu and toolbar
  commands when selection changes. `invoke_format_command` refuses selections below each command's minimum and
  explains how to select more controls.

- **Short test runs fail instead of reporting a clean green** (#883). CI's four main-suite count floors
  are refreshed to about 95% of measured totals, and the WSL test runner uses the same floors. Filtered
  runs require at least one test. The async test-context lifetime fix is tracked separately in #2194.

- **String functions validate compare modes at their measured VB6 validation points** (#881).
  InStrRev, Split and Replace treat vbDatabaseCompare as text; InStr and StrComp
  reject it. Negative modes raise error 5, and locale modes are refused explicitly.

- **A running form's Left and Top move its window** (#852). They read the outer position in twips,
  whatever its ScaleMode, retain assigned twips between pixels, and CenterScreen takes effect at Show.

- **BOM-less UTF-8 keeps its encoding after its last character above U+00FF is removed** (#846).
  Save Project records ambiguous UTF-8 members in a trailing `[HexIDE]` section of the `.vbp`, so a
  fresh open reads accented text correctly without adding a byte-order mark to the source file.

- **Project Explorer removal offers to save unsaved forms and modules** (#835, first slice). Standard modules,
  classes, UserControls and property pages now have the same removal route as forms. Removing a member
  closes its editors and leaves its file on disk; the next project save drops its entry. Designers and
  related documents remain to be implemented.
- **Project → Remove now removes the member its header names** (#835). The menu uses the Explorer's
  save/cancel flow for the selected or focused form or module, and is disabled when there is none.

- **Timer members follow the measured VB6 runtime table** (#801). Absent members raise 438
  (TabIndex and ToolTipText reads retain the missing-member compile error), designer-position writes raise 382, Interval reads as Long and Parent returns the owning form.
  Unmeasured Timer members and scroll-bar colours refuse explicitly.

- **Locals omits unavailable control properties** (#801): Timer designer positions and absent members,
  and scroll-bar colours, no longer appear as error rows.

- **Hex and octal Strings convert at VB6's measured widths** (#788). CInt and Val use signed
  16-bit values through &HFFFF, conversions use signed 32-bit values through &HFFFFFFFF,
  and larger values follow each function's measured wrapping or overflow rules. Numeric
  conversions refuse a trailing `&` with error 13.

- **A date with a fraction of a second rounds to the second, as vb6.exe shows it** (#783), where the fraction was cut
  off: 12:00:00.6 is `12:00:01`, and 23:59:59.6 the next day. A Date keeps the serial it was given exactly, and display,
  the date functions and `Write #` read it rounded by the rule measured on vb6.exe across 418 serials. `DateAdd` counts
  toward zero, as vb6.exe does, where it rounded, and `Int` and `Fix` of a Date return a Date, where they returned a
  number.

- **Made programs report fatal errors** (#757). An unhandled run-time error shows a warning titled with the
  program's name and OK only, freezes events and Timers, and exits on dismissal without unload or terminate
  events. A RaiseEvent sink's error cannot reach the raiser's handler. Compile errors, unimplemented constructs
  and engine faults also surface.

- **A name read through its type character is read, as vb6.exe reads it** (#742). `Debug.Print s$`, `i% + 1`, `arr%(1)`,
  `UBound(a%)` and `Bump i%` stopped the run with "Type hint is not supported". The character is now checked against the
  name's type: the declared one, a Const's value's, an Enum member's Long, or an array's element type. A mismatch, or any
  character on a Variant, an object, a Byte, a Boolean or a Date, is VB6's compile error. A name never declared is declared
  by the read with the character's type, ahead of a DefType, and a ByRef parameter given `i%` writes into `i`.

- **A Function declared with a type character loads and returns that type** (#740). `Function GetName$()` and
  `Function Twice%(…)` stopped their whole module loading. Each of `% & ! # @ $` now declares the return as its `As`
  clause would, ahead of a DefType, and a call may carry it, `Twice%(2)` or `Twice% + 1`, checked against the
  return type as vb6.exe checks it.

- **Assignments and For counters through type characters update the declared variable** (#739). `s$ = "x"` wrote
  a second variable named `s$`, and `For i% = 1 To 2` ran on one, so the value was lost with no error. Both now reach
  the variable of the bare name, at any scope, checked against its declared type as vb6.exe checks it, and an
  undeclared name takes the character's type. `Next i%` runs too, where it stopped the program, and a Function's
  return assigned through its character, `F% = 7`, sets it.

- **VB6 syntax highlighting is authored clean-room under MIT** (#715), using HexIDE's MIT lexer grammar and the existing theme palette names. Keywords match regardless of case (#716); comments, strings, numeric and date literals, type suffixes and line continuations receive syntax colours. The licence guard now rejects non-permissive or unknown licence declarations in tracked files, with named third-party credits as exceptions.

- **VB6 block terminators use the keyword colour** (#715). `End`, including in `End Sub`, `End If` and the other lexer-defined block terminators, now uses keyword blue in every theme.

- **Project-load warnings use the selected language** (#698). Missing forms and failed form or module loads now use translated messages, retaining file paths and failure details.

- **Language confirmations block Options even when automation opens them** (#623). The gate belongs to
  the topmost open owned window, preventing Language from silently snapping back and Region from changing
  the applied language underneath the confirmation.

- **The automation gap catalog now records new documents as unsaved until their first save** (#597).
  The existing `get_file_content` description and save behavior already follow the decision to leave
  new documents without files; regression coverage now checks all five document kinds.

- **Launched language-server exits remain observable during disposal** (#491). Both stdio and named-pipe
  transports keep their exit handlers attached until the exit has been recorded once, log deliberate
  exits at information level with the server identity, and report when a timeout forces a kill.

- **Workspace artefact regeneration preserves unchanged files** (#455). Changed content is written
  atomically so an interrupted write leaves the previous file intact. Refusals and failures appear on
  the server's connection in the protocol inspector while the server still launches.

- **Workspace artefact notes keep local paths out of conversation exports** (#455). Notes name the
  configured relative file and exception type; full exceptions remain in the application log.

- **Unknown add-in menu locations are logged and dropped** (#416). The warning names the contributing
  add-in and requested path instead of silently placing the item under AddIns. Tools and AddIns remain
  the supported locations; separately registered commands remain available.

- **Launched language servers end with HexIDE on Windows** (#406). Stdio and launched pipe
  servers use the shared kill-on-close job, including after a crash or forced stop. Descendants
  created after assignment are covered; externally owned servers are left alone. Linux and macOS
  watchdog teardown remains a follow-up.

- **The launched-server lifetime test helper starts with its own dependencies** (#406).
  Stdio and pipe readiness checks also run on Linux, so helper startup failures cannot hide
  behind the Windows-only job-object checks.

- **A member used on a non-object Variant raises error 424** (#257) for the measured Empty, Null,
  Integer, String and array receivers, so `On Error Resume Next` records 424 in `Err.Number` instead of
  zero or a member-not-found compile error.
  Nothing continues to raise 91, and an unknown Collection member 438. A missing form member through
  Object now raises 438 too.

- **Numeric `<=` and `>=` compare in VB6's measured pair domains** (#229), including Single/Double, Single/Long, Currency/Double and Decimal/Double. Boolean ordering treats True as -1 and False as 0.

- **Arrays of a user-defined type, and array fields inside a Type, now run** (#178). Each element owns
  its fields; whole-record assignments copy nested records and arrays independently. ReDim Preserve
  keeps surviving records, ReDim and Erase reset them, and passing an element ByRef writes through
  while locking its array. Indexed field assignments retain their subscripts, and whole-array field
  assignments use the same type checks as array variables. Nonliteral Type-field bounds remain refused.

- **Procedure calls reclaim their storage when they return** (#79, phase 1), including error unwinds and
  failed parameter binding. ByRef aliases and Static locals keep their storage. Freed slot numbers are
  never reused, stale debugger frames are safe to inspect, and slot-counter exhaustion raises error 7.

- **Add-in tool windows retain their saved layout** (#50). Late registration restores the saved region,
  order, size and open state; pending entries survive saves before the add-in loads.

- **Reset Window Layout keeps add-in windows open** (#50). Open add-in tools return to the default right
  column instead of disappearing; closed add-in tools stay closed.

- **Missing modules stay missing on save** (#44). Standard modules, classes, UserControls and
  PropertyPages whose files are absent remain preserved entries with missing-file markers, rather than
  empty live modules that Save writes to disk. Sidecar marks stay carried when a preserved missing module
  shares a loaded document's name.

## [0.3.0] — 2026-10-26

Most of this release is about running real programs. Files, the registry, conditional compilation and the
byte-string functions now run; the ComboBox, ListBox, TextBox and CommonDialog gain the events and properties
programs rely on, each measured against vb6.exe; the ProgressBar arrives as the first of VB6's in-box ActiveX
controls; and a made program starts. Windows gets an installer beside the archives, and the code window gains
folding, keymaps and a mark on every tab with unsaved changes.

### Added

- **MCP: `get_document_tabs` and `get_open_editors` say whether a tab has unsaved changes** (#1945). Each
  tab that can be saved carries `isModified`, the state its `*` marker shows, which the title leaves out. A tool
  tab and the read-only project and archive views have none, since they hold nothing to save.

- **`LenB`, `AscB` and `InStrB` run, and `MidB`, `LeftB` and `RightB` where their result is whole characters**
  (#1510). Each counts bytes, as measured on vb6.exe: `LenB("abc")` is 6, `InStrB` searches by byte, and `LeftB("abc", 4)`
  is "ab". A result of an odd number of bytes, and so `ChrB`, is refused, where all seven stopped the run as undefined.

- **Isolated parse-depth probes** (#1223) measure long operator chains, line continuations,
  interpreter execution and language-server tree walks in separate processes, with a recorded
  matrix of results and generated modules for live IDE checks.

- **The VB6 behaviour oracle is mirrored to its neutral home** (#1216). Changes to the working copy in
  HexIDE publish to hexide-io/vb6-behaviour, with CC BY 4.0 for measurements, prose and tables and MIT for
  probe code. A missing mirror credential fails visibly.

- **The ProgressBar, the first of VB6's in-box ActiveX controls** (#1082). A form holding one from MSCOMCTL.OCX or
  COMCTL32.OCX opens, runs and saves byte for byte, and keeps the library it was read under. Its members, errors and
  constants are VB6's, measured on vb6.exe, and it is drawn as the Windows progress bar. A form holding any other
  in-box control is refused with a message naming the issue that tracks it.

- **Toggle Fold in the code window** (#1014): Edit and context-menu commands, with Ctrl+M (Cmd+M on macOS),
  fold or expand the innermost section at the caret or starting on its row. An expanded member attribute run
  can be folded again without folding its procedure or reopening the window.

- **Unsaved-change markers on document tabs** (#901): designer edits, code edits and carried-file edits now
  light Dock's modified marker; saving clears it using the existing dirty-state checks.

- **Add-Ins → Add-In Manager…** (#898) opens Options at Add-Ins, including when no add-ins are installed. Contributed commands follow the manager after a separator.

- **Profile bundle design** (#504): records the approved versioned directory format, name/path/safe-mode
  selection and replacement for `--personality`, with built-in compatibility and separate implementation
  tasks. Profile loading and CLI migration remain future phases.

- **Rename modules in the Properties window** (#493). Selecting a module, class, UserControl or PropertyPage
  in Project Explorer offers `(Name)`, with the existing project naming checks. The filename stays unchanged;
  a designer root rename also keeps its module name and `VB_Name` together.

- **Diagnostic tooltips** (#451). Hovering a squiggle shows every overlapping message and its source
  when supplied, alongside quick info, in every personality and with Auto Quick Info disabled.

- **Icon geometry contract and guard** (#420). The live spec permits attributed, permissively licensed icon
  sets and requires keyed single paths without a fill for host tinting. Tests guard the shipped geometry
  resources; artwork extracted from another application remains forbidden.

- **User keymap packs** (#414). JSON files in the active user-data directory's `keymaps` folder appear in
  Options → Keymap on startup and apply without a rebuild. Built-in Default and VB6 remain available.

- **One-answer language-server routing is recorded** (#407). Skipped advertisers record which server
  has priority for folding, document symbols, formatting and rename, once per capability per connection.

- **Language-server argument arrays and environment overrides** (#385). Stdio and launched pipe servers
  accept an argv array alongside the existing command-line string, and an environment map layered over
  inherited variables. Pipe placeholders expand within each argument.

- **Incoming WebSocket language servers** (#384). Set `websocketRole` to `listen` to let a server
  dial HexIDE at the configured endpoint; the default remains `connect`.

- **Language-server work progress** (#360): attached servers can create progress tokens and report
  indexing in the status bar. Progress finishes immediately on end or disconnect; titles and messages
  are flattened and capped, with a localized prefix in every shipped language.

- **Release CI coverage** (#354). Both shipping projects compile in Release, and CI checks the Desktop
  output for automation server code, MCP dependencies and AspNetCore framework references.

- **A language server can be given extra arguments without replacing its entry** (#262). Name the server's id
  in `lsp-servers.json`'s top-level `arguments`, and those arguments are added after the entry's own. This is
  how the bundled VB6 server takes an extra argument: its command is found when HexIDE starts, so replacing the
  entry meant writing a path a user cannot know. See "Adding arguments to a server without replacing it" in
  `docs/language-servers.md`.

- **Export picture properties** (#159). Right-click a framed picture property in the Properties window to save
  its image payload without changing the form or its companion. PictureBox now peels the same framing before decoding (#156).

- **Windows x64 installer** (part of #77): Inno Setup packages the self-contained build for all
  users in Program Files, with a Start-menu shortcut and an uninstaller. Portable archives remain
  available; auto-update and file associations are not included.

- **Code printing proposal** (#45): documented platform print paths, scope and page setup choices,
  dependencies and verification for review before implementation.
- **Code printing contracts** (#45): recorded the accepted platform paths, scope and page setup
  defaults in an OpenSpec change with phased implementation and verification tasks; printing remains
  unimplemented.

- **Owner PID for launched pipe language servers** (#406). `{pid}` in `arguments` expands to HexIDE's
  process ID, so a server that watches its owner can exit after a crash or forced IDE shutdown.

- **Release download and run instructions** (#863): README now covers Linux asset selection, extraction
  into a separate folder, launch and missing-`tar` alternatives, with the measured openSUSE GNOME result.
  The next release's download notes link there, name the plain .NET 10 Runtime for smaller builds on
  every platform, and include ICU among Linux's native dependencies.

- **`Declare PtrSafe` parses** (#1548) in the interpreter and language server. VBA7 declarations using existing
  types now let a module load; external API calls remain deferred. `LongPtr`, `LongLong` and their conversions
  are follow-up work.

- **MCP tool table drift guard** (#1339). Tests check CLAUDE.md's curated signatures against the tools,
  including parameter order, optionality and every `interact` action. Corrected missing `project?`
  parameters on `open_file` and `view_designer`, and the optional `armed?` parameter on `arm_lsp_capture`.

- **CI keeps test-host crash logs** (#1309). Failed test jobs upload their `TestResults` directories
  as artifacts named for the job and operating system, retained for seven days.

- **Start automation with an unsaved project** (#502). `--newproject-unsaved` skips the startup dialog without
  writing project or component files, so `untitled:` language-server URIs can be exercised from launch.
  `--newproject` still saves to a temporary directory; `add_file` already leaves new documents pathless (#500).

- **A Type field declared `As New C1` makes its object on first use** (#1488), as measured in VB6. Declaring the Type and writing another field make nothing; copying it shares the object, which terminates when the procedure exits.

- **Compiler exchange recording and replay tooling** (#1218): versioned JSON-lines captures, explicit run-value
  placeholders, redaction checks and an offline synthetic preparation exchange. Real beta recordings follow in #1689.

- **Keyboard parameter-info regression tests** (#1262). Headless tests cover typing `MsgBox(`, the signature-help
  request position, and parameter info being open before the typing reply when the server answers after a delay.

- **Random and Binary files open, and Get and Put run** (#1646). They were refused at the `Open`, so no program that
  keeps records or structs in a file got past it. Measured on vb6.exe first, in eight batches: the bytes of every
  type, String, Variant, array and Type; records and `Len =`; EOF; a second handle and the `Lock` and `Access` clauses;
  and how, after a read past the end, the next `Put` appends where the file stopped. Line Input, Input # and Input read
  a Binary file too.

- **LOF, Loc, Seek and the Input function run** (#1645). Each stopped the program, so `Input(LOF(f), f)`, the usual
  way to read a whole file, never ran. All four were measured on vb6.exe first, with the `Seek` statement, line breaks,
  a Ctrl+Z, the code page and Output and Append files, and run as measured. `Input$` is `Input`; `InputB` waits on byte
  strings (#1510).

- **GetSetting, SaveSetting, DeleteSetting and GetAllSettings run** (#1562). A program that remembered anything between runs used to stop, often in `Form_Load`, before its first form appeared. All four were measured on vb6.exe first. On Windows they use the registry key VB6 itself uses, so a program run in HexIDE and the same program compiled by VB6 share their settings. On Linux and macOS they are kept in `program-settings.json` in HexIDE's user-data directory, which `--user-data-dir` moves.

- **`Like` runs** (#1547). Any program that used it stopped there. It now matches as vb6.exe does: `?`, `*`, `#`, `[list]` and `[!list]`, under the module's `Option Compare`, with error 93 for a pattern it cannot read. That behaviour was measured over about 190 rows, native and p-code. One known difference remains. Under `Option Compare Text`, vb6.exe takes a ligature or a sharp s for the letters it stands for, so `"ß" Like "ss"` is True there and False here (#1479).

- **VB6 filesystem operations** (#1561): `CurDir`, `MkDir`, `RmDir`, `FileLen`, `FileDateTime`, `GetAttr`, `SetAttr`, `FileCopy`, `Name`, `Kill` and stateful `Dir`, using compiled VB6 measurements.

- **Conditional compilation runs: `#If`, `#ElseIf`, `#Else`, `#End If` and `#Const`** (#1112). A module holding any
  `#If` stopped the program as it loaded, and `#Const` did not parse. Now each branch is chosen as vb6.exe chooses it,
  measured with `/make`, native and p-code. The names are the module's `#Const`s, the project's Conditional Compilation
  Arguments (`CondComp`) and VB6's `Win32` and `Win16`. An inactive branch is never parsed, so code written for another
  compiler, such as VBA7's `Declare PtrSafe`, runs unchanged. A directive's error stops the program as it loads, even in a
  branch never taken, as vb6.exe refuses to build. The module saves exactly as written: the selection exists only for the
  parser. Every personality uses VB6's constants for now (#1548).

- **The language server no longer squiggles code vb6.exe never compiles** (#1112). An inactive `#If` branch is no longer
  parsed, so a VBA7 `Declare PtrSafe` behind `#If VBA7 Then`, or a procedure whose `#If` gives it two headers, has no
  error marks. The branches are the project's: the server reads `CondComp` from the `.vbp` that lists the document. A
  directive out of place is reported on its line in vb6.exe's words. Where a condition is beyond the server's reduced
  evaluator, such as a string comparison or a function, nothing inside that block is marked.

- **A control's `ZOrder` method** (#1503). `ZOrder 0`, or no argument, brings a control to the front of the controls
  like it in its container, and `ZOrder 1` sends it to the back, as measured on vb6.exe. A Label never rises over a
  windowed control, a container takes its contents, and the position is 0 or 1 or it raises VB6's error. A program
  that called it used to stop as not implemented.

- **A control HexIDE does not model is shown in the designer** (#157). An ImageList, a Toolbar or any other control
  HexIDE carries without modelling used to be invisible, so a developer could move a button onto it unknowing. Now
  each is a box where VB6's IDE puts one: at its position, sized by its `Width` and `Height`, or 1200 by 480 twips
  when it records neither, as VB6 measures it. The box names the control's class and says HexIDE does not support
  it; it cannot be selected or moved, and the control is still saved exactly as it was read.
- **A ComboBox raises Click, Change and DropDown** (#1429). It raised none, so a program's `Combo1_Click` and
  `Combo1_Change` never ran, whatever the user or code did. Now each comes where vb6.exe raises it, measured with real
  input, native and p-code:
  - **Click** for every item chosen, by the mouse or an arrow key, even the one already selected, and for code's
    `ListIndex` when it moves the selection;
  - **Change** for typing, and for code's `Text` when it changes, but not for choosing an item;
  - **DropDown** as the list opens.

  Typing an item's text into the box selects nothing, as in VB6.
- **A ComboBox has a Text** (#926). It had none, so code reading `Combo1.Text` stopped, and a combo's Text could be
  neither set in the Properties window nor drawn. Now a Dropdown Combo's and a Simple Combo's Text works as vb6.exe's
  does:
  - code's Text selects no item, even one it matches;
  - choosing an item puts its text in the box.

  A Dropdown List's Text is refused at run time until its Style is modelled (#1428), and is never saved, as in VB6. A
  newly drawn TextBox or ComboBox starts with its name as its Text, as VB6's designer gives it.
- **A ListBox's MultiSelect** (#1413). A list set to Simple or Extended used to run single-select, and its `ListIndex`
  read -1 where VB6 reads 0. Now it selects as vb6.exe's does, with real input measured, native and p-code:
  - **Simple** toggles the item clicked, and Space toggles the focused one;
  - **Extended** selects the item clicked alone, Ctrl+click toggles it, and Shift selects a range from the anchor;
  - **`ListIndex`** is the focused item, and `Selected(i)`, `SelCount` and `Text` read and set the selection as VB6's do.

  MultiSelect is in the Properties window, saved as VB6 writes it, and read-only to a program, as in VB6.
- **Shared DAP protocol capture** (#1166). The DAP library can record wire traffic and process diagnostics
  in the existing inspector's conversation log, with bounded retention and source-path pseudonymisation on
  export. A host that launches external debug sessions from the IDE remains subsequent work.

- **DAP adapter process ownership** (#1162). The client library launches explicit adapter commands, retains
  bounded stderr diagnostics and exit status, and cleans up unresponsive processes. Backend selection,
  protocol-inspector capture and external debugging through the IDE remain subsequent work.

- **DAP wire client foundation** (#1160). A stream-based client library supports framed requests, ordered
  events, adapter errors and bounded connection teardown. External debugger launch and IDE integration remain
  subsequent work; this does not yet change F5 or expose an external debugger in the UI.

- **Text clipboard operations** (#1156). `Clipboard.Clear`, `SetText`, `GetText` and `GetFormat(vbCFText)` now
  use the host clipboard, enabling Notepad's Edit menu, Cut, Copy and Paste. Defaults, ANSI conversion, NUL
  truncation and argument errors follow measured VB6 behavior. Non-text operations remain explicitly unsupported;
  setting text over non-text formats is refused until they can be preserved (call Clear first).

- **A TextBox's `MultiLine` and `ScrollBars`** (#1340). They were kept in the file and ignored, so Enter added
  nothing, no long line wrapped and no box drew a scroll bar. Now a MultiLine box takes Enter as a CR LF on every host,
  wraps unless it has a horizontal scroll bar, and draws the bars it asks for. A single-line box draws none, which also
  ends the vertical bar fragment a short one showed (#914). Both read back at run time and refuse a write with 382, as
  vb6.exe measured.
- **A TextBox's `SelStart`, `SelLength` and `SelText`** (#1150). They raised 438. They now read and write as vb6.exe
  measured: setting SelStart drops the selection, positions past the end clamp, a negative one is 380, and SelText
  leaves the caret after what it put in.
- **A form's `WindowState`** (#1152). Reading or setting it stopped the run, and a form saved maximized opened at its
  normal size. It now reads and writes as vb6.exe measured: 0 until the form is shown, then the state it is in, with
  380 for a value other than 0, 1 or 2. A form saved minimized or maximized shows that way.
- **The CommonDialog control** (#1084). A form holding one used to be refused at run time. Now `ShowOpen`,
  `ShowSave` and `ShowFont` show the IDE's own dialogs, and an automation client can answer the pickers with
  `answer_next_file_dialog`. Cancel and `CancelError`, `FileName`, `FileTitle`, `DefaultExt` and the Font properties
  behave as vb6.exe measured, and the control's 102 constants resolve in a project that references it. The form still
  saves byte for byte. `ShowColor`, `ShowPrinter` and `ShowHelp` are still to come.
- **`Command`, and `Left$` in a form's code** (#742). Both stopped a run. `Command$`, `Command` and `Command()`
  now give the project's command line arguments, empty where it has none. `Left$("abc", 1)` in a form's code is
  VBA's `Left$`, as vb6.exe measured.
- **Text files** (#1145). `Open` For Input, Output and Append, `Line Input #`, `Print #`, `Close`, `FreeFile` and
  `EOF` used to stop a run at the first of them. Now they read and write as vb6.exe does, byte for byte:
  - lines end at CR or CR LF, and Ctrl+Z ends the file;
  - numbers are printed with their sign's space, and commas move to 14-column zones;
  - text is in the ANSI code page;
  - the errors are VB6's: 52, 53, 54, 55, 62, 75 and 76.

  Every rule was measured, native and p-code. Random and Binary files, `Input #` and `Write #` are still to come.
- **The Line control** (#163). A form holding one used to be refused at run time, since #857. Now:
  - it loads, runs and saves byte for byte, VB6's About Dialog included;
  - it draws pixel for pixel as VB6 does: dashes, widths, and which of two lines is on top;
  - its members read and write from code, with VB6's types and refusals.

  The designer draws a Line and moves it by the rectangle it covers. Every rule was measured on vb6.exe. Still to
  come: the toolbox entry, and dragging one endpoint at a time.
- **`RGB` and `QBColor`.** They stopped a run with "Sub or Function not defined"; both now return VB6's Long,
  with its rounding, clamping and errors, measured.
- **The protocol inspector says when a server's bytes actually moved**, not only when a message was prepared. For
  a server started over standard input and output or a named pipe, the timeline now shows when the first message
  began to be written, when it was handed to the server, when the IDE began waiting for a reply, and when the
  server's first bytes arrived. A server that seems to ignore the IDE can now be told apart from a message that
  never reached it.
- **`--ansi-code-page <n>`, in Debug builds**, reads and writes VB6 sources as a machine whose ANSI code page is
  `n` would, so a file from, say, a Japanese machine can be checked on a Western one.

### Changed

- **Silent-server warning test failures include the connection attempt and recorded logs** (#1937),
  including exception details, so an early initialize failure can be distinguished from the expected timeout.

- **Headless font failures report their stage at startup** (#1578). The Integration test app probes the
  default Bold face once and reports the family name and whether glyph creation returned null, so a
  recurrence of the intermittent macOS failure carries evidence. Its underlying cause remains open.

- **Changelog entries are one file per change** (#1375). Each change records its entry under `changelog.d/`,
  named for its issue, and `scripts/changelog-gather.sh` folds them into this file at release, so pull requests
  no longer conflict with each other here on every merge. `ChangelogFragmentTests` checks each fragment.

- **Merges wait for every head-commit job, including advisory macOS** (#1331). The contributor and agent
  guides now require all jobs to finish without red and main's latest run not to be red; a known flake
  must have its run posted on its issue and be re-run green first. The workflow comment records that
  Windows is now required by branch protection and why macOS remains advisory.

- **The VB6 fidelity oracle is split by subject** (#1216). Its index and probe harness are in
  `docs/vb6-fidelity-oracle/README.md`; the measured rows are preserved in separate subject files.

- **MCP restart test diagnostics** (#778): the first call after a restart reports phase timings and
  the full exception chain on failure. Passing runs keep debug timings in their test output. The test
  uses the SDK's original HTTP transport and states that reconnect attempts are unavailable from the
  SDK; the 20-second bound is unchanged.

- **MCP SDK 2.2.0** (#764). The automation server supports discovery and the sessionless 2026-07-28
  protocol, while keeping legacy clients stateless across IDE rebuilds.

- **Native calls use source-generated marshalling** (#608). All P/Invokes now use `LibraryImport`,
  with explicit string and Boolean marshalling. The three interop projects now permit generated unsafe
  marshalling, with a test guard against handwritten unsafe code in those projects.

- **Personality specification matches its implemented scope** (#419): personalities control menu entries
  and project types; removed the unsupported claim that they control toolbar buttons.

- **Shutdown wire documentation** (#368) records rumdl’s rejection of `"params":null` and distinguishes
  it from StreamJsonRpc’s null argument object, which omits the member. The client is unchanged.

- **Full-text language-server sync is deliberate** (#282). Documented that servers declaring Incremental
  receive full-document changes too, as LSP permits. Incremental sync is deferred until a server needs it
  to justify the risk of desynchronizing document copies; client behavior is unchanged.

- **The Windows installer is `hexide-windows-amd64-setup.exe`** (#2051). Its name used to carry the version,
  so hexide.io, which links every download through the latest release, could not link it. The name is now
  fixed, like every other download. THIRD-PARTY-NOTICES.md now credits Inno Setup, whose setup code the
  installer carries.

- **The automation server’s loopback gate is exercised through real HTTP requests** (#773). The IDE and
  the Kestrel test server share its middleware wiring; foreign Host and Origin headers are refused on
  `/mcp`, and a foreign Host is refused on `/health`, while loopback clients are answered.

- **The code window shows the whole file, header included, and one line number now means the same thing
  everywhere** - to the editor, to a language server, to the interpreter and to the debugger. A form's
  designer block and a module's `Attribute` header are no longer hidden from the code window.
- **That header opens folded**, into one line at the top of the window, so a form's code still starts where
  it did. Expand it and it stays expanded in that window, through formatting, a change in the form designer
  and a reload, until you fold it again. It needs no language server. A module whose header is a single line
  has nothing to fold.
- **The header and a procedure's `Attribute` lines are drawn in grey**, so the lines only the IDE writes look
  it. Each theme sets its own grey, and each stays legible on that theme's editor. A folded section's label
  takes the same colour, which makes those labels legible on a white or Dark editor, where the old grey was
  not. A web or mail address in those lines is greyed with them and is no longer a link. A theme pack sets
  the colour as `ReadOnlyText`.
- **Undo in the code window never undoes a designer change.** A change committed in the form designer
  reaches the code window as a rewrite of the header at the top of it, and Ctrl+Z there undoes the last
  thing typed in that window, exactly as before. **One consequence is worth knowing:** undoing a code edit
  made *before* a designer change costs the redo for it. The header has to be put back afterwards, and
  putting anything back is itself a change, which is what clears a redo stack. The alternatives were worse -
  discarding the whole history, or refusing to undo past the designer change at all.
- **Opening a file leaves nothing to undo.** Previously the first Ctrl+Z in a newly opened code window
  emptied it, because loading the text counted as an edit.
- **Reloading a file changed outside the IDE discards that window's undo history**, as reloading it into
  the form designer already did. The history described a document that is no longer there.
- **Only the IDE rewrites a file's header or a procedure's `Attribute` lines.** Now that the code window
  shows them, the commands that write into it keep off them. Formatting leaves them as they are. Find and
  Replace do not search them, as VB6's Find never searched the header it hid: searching a form's code for
  `Command1` finds it in the code rather than on the control's line in the designer block, and replacing
  every `Command1` does not rename the control behind the designer's back. Insert File puts its text after
  them. A rename that would reach them is
  refused with a message saying why - except for the renamed procedure's own `Attribute` line, which
  follows it. Add-ins and automation clients are refused the same writes, and told so. Typing keeps off
  them too, and cannot split a procedure from its `Attribute` lines: nothing can be typed at the start of
  one, and Delete at the end of a declaration cannot join the line below onto it. Nor can Replace, a
  rename, formatting or an add-in. Deleting or cutting a whole procedure takes its `Attribute` lines with
  it.
- **The bundled language server keeps to the same rule itself**, rather than leaving the code window to
  discard what it should not have sent. It reports nothing inside a header or a procedure's `Attribute`
  lines, its formatting touches only the lines that change, and rename and highlight leave those lines
  alone. So renaming a local that shares its name with a property the layout sets - a `Caption`, a `Text`,
  an `Index` - renames it in the code instead of being refused because the layout uses the same word. A
  control's or the form's own name is still not renamed from the code, and the code window now says so
  before asking for a new name: rename it in the Properties window. Another language server still gets the
  code window's protection.
- **The warning above the code of a form HexIDE cannot save now says what it means**: the file will not be
  saved, its code included, so typing in that window is refused. Typing now is refused everywhere in it:
  Enter, and accepting a completion, used to write a line break or a word there despite it, and an automation
  client's `type_text` and `press_key` are refused with the reason. It used to say the code could not be edited,
  which read like the header's read-only lines and was not true: Replace All, formatting, a rename, Insert File,
  undo and redo, an add-in or an automation client's `set_file_content` can still change the code there, and
  nothing they change is saved ([#701](https://github.com/hexide-io/HexIDE/issues/701)).
- **No breakpoint or bookmark can be set on a file's header or a procedure's `Attribute` lines**, by F9,
  Ctrl+F2, a click in either margin or an automation client; the status bar says why. Those lines never run.
  One already there can still be removed, and one found there among a project's saved marks is dropped as the
  project opens, with a note in the log.
- **Bookmarks and breakpoints saved by an earlier HexIDE stay on the statements they were set on.** They were
  counted from the first line of code and now count from the top of the file, so the file a project keeps them
  in (`<project>.user.hexproj`) is migrated as the project opens and rewritten once, as version 2, with the
  marks under new keys, `fileBookmarks` and `fileBreakpoints`. A mark for a document whose file is missing is
  kept as it is, and moved by the first opening that finds the file. A mark the old code window allowed on what
  is now a read-only line, such as a form's `Attribute VB_Name`, is dropped instead. **Going back to an earlier
  HexIDE afterwards costs those marks:** it shows none of a migrated file's bookmarks or breakpoints, and HexIDE
  0.2.0 and earlier delete them the next time they write that file, which saving the project or setting or
  clearing any mark does.
- **Editing code while a program is paused asks about resetting it only when the edit actually lands.** A
  keystroke in the header writes nothing and no longer asks, and nor does a change made in the form designer.
  Paste, cut and deleting a selection now do ask: until now they slipped past the prompt by every route, the
  keys, the menus and the toolbar alike.
- **For automation clients: `get_file_content` returns a form's, module's or class's whole file**, header
  included, whether or not it is open in the IDE, where it used to return the code alone. Its line 1 is the
  file's first line, as the breakpoint, diagnostic and debugger tools count, and an editor offset indexes it.
  `set_file_content` takes that text back unchanged, or the code alone as before. A whole `.frm` is no longer
  refused for being one: only a header that differs from the document's is refused, and nothing changes. Its
  reply now counts the lines the document holds from the top of the file. `hasUnsavedChanges` means what it
  did. A client that edits what it read and sends it back needs no change; one that searched `content` for
  line numbers now finds them counted from the top of the file.
- **For automation clients: every tool that takes a form, module or class by name finds it the same way** -
  in any loaded project, without regard to case, with an optional `project` for when a group holds two of one
  name. Without `project`, such a name is refused and the refusal lists both. `set_control_property`,
  `move_control` and `add_control` were the exceptions: the first looked only in the startup project, and the
  other two used whichever open designer of that name came first. So with a group open, a bare name those two
  accepted may now be refused as ambiguous; pass `project`. `add_file` takes `project` too, for the project to
  add to. The reply of every tool that takes a document's name now names what it found in `project` and
  `document`, spelled as the IDE spells them, and so do `add_file`'s reply and each `get_diagnostics` item; a
  diagnostic for a file two projects of a group share names neither. Where a reply also carries a `uri`, the
  tool's description now says what it is: the name the language server knows the document by. `get_debug_state`
  and `get_call_stack` name the running project. Not yet: the run-control replies' "Paused at" note, the
  locals' and watches' `context`, the designer undo, redo and format tools, and the tab tools, which take and
  report a tab's title, still name a form or module without its project.
- **For add-in authors: `AddinDocument.Content` is now the document's whole file**, as the code window shows it - a
  form's designer block, a class's header and the leading `Attribute` lines first, then the code - where it used to
  be the code alone. Every position on the add-in surface counts from the top of that file - selections, edits,
  navigation and diagnostics - where in 0.2.0 it counted from the first line of code, so line N of `Content` is
  line N everywhere. An add-in that counted lines itself is out by the header's length until it counts them in
  `Content`. `SetContent` takes `Content` back unchanged, or the code alone, and keeps the header either way; a header
  that differs is refused. **New `TrySetContent`, `TryApplyEdits` and `TryNavigateTo` say why a call was refused**:
  an unknown or ambiguous name, a header the call would change, or a line the document does not have. **New
  `IDiagnosticsAccess.TryGetFor` says why a name found no single document**, where `GetFor` answers an empty list for
  that and for a file with no diagnostics alike. The existing members keep their signatures and answers, so a
  packaged add-in keeps running; and the new ones have default implementations, so a hand-written `IEditorAccess` or
  `IDiagnosticsAccess` keeps compiling. A line outside the document passed to
  `NavigateTo` now moves nothing, where it closed the IDE, and navigating to a document open behind another tab
  brings it to the front, where the caret used to move out of sight.
- **The AI Chat panel is told, and tells you, when the IDE refuses an edit.** It is shown the whole file, header
  included, and told the header is read-only. An edit it asks for, or a block you apply, that would change the
  header is refused: the reason is shown in the transcript and the assistant is told it, on both paths, where
  before the edit was reported as done whether it was or not. Asking for the diagnostics of a file name two
  loaded projects share, or one none has, is refused with the reason rather than answered with no diagnostics.
  Its privacy notice now says the active file's contents are sent, layout and captions included, and is
  shown once more to anyone who saw the old one, which said "code".

### Fixed

- **MCP: `dump_visual_tree` cuts a tree too large for a client to take inline, and says so** (#1982). The whole IDE
  window ran to 66,000 to 84,000 characters, which Claude Code saved to a file instead of showing. A tree over 40,000
  characters is now cut at the most levels down that fit, and the `note`, written first, says where and how to see
  the rest.

- **MCP: `dump_visual_tree` no longer lists a row a long list has scrolled away from** (#1981). The list keeps the
  row a Tab into it would land on, usually the selected one, realized out of view, and the dump listed it with
  `isOffscreen: true`. It is now left out, as other rows out of view are, unless it has keyboard focus, and `select`
  with its value scrolls to it first.

- **MCP: every reply that names a waiting dialog says what it is** (#1975). The reset prompt and the run-time error
  dialog are both titled "HexIDE". Four replies named a dialog by its title alone: the program wait, keyboard typing's
  stop, the key refusal and `close`'s refusal. They now give its kind, first line and buttons where it has them, as
  the `modalDialog` note does since #1624.

- **A program reads and sets a control's TabIndex and TabStop** (#1974). Reading either stopped the run with "Method or
  data member not found". Setting TabIndex now moves the control in the form's one tab order, renumbering the rest, as
  vb6.exe does, and the running form tabs in that order.

- **A made program starts** (#1971). File → Make writes the standalone runner, renamed, beside the project
  archive, and that runner is now a single self-contained executable, with .NET and the native libraries
  inside it. So a made program is one file plus its archive, and it runs on a machine with neither .NET nor
  HexIDE installed. Since the runner stopped being compiled AOT (#30), Make had been copying an executable
  that needed `HexIDE.Standalone.dll` and the rest of its publish folder beside it. Every program made that
  way exited at once with `0x8000809A` ("The application to execute does not exist"). **Programs made with
  v0.2.0 don't start, and need making again with this release.** The framework-dependent download grows
  accordingly, since its runner now carries a runtime of its own. A made program on macOS hasn't yet been
  started, so it is unverified there.

- **MCP: `take_snapshot` no longer throws "Positive number required" for a popup it cannot place** (#1960). A popup
  reported on no screen, or so far from the window that the image would pass 16,384 × 16,384 pixels' worth, is left
  out, and the reply's `note` says what it held, where it was and why. The window and every other open popup are
  still drawn.

- **CI actions use their supported Node 24 releases** (#1950). Build and publish workflows use the
  current GitHub action majors, and the release-writing action is pinned to a full commit SHA.

- **Active-form Save integration tests wait for completion** (#1906). Save, focus and command-requery
  assertions use bounded condition waits with the last observed state in timeout failures, avoiding
  timing failures under contention. The requery observer stays attached through the Save command's
  retained subscriber while waiting.

- **Long addition chains no longer crash the bundled language server's symbol and declaration walks**
  (#1894). Bound visitor recursion also protects Option Explicit checks while retaining declarations
  and symbols after the expression.

- **Long operator chains raise run-time error 28 instead of crashing the IDE** (#1893). The interpreter checks
  available stack space during expression execution, and declaration collection skips expression subtrees.
  Conditional-compilation expressions also check finished-tree depth before evaluation, refusing excessively
  deep chains through the existing not-an-expression path instead of terminating the host.
  Module-level constant-expression validation also checks available stack space before recursing, including
  on smaller Windows-sized stacks.

- **The tree hygiene guard detects C1 controls in Git for Windows' UTF mode** (#1881). Its pattern
  matches Unicode code points explicitly; the selftest exercises UTF matching on Linux too, including
  invalid UTF-8 alongside a control and ordinary punctuation that must pass.

- **Insert now toggles overtype in the code window** (#1870). The status bar switches immediately
  between its localized INS and OVR labels, without waiting for the caret to move.

- **Object Browser inputs and lists have localized automation names** (#1869). Screen readers and automation can identify the library selector, search field, search results, classes and members without relying on position.

- **Scroll bar buttons use the IDE language** (#1868). Screen readers and automation now receive localized
  line and page action names on vertical and horizontal scroll bars, including after a language change.

- **New event handlers keep the caret in their body** (#1865). A diagnostics refresh no longer reselects the
  Procedure box entry and moves typing onto the `Sub` declaration.

- **A form's `Move` runs** (#1569), where it stopped the program as not implemented. As measured on vb6.exe, it takes
  twips whatever the form's ScaleMode, checks every value before moving anything, and raises one Resize for a change of
  size, where assigning Width and then Height raises two.

- **Code windows and designers stay in the IDE** (#1361). Their tabs no longer offer Float or Float
  all, and float attempts keep the document docked instead of losing it in a hidden window.

- **The edit-while-running reset prompt** (#1360) uses VB6's measured wording and OK/Cancel buttons. Cancel reverts the edit and keeps the run; OK keeps the edit and ends the run. Updated all shipped translations.

- **Quick info during an external run** (#1347): hovering requests language-server quick info while the external
  program runs; a selected stopped frame still takes precedence for data tips. Headless regression tests cover
  this routing and hovering a visible identifier after scrolling.

- **Integration tests close the windows they open** (#1332). Dialogs and hidden windows no longer leak
  into later tests; a suite-wide guard reports any window left open and the test that opened it.

- **Running-form replies wait for loops resumed after a dialog, even after an earlier DoEvents** (#1318). A watch
  taken during a busy loop also stops attributing an unrelated dialog left open earlier to the action.

- **Call Stack row automation names follow the IDE language** (#1260), including the line label announced by screen readers and used in automation paths.

- **Selecting a Watches child no longer edits or deletes another watch** (#1229). Edit and Delete act only on a selected top-level watch; rebuilding the list clears selection.

- **A language server that stops reading can no longer hold HexIDE open when it closes** (#1221). Stopping a
  server waited two seconds for its answer to `shutdown`, but sending `exit` had no deadline. A server that had
  stopped reading left the client's last write unfinished, and `exit` waited behind it for good. Sending `exit`
  now has the same two-second deadline, and the connection is closed after it. A new test server that misbehaves
  on demand drives this and the client's other failure paths. It ignores `shutdown`, stalls or dies halfway
  through a reply, and sends malformed frames and floods.

- **Disposing a debug adapter during a request write reports `IOException` consistently** (#1192), rather than
  exposing a scheduling-dependent `ObjectDisposedException` from the disposed process pipe.

- **The minimap follows code folding** (#1131): hidden lines take no space, the viewport marker spans the visible rows, and clicks target visible lines. Folding and unfolding redraw the map.

- **Pasting a declaration with its attribute run at another described declaration's end keeps both runs separate** (#1041). The pasted declaration now starts after the existing run, so saving cannot give both members' descriptions or dispatch ids to one member.

- **Deleting a described declaration through Replace, Replace All or an add-in range edit takes its Attribute lines with it** (#1038), matching deletion in the code window. Non-empty replacements keep the run.

- **Toggle Fold leaves Cmd+M to Minimize on macOS** (#1014). The command has no default shortcut or menu
  gesture there; Ctrl+M still toggles folds on Windows and Linux.

- **The diagnostics reopen regression waits for the reopen answer** (#963). Its scripted server now
  distinguishes the initial answer from the fresh one, so a slow run cannot mistake the initial answer
  for completion and report the close's empty clear as a later publication. The failure trace names that clear.

- **Status bar caret position and insert/overtype labels follow the selected language** (#960), including live language switches, with translations in every shipped pack.

- **The code window names form handlers `Form_<Event>`** (#937). Its Object box lists `Form` and
  `UserControl` rather than the root component's name; form events include Load and Resize, and an
  existing handler is selected instead of adding another stub.

- **The last control added to the designer selection is the reference** (#928). Its primary handles and Properties follow the selection, and Align and Make Same Size use it, including after reselection.

- **Short TextBoxes keep their text visible** (#914). Their vertical padding now follows the measured
  font line height, so single-line boxes at 285 and 315 twips also hold the taller default Windows font
  in the designer and running forms.

- **The Font dialog selects an uninstalled current family** (#912). The family is offered in sorted order,
  including MS Sans Serif, and OK keeps its name. The property picker passes the stored name without rendering fallbacks.

- **The Object Browser and Properties object box show control class names** (#905), such as TextBox and CommandButton, while new controls keep their existing default-name prefixes. Object Browser search also finds those class names.

- **Object Browser project classes stay current** (#904). The class list follows form and module additions, removals and renames, along with project renames. Deleted classes are removed from navigation history.

- **The Object Browser loads VBA built-ins** (#903). A capable running language server supplies the Globals members; before one starts, the browser explains why they are unavailable and retries when VBA or All Libraries is selected again.

- **Object Browser status rows stay under VBA** (#903). All Libraries shows only real classes, so opening the browser before a language server starts no longer adds a no-server entry to the combined class list.

- **Project Explorer Save and Save As now save the selected form** (#902), through the same service as the designer. Project → Add MDI Form is disabled until MDI forms are implemented.

- **Designer tab stays marked as saved after deleting a control** (#901): releasing a pointer selection now
  ends the designer's drag, so a later Delete key commits the deletion, records its undo and shows the tab's
  unsaved-change marker.

- **Docked panes keep their full opacity after switching tabs or reopening tools** (#900). Disable Dock's
  reveal animation, which could leave the designer and tool contents dimmed at 85% or 72.25% opacity.

- **Save Form and Save Form As follow the active tab even while a tool window has focus** (#899).
  The shell delegates to the active designer or form code window, preserving format-on-save. Switching
  tabs refreshes the commands without requiring a focus change.

- **Start with VB6 shows its shortcut once** (#897). Removed the embedded Alt+F5 from the menu label in
  English and all 29 translations; the gesture column continues to show the live keymap's shortcut.

- **The form designer paints its title bar** (#896). It now uses the active title-bar brush directly,
  restoring a readable white caption on Classic's navy bar and removing a failed MDI ancestor binding.

- **Named-pipe process tests tolerate a loaded runner** (#892). Wait for stderr, exit and close callbacks
  directly with a two-minute safety bound, and name the missing callback on timeout rather than implying
  that an exit code was lost.

- **Bookmarks and breakpoints survive a conflicted external header change and a reopen** (#884).
  Sidecar version 3 counts code marks from the end of each document's header, with separate file-relative
  entries for existing header marks. Earlier marks migrate on open; older builds preserve the new keys.

- **Break mode shows in the IDE title** (#837). Paused programs show a localized `[break]` tag and clear the running status message. Design and run tags and the no-project title are localized too, and refresh when the language changes.

- **Program message box buttons follow the IDE language** (#828), with all seven captions translated in every
  shipped language pack. The standalone runner keeps English.

- **Made programs leave carried files out of their package** (#750). Their project item lines are omitted,
  including lines reclassified from modules or classes, so they cannot publish paths back to the developer's
  source tree. Other project entries keep their order, and ordinary project saves still include carried files.

- **Make Project messages follow the IDE language** (#747). All five messages are translated. An unsupported
  platform message now names supported systems, and a failed build says the program was not made with an Error icon.

- **Carried files keep their bytes when saved** (#745). Invalid UTF-8 is read and written as Latin-1,
  preserving untouched bytes; saving an unchanged buffer does not rewrite the file.
- **Carried-file save refusals explain why nothing was written** (#745). An edit the file's encoding cannot
  hold now shows the source-encoding warning, naming the file, code page, character and line. The original
  bytes stay untouched, the edit stays unsaved, and the refusal is logged as a warning.

- **Menu shortcut labels follow keymap changes immediately** (#732), including clearing unbound shortcuts.
  Bookmark menus also derive their labels from commands, preserving platform-specific modifiers.

- **VB6 keywords are highlighted regardless of case** (#716). `Begin`, `begin` and a standalone `End`
  now receive their keyword colour, as do keywords typed with different casing from the syntax definition.

- **Light syntax colours meet the editor contrast bar** (#713). Metadata keywords use a darker magenta
  that clears WCAG AA 4.5:1 on Classic’s white editor. Tests cover every named light colour and include
  metadata keywords in both dark palettes, refusing missing or non-solid foregrounds.

- **Dark and Abyss warning colours** (#712). Warning banners now use dark amber backgrounds and visible borders;
  validation errors use legible light red text. Shipped dark packs are checked for every mapped colour key.

- **Event-handler navigation skips the file header** (#711). A designer caption naming the handler no longer takes the caret into the designer block or prevents a missing handler from being generated, from either a double-click or the Procedure box.

- **Delete in the code window removes the next character when nothing is selected** (#708), including through
  the Edit menu. Editing during a run still asks whether to reset the project.

- **The automation gap catalogue now records gutter clicks as supported** (#707). `interact click`
  already reaches breakpoint, bookmark and fold margins through editor-relative coordinates (#1033);
  the closed entry preserves the original observations and documents the targeting recipe and pending
  live verification.

- **Find explains protected matches** (#705). When text occurs only in a file's header or procedure
  `Attribute` lines, Find says why it skips them. Replace All reports skipped matches beside replacements.

- **Find Up visits other open documents before wrapping** (#703). From offset 0 under All Open Documents,
  plain-text Find now searches the other documents before returning to the bottom of the active document.

- **End-of-line syntax errors name the token** (#665). Whitespace-only offending tokens now read
  `end of line` instead of empty quotes in unexpected-input and syntax-error-near messages.

- **IDE timer failures reach the log** (#652). The dock-layout save, language revert countdown, minimap
  redraw and designer code-navigation follow-up log exceptions with their purpose and stack. They remain
  nonfatal; VB6 Timer events keep the interpreter's existing runtime-error handling.

- **MCP shutdown ordering** (#644): finish the reply before closing the main window, and keep the
  automation server alive when shutdown is cancelled by stopping it only on desktop exit.

- **Translated control rename refusals** (#629). Empty and duplicate names now use the active IDE language in the Properties window and `set_control_property`, with translations in all 29 shipped language packs.

- **Form Layout previews the form it represents** (#576). The thumbnail shows the form name, follows
  renames, and disappears when the form is removed or there is no form to preview.

- **Saving IDE settings preserves hand edits and other instances’ changes** (#557). Only properties
  changed in this instance are merged into the latest `settings.json`, so a window move no longer
  overwrites trust settings such as `revocationListUrl`. Settings are replaced atomically.

- **Icon-only buttons have localized automation names** (#542): Dock pane buttons and tab scroll arrows, document-tab
  close buttons, designer caption buttons, dropdown toggles and property pickers. A headless render-and-walk guard
  checks the main window, designer, Properties, Project Explorer, Object Browser and New Project dialog for type names
  and unnamed rows.

- **Localized the code window’s Rename prompt** (#537): its title and new-name label now follow the IDE language in every shipped pack. Shared dialog buttons remain tracked by #828.

- **Code-window writes keep the document's line endings** (#530). Enter, Insert File, automation typing and
  whole-content replacements (including add-ins and AI Chat) normalize new line breaks to the document's own.
  LF files stay LF; a buffer with no line break starts with CRLF. Saving does not rewrite existing terminators.

- **Command-line output redirected by a Windows batch file is preserved** (#528). Help and refusals
  write to the inherited file or pipe before attempting to borrow a console, including when the
  launching cmd has no console window.

- **Properties window values no longer scroll vertically inside their rows** (#524). Single-line text
  and numeric editors disable vertical scrolling in both the Alphabetic and Categorized lists.

- **Every routed application command can be rebound by a keymap** (#504, #415). Call Stack,
  Reset Window Layout and Go To Declaration now participate in pack bindings and Default restoration;
  a test guards the command table against missing, extra or miswired entries.

- **Foreign-server checksum mismatches fail visibly outside CI** (#487). Rejected downloads record the
  expected and actual digests for an ordinary test to report, while affected protocol tests skip with a
  checksum-specific reason instead of disappearing during discovery.

- **An unanswered language-server request no longer waits forever** (#486). Requests have a 30-second
  local deadline, compose with caller cancellation, and record a timeout while keeping the connection usable.

- **External changes are watched after first save and Save As** (#476). Watches follow loaded forms and
  modules in subfolders and shared directories, update when documents are added, removed or repointed,
  and release the directories actually attached when a project unloads.

- **Foreign-server diagnostics waits report a process crash as a crash** (#449). The lazy-start,
  configuration-file and untitled-document tests name the connection, non-zero exit code and outstanding
  requests, with the captured conversation, instead of reporting only missing diagnostics. No retry is added.

- **The tree-hygiene link scan refuses to pass if it cannot create its temp file** (#443).
  It uses `mktemp`, honours `TMPDIR`, and removes the file on exit instead of leaving a predictable PID path.

- **Hygiene selftest pins scan scope** (#442). Probes now catch broadened documentation allow-lists
  and script exclusions, ignored build output re-entering scans, and root-level Markdown links being
  skipped. Probes outside the scratch directory refuse to overwrite existing files and are cleaned up.

- **The tree-hygiene guard accepts Markdown link titles, angle-bracket destinations and percent-escaped
  filenames** (#438). Existing files linked with these forms no longer produce false dangling-link failures;
  the guard's selftest covers all three alongside a plain destination.

- **Localisation test packs stay out of the shipped assembly** (#423). The synthetic `zz` and `zz-ZZ`
  fixtures now belong to the test project; they were never offered in the language selector.

- **Protocol inspector pending count appears as traffic arrives** (#401). The visible inspector polls a
  cheap capture counter while keeping the grid still until Refresh; filters and ring eviction no longer
  distort the count of new entries.

- **Workspace folder names no longer disclose project names in conversation exports** (#397).
  HexIDE sends the folder URI's session pseudonym as the handshake's folder name, keeping textual
  body redaction intact. Names in server-authored fields remain outside this fix.

- **A wedged language server no longer blocks IDE shutdown** (#392). The shutdown request has a two-second
  deadline, `exit` is still attempted, and stdio disposal waits up to two seconds after killing the child.

- **Report unrecognised pipe roles** (#383). A misspelled `pipeRole` on a pipe language server now
  produces a configuration warning while keeping the existing `connect` fallback.

- **The tree hygiene guard catches Windows user-profile paths in LSP URIs** (#366), including
  forward slashes, case variants and percent-encoded drive colons. Clone roots under `Repos` warn
  about portability. Individual tracked and untracked path fixtures protect the check in CI.

- **Identify each stdio language server in its logs** (#364). Start, start failure, stderr and exit
  messages carry the configured server ID, so interleaved output can be traced to its source.

- **Server running commentary reaches the default log** (#359). LSP type 4 now logs at Information;
  type 5 stays at Trace. A sink-level regression and metamodel enum guard cover the logging seam
  and future protocol values; existing completion display-kind omissions remain deferred.

- **Pipe server teardown respects process ownership and bring-up** (#313). Shutdown and exit are sent
  only after initialize answers. A launched pipe server gets two seconds to exit before its remaining
  process tree is killed; a server HexIDE only connected to is left running.

- **Add-in packages enforce their manifest compatibility requirements** (#310). A newer schema, a
  newer minimum HexIDE version, or an invalid minimum version is refused even in developer mode;
  Options → Add-Ins explains the requirement. The packer stamps the schema and accepts
  `--min-hexide-version`; unknown manifest members are retained for inspection.

- **Failed add-in initialization leaves no partial registrations** (#309). Tool-window factories remain
  eager; a throwing factory records the add-in's error, removes its earlier contributions and event
  subscriptions, and lets the IDE and other add-ins continue. Cleanup failures do not replace the original
  error or stop the remaining cleanup.

- **Carried `.cls` files reach VB6 only with a VB6 class header** (#279). The first non-blank line must
  be `VERSION 1.0 CLASS` (case-insensitive); LaTeX classes keep their other extension claimants without
  spurious VB6 diagnostics. Routing is recorded on open and retained through edits until reopened.

- **A continuation separates identifier names** (#224). Splitting `val _` / `ue = 5` no longer assigns to
  `value`; the measured probe prints Long 0. Both grammar copies keep the separator between word fragments
  as whitespace, while continuations inside multi-word keywords remain legal.

- **Leading colons and labels before `End If` run** (#223). Empty `If True Then:` lines also run in the
  interpreter and parse in the language server. Jumps to labels sharing a loop terminator remain deferred.

- **Unsupported constructs fail clearly** (#181). Declaring an array of a user-defined type now stops with a
  message naming the type, instead of accepting an unusable array. Bang access in an array-result assignment
  target now names the unsupported construct instead of showing .NET's generic message.

- **Windows installer compiler verification** (part of #77): release builds always install pinned
  Inno Setup 6.7.3, checking the download's size, SHA-256 and publisher signature before running
  it, and checking the installed compiler version before packaging.

- **AI Chat reloads its settings when reopened** (#57). Changes to `chat-settings.json` take effect without
  restarting the IDE, while preserving the conversation. An in-progress response finishes with its original settings.

- **The retained Option Explicit analyzer knows designer controls and project class/module names** (#54),
  plus `vbPixels`. It uses the form header and `.vbp` declarations without cross-file binding. Live
  undeclared-variable warnings remain default-off because the baseline does not cover all external names.

- **RTL icon direction** (#40). Object Browser navigation arrows now mirror with the interface language,
  while Start keeps its right-pointing play icon. Drawn command icons and tree chevrons retain Avalonia's
  built-in mirroring without a second flip.

- **Object Browser refresh and navigation** (#24). Selecting a project type reloads its members after edits;
  double-clicking a member opens its declaration, and Escape closes the browser.

- **Standard and class module breakpoints are covered through the IDE's run path** (#10). Regression
  tests confirm that stored breakpoints stop under the correct module name and file line; the existing
  module-loading boundary is UserControl (`.ctl`) and PropertyPage (`.pag`) modules.

- **Project-load warnings follow the IDE language** (#698), including form diagnostics, the warning heading and
  overflow count, and skipped ActiveX Documents. Runtime hosts without localization retain English diagnostics.

- **The Toolbox starts on the left and remembers being reopened** (#895). Removed a dependency cycle
  that could hand the dock factory a null tool; real composition tests and constructor guards catch it.

- **Redirected command-line output starts with the text, not a byte-order mark.** `--help` or a refusal
  written to a file could begin with a UTF-8 BOM whenever something in the process had set the console to
  `Encoding.UTF8`, which also made a ConsoleOutputTests case pass or fail by test order.

- **The bundled language server counts lone CR line endings** (#1052). Diagnostics, folds, rename, highlight
  and formatting now use the editor's line numbers in files with CR, LF or CRLF, and keep off the same protected lines.

- **Reversed scroll bars run in the right direction** (#787). A bar loaded with Min greater than Max keeps
  its Value, accepts values between either endpoint, and reverses the thumb-to-value mapping.

- **Save Project keeps recognised `.vbp` lines in their read order** (#855), including renamed forms,
  missing members and reclassified carried files. A form before a reference stays before it.

- **Cross-module Const initialisation** (#893). Module-level Consts and array bounds initialise a referenced
  module's declarations on demand, in either startup order. Module cycles raise "Circular dependencies between
  modules"; a class created by the test harness's top-level code sees the startup module's completed Consts.

- **Made programs keep non-ASCII source text across machines** (#731). Make packages its source copies as UTF-8
  with a BOM; source files keep their own encoding. Standalone `--check` now reads project and form files with
  the IDE's source decoder.

- **Delete Line and word deletion ask to reset a running project** (#1025). AvaloniaEdit's editing keys now
  arm the code window's reset prompt, including Delete Line, Delete Previous Word and Delete Next Word.
  A refused edit in the file header still asks nothing, and No restores the deleted code.

- **Add-in range edits preserve member Attribute runs** (#1012). Edits that add a run, promote an orphan,
  extend a run or turn it into an orphan are refused together, naming the affected members.

- **Pasting a comment and a declaration over a described declaration keeps its attributes** (#1039). Leading
  blank lines and continued declarations work too, including through Insert File; the run stays under the
  first compatible declaration.

- **Making a project again to the same path succeeds** (#748). The new project archive is completed beside
  the destination before replacing the previous build, so the second Make includes the current source.

- **Feature catalogue corrections** (#862): distinguish unbuilt COM features from platform gates, describe refused ActiveX templates and unmodelled controls at open, save and F5, and explain the DAO Data control exclusion.

- **Unimplemented constructs name the failing statement** (#758). The runtime-error dialog now appends
  `at <statement>`, including failures inside nested blocks or called procedures, without making them trappable.

- **A form resized from code raises Resize inside the assignment** (#1570), before the next statement,
  including while hidden in Load. The first Show still raises its own Resize; layout does not repeat the assignment's event.

- **Restart stops the previous run at its time-slice yield** (#761). End followed immediately by a new run can
  no longer let the old walk continue. Faults and `End` events from an old or unrelated interpreter cannot end
  the current run, for either startup forms or Sub Main.

- **A Label's Appearance can be set at run time** (#796). It resets BackColor to the button face for 3D or the
  window colour for Flat and redraws the border, as measured on vb6.exe. The assignment was refused.

- **Data-bound controls refuse to run** (#859). F5 and made programs name the control and its DataSource before placing any controls, instead of showing its design-time value as though it were the current record.

- **MousePointer reads back the value set** (#769), including the value loaded from a form. Reading it always returned 0 even when the displayed cursor had changed.

- **Clipboard.SetText preserves other clipboard formats** (#1246). Replacing text no longer stops a run merely
  because other formats are present. A format that cannot be read is still refused before the clipboard is changed.

- **Member call statements accept a parenthesised argument without a space** (#1064). `Form2.Show(0)` and
  `Text1.Move(100)` now run as their spaced forms do, and the language server accepts them too.

- **Unimplemented faults survive reference cleanup** (#767). A raising `Class_Terminate` no longer replaces an
  engine-limit failure with a VB6 error that `On Error` can trap; termination hooks are skipped during its unwind.

- **Quiet startup bindings** (#864): Lock Controls accepts no focused form as unchecked, and the dock theme
  accepts a layout that has not arrived without logging five null-path errors.

- **Options names its Tab width, grid Width and grid Height fields for accessibility** (#950). Each name follows
  its localized label without the access-key marker, so automation can address the field by that label.

- **IDE logs survive frequent relaunches** (#1307). Pruning deletes a session only when every part was last
  modified more than seven days ago, and always keeps at least the seven most recent sessions. Logs remain
  shared across profiles; a recently written part protects its whole session.

- **AI Chat Apply preserves trailing whitespace and final line breaks** (#1068). Only the single line break
  before the closing code fence is removed; applying a whole file no longer strips its final blank lines.

- **Automation replies identify documents by project** (#886). Paused locations, locals and watches identify
  the running project; designer undo, redo and format replies name their document. Tab and editor listings
  include project and document, and tabs can be activated or closed by document name and project, with a
  code/designer choice when both are open. Ambiguous tab titles are refused.

- **Make with VB6 maps errors around continued attributes to the right line** (#968). Attribute continuations
  are hidden with their logical line; an `Attribute` line continuing code or a comment is counted.

- **WithEvents applies to each variable in a comma list** (#1021). Both parsers accept it after a comma, and
  only variables marked WithEvents bind event handlers, as measured on vb6.exe.

- **System default follows the OS display language** (#799). The UI culture is captured before the IDE
  pins its threads to invariant, so German and French systems get their language and British English
  systems get Colour Palette and Centre in Form instead of US English.

- **Untouched modules reload silently after an external file change** (#730). Not-open `.bas` and `.cls`
  files are compared in their source encoding, including a UTF-8 BOM; UserControls and PropertyPages
  use the same render comparison as forms, so their designer blocks no longer cause false conflicts.

- **Option lines no longer empty the language server’s procedure tree on a syntax error** (#939). A broken
  first procedure now retains its symbol and the same diagnostics as it does without the Option lines.

- **Debugger and language documentation corrected** (#478). Standard and class module breakpoints already
  fire in a form's run; the spec now says so, with a regression test. The `VB_PredeclaredId` rows distinguish
  an inert header from a missing default class instance, remaining VSTest filters use MTP syntax, and the
  closed MCP gap no longer calls a form's attribute header invisible in HexIDE.

- **Snapshots leave room for menus above or left of the window** (#648). The window and its popups now share
  the same image origin. `take_snapshot` reports the window offset and explains how to map inspected bounds
  into the full-size or inline image.

- **A broken assignment no longer hides later syntax errors and procedures** (#938). The bundled language
  server recovers at the next statement separator and reports each offending token once. Recovery from
  extraneous tokens and errors between procedures remains partial.

- **Bundled-server completion labels are unique and carry declaration kinds** (#940). `Date` appears once as a function; declared names take precedence over keywords and built-ins, and procedures, properties, constants, enums, enum members and types no longer appear as variables.

- **Corpus and grammar reports stay with their test run** (#1018). All four reports are written beside
  the test assembly, so separate checkouts and build output directories no longer overwrite each other's
  reports or contend for a shared temp file.

- **Corpus lanes fail when repository roots are missing** (#1016). The round-trip, grammar, header and
  region-parity checks now require `demo/` and `corpus/designer`, so losing either cannot silently reduce coverage.
  An explicit round-trip corpus still replaces the default demo root.

- **A For loop honours changes its body makes to the counter** (#841). Next reads the variable back before adding
  the step: incrementing a Long or Variant counter inside `For i = 1 To 10` now gives five passes and leaves 11.

- **Editing bundled-server tests no longer makes the server look stale** (#1017). The IDE test harness
  checks C# and grammar files in the server project only, so test edits keep the parity checks running
  and grammar edits require a rebuild.

- **The pull-diagnostics race test always exercises the race** (#812). The stub holds the fresh answer until
  the stale publication starts, so a run cannot pass merely because the fresh answer arrived first.

- **Unavailable tests report a skip** (#693). Windows-only path and file-lock checks, empty serialization
  corpus selections, and the opt-in VB6 build oracle no longer report an unearned pass. The documented
  oracle commands now use the MTP method filter.

- **The design-record guard recognises renamed requirements** (#1050). It reads the CLI’s `FROM` and `TO`
  list items, so an archived rename retires the old name and requires the new name in the live spec.

- **Foreground-window tests close their windows** (#1319). Dialogs close before their owners, so later
  headless tests no longer inherit these tests' modal dialogs.

- **Repaired three corrupted comment banners in `PropertyTextTests.cs`** (#1312). The tree-hygiene guard now
  rejects C1 control characters in text files, with a planted selftest case to catch encoding accidents.

- **The Gregorian locale test follows the platform's calendar data** (#1045). It no longer assumes ar-SA's
  first month differs from January, which failed under WSL's ICU 78 even though the program's output was correct.

- **Forced-exit cleanup order is regression-tested** (#563). A server startup failure disposes the add-in loader
  before closing consent dialogs, exits even if cleanup throws, and closes the log after the windows close.

- **A server cancelling an outdated language request no longer warns that the feature is broken** (#723).
  ContentModified and RequestCancelled replies are logged at debug, leaving the method's warning available
  for a later genuine failure.

- **Project saves and startup-project changes immediately move language servers to the new workspace root**
  (#1051), reopening every held document with its latest text even when no editor opens a new file.

- **Document changes keep rising versions** (#720). A debounced change overtaken by a flush is dropped,
  so it cannot reach the language server afterwards or replace the newer text and version replayed on reconnect.

- **An index on a class's Variant field runs** (#1435). `k.V(1)` reads the array element through a variable declared as
  the class, with error 9 past its bounds; through `As Object` the read raises 451. An in-bounds assignment leaves the
  field unchanged without error, as measured on VB6. These operations were refused as not implemented.

- **A bare `Reset` calls the module's own Sub named `Reset`** (#851), including a private sibling called from
  `Class_Initialize`, as measured on vb6.exe. It previously stopped with "Reset not implemented".

- **Library-qualified calls to missing intrinsics stop the run** (#867). `VBA.CallByName` and unknown names such as `VBA.Frobnicate`, including explicit `Call` statements, can no longer be swallowed by `On Error Resume Next` as error 461.

- **Len returns a typed scalar variable’s byte size** (#780), including Integer, Long, Single, Double, Currency, Date
  and Boolean. Strings and Variants keep their text lengths. Numeric literals and arithmetic expressions are refused
  with VB6’s measured “Variable required” compile error when executed.

- **Interpreter errors no longer print `Right(Control)` and other internal Either wrappers** (#935). Missing-member errors omit the receiver type; other type diagnostics retain the inner name and array suffix.

- **A raised For Each collection under Resume Next uses more declarations** (#969). Parenthesised calls, array
  elements, direct class methods and Property Gets, and module-qualified Functions now run the body once and raise
  92 at Next, preserving the element only for a declared array. Unavailable declarations still stop with an explanation.

- **`Val("1E+400")` raises Overflow (6)** instead of returning infinity, as measured on vb6.exe (#782).

- **An omitted `Optional o As Variant` is Missing** (#866). `IsMissing`, `TypeName` and `VarType` now
  report True, Error and 10, as measured on vb6.exe, so an `IsMissing` defaulting guard runs. Explicit
  Empty and a declared default keep their values.

- **`TypeName(Debug)` raises “Syntax error”** (#872), as measured with VB6, instead of returning the runtime proxy's class name. The error stops the executing statement even under `On Error Resume Next`.

- **Join and Filter reject allocated arrays other than one-dimensional String or Variant arrays** (#875): Join raises
  5 and Filter 13, as measured in VB6. Long arrays gave values, and 2-D String arrays raised 9. Unallocated arrays
  still give an empty string or an unallocated String array.

- **Arithmetic uses VB6's per-operator result tables** (#230). Long with Single computes as Double;
  Currency multiplied by Single or Double gives Double, while addition and subtraction convert to Currency
  first. Decimal division stays Decimal, and Byte integer division and Mod stay Byte.

- **String function error precedence** (#959): Left, Mid, Replace, Split and InStr check the measured typed value argument before reading a class object, so its error 13 (or Left's 94 for a Null length) wins over the object's 438 or default-member refusal.

- **MCP sets a non-editable dropdown by item text** (#1672). `interact set_value` uses the same case-insensitive selection as `select`, closes the dropdown and reports the selection. A miss names the available items, with a count when the list is capped. `inspect_element` reports `isReadOnly` for value providers, and other read-only values are refused with an explanation rather than a provider exception.

- **Yearless dates followed by a time** (#1445) now use the current year through `CDate`, a store into a Date and `IsDate`. Measured under en-GB with comma, slash and hyphen separators, seconds and PM; a full stop remains refused.

- **Whole-file replacement refusals report every protected region changed** (#1054). When the header and a
  member's Attribute lines differ, `set_file_content` and an add-in's `TrySetContent` now name both in one reply.

- **An empty one-line For Each over Nothing follows p-code** (#1486). Under On Error Resume Next it raises 424, keeps the loop variable and skips Next; a statement after Next on the line still runs. Other loop forms keep their measured behaviour.

- **Compiler test sockets are disposed when peer creation fails** (#1333). Loopback test helpers now own
  resources until creation succeeds, and each socket-using test checks for open handles at teardown so a leak
  fails where it occurs. Production transport is unchanged.

- **For Each locks its array until the loop ends** (#1460). ReDim, ReDim Preserve and Erase inside the loop now raise 10 and leave the array unchanged. Every way out releases the lock, so a later ReDim can succeed.

- **An Enum member named like its Enum reads qualified** (#1637): `E.E` now works in modules and classes, including across modules. Bare `E` as a value raises VB6's "Expected variable or procedure, not enum type" at use; assigning `E.E` raises "Assignment to constant not permitted".

- **Numeric Strings accept a thousands separator after the decimal point** (#1446). `CDbl("1.5,2003")`
  now gives 1.52003 under en-GB, as measured on vb6.exe SP6, native and p-code. Long, Currency,
  IsNumeric and arithmetic read these Strings too; a leading separator is refused and Val still stops at it.

- **Completion descriptions are readable through MCP, and double-click commits an item** (#1334).
  The dump reaches the description popup under the completion list; `double_click` uses the list's own
  insertion path. Snapshot composition now traverses the same description popup; its desktop rendering
  still needs a live check.

- **Automation saves explain rewritten member Attribute qualifiers** (#1043). `set_file_content`,
  `set_control_property` and `add_control` now name the old and new qualifiers in their successful reply,
  explaining why a save can write text different from what the caller supplied.

- **Named automation controls expose their visible labels** (#948). Dumps and inspection keep stable x:Name paths and add a distinct `label`; discovery searches and interaction replies use that text too.

- **A failed Let from a temporary object or App empties a Variant** (#1485, #878), as measured on vb6.exe.
  `New Collection`, an object returned by a Function, and a class with no default member used to leave the old value
  after the error. A program variable still leaves it alone, and a String destination keeps its value.

- **Procedure Consts and array bounds reject later local Consts** (#894), with VB6's measured error for
  Option Explicit on or off. Under Option Explicit, a later local Const shadowing a module Const already read by an earlier Const is
  "Duplicate declaration in current scope"; shadowing without that earlier read still works.

- **An array used as text raises Type mismatch (13)** (#879): concatenation, CStr, a store into a String, Len,
  UCase and Print # no longer use its CLR type name. A String store leaves its target unchanged. The debugger's
  own array display is preserved; a typed Long array in concatenation is refused at runtime.

- **Selection replies name the designer reference** (#951). `interact` lists the primary first when several
  items are selected and explains that Align and Make Same measure against it, including the replacement
  after removing it. Its action description explains how to choose it: select it first.

- **MCP Delete and Backspace delete the editable part of a selection reaching protected lines** (#1055).
  The key runs through the editor's own deletion path; the reply counts what was deleted and left, names
  the header or member's Attribute lines, and reports the caret and remaining selection. `type_text` also
  uses the editor's selection replacement for these spans. A wholly protected selection still refuses.

- **MCP inspection reports a window's frame state** (#1595, part 1). `inspect_element` on a Window now reports
  resizing, decorations, minimize/maximize buttons, taskbar visibility and window state from Avalonia, so a running
  form's BorderStyle mapping can be checked. Native styles are explicitly unreported. Snapshot frame capture remains open.

- **A For loop with a fractional start, limit or step runs** (#983). `For x = 0 To 1 Step 0.25` and its kind stopped
  the run whenever the counter could hold a fraction: a Variant, Single, Double, Currency or Date. Measured on vb6.exe
  first, in 80 cases: the step is added each time round in the counter's type, a Single sums in Double before it
  is rounded, as p-code does, and a Variant counter counts in the type its start, limit and step make together.

- **MCP guarded-edit refusals name the region** (#1042): the file's header or the Attribute lines describing a named member. Backward deletion explains how to remove a declaration with its Attribute lines instead of sending the caller back to the same caret offset; insertions still give a new editable offset.

- **A Double rounds to Currency from its exact binary value** (#966). Storing `1.00005`, calling
  `CCur(1.00005)` or declaring it as a Currency Const now gives 1.0001, and `CCur(0.00015)`
  gives 0.0001, as measured on vb6.exe. In `+` and `-` beside a Currency, a Double becomes a Currency the same way before the sum, so `2@ + 0.00005` is 2.0001.

- **MCP can double-click a running form** (#1389). `interact`'s `click` accepts `double` in its value, as in
  `10,5 double`, `10,5 right double` or plain `double`. It presses and releases twice, so the program's DblClick
  runs, waiting for the first click's work before the second; if the target hides or closes, it stops and says so.
  `double_click` still raises DoubleTapped and selects for the Project Explorer, but its reply now says no pointer
  press was sent and points to `click` with `double` for a running form's DblClick.

- **Module-qualified stores and array-element reads reach the module's variable** (#1006, #965).
  `Module1.G = 8` and `Module2.g(2) = 7` raised Object required, and `Module2.g(1)` read the whole array.
  Both stores now work and the read returns its element. `Set Module2.pq = New C1` holds the object,
  and `Set Module2.pq = Nothing` releases it, as measured on vb6.exe.

- **String functions honour the calling module’s `Option Compare`** (#1572). With no compare argument,
  `InStr`, `InStrRev`, `Replace`, `Split`, `Filter` and `StrComp` now use that module’s setting, including
  calls written in classes and forms. An explicit compare still wins. `InStr("ABC", "b")` in a Text module
  now answers 2, as measured on vb6.exe, instead of silently answering 0.

- **MCP paths survive growing editor text** (#1436). A collapsed layout wrapper no longer appears when its
  content begins to scroll, so the Immediate window's text-area path stays usable. Range capability keeps a
  node regardless of its current bounds. A refused path that skips a visible wrapper names it and gives the
  complete corrected path when there is one unambiguous continuation.

- **MCP project saves report unused file-dialog answers** (#1335). `invoke_menu_item` for Save Project / Save Project As and `interact` on their buttons now say when an answer armed before the call remains pending. A document save through a menu, key or click can still wait on formatting after the reply; `answer_next_file_dialog` explains that limit.

- **MCP commands bound through another view model are reachable** (#1336). `inspect_element` lists a menu item’s or button’s resolved `Command` and its binding; `invoke_command` accepts `Command`, checks `CanExecute` with the control’s `CommandParameter`, and runs it. A wrong name explains the binding and how to invoke it, including on the designer’s Delete item.

- **The code editor keeps its caret and view model in agreement** (#1608). An offset past the text now goes to its end,
  and a negative offset goes to its start; the view model reads back the position the editor took.

- **MCP keys, text and hover skip rows their list has released** (#1327). Addressing a list no longer sends input
  into a recycled row omitted from the control tree or reports the list’s path as confirmation.

- **A running form's window carries the form's own Icon, or none for (None)** (#1594). Every window carried HexIDE's
  icon, big and small, whatever the form's file said. So VB98's dialog templates, which save Icon = (None) to keep a
  dialog's caption bare, showed HexIDE's mark, and a form's own icon never appeared. A form with no Icon line keeps
  HexIDE's mark, as VB6 gives its default there. Measured on vb6.exe: the default is VB6's big icon only, (None) is no
  icon at all, and a form's own is both its big and small icon.

- **MCP shared context menus belong to the control that opened them** (#1323). A tab menu appears only under its current tab in the dump; expanding it on another tab moves it there, and collapsing it from another tab leaves it alone. Menu headers also read back opens and closes, refusing an action a handler cancelled.

- **A name declared twice is vb6.exe's compile error** (#1622, #1499). A module that declared one name twice ran,
  keeping one declaration, and so did a procedure, so `Dim n As Long` then `Static n` gave an ordinary Long. At module
  level it is now "Ambiguous name detected", and in a procedure "Duplicate declaration in current scope", parameters
  included, as the module loads. A variable beside an Enum or Type of its name, and a Property's Get beside its Let,
  still compile, as they do in vb6.exe.

- **MCP `get_form_controls` reports absent properties as null** (#1635). A Line or Shape no longer claims to be enabled when its class has no Enabled property. The summary's `visible` and `enabled` now follow `caption` and `text`: null where the control's class has no such property.

- **A double-click on a UserControl's or PropertyPage's background writes its own event** (#1632). It wrote
  `Form_Load`, an event neither root has. Now it writes `UserControl_Initialize` or `PropertyPage_SelectionChanged`,
  as VB6's designer does. The code window offers each root VB6's own events, 35 and 24, with the arguments VB6's stubs
  declare, where it offered a form's five. Measured on the release build and on SP6. HexIDE does not run a UserControl
  yet, so none of these events is raised.

- **MCP replies distinguish dialogs sharing a title** (#1365). The modal note names a message box or run-time error dialog, its first line of text and its actual buttons; other dialogs include their buttons. The window parameter explains how to discover open windows through an unknown-name refusal.

- **MCP replies name ignored arguments** (#1315). A misspelled optional parameter still lets the call run, but
  now appears in `ignoredArguments` in a JSON-object reply, or in a separate text block, with a suggested spelling
  for case, snake_case or kebab-case mismatches. Binding refusals check each parameter's actual type, so an
  overflowing `int`, numeric string or array item is named without blaming a valid `long`; serializer paths stay.

- **A Simple Combo shows its list under its edit box** (#1603). It was drawn and run as a Dropdown Combo, with a button
  that dropped its list. Its list is now always shown, in the designer and at run time, and scrolls when its items
  don't fit. A click on it picks an item as vb6.exe's does: the one already selected again, the last one from below
  the items, and a double click as Click and then DblClick, with no Change or DropDown.

- **MCP `interact set_property` reads back the value it set** (#1326), naming both sent and actual values when
  they differ. For an editor's `CaretOffset`, it reads the editor itself, so an offset the caret never took is no
  longer reported as set.

- **A numeric literal ending in its point loads, as vb6.exe compiles it** (#1615). `x = 5.` and `5.D1` failed to
  load, and the editor marked them. Both grammars now read them, as the literal with a digit after the point. A
  literal with a point and `%` or `&`, such as `2.5%`, is now vb6.exe's compile error "Syntax error", where HexIDE
  rounded it to an Integer or a Long.

- **The designer draws a Dropdown List's box with the control's name** (#1604). It drew the box empty, as the running
  control is. VB6's designer shows the name the control has now, after a rename too, and not a Text the file saved.
  A Dropdown List renamed and then set back to Style 0 writes its new name as its Text, as VB6 SP6's designer did.

- **MCP `hover` cleans up after a throwing pointer handler, reports a cover's tip over a disabled target, and refuses
  targets that are not showing** (#1328). Containers prefer a showing editor; the tool explains the real mouse's effect
  on the tip, cursor and pointer-over.

- **The arrow keys move between controls from an option button, a check box and a command button** (#1625). They did
  nothing there. Up and Left now go to the previous control in the same container and Down and Right to the next, by
  TabIndex and wrapping, as VB6's do, and an option button they land on is selected. As in VB6, a control whose
  TabStop is False is reached, and one that can't take the focus, such as a Label or a Frame, is passed over.

- **A Const declared with a type character runs, as vb6.exe's does** (#1614). `Const K% = 2.5` stopped the program as
  the module loaded, with "Const type hints not supported". The character is now the As clause it stands for: `K` is
  the Integer 2, `Const K$ = 5` the String "5", and `Const K% = 40000` is vb6.exe's compile error "Overflow".

- **MCP reports keyboard focus and presses where it is** (#1627). `inspect_element` reports `isFocused`, and `dump_visual_tree` names the focused path, or explains when no control in that window has focus. `press_key` at a container keeps the focus inside it, so Tab advances from the current control. An explicit target still takes focus first; the reply names where that move came from, since it raises a running program's focus events.

- **A `D` exponent in a numeric literal loads, as vb6.exe compiles it** (#1611). `x = 1D3` failed to load, and the
  editor marked it. `D` is now an exponent marker wherever `E` is, in both grammars. A literal vb6.exe refuses to
  build, such as `1E400`, `1E3%` or `1E20@`, is now its compile error "Syntax error", where HexIDE gave infinity, a
  Long or a Currency beyond Currency's range, or stopped with a .NET error.

- **A refused MCP call names a blocking dialog once** (#1325). Closing a form under a MsgBox, or typing or pressing a key under stacked dialogs, no longer repeats the dialog in the reply’s note. A refusal never claims the call reached past a dialog; calls that acted behind one still say so.

- **The Properties window shows Left, Top, Width and Height in VB6's units** (#1636). It showed the pixels HexIDE
  keeps them in, so a new form read 312 by 206 where VB6's reads 4920 by 3675. A control's position is now in its
  container's ScaleMode, twips by default and twips inside a Frame, and a form's own size is twips with its frame, as
  VB6's window shows them. A value typed in is in the same units. `get_form_controls`' rows and `set_control_property`
  follow the window, each row naming its unit; the summary and `move_control` stay in pixels.

- **A CommandButton, TextBox, ComboBox, ListBox and Frame have ToolTipText** (#1403). Only five controls declared it,
  so on these the Properties window did not list it, a running form showed no tip, and code could not read or set
  it. Measured on vb6.exe across every intrinsic control: these five have it, and the scroll bars, Shape, Line, Timer
  and menus do not. On all ten that have it, ToolTipText now reads and takes values at run time as vb6.exe's does.

- **A ComboBox's Style is modelled, and a Dropdown List runs as one** (#1428). Style was kept as a preserved line, so
  the Properties window could not show or set it, and a program reading or writing a Dropdown List's Text was stopped
  as not implemented. Now Style is offered with VB6's three names and reads as an Integer, and a Dropdown List's Text
  selects the item it names, or raises 383 as vb6.exe does. Setting Style to Dropdown List in the designer resets
  the Text to the control's name, as VB6's designer does.

- **MCP `type_text` refuses hidden targets before finding an editor** (#1317), in both modes, so a hidden dropdown is no longer told to expand itself. Its target description explains ambiguous surfaces and non-editable dropdowns; `select` now also refuses realized rows whose visibility cannot be read.

- **MCP command guards follow the compiled XAML binding** (#1324). `invoke_command` now finds private guards
  on base types and ignores guards below the bound method's type, so its refusal agrees with the menu.

- **A UserControl's and a PropertyPage's Properties windows list their own kind's properties** (#1585). Both listed
  a form's, Caption, Icon, Left, Top, Visible and the window's buttons among them. Each now lists only properties
  VB6's Properties window gives it: a UserControl has no Caption, and a PropertyPage has no Enabled. A line a
  loaded file holds that its root doesn't list is kept as the file's own text.

- **A Let into an array variable takes only what vb6.exe's does** (#998, #1588). It stored whatever it was given:
  `a = Array(1, 2)` left an `Integer()` a Variant array, Empty left a `String()` Empty, and `b = "ab"` left a `Byte()`
  a String. Now an array of exactly the element type is copied, a String into a `Byte()` becomes its bytes, and anything
  else is error 13 and leaves the array as it was, or VB6's compile error "Can't assign to array" where the statement
  shows the type. A Function declared to return a `Byte()` converts a String too, where it stopped the program.

- **MCP paths for indented list items resolve** (#1357). `dump_visual_tree` trims names in paths while keeping
  the reported name as drawn. Names that become duplicates after trimming use positional paths, as do blank names.

- **Ending an external debug session discards pending evaluations immediately** (#1599). A late adapter reply could
  reach the Immediate window while End was waiting for disconnect. The stopped context now expires before disconnect
  is sent, so an answer arriving during teardown cannot be shown as a current evaluation.

- **Numeric source literals keep their value on every locale** (#781). A made EXE on German regional settings read `0.6 * 10` as 60 and `1.5!` as 15; French settings could stop the program with a parse error. Literal parsing now uses the invariant culture, including suffixes, exponents and conditional compilation. Runtime string conversions retain their locale rules.

- **The Tab key stops once in each option group** (#1583). It stopped at every option button, where VB6 stops
  only at the selected one, or, in a group with none selected, at the first in the key's direction. That option is
  selected there, with Click after GotFocus, as under `SetFocus`. An option is now selected before its GotFocus
  runs, which reads it selected, as vb6.exe's does.

- **Persian words in MCP replies remain readable** (#1316). A zero-width non-joiner between Arabic-script
  letters (including their combining marks) is written as itself. Joiners elsewhere remain escaped.

- **A helper's `On Error Resume Next` no longer sees its caller's stale error** (#877). `On Error`, `Resume` and leaving a procedure from its handler now clear `Err`, as vb6.exe does, so `If Err.Number <> 0 Then` after them reads only what has happened since. While a handler runs, an error in it now goes to the caller even after `On Error Resume Next`, where HexIDE used to trap it and run on. `On Error GoTo -1` runs, and `Err.Raise` with no Description keeps the one already set, as vb6.exe's does.

- **MCP window wording matches how `auto` picks** (#1321). `interact` now says it usually picks a running form, and the unknown-window refusal describes its preference for another visible window, a dialog first.

- **A running form has the window its file describes** (#1575). Every form ran as a sizable window with a title bar,
  minimize and maximize buttons and a close box, whatever its BorderStyle, ControlBox, MinButton and MaxButton said. A
  Fixed Single or Fixed Dialog form can no longer be resized by its border, a borderless form has no frame, a form
  with ControlBox False has no close box, and the tool windows have a tool window's caption. On Windows the window is
  given the styles vb6.exe gives it, measured for 20 combinations.

- **MCP refuses arguments containing an unpaired UTF-16 surrogate** (#1337). The reply now names the tool and
  parameter, says the argument is not valid text and that nothing was done. It previously returned only the SDK's
  `-32603 "An error occurred."` before any tool ran.

- **MCP `press_key` reaches access keys and menus** (#1523). Keys now pass through the window input path,
  so Alt+W activates a button captioned `One &Wide`. The reply names `window-input`, or `routed-key`
  when focus or platform input is unavailable and the previous routed delivery is used.

- **A carried control's box is drawn in its place among the designer's controls** (#1553). The designer drew the box
  for every control it carries as text over every control it models, so a control brought in front of one was saved in
  front of it (#1498) but still drawn under its box. The boxes now stack with the controls, as VB6 stacks the control:
  over its Frame or PictureBox, and over or under each control as the file and Bring to Front or Send to Back put it.

- **`Like` runs in an `#If` or a `#Const`, and a directive orders text as vb6.exe does** (#1598). `Like` there was
  still refused as not implemented after `Like` itself ran. It now matches as text in every module, as vb6.exe's does,
  and a pattern it cannot read is vb6.exe's compile error. A directive ordered two strings case-insensitively by code
  unit, so `#If "_" < "a"` took the other branch from vb6.exe's; it now orders them by vb6.exe's measured text order.

- **The designer draws and names a UserControl or PropertyPage as VB6's does** (#1544). Each was drawn in a form's
  window frame, with a title bar and caption buttons, a PropertyPage's tab read `(Form)`, and the Properties window's
  object box gave both the class `Form`. Each is now a borderless surface, as neither has a window of its own at run
  time, its tab reads `(UserControl)` or `(PropertyPage)`, and the object box names its class.

- **Every focusable control raises GotFocus and LostFocus, and a form or UserControl saved disabled runs disabled**
  (#1101). Only the TextBox and the CommandButton raised them. The CheckBox, OptionButton, ListBox, ComboBox, scroll
  bars and PictureBox now do, as vb6.exe does: one GotFocus as a control is entered, however its items or edit box take
  the focus. The ListBox, scroll bars and PictureBox take the focus, where `SetFocus` on one was refused. An option
  button given the focus by `SetFocus` is selected, raising Click after GotFocus. A form or a UserControl's `.ctl`
  saved with `Enabled = 0 'False` dropped that line and ran enabled. A disabled form is shown without taking the focus.

- **MCP reply numbers use a decimal point in every culture** (#1550). Coordinates, sizes, scroll percentages and
  wait durations now use invariant formatting, so a fractional click point can be read and sent back unchanged.

- **A form's Width and Height are its outer size at run time** (#1072). They read and set the client area, so a
  program got a form 90 to 240 twips narrower and 345 to 885 shorter than vb6.exe reports. They now include the frame
  VB6 draws on the themed Windows desktop for the form's BorderStyle, caption bar and menu bar, read back as assigned,
  and leave the client area that is left inside the frame. `Width = (Width - ScaleWidth) + needed` lands as before.

- **Surplus call arguments are refused** (#741): procedures and intrinsics with known limits raise VB6's
  "Wrong number of arguments or invalid property assignment"; a declared scalar called with arguments raises
  "Expected array". Parameterless Variant and object results still index as before (#1476).

- **A running form raises Activate and Deactivate, and Resize once as it is shown** (#994). Activate and Deactivate
  were never raised. They now come as VB6 raises them: when another of the program's forms is shown, given the focus
  or left active by a Hide or an Unload, after the statement that did it, and not when the user switches to another
  application and back. A form raised Resize two or three times as it was shown, as the window settled. It now
  raises one, inside the Show, and after that one for each change of size or state. A form shown again after Hide
  raises none.

- **Cancelling a language-server request now reaches the server** (#944). The cancellation notification
  failed to serialize on every keystroke that abandoned a completion request. It now sends the original
  request’s bare ID, so a server can stop that work; the client still awaits the server’s response.

- **A disabled menu item says it is disabled in automation** (#1534). `invoke_menu_item` used to blame keyboard
  focus when a disabled item had a routed command. It now reads the materialised item's enabled state first;
  when the menu has not been read, the refusal says its disabled state is unknown and how to check it.

- **An automation click outside its target says so** (#1530). When the point lands on another control,
  `interact` now gives the target's size and names what took the press, rather than saying it covers the target.

- **Ctrl+S on a new UserControl or PropertyPage saves its .ctl or .pag** (#474). From its designer or its code window,
  Ctrl+S saved the designer half as a form: it asked for a `.frm`, wrote the edit there, and left the `.ctl` the project
  names unwritten. A save from either half is now the module's save. It asks for the `.ctl` or `.pag` once, and writes
  it in place after that.

- **A UserControl or PropertyPage is named by its `Begin` line, as VB6 names it** (#1411). HexIDE named it after its
  file, so `Gauge.ctl` holding `Begin VB.UserControl ctlDial` was "Gauge". A form using the control as VB6 does, as
  `Begin probe.ctlDial`, then found no such control, and kept it as text it could neither draw nor run. Where its
  `Begin` line and `VB_Name` disagree, the project now opens with VB6's warning and makes `VB_Name` agree, as #1407
  does for a form.

- **Bare Print no longer prevents a form from loading** (#993). The interpreter and editor accept its output list.
  Reaching it still stops the run at the form's unimplemented Print method (#989); standard and class modules refuse
  it at runtime, naming #993, pending a VB6 measurement.

- **Indexing a call's result runs** (#1476). `Split(s)(0)`, `Array(1, 2)(1)`, `F()(1)` and `a(1)(0) = 9` stopped the
  program. They now run as vb6.exe runs them, evaluating in its order. A parameterless Function that returns a Variant
  now reads its arguments as an index into its result, so `F(1)` is `F()(1)`; it used to drop them and return the whole
  array. A method's result indexed after its arguments, `k.Echo("m n")(1)`, used to call it with no arguments at all.

- **A form's `Controls` collection runs, and `TypeOf` knows a control's class** (#1060). `For Each c In Controls`,
  `Controls.Count`, `Controls(i)` and `Controls("Name")` stopped the run as not implemented. They now hold every control
  in the order the file holds them, menus and each control-array element included, as vb6.exe does, with VB6's
  errors for an index out of range or a name not there. `TypeOf c Is TextBox` was False for every control, so the
  usual loop over a form's controls would have done nothing, silently. It now matches a control by its class, as a
  `Control` and as an `Object`, and a form as a `Form`. `Unload c` unloads a control-array element held in a
  variable. `Controls.Add` and `Remove` are still refused (#1556).

- **A new form, UserControl or PropertyPage is the file VB6 makes, byte for byte** (#1538). A new form was 6000 × 4500
  inside and had no `ClientLeft`, `ClientTop` or `LinkTopic`. A new UserControl had a `Caption`, which a UserControl
  doesn't have, and `Attribute VB_Name` alone. Each now carries what VB6's Add Form, Add User
  Control and Add Property Page write (measured in the IDE): a form 4560 × 3015 at 120, 465 with its `LinkTopic`, and a
  UserControl or PropertyPage 4800 × 3600 at 0, 0 with its own five `Attribute` lines.

- **A new form has no code, and an event procedure is added as VB6 adds one** (#1532). A form HexIDE created started
  with an empty `Form_Load` procedure, and its first save wrote it to the file. VB6's Add Form saves a form ending at its
  `Attribute` lines (measured in the IDE), and adds `Form_Load` when the form is double-clicked. HexIDE's double-click
  wrote that procedure `Public`, in bare LF, after two bare line feeds. Now a double-click, or an event picked in the
  Procedure box, writes it `Private`, in the file's line breaks, at the end and one blank line below any code, with the
  caret on the blank line inside.

- **Object and Collection arrays** (#1482). Elements start as Nothing, accept Set with the same type checks as
  scalar variables, and release their objects on Erase. Class arrays now report TypeName "Object()", too.

- **View Code opens with the caret in the code** (#1518). It opened at the top of the file, on the folded header that
  only the IDE may change, so the first keystroke was refused and nothing appeared. Since #1369 that was every new
  form's first View Code too. Now a code window opened with nowhere asked for puts its caret on the first line below
  the header, as VB6's View Code does. Navigating to a procedure or a breakpoint still puts it there.

- **`Write #` and `Input #` run** (#1521). Both stopped the program as not implemented. `Write #` now writes
  as vb6.exe does: quoted strings, `.5` with no leading zero, `#TRUE#`, `#NULL#` and `#2003-01-02#`, with a point under
  any locale. `Input #` reads each field for the variable it fills, so a Variant comes back typed by what the field holds,
  as an Integer, Currency, Decimal, Double, Boolean or Date. A typed variable takes vb6.exe's rules: 0 for text in a
  number, and 6 or 13 leave the variable as it was. A program that saves its settings with one and loads them with the
  other now runs.

- **`CCur` and `CDec` preserve decimal precision in Strings** (#1531). Plain decimal numbers are read in the
  program's locale without rounding through a Double, including the Currency and Decimal maxima. Out-of-range
  Strings raise run-time error 6. Hex, octal and exponent forms keep their existing conversion.

- **StrComp, StrConv, Environ, Switch and Error() run** (#1560). Each stopped the program that called it. All five were measured on vb6.exe first: StrConv's case goes through the ANSI code page as VB6's does, Switch reads its conditions only until one holds, and Error(n) answers from vb6.exe's text for every number from 0 to 65535. Err.Raise with no Description now takes that same text, so `Err.Raise 340` is "Application-defined or object-defined error", as in VB6, and 21 messages that had other words have vb6.exe's. UCase and LCase now keep the case of µ, ı and four others, as vb6.exe does, where they took .NET's.

- **`Option Compare Text` compares as vb6.exe does** (#1567). A module saying it still compared strings as Binary in `=`, `<`, `>` and `Select Case`, so `"abc" = "ABC"` was False. Now those compare by vb6.exe's own text order, measured character by character: case and accents, the hyphen and apostrophe that a Windows sort sets aside, and ß, æ, œ and þ as their two letters. The same compare now serves a Collection's keys, and a compare of 1 in InStr, InStrRev, Replace, Split and Filter, which had read it as "ignore case" and kept "straße" and "strasse" apart (#1479).

- **A duplicate Collection key's error 457 has vb6.exe's words** (#1567): "This key is already associated with an element of this collection". The oracle had recorded them since the Collection was measured, and `Err.Description` still gave other words.

- **`InStr` and `InStrRev` agree with vb6.exe at the ends** (#1567). `InStr` with an empty string to find returned 0 once the start was past the end, where VB6 returns the start, and `InStrRev` found text before a start past the end, where VB6 returns 0. `InStrRev("abc", "")` was 4, not 3.

- **An intrinsic called with too few arguments raises VB6's compile error** (#1512). Calls such as `Chr()`
  now report "Argument not optional" or "Syntax error", as measured in VB6, instead of stopping with a .NET exception.

- **A control added, brought to front or sent to back stacks past the controls HexIDE carries as text, as VB6's does**
  (#1498). A control HexIDE does not model, such as an OCX's, was written back where it was read, whatever the designer
  did around it. So a button added to Web Browser.frm went in after its toolbar, and the form's ImageList was written
  ahead of a PictureBox nobody had moved. VB6 stacks an OCX control like any other (measured): a new control goes in
  front of the toolbar, and Send to Back puts it behind the ImageList. A form loaded and saved untouched is unchanged.

- **`Print #` writes a character the ANSI code page lacks as its best fit** (#1511). On code page 1252, Ā is written as
  "A", Ł as "L" and α as "a", as vb6.exe writes them, and Ж, which has no fit, as "?". Every one of them was "?".

- **`CVar` runs** (#1466). It stopped the program as undefined. Now it returns its argument as a Variant with the
  argument's own type, an object as the object, as vb6.exe's does. The result reads as a Variant's, so `CVar(5) * 7000` is
  a Long 35000 where `5 * 7000` overflows.

- **OK on the References dialog no longer drops the project's OLE Automation reference** (#1026). Its first row
  carried OLE Automation's GUID, so the project's own reference was hidden, and OK, even with nothing changed, took it
  out. The next save wrote the project without it, and VB6 then failed to build one that uses `StdFont` or
  `OLE_COLOR`. The dialog now lists what VB6's does, as measured: three fixed rows (Visual Basic For Applications and
  VB's two libraries), then every reference of the project in its order, OLE Automation among them. Nothing moves
  above the fixed rows, and it opens on the last checked row.

- **A form whose `Begin` line and `Attribute VB_Name` disagree opens as VB6 opens it** (#1407). HexIDE already named
  such a form by its `Begin` line, as VB6 does, but said nothing and saved the contradiction back. HexIDE itself wrote
  such files before #1059. Opening the project now shows VB6's "Conflicting names were found…" warning. The form's
  `VB_Name` is made to agree, and the form comes up edited, so its next save writes it, as VB6's does. Case alone
  counts as a disagreement, as it does in VB6.

- **The form designer draws a form at the size it runs** (#924). The designer drew the whole window, frame and
  caption bar included, at the form's client size, so the area controls were designed in was 8 pixels narrower and
  about 25 shorter than the running form. A layout drawn flush to the edges ran with a margin, and resizing the form
  saved a size nobody saw. The frame is now drawn around the client, and a resize saves the client it leaves.

- **Six VB6 system colours no longer draw hot pink** (#908). A control given Scroll Bars, Active Title Bar, Active
  Border, Inactive Border, ToolTip Text or ToolTip (`vbInfoBackground`) painted DeepPink, in the designer and at run
  time, because no colour was defined for them. They now take Windows 10's values, as measured on vb6.exe, and a
  system colour that still has none is named in the log.

- **A caption's `&&` and `_` are drawn as VB6 draws them** (#920). `Tom && Jerry` drew `Tom _ Jerry`, and
  `File_Name` drew `FileName` with Alt+N as its key, on buttons, check boxes, option buttons, labels and menus, in the
  designer and at run time. A Frame drew `Fra && Me` with both ampersands. Now `&&` is one `&`, an underscore is a
  letter, a trailing `&` is not drawn, and a caption marking two letters takes the one VB6 does, all as measured on
  vb6.exe. A caption with an underscore before its access key now has no key, because Avalonia would take the
  underscore as the key instead.

- **A control array's elements name themselves with their index in automation** (#1320). The Properties window's
  object selector and the designer's canvas named every element of `optSize` as `optSize`, so a caller could not tell
  which was selected, and `select "optSize(1)"` matched nothing. Each element is now `optSize(0)`, `optSize(1)` and so
  on, the spelling `set_control_property` and `move_control` take.

- **A control-array element renamed out of its array can be renamed back** (#1276). Renaming `optSize(0)` to
  `optBoard` worked, but every way back was refused as a duplicate name, so the array stayed split. A name is now
  taken by name and Index together, as in VB6: an element may take an array's name where its Index is free there.

- **The Project Explorer's Add Form adds a form as Project > Add Form does** (#468). It added a form with no file and
  opened nothing, a second Add Form of its own, and closing the IDE then left that form out of the project file
  (#1359). It now writes the form's file and opens its designer.

- **A new form is written as VB6 writes one** (#931, #932, #516). Its code section was bare LF with no final line
  break, it had none of the five `Attribute` lines VB6 writes for a form, and it had no `StartUpPosition` line, which
  VB6 reads as Manual: the form opened at the screen's origin with its title bar off the top. A form HexIDE creates now
  has all three, and says `StartUpPosition = 3 'Windows Default`. A form with no such line, as VB6 writes a Manual
  one, now runs where its file puts it rather than centred, which is how vb6.exe reads it.

- **A running form stacks overlapping controls as VB6 does** (#1497). The first block in a container is drawn on top,
  and a Label, Shape, Line or Image under every other kind of control, whatever the order. Among those, the first is on
  top, so a Line no longer goes under every Label. A control-array element loaded at run time goes under the controls
  already there. HexIDE drew the last block on top, so a form's overlapping controls came out upside down. The
  designer stacks Lines the same way, so a Line in a Frame is drawn over the Frame rather than under it.
- **The designer draws and saves controls in VB6's stacking order** (#1481). In VB6 the first `Begin` block in a
  container is the control on top, and windowless controls (Label, Shape, Line, Image) are drawn under the windowed
  ones and written after them. HexIDE drew the last block on top and wrote a new control last, so a control added here
  was the bottom one when VB6 opened the file, and Bring to Front and Send to Back each saved the opposite of what they
  showed. Now a new or pasted control is written first in its plane, Bring to Front and Send to Back move a control
  within its plane and take a container's contents with it, and a loaded form is drawn as VB6 draws it. A pasted
  container keeps its source's stacking, where VB6 turns its contents over.

- **`Debug.Assert` runs** (#1467). It stopped the program, even when its condition held. Now, in the IDE, an assertion
  that fails enters break mode at the statement, as `Stop` does, and one that holds goes on, the condition read as `If`
  reads it. A compiled program has no `Debug.Assert`, its argument not even evaluated, so the Make EXE runner skips it,
  as vb6.exe's EXE does. `get_debug_state` reports the break as `Assertion`.

- **ChrW and AscW run, and Chr and Asc go through the ANSI code page** (#1134). `ChrW` and `AscW` stopped the program
  as undefined. `Chr` and `Asc` ran, but in UTF-16 units, so `Chr(&H80)` was a control character where VB6 on a 1252
  machine gives "€", and `Asc("€")` was 8364 where VB6 gives 128. All four now follow vb6.exe, measured:
  - **ChrW:** a negative code is its unsigned unit, so `ChrW(&HF050)` is U+F050.
  - **AscW:** a signed Integer.
  - **Asc:** best-fits a character the code page lacks, so Ā is "A".
  - **Errors:** each raises 5 out of range, 94 for Null and 6 past a Long. `Asc(Null)` is 94 where it returned Null.

  A `$` form's result is now a typed String, as VB6's is, so `Select Case Mid$("9", 1)` compares text. Plain `Str` is a
  Variant, as `Mid` and `Chr` are.

- **Make Project Group is offered only with a group open, as VB6 offers it** (#746). With one project loaded it
  was offered anyway, then failed on the missing group and said nothing. It is now greyed out until a group is
  open or a second project is loaded. With a group, it used to save every project and the group under a Make
  label. VB6's builds them, which HexIDE can't do yet (#1515), so it now says the feature isn't implemented and
  writes nothing. Save Project Group still saves.
- **An undeclared variable passed ByRef takes the procedure's write** (#1505). `Bump a12`, with `a12` never declared
  and `Sub Bump(x)` adding 1 to `x`, left `a12` Empty, so the program ran on with a wrong value and nothing said why. VB6
  declares the variable where it is passed, and the write lands in it. Now HexIDE does the same for a Sub, a Function, a
  class's method and a named argument, under DefType too. A name in parentheses, or given to a ByVal parameter, is still
  a copy, as in VB6.

- **A new control is numbered from 1, as VB6 numbers it** (#540). The first button on a form was Command0, and a
  pasted control took its number from the count of controls on the form, so its name skipped a gap VB6 would have
  filled. A new or pasted control now takes the lowest number from 1 that no control on the form has. That is
  Command2 beside Command1 and Command3, Text1 beside Text5, and a deleted control's number again, as measured.
- **`DefInt A-Z` and the rest of DefType run** (#169). A DefType statement stopped the program. Now each declaration with
  no As clause takes its type from the first letter of its name, as vb6.exe does. That covers an undeclared variable, a
  `Dim` or `Static`, a field, a parameter and a Function's return, and under DefObj each is an object variable. A `Const`
  keeps its value's type. A DefType that overlaps another, follows a declaration or sits inside a procedure is VB6's own
  compile error. Two defects found on the way are fixed too:
  - a `Set` into a name never declared raised 424, where VB6 declares it, as a Let does;
  - a Function `As Object` or `As Collection` returned Empty before any Set, not Nothing, so `F = 5` in one took the
    value where VB6 raises 91.

- **A control added by double-clicking its tool takes the size VB6 gives it** (#1473). Every class came out 50 x 50
  pixels, a square CommandButton among them, and that size went into the saved file. Now a CommandButton is 1215 x 495
  twips, as are most classes, while a ComboBox, ListBox and either scroll bar each have their own size, all as
  measured. Each is centred by its own size and put on the grid, where VB6 puts it.
- **Val, Round, Split and 13 more intrinsics take named arguments** (#1475). `Val(String:="12")`,
  `Round(Number:=2.567, NumDigitsAfterDecimal:=1)` and `Split(Expression:="a b c")` stopped the program, because
  signature help had no entry for them, and so neither did the interpreter's table of names. All 16 are now measured
  against vb6.exe and listed, each with a tooltip: their names, how many are required, and what a wrong or missing name
  gives. Now, Date, Timer and the other intrinsics with no parameters refuse a name with VB6's own compile error.

- **A Let into an object variable no longer drops the object** (#1493). `k = 5`, with `k As C1` holding an instance,
  released it, running its Terminate there and then, and stored 5, so the program ran on without it. VB6 hands the
  value to the object's default member: a class with none raises 438, and `k` keeps the object; Nothing raises 91. That
  holds for `As Object`, object parameters, an object Function's return name and a class's object fields too. A
  Variant still takes the value and drops the object, as VB6's does.

- **`Static` runs, and a `Static Sub` keeps its locals** (#1491). A `Static` local stopped the program. Now it keeps its
  value from one call to the next, as vb6.exe does: shared by recursive calls, kept per instance in a class or a form,
  and released with the instance. The `Static` keyword on a procedure was read and then ignored, so a `Static Sub`'s
  locals silently started again at zero on every call. Now they keep their values too, though a Static Function's own
  return value starts again, as VB6's does.

- **`Dim o As New C1` runs** (#435). The whole module stopped loading at the first `As New`. Now a variable declared
  `As New` holds Nothing until it is used, and its first use makes the object, as vb6.exe does, for a class and for a
  Collection, at procedure and module level and as a class field. Every use counts: `If o Is Nothing` makes the object,
  so it is never True. After `Set o = Nothing`, the next use makes a new one. An array `As New` (#1482) and a Type's
  field `As New` (#1488) still stop the run, by name.

- **An empty Collection refuses an index as VB6's does** (#1492). Every index into an empty Collection is 5 in VB6: a
  position, a key, Null or Empty, for Item, Remove, and Add's Before and After. HexIDE gave what a Collection with items
  gives, 9 for a position and 13 for Null. Assigning an item, which no Collection allows, now looks the index up first,
  as VB6 does: `c(3) = x` past the end is 9, and `c("zz") = x` with no such key is 5. Only an item that is there gives
  424, and late-bound it is 451 or 438, by form, as measured.

- **A control added or pasted no longer takes the TabIndex of a control HexIDE does not model** (#925).
  Such a control, an OCX's Toolbar for one, is carried as text, and its TabIndex went uncounted, so a new control could
  be given the same number. It now counts, nested ones included.

- **VB6's Collection runs** (#1471). There was none, so a program that made one stopped at `New Collection`. Now
  `Add`, `Item`, `Remove`, `Count`, For Each and `c!key` give vb6.exe's answers and errors, each measured native and
  p-code. That includes the two that surprise people:
  - For Each walks the Collection itself, so removing the current item goes on to the next;
  - a key that isn't a String counts as a duplicate when it names an existing position, so `c.Add x, 5` is 457 in
    a Collection of five and 13 in one of four.

  Objects held in a Collection are released when the last reference goes, in VB6's order. Locals and Watch open a
  Collection to its items. Still to come: arrays of Collection (#1482), and keys
  compared as VB6's text compare does rather than only ignoring case (#1479).

- **A control added or pasted takes the TabIndex VB6 gives it** (#925). The first control got 1, a Timer or a Shape used
  a number up, a Label or a Frame was saved without one, and a pasted control kept its source's, so the file held two
  controls at one TabIndex. A control on a loaded form could also take a Label's or a Frame's number, which HexIDE
  couldn't see. Now each takes the next after the highest on the form, as VB6's designer does, measured: the first is 0,
  a Timer takes none, a Label and a Frame have theirs, and a paste numbers its container and then what it holds.

- **Center in Form, Align Centers and Make Equal spacing put a control where VB6 does** (#929). They worked in
  fractional pixels, so a control centred on an odd difference landed half a pixel off, and a save wrote it as
  `Left = 4192.5`. Each now works as VB6's does, measured in its IDE: in whole twips, halving each size on its own and
  truncating. Make Equal truncates the gap and leaves the last control where it was. A control added by
  double-clicking a tool is centred the same way, then put on the nearest grid line when Align to Grid is on.

- **Named arguments run** (#1465). `MsgBox Prompt:="Done", Title:="App"` and `Foo Count:=3` stopped the program with
  "Assign is not implemented". A named argument now goes to the parameter of its name in a call to the program's own
  procedures and methods, and to the 57 intrinsics vb6.exe takes names for, with VB6's compile errors for an unknown or
  repeated name. The arguments are evaluated in parameter order, not the order written, as vb6.exe evaluates them, so
  `Foo c:=F(), a:=G()` calls G first. A named argument to a control's, a form's or an object's method is still refused
  (#1474).

- **IIf and Choose run** (#1461, #1464). Neither existed, so a program that called either stopped there. Both now give
  vb6.exe's answers, including the two that surprise people: IIf evaluates both parts, so `IIf(n <> 0, 1 / n, 0)` still
  divides by zero, and Choose truncates its index, so `Choose(1.6, "a", "b")` is "a".

- **ReDim Preserve runs** (#1459). It was refused as not implemented, so a program that grows an array as it goes stopped
  at the first one. It now keeps the elements in range and changes only the last dimension's upper bound, and refuses
  everything else as vb6.exe does, each case measured: another bound changing is 9, too big is 7, and a Variant holding
  no array is 13, each leaving the array as it was.

- **Cutting and pasting a control keeps what HexIDE carries without modelling** (#1453). A Frame or PictureBox cut
  and pasted lost the controls inside it that HexIDE does not model, such as an ImageList, and any control lost its own
  properties HexIDE does not model. The next save left them all out, with nothing said. They now go with the control,
  with the companion records they cite. A copy takes the properties too. It still leaves the controls inside a
  container behind, because their names would repeat the originals' (#1458).

- **An array element passed ByRef is the element itself** (#1455). `Swap s(0), s(1)` swapped nothing, so a sort written
  the usual way ran to the end and left its array as it was. The element is now passed by reference, as in VB6, and
  while the call runs its array is locked: ReDim, ReDim Preserve, Erase, or a store into the array's variable raises 10.

- **A UDT field passed ByRef is the field itself** (#1451). `PL u.l`, with `PL(x As Long)` storing `x = "9"`, left `u.l`
  unchanged: the field was copied in and never written back. A procedure that fills in a record through ByRef
  parameters now fills it in, as in VB6, and a store through the parameter converts into the field's type.

- **A store into a UDT field converts into the field's type** (#221). `u.l = 5`, with `l As Long`, read back as an
  Integer, `u.dt = "1/2/2003"` stayed a String, and `u.i = 40000` with `i As Integer` stored 40000 with no error. Each
  now converts as a store into a variable of the type does, as VB6 does, and eleven corpus cases that measured it match.
- **A typed parameter converts what it is given** (#874). `Sub S(ByVal x As String)` called with `5` received an
  Integer, `B "abc"` with `ByVal x As Boolean` ran with a String where VB6 raises 13 at the call, and `NI "12"` with
  `ByVal x As Integer` received 0. A parameter, ByVal or ByRef given a literal, and an Optional default now convert as a
  store into a variable of the type does.

- **A typed Function or Property Get converts what is stored into its name** (#1444). `FS = 5`, in a Function
  `As String`, returned an Integer, `FD = "0.5"` `As Date` a String, and `FI = 40000` `As Integer` raised its Overflow in
  the caller, after the Function had returned. Each now converts at the store, as VB6 does, so a failure is trapped by
  the procedure's own handler.

- **A String stored into a Date reads as VB6 reads it** (#967, #800). `d = "1/2/2003"` was Type mismatch, and
  `CDate("36526")` was too. Now `CDate`, a store into a Date, a Date `Const` and the date functions share one reading: a
  date or time in the program's locale first, then a serial. Under en-GB, a dot between digits separates a time's parts
  (`"1.5"` is 01:05), so `"1.2.2003"` is no date, as in VB6. `IsDate` agrees with it, and `CVDate`, which was missing, is
  `CDate`. `DateValue` and `TimeValue` still take no serial, as in VB6.

- **Another module's Public Enum member reads bare** (#889). `colMax`, declared in Module2's `Public Enum Cols`, read
  as Empty in Module1 with no error, so `For i = 0 To colMax` ran once. It now reads as the Long VB6 gives it, from a
  class's Public Enum too, and sizes a module-level array or folds into a Const. Two modules' members of one name are
  "Ambiguous name detected" where used bare, as in VB6.

- **A lone CR ends a line when a program runs** (#1426). VB6 ends a line at CRLF, a lone LF or a lone CR. The
  interpreter took only the first two, so a form whose code had a lone CR, which VB6 builds and runs, was a syntax
  error to F5. A lone CR now ends a statement, a line continuation and a comment, as each was measured to.

- **A form whose OCX control keeps pictures in the `.frx` can be saved, and keeps them** (#60). A picture cited by a
  control HexIDE does not model, such as an ImageList's, or by a property it does not model was dropped with its line,
  so the form was held read-only. Now the line and the picture go back out together, and a save rewrites the citation
  to wherever the picture lands, so a picture HexIDE does model that grows moves the others without losing them. VB6's
  own `Web Browser.frm` was the last of its template forms held read-only; it now saves byte for byte, `.frx` included.
- **A control array named through its form, `Form1.Text1(0).Text`, reads and writes** (#1386). It stopped the program
  with "Method or data member not found", in code and in the Immediate window alike, because the index was dropped. A
  missing element is VB6's 340, whose message now names the index, as vb6.exe's does.
- **A `?` line in the Immediate window prints as VB6's does** (#1386). `? 5` prints ` 5 `, as Print does, and a list
  such as `? a; b` or `? a, b` prints as `Debug.Print` prints it, where it was "Syntax error". A compile error raised as
  the line is evaluated now says what it is, and only a line that doesn't parse is a syntax error.

- **A control's Name and Index can be read at run time** (#1431). Reading `Combo1.Name`, or `ctl.Name` in a generic
  handler, stopped the program with a compile error that `On Error` couldn't trap, and so did an element's `Index`. Both
  now read as vb6.exe's do, measured on every kind of control:
  - **Name** is the control's name, its array's for an element, and a menu's too.
  - **Index** is an element's index, and 343, "Object not an array", for any other control.
  - **Assigning either** is VB6's 382, "property cannot be set at run time", which `On Error` can trap.

- **Ctrl+End after a selection made in code goes to the end of the text** (#1385). After code selected text that ran
  to the end, such as the Notepad demo's Select All, Ctrl+End put the caret at the start, so a paste went in there and
  Find Up searched from the middle. Ctrl+End and Ctrl+Home now land where VB6's do, measured with real keys.

- **A project's description is saved in its project file** (#42). Project Properties set it, and nothing wrote it,
  so a description typed there was lost at the next save. It is now read from and written to the `.vbp` as VB6
  writes it: `Description="..."` with a quote inside doubled, after `HelpContextID`, and no line when there is
  none. Measured with VB6's own Save Project As.

- **A `.bas` whose lines end with a lone CR no longer loses its code on Save Project** (#1020). The header split, the
  `VB_Name` retarget, the read-only regions and the guard ended a line only at LF. So a CR-only module was one line to
  them, taken whole for its header, and Save Project wrote the file back as its `VB_Name` line alone. One stray CR lost
  the line it joined, and a CR-only form lost its whole code section on the first nudge of a control. They now end a
  line at a lone CR and a lone LF as well as at CRLF, which is what VB6 does (measured with `vb6.exe /make`). A module
  with lone CRs is written back with CRLF throughout. To VB6 it is the same file.

- **Save Project no longer writes a second header into a `.bas` or `.cls` whose first line is blank** (#472). The
  header reader looked for the header on line 0 alone. So one blank line above it left the whole file as code, and
  Save Project, which writes every module, put the canonical header in front of it, unasked. For a class, that
  header contradicted the file's own instancing. The reader now finds the header below blank lines, and keeps them.
  A save no longer adds a header to code that carries its own `VB_Name`. A class whose `BEGIN` has no `END` no
  longer loses its code to the header, or grows by a line on every save.

- **`FormCodeText.PreserveAttributes` is gone** (#686). It could weld a header with no line ending onto the code
  after it, but it has had no caller since #273's phase 3 kept a form's header with `GuardedContent` instead, so it
  was deleted rather than repaired.

- **A ListBox's `Selected(i)`, `SelCount` and `Text` work** (#1395). Each was "Method or data member not found", so
  code that read which items were chosen, or chose one by its text, stopped there. Now each does what vb6.exe does on a
  single-select list:
  - `Selected(i)` reads and sets the selection, and is 381 outside the list;
  - `Text = s` selects the first item whose whole text is `s`, without regard to case, and raises Click every time;
  - Click comes from `Selected(i) =` only when the selection moves, as from `ListIndex`.

- **Save Project As names a designer, the resource file and the build folder from the new folder** (#1406). VB6's
  Save As rewrites the paths in `Designer=`, `ResFile32=` and `Path32=` so that they still name the same files.
  HexIDE kept them as read, so from another folder they named files beside the new project file. A reference's
  relative library path follows too. `HelpFile=` stays as typed, as VB6 leaves it.

- **`Debug.Print` prints every item, as VB6's Immediate window does** (#1380). Only the first item reached the Immediate
  window, so `Debug.Print "x ="; x` showed `x =` alone, and a trailing `;` still ended the line. Now `Debug.Print` writes
  what `Print #` writes, which is what VB6 does, line for line, as measured in its IDE:
  - numbers have a space for the sign and one after;
  - a comma moves to the next 14-column zone;
  - a statement ending in `;` or `,` leaves its line open for the next, even in the next run.

  `Spc(n)` and `Tab(n)` now work in both, where `Print #` refused them.
- **`Debug.Print ,` and `Debug.Print ;` load and run** (#1409). A `Debug.Print` of separators alone was a syntax error,
  so a module holding one could not load at all. Now it prints as VB6 does: each comma moves to the next zone, and the
  line stays open. A call of an ordinary procedure with separators alone, `Foo ,`, is still a syntax error, as in VB6.

- **Save Project As to another folder no longer cuts off the members the project keeps only as a line** (#737). A
  form whose file is missing, one that failed to load, or a UserDocument is kept in the project file without being
  loaded, and its line was written back as it was read. From a new folder that line named a file beside the new
  project file, where there is none, and the next open lost the member. It now names the same file from wherever the
  project file is written, as do the original lines of carried files.

- **A quote inside a form's string property is read and written as VB6 does** (#218). VB6 writes a quote inside a
  caption, tag or text doubled, and reads the pair back as one. HexIDE kept both quotes on read, so a VB6 form's
  `"He said ""hi"""` showed as `He said ""hi""`. It wrote a lone quote single, which vb6.exe refuses: the whole form
  fails to load, and the build fails.

- **A Label's, an Image's, a Frame's and a PictureBox's Click needs the release over them** (#1373). A press on one,
  dragged off and released on the form or another control, still raised Click, so dragging off a control never
  cancelled its click as it does in VB6. Now Click comes only for a release over the control again, as vb6.exe
  measured; a TextBox and a ListBox, which VB6 lets click wherever the release lands, are unchanged.

- **A control can no longer be given a name already on its form** (#854). The designer checked a new name only
  against the controls it models, and mostly with case. A rename, a control drawn from the toolbox, or a paste could
  take the name of a control HexIDE keeps as preserved text, such as a Data or OLE control, or a sibling's name in
  another case. vb6.exe builds such a form without either control, and logs nothing a build would stop on. Every name
  on the form now counts, compared without regard to case, as vb6.exe compares them.

- **A list's `ListCount`, `List(i)`, `RemoveItem` and `AddItem`'s index work** (#821), on a ListBox and a ComboBox.
  `ListCount` and `List(i)` were "Method or data member not found", `RemoveItem` ended the run as not implemented, and
  `AddItem "x", 0` appended, leaving the list in another order than the program built. Each now does what vb6.exe
  does, in range and out: `List(i)` reads "" outside the list, a write at `ListCount` or -1 appends, an index out of
  range is 5 or 381, and the selection follows its item.

- **A ListBox raises Click where VB6 does** (#1374). It raised Click after MouseUp on every release, and never when
  code set `ListIndex` or an arrow key moved the selection. Now a left press on an item raises Click before MouseUp,
  the selection follows the pointer while the button is held, `ListIndex = n` and an arrow key raise it when the
  selection moves, and a right or middle click, or a press below the items, selects nothing and raises none, as
  vb6.exe does with real input.

- **MousePointer has all seventeen of VB6's values** (#120). Icon (4), Size (5), Arrow and Hourglass (13) and Arrow and
  Question (14) were missing. A form using one saved it without the name VB6 writes beside it, the Properties window
  showed a blank, and the control drew the default pointer. All four now carry their names and draw the pointers
  vb6.exe draws, as measured.

- **DblClick runs** (#1031). It was declared nowhere and raised by nothing, so a `List1_DblClick` or `Form_DblClick`
  never ran. Now a double click raises it on the form and on each control VB6 gives one, in vb6.exe's order with real
  input: MouseDown, MouseUp, Click, DblClick, MouseUp, with no MouseDown for the second press. An OptionButton raises
  MouseDown then DblClick there, and a CommandButton or CheckBox, which have none, two clicks.
- **A form's own Click runs** (#1364). A click on the form's area, over no control, ran `Form_MouseDown` and
  `Form_MouseUp` and never `Form_Click`. Now Click follows MouseUp, for the left, right and middle buttons, as
  vb6.exe does with real input. As in VB6, a press released over a control or outside the form gives no Click. A
  double click gives one Click, not two.

- **Collapsing a language server's fold no longer hides its closing line** (#1363). The editor folds a server's
  range by whole lines but never said so, so a server could end a fold part-way along its closing line. clangd
  ends each fold just before its closing brace, and collapsing the fold hid the brace. The client now declares
  `lineFoldingOnly`, and such a server ends the fold on the line before.

- **`FontSize`, `FontBold`, `FontItalic`, `FontUnderline` and `FontStrikethru`, and a TextBox's own Font** (#1343,
  #915). The five members stopped a run at the first read or write, and a TextBox had no Font of its own, so Notepad's
  Format > Font stopped at its first line. Now every control with a font, and the form, reads and writes them as
  vb6.exe measured: sizes snap to whole pixels, and GDI's bitmap fonts to the heights they have, turning into Arial past
  a size of their own; the Booleans coerce and refuse as VB6's do. A TextBox draws in its own font, and its Font block
  saves byte for byte.
- **The Font dialog's sample draws in the face chosen** (#1353). It always drew in the default face, because a family's
  name was bound where a FontFamily was wanted.

- **A form the Project Explorer adds is saved into the project** (#1359). Closing the IDE wrote the form's file and
  left the project file without it, so the next open had no such form, even after Yes to everything in the save
  prompt. A project holding a form or module that has never been saved now counts as changed, so closing saves its
  file listing them.
- **A project is not saved without its never-saved forms** (#497). With the project ticked in the save prompt and a
  never-saved form not, closing wrote a project file without the form. Where that was the startup form, vb6.exe
  built the file in silence into a program that starts at `Sub Main`. Now nothing is written, a warning names the
  files, and the project stays open.
- **Add Form and Add Module no longer write over a file already in the folder** (#738). A new member's file replaced
  any file of its name there, such as another project's `Module1.bas` in a shared folder. The Add commands now skip
  to a number whose file is free.
- **A new form or module has no file until the project is saved, as in VB6** (#500). Each Add wrote a file at once,
  and in a project never saved that went into a temporary folder the developer never chose. Its first Save Project
  then wrote the `.vbp` where the developer asked and left the members in that folder, named from the project file
  by a path into it, so the next temp clean-up lost them. Now Save Project asks where to put each new form and module,
  as VB6 does. A new document's code window still shows the header its first save will write. Additions that have
  never been saved no longer survive a crash in that folder: there is no crash recovery yet (#499).
- **A language server that waits to be told sends document symbols as a tree** (#463). HexIDE read the tree
  but never declared that it could, so a server that honours the declaration, clangd among them, sent the flat
  form and every outline from it lost its nesting.

- **Two code windows over one name are one document to a language server** (#721). One file shared by two projects
  of a group opens two windows that name it alike. Each kept its own version count, so a server was opened twice and
  discarded whichever count was behind, and closing either window closed the document for both. The windows now
  share it: opened by the first, one sequence of versions, closed by the last.

- **An edit made while a language server starts reaches it** (#719). It was dropped, and the server analysed the
  text from before the edit until the next edit or a save. With two servers on one language, the second was also
  opened twice, the second time at a lower version with the older text. A change made during a start is now
  recorded and carried by the open the server receives, which is sent once and before any change.
- **A font's size is drawn in points** (#910). It was drawn as pixels, so every VB6 font came out a quarter too small:
  a Label in 12-point Arial drew at 12 pixels instead of 16. The default font was held as 11, the pixels
  VB6's 8.25 points draw at, and the designer's Font dialog wrote that into a .frm as `Size = 11`. It also turned 8.25
  and 9.6 into 8 and 9, and reset the charset, underline, strikethrough and weight on OK. Now sizes are points
  throughout, the default is 8.25, and the dialog keeps what it has no say over. A control on the default font draws
  as it did. The dialog's sample now redraws as a size or a style is chosen; it kept the ones it opened with.
- **Missing VB6 global objects fail explicitly** (#871). Reading `Screen`, `Clipboard`, `Printer`, `Printers` or
  `Forms` no longer silently returns Empty without Option Explicit. Bare and member reads report an unsupported
  object, which On Error cannot swallow; declared variables and user functions still take precedence.

- **Multiple file numbers on one source line** (#1146). `Line Input #1, s: Print #2, s` and `Close #1, #2`
  now parse in both the interpreter and language server. The date-literal token no longer consumes the file statements.

- **FontName reads and sets at run time** (#1141), on a control or the form. `Label1.FontName = "Wingdings"` stopped
  the run with "Method or data member not found"; now it switches the font, with the charset that makes a symbol font
  draw its symbols. As vb6.exe measured, a family the machine doesn't have is 380, "Invalid property value", which a
  program can trap to fall back. On Linux and macOS, Windows' text fonts are taken and drawn in a substitute, but a
  missing symbol font is 380.
- **A caption in a symbol font draws the font's symbols** (#1133), where the font is installed. A Wingdings Label
  captioned `PMJ` drew those letters; now it draws a flag, a bomb and a smiling face, as vb6.exe does, and Marlett draws
  its ticks, cross and arrows. Labels, CommandButtons, CheckBoxes and OptionButtons are covered. The caption still reads
  back as the letters. On a machine without the font, as Linux and macOS are for Wingdings, the letters are drawn as
  before.
- **A menu's Click as it opens, and a shortcut's parents' Clicks** (#1344). A menu that holds items never raised its
  Click, so a program could not enable or disable the items before they showed: Notepad's Edit menu offered Cut with
  nothing selected. A shortcut raised its own item's Click alone, even for an item disabled or hidden. Now opening a menu
  raises its Click, and a shortcut raises each menu's from the top down to its item's, stopping at the first that is
  disabled or hidden, with each asked after the Clicks above it have run, as vb6.exe measured.
- **A program can use its menus** (#1126). Naming one in code stopped the run with "Variable not defined", so
  `mnuFile.Enabled = False` or a ticked level menu couldn't run, and choosing an element of a menu array called its
  Click without the Index. Now each menu has its name, a menu array works like a control array, Index on its Click
  included, and Caption, Checked, Enabled, Visible and Tag read and write with VB6's refusals. `Load` and `Unload`
  grow a menu array, as a recent-files list does. Choosing an item no longer ticks or unticks it, which is the
  program's to do. Every rule was measured on vb6.exe.
- **A form's fonts are drawn in the family they name** (#1132), when it is installed. Every VB6 font used to draw in one
  sans serif: Courier New wasn't monospaced, Times New Roman had no serifs. The family was looked up only in a bundled
  collection that holds no fonts; now the installed fonts come first. VB6's own default, MS Sans Serif, isn't shipped,
  so a form on it still draws in the fallback.
- **A Label with `BorderStyle = 1` draws its border** (#1077), at run time and in the designer. It drew none, so a
  grid of bordered Labels, such as the Battleship and Minesweeper demos' fields, was one block of colour. As vb6.exe
  draws it: a sunken edge two pixels deep when Appearance is 3D, and one pixel of the window-frame colour when Flat.
  `vbWindowFrame`, which had no colour, now has Windows 10's.
- **A form with a menu bar runs with its whole client area** (#1127). The bar sat inside the form's ClientHeight, so
  the bar's height was cut off the bottom of the form, and reading `ScaleHeight` stopped the run. As in VB6, the bar is
  now above the client area, 19 pixels high, and `ScaleHeight` is the area below it, so a form can fit itself to its
  content with `Height = (Height - ScaleHeight) + needed`.
- **A form holding a control HexIDE cannot create is refused, rather than run without it** (#857). Examples are a
  `VB.Data`, a `VB.OLE` and any ActiveX control. The form used to run with the control, and everything inside it, left
  out. Code naming it read Empty or wasn't defined, a call on it under `On Error Resume Next` was skipped with
  `Err.Number` 0, and a form whose code never named it ran as a different program. Now the run stops before the form
  is built, naming each control it can't create, and the program can't trap it. The designer still opens the form.
- **A run-time error nothing handles halts the program where it stands, as VB6 does** (#1011). The event procedure that
  met it used to be abandoned while the run carried on, so a Timer tick or a click still ran, on whatever state it had
  left half done. Now the dialog offers End and Debug, and nothing runs behind it. Debug breaks at the statement with the
  procedure still live, so Locals and the Immediate window work there, and Continue runs the statement again. A handler
  in the code that raised an event, `RaiseEvent` included, no longer catches the event's error, as in VB6.
- **`SetFocus` runs** (#1061). It stopped the run as not implemented, on every control. Measured on vb6.exe:
  - the control has the focus at once;
  - its LostFocus and GotFocus come after the handler that called SetFocus;
  - it is error 5 on a form not yet shown, or on a disabled or invisible control.

  A TextBox now raises GotFocus and LostFocus too.
- **A TextBox, Frame or scroll bar saved disabled runs disabled** (#1061). `Enabled = 0` in the form was dropped, and the
  control ran enabled.
- **Operators group as VB6 groups them** (#1074). Each had a precedence level of its own, so `10 - 2 + 3` was 5,
  `5 * 5 \ 2` was 10, `1 < 2 = True` was False and `Not False And False` was True, with no error. Measured on
  vb6.exe across the whole ladder: `*` and `/` share a level, then `\`, then `Mod`, then `+` and `-`, then every
  comparison, and operators of one level go left to right. A Boolean beside a number now compares as that number,
  True as -1, where it raised Type mismatch.
- **Formatting no longer changes a file's line endings.** The bundled language server joined its formatted
  text with bare line feeds, so a file VB6 wrote, with CRLF endings, was never already formatted: every
  save sent an edit covering the whole file, and applying it put LF endings into the code window.
- **Renaming a form now renames it in its file's `Attribute VB_Name` line as well as its `Begin` line.**
  Previously a renamed form was saved naming two different forms, one in each. The code window follows
  the rename as it happens, and Ctrl+Z there does not undo it.
- **Find with Use Pattern Matching and Find Whole Word Only no longer gives up at the first match that is
  not a whole word.** It reported the text as not found, however many whole-word matches came after it.
- **An open code window now follows a reload that changes whether its form can be saved.** Previously,
  when a form's file changed on disk so that HexIDE could now reproduce it, or could no longer reproduce
  it, the code window kept its old answer until it was closed and reopened. It stayed locked with the
  warning showing, or stayed typable with no warning. The designer already followed the reload.
  ([#475](https://github.com/hexide-io/HexIDE/issues/475))

- **A call written with a `$`, such as `Trim$(s)` or `Left$(s, 1)`, runs** (#1071). It stopped the run. The 23 library
  functions that have a `$` form answer as their plain form with the result made a String, so a Null is error 94, all
  measured on vb6.exe. A program's own String Function answers its `$` form. Any other `$` still stops the run,
  naming #742.
- **A control's `Move` runs, and a form's `ScaleWidth` and `ScaleHeight` can be read** (#1071). Both stopped the run.
  Move sets Left, Top, Width and Height as given, and checks before it moves, as vb6.exe was measured to: a negative
  size is error 5, and nothing moves.
- **Arithmetic on a numeric String works, as in VB6** (#1047). `Text1.Text * 2` used to stop the run with Type mismatch.
  - **The reading:** every operator now reads a String operand as `CDbl` does, in the program's locale, and the result
    type is the one a Double would give.
  - **`+`:** a String beside a String or Empty concatenates.
  - **`CDbl` and its relatives** read a thousands separator, so `CDbl("1,000")` is 1000 under en-GB (#770). They also
    read a trailing sign (`"6-"`), parentheses (`"(6)"`) and a `D` exponent (`"1D3"`), all measured on vb6.exe.
- **Two overflow edges now match VB6** (#234). Both were measured as p-code and in vb6.exe's IDE.
  - **`0 / 0` raises Overflow (6)**, not Division by zero (11), for every operand type except Decimal, which keeps 11.
  - **A Variant Single that overflows** widens to Double, as a Variant Integer widens to Long, instead of raising
    Overflow. Two typed Singles still raise.
- **`Unload Me` unloads the startup form, as in VB6** (#973). It used to stop the run as not implemented. Measured on
  vb6.exe:
  - **The events:** `Form_QueryUnload` (UnloadMode 1 from code, 0 from the close box), then `Form_Unload`, each with a
    Cancel that keeps the form loaded.
  - **What comes after:** the rest of the handler still runs. Then `Form_Terminate`, the form's fields are released,
    and the run ends.
  - **The close box** asks the same way, and a refusal keeps the window open.
  - **`Unload Me` in `Form_Load`** ends the run without showing the form. A second Unload, or one from inside
    QueryUnload or Unload, does nothing.
- **MouseDown, MouseUp and MouseMove are raised** (#974) on the form and on every control that has them in VB6:
  CommandButton, Label, TextBox, PictureBox, Frame, CheckBox, OptionButton, Image and ListBox. Before, no handler for
  them ever ran. Measured on vb6.exe:
  - **Order:** a CommandButton and a CheckBox raise Click between MouseDown and MouseUp, and the others after MouseUp.
  - **Arguments:** Button is 1, 2 or 4; Shift is 1, 2 and 4 for Shift, Ctrl and Alt; a control array's Index comes first.
  - **X and Y** are twips from the control's client area, even on a pixel form, and the form's follow its own ScaleMode.
  - **Image:** an Image takes the mouse, and Click, across its whole rectangle, with or without a picture (#861).
  - **No passing up:** an event a control doesn't handle is not passed to its container, as in VB6.
- **A running program's event handlers run one at a time, as in VB6** (#766). Under the debugger, a handler that gave up
  the thread at its time slice used to let any other event start and finish first, and let the statement that raised
  it run on. Measured on vb6.exe:
  - **What waits:** nothing runs inside a handler unless it calls `DoEvents`. An event that comes meanwhile waits its
    turn.
  - **Nested events:** a handler a statement raises (`Text1.Text = "x"` raising `Text1_Change`) finishes before the
    raiser goes on.
  - **`DoEvents`:** it now runs what is waiting, inside the caller, so a `Do: DoEvents: Loop Until clicked` loop sees
    the click.
  - **Timers:** a Timer delivers one tick, not several, once it is free, and never inside its own handler.
- **A control's Left, Top, Width and Height are twips at run time** (#933), as VB6 has them, measured on vb6.exe:
  - **What changed:** a program read and wrote them in pixels, as a Double. A .frm Left of 120 read 8, and
    `Text1.Left = 1200` moved the control 1200 pixels, fifteen times too far. They are now twips, as a Single.
  - **ScaleMode:** they are read in the container's ScaleMode: the form's, a PictureBox's for the controls inside it,
    and twips inside a Frame. A form's own Width and Height are twips whatever its ScaleMode.
  - **Rounding:** an assignment is rounded to whole twips, half to even, so `Left = 1.5` reads back 2 (#791).
  - **Labels:** a Label is at least a pixel wide and high.
  - **Not yet:** a User, centimetre or millimetre ScaleMode is refused (#990). The form's own size is its client
    size, without the frame (#852).
- **A module's Public variables and Consts are visible bare from every other module** (#880), as VB6 makes them,
  measured on vb6.exe:
  - **What changed:** `Debug.Print M`, where Module2 has `Public Const M = 3`, is 3, and `G = 7` stores into Module2's
    `Public G`, from a module, a class or a form. Before, both made a new variable of their own: a read gave Empty,
    `For i = 1 To M` ran no times, and under Option Explicit both were "Variable not defined".
  - **Order:** the module's own names come first, a procedure's local shadows them, and in a form the form's members
    come first too. They are found before a built-in constant of the same name.
  - **Clashes:** a name two modules export is "Ambiguous name detected" where it is used bare, and so is one module's
    variable beside another module's Public Sub or Function of that name.
  - **Private:** a Private variable, a module-level `Dim` and a `Private Const` stay invisible from other modules.
  - **Consts:** a module-level Const or array bound reads another module's Public Const bare, where it was refused, and
    a store into one is "Assignment to constant not permitted".
- **A bare name in a form's code is the form's member** (#995), as VB6 resolves it, measured on vb6.exe:
  - **The common case:** `Caption = "Ready"` sets the form's caption, and `If Width > 3000` reads its width. Before,
    the first made a new variable and the second read Empty, or both were "Variable not defined" under Option
    Explicit.
  - **Order:** the form's members come after the procedure's locals and the form's own variables and controls, and
    before a module's `Public` of the same name.
  - **Members HexIDE lacks:** one VB6's Form has and HexIDE doesn't, such as `CurrentX`, `ScaleMode` or `Cls`, is
    refused as not implemented rather than made a variable. `Me.ScaleMode` says the same, where it raised 438.
  - **Read-only members:** assigning one, such as `hWnd` or `Count`, bare or through `Me`, is VB6's own compile error.
  - **Calls with arguments:** a property called with arguments is VBA's function only for `Left("abc", 1)`, and is
    never a project procedure of the same name.
  - **The member table** is read out of VB6.OLB by a new `scripts/dump-typelib-members.ps1`, not written by hand.
- **A running form is an object, with a real `Me`** (#822, first slice). A form's code ran as a standard module, with
  `Me` and the form's name bound to its window. The form is now an instance of its module, as VB6 has it, measured on
  vb6.exe:
  - **`Me` is the form:** `TypeName(Me)` is `Form1` and `Me Is Form1` is True. `Me.Caption` and `Me.Hide` still reach the
    window, after the form's own procedures, variables and controls.
  - **A module reaches the form through its name:** `Form1.Caption`, `Form1.Score` and `Form1.Text1.Text` work from a
    standard module.
  - **A form's Public Subs aren't globals:** called unqualified from a module, one is "Sub or Function not defined", as
    in VB6, and a module's Sub of the same name is no longer "Ambiguous name detected".
  - **Declarations and events:** a form's declarations run before `Form_Initialize`, so an array sized by a Const is
    sized (#256, #760), and a module's Public Const reads its value from `Form_Load`. `Form_Initialize` is raised, and
    `Form_Load` runs before the window is shown and before `Form_Resize` (#994).
  - **`WithEvents` in a form** binds, so the form's handlers run (#972).
- **A store into a Const is refused, as VB6 refuses it** (#1003). `MK = 2` after `Const MK = 1` ran and overwrote the
  constant, so every later read gave 2. It is now VB6's compile error "Assignment to constant not permitted", raised
  as the statement runs. VB6 gives other messages for other statements, and so does HexIDE now, for a Const and an
  Enum member alike: a For counter, a For Each variable or a Mid target is "Variable required - can't assign to this
  expression", and ReDim or Erase of one is "Expected array". Measured on vb6.exe, native and p-code.
- **Select Case compares as VB6 does, and arithmetic overflows where VB6's does** (#987). VB6 treats a value as typed or
  as a Variant by where it came from, and both decide the result. Measured on vb6.exe, native and p-code:
  - **A typed selector** converts each Case value into its type, so a declared 2 is no longer `Case Is < 2.4`, and a
    declared Boolean True is `Case 3 To 6`.
  - **A Variant selector** compares as VB6 compares Variants: a Variant 5 is below `"10"` as text, and a Variant 2 is
    not `Case 1.7`. True is below False.
  - **A typed Function's result, a conversion function, a typed array element or Type field, and the typed intrinsics**
    (`Asc`, `Sgn`, `UBound`, `Len` of a typed argument and others) now overflow past their type's ceiling, as a
    declared variable does: `F() * 7000` with `F` an Integer returning 5 raises Overflow, where it gave 35000.
  - **A Variant array element, a Variant Type field, a late-bound member and the Variant intrinsics** (`Hour`, `Day`,
    `Mid`, `Left` and the rest) widen, as a Variant does: `w(0) * 7000` with `w(0)` holding 5 is 35000, where it
    raised Overflow.
  - **A Const, an Enum member, and an Enum parameter, return or Type field** are typed, as in VB6: `K * m` with
    `Const K = 5` and `m As Integer` 7000 raises Overflow, where it gave 35000. A Const declared `As Variant` is a
    Variant.
  - **A parameter or return declared `As Object`** is late-bound, as a Variant is, so its members' results widen.
- **A ByRef `As Variant` parameter writes back to its caller** (#1001). `Sub S(v As Variant): v = 5` left a caller's
  Variant, or a caller's `Integer`, as it was, because the parameter got a copy of anything but an array. It now
  shares the caller's variable, as an untyped parameter does. Along with it, an untyped or Variant parameter given a
  typed variable is a Variant in arithmetic, as in VB6: `v * 7000` with an `Integer` 5 is 35000, where it overflowed.
  And a constant passed ByRef goes by value, as VB6 passes it: a Const given to a parameter that writes it is
  unchanged, where it was overwritten, and an Enum member no longer raises "Assignment to constant not permitted".
- **An array assignment copies the array** (#970). `d = f` shared one array between the two names, so a write through
  either reached the other, with no error. Measured on vb6.exe, VB6 copies wherever it stores an array: into a
  variable, an element, a ByVal parameter and a For Each variable, and so the row-buffer idiom, `rows(i) = row`, keeps
  each row. Along with it:
  - **A ByRef `As Variant` parameter given an array shares it**, as an untyped one does, so a `ReDim` through it reaches
    the caller's array, and is VB6's 10 for a fixed one.
  - **An untyped or Variant parameter's `ReDim`** follows VB6's Variant rule even when the caller's array is declared:
    `ReDim v(-1)` raises 9 and keeps the caller's array, where it erased it.
  - **A copied array shares its objects, and each holder keeps them alive.** A local object array's objects now
    terminate at `End Sub`, as in VB6; they never terminated at all, and an object could terminate while another array
    still held it.
  - **A fixed array can't be assigned an array:** VB6's "Can't assign to array", or 13 through a parameter, where the
    fixed array was replaced.
  - **A For Each variable is Empty once the loop runs out**, as in VB6, even when there was nothing to walk; it kept
    the last element. `Exit For` still leaves it holding the element it reached.
  - **`End Sub` releases a procedure's scalar locals before its arrays**, each in declaration order, as VB6 does.
- **A Function declared to return a typed array takes only an array of its type** (#986). `F = Array(1, 2)` in a
  Function `As Integer()` returned a Variant array with no error, and `F = 5` in one `As Long()` returned an Integer.
  A mismatch now raises Type mismatch at the store, as VB6 does, or stops the run with VB6's compile error "Can't assign
  to array" where VB6 would not have compiled the program. A String stored into a Function declared `As Byte()`, which
  VB6 converts into its bytes, stops the run as not implemented (#998). Before, it was returned as a String.
- **Under On Error Resume Next, a block whose condition or header raises now runs, as VB6's does** (#839). HexIDE skipped
  the whole statement, so the program carried on down a path VB6 would not take, with no error. VB6 goes on at the
  first statement inside the block. Measured on vb6.exe, as native code, as p-code and in the IDE:
  - an If or ElseIf runs the branch whose condition raised;
  - a Case test runs its own Case;
  - Do While, Do Until and While run the body, asking again each time round, and a Loop While or Until ends the loop;
  - With runs its body with no object;
  - For and For Each run the body once, and then carry on as the IDE does.
- **For Each over something it cannot walk raises VB6's error** (#839). An unallocated array ran no passes and said nothing;
  it now raises 92. Nothing raises 424 and any other non-object value 13. Over an object, which HexIDE cannot walk yet,
  it now says so rather than raising an error On Error Resume Next stepped over. A For Each whose collection itself
  raises now stops the run unless the collection is a call of one of the project's own Functions (#969), where before
  it ran on.
- **A For converts its start, limit and step into its counter's type** (#839), as VB6 does. A Byte counter's
  `Step -1` is an Overflow, and so is any bound its type cannot hold, raised at the For statement. A fractional bound
  rounds half to even into an Integer or Long counter, so `For i = 1 To 2.5` runs to 2. Each was read as a Double, and
  a fractional one raised a trappable Type mismatch that skipped the loop under On Error Resume Next. A bound still
  fractional for its counter, a Single, Double, Currency, Date or Variant one, now stops the run as not implemented
  (#983), where it skipped the loop without a word.
- **A typed parameter keeps its type** (#839), as a variable declared with it does. `Optional i As Long = 7` holds a
  Long, a store of 2.5 into a `ByVal i As Long` gives 2, and a store of 300 into a `ByVal b As Byte` raises Overflow.
  The parameter's slot recorded no type, so each held whatever was stored into it. The parameter is now a ceiling in
  arithmetic too, as a declared variable is: with a `ByVal i As Integer` holding 5, `i * 7000` raises Overflow, where it
  widened to 35000. For the numeric types only; a String, Boolean or Date parameter is still not converted (#874).
- **A Function declared to return an array returns it** (#975). `Function F() As Long()` returned a Long 0 with no
  error, because its return was read as the element type, so `d = F()` left `d` a scalar and the next `d(1)` stopped
  the run. It now returns the array, and one that never assigns its name returns an empty one, as VB6 does. A Property
  Get declared the same way is fixed with it.
- **Calling a procedure that carries an `Attribute` line no longer stops the run.** VB6 writes these inside a
  procedure for a default member (`VB_UserMemId`), a description from Tools → Procedure Attributes
  (`VB_Description`), a property category (`VB_ProcData`) and more, and after a module-level variable for its
  description. Each stopped the run with "Attribute not implemented". They are metadata, and now run as nothing,
  as in VB6. The debugger treats them as blank lines: a step passes over them, and Set Next Statement refuses one.
  A default member they declare is not read yet, so a use of the object as a value is refused naming #174.

- **A member call inside `While...Wend` runs** (#882). `Debug.Print x`, `Err.Clear` or `rs.MoveNext` in a
  `While` body stopped the run with "Sub or Function not defined", naming the object, so the common
  `While Not rs.EOF ... rs.MoveNext ... Wend` loop could not run at all. `Do While` was unaffected.

- **A condition is read as VB6 reads it** (#847). A block `If` or `ElseIf` refused anything but a Boolean with an
  error numbered 0, so under On Error Resume Next it skipped its body with nothing in Err. A one-line `If` raised 13
  for anything but a Boolean, and the loops raised 13 for a Long, a Double, Empty or most strings. So
  `If Len(s) Then`, `If InStr(s, "x") Then`, `If flags And 4 Then` and `Do While n` with a Long `n` stopped the run.
  Every form now reads its condition as `CBool` does: a number holds when it is not 0, a string when it reads as True
  or as such a number, and Null and Empty do not. An object in a condition raises VB6's 438 and Nothing 91, where
  each was a trappable 13, or in a block `If` or `ElseIf` the error numbered 0. `If Err Then` reads Err's Number
  (#840), and a class with a default member and a control are refused until #174 and #790.

- **A subscript, a control-array index or an array bound may be any number** (#849). Only an Integer, a Byte or
  an integral string was accepted, so `a(i)` with `i` a Long, `a(UBound(a))` and `ReDim b(n)` stopped the run.
  Each is now coerced as VB6 coerces it. A subscript or a bound is coerced as `CLng` does: `a(2.5)` is `a(2)`, Null
  raises 94, `"x"` 13, and an omitted `Optional` 13. A control-array index is coerced as `CInt` does, so one outside
  an Integer raises 6 and a negative one 341, and an omitted `Optional` is 13 there too. `UBound`'s and `LBound`'s
  dimension is coerced as `CInt` does as well, and an omitted `Optional` one is 13, where it was read as dimension 1.
- **`ReDim` follows VB6 at its edges** (#849). An upper bound below its lower raises 9 and erases a variable declared
  an array, or an array parameter, keeping its type. A Variant keeps its array when any dimension's upper bound is
  one below its lower, and is emptied with 7 otherwise. `ReDim b(2147483647)` raises 7, Out of memory, where it
  raised 9. A Variant that never held an array can be `ReDim`med at all; it stopped with an internal error.
- **A typed array parameter shares its caller's array** (#849). `Sub P(b() As Long)` took `b` for a Long, so an array
  passed to it was copied rather than shared, and narrowed as if it were a scalar.
- **A fixed array can't be resized through a parameter** (#873). `ReDim b(10)` in a callee given a caller's
  `Dim buf(3)` raises VB6's 10, "This array is fixed or temporarily locked", and leaves `buf` as it was, whether `b` is
  an array parameter or an untyped one; named directly, `ReDim` of a fixed array is VB6's "Array already dimensioned".

- **A module-level `Const` is checked as VB6 checks it** (#760). Its value may read only other constants: a Const
  declared below it or the Const itself, a variable, a user function and any function call are VB6's "Constant
  expression required", where they ran from whatever HexIDE made of them. Another module's Public Const is read, bare
  (#880) or qualified, once that module's declarations have run, and refused before (#893), rather than read as
  Empty. A module naming itself is "Circular dependencies between modules".
- **A `Const` holds its value as its `As` type** (#873), at module and procedure level: `Const C As Integer = 2.5` is
  2, and `Const HALF As Long = GRID / 2` rounds as a store into a Long does. A value the type can't hold is VB6's
  compile error: `Const C As Byte = 300` is "Overflow", `Const C As Integer = "x"` "Type mismatch", and
  `Const C As Integer = Null` "Invalid use of Null". The As clause was ignored, so a module array sized from such a
  Const had the wrong bounds. A String given to a `Const … As Date` is refused, until HexIDE reads a date string as
  VB6 does (#967).
- **A module-level array sized by a `Const` loads** (#760). `Private mGrid(0 To GRID_MAX)` made the whole module
  fail to load; so did a typed literal such as `Dim d(2&)`. Its bounds are read where it is allocated, by VB6's
  rules: a Const or Enum declared below, a variable, a user function, and the built-ins VB6 refuses there are its
  "Constant expression required". A constant qualified by its library, container, module, Enum or the project is read
  (`VBA.vbCrLf`, `KeyCodeConstants.vbKeyA`, `Module2.Cols.colMax`), and `Len` sizes only from a String: of a number
  it is VB6's "Variable required". A dimension with no values, such as `Dim d(-1)` or `Dim d(5 To 2)`, is VB6's
  "Range has no values", in a module, a class or a procedure, where it built an empty array or raised 9. Another module's Public Const before that module has run
  (#893), and its Enum member read unqualified (#889) are refused, rather than sized from Empty. A form's run refuses
  such an array, naming it, because it does not run the module's declarations yet (#256). A procedure's `Const`s are declared before its `Dim`s, so
  `Dim a(P)` inside a procedure sees `P` too.

- **A method called from `Class_Initialize` no longer terminates the object** (#850). The call fired
  `Class_Terminate` in the middle of `New`, and the real `Class_Terminate` never ran afterwards.

- **An object used as a value no longer gives a wrong value.** A class with no default member raises VB6's 438
  wherever a value is wanted, where HexIDE raised 13, printed its CLR type name (`"v=" & k`, `CStr(k)`,
  `Debug.Print k`), read it as 0 (`Val(k)`, a ByVal Long argument), or matched nothing in `Select Case`. That covers
  every operator, a Let, a ByVal argument for a typed parameter, every condition, bound and Case, `Randomize`,
  `Err.Raise`, `Error`, `ChDir`, `ChDrive`, the Mid statement, and every built-in function's value arguments.
  `TypeName` and `IsObject` still take the object, and `VarType` and the Is functions answer about it. A class
  with a default member, which VB6 reads in all those places (bar a few, measured, where a Function default member
  gives 450: `Format`, `Abs` and `Not` through a Variant, and in the IDE a Case value against an Empty selector), stops
  the run with a message saying class default members are not implemented yet (#174),
  rather than an error `On Error Resume Next` would step over.
- **`Err` used as a value is its `Number`**, as in VB6: `"Error " & Err`, `x = Err`, `Err + 1`. It was refused. App
  used as a value raises VB6's 438, where it printed a .NET type name (VB6 also empties a Variant `v` in `v = App`
  first, which is #878). A control still gives its .NET type name in `"v=" & Text1`, `CStr(Text1)` and
  `Debug.Print Text1`, until #790.
- **`TypeName(Err)` is "ErrObject" and `TypeName(App)` is "App"**, as in VB6. They were HexIDE's own class names.
- **`Join` and `Filter` of something that is not an array raise 13**, as in VB6, where they raised 5. Of an array
  never allocated, `Join` gives `""` and `Filter` an unallocated String array, where both stopped the run with an error no
  `On Error` could trap.
- **`Filter` always gives a String array**, as in VB6, where it gave a Variant array for any allocated input. A Null
  match, switch or compare is 94, even for an array never allocated, and even beside something that is not an array.
  The switch reads as `CBool` does, so an Empty one no longer raises 13. The compare is 0 for binary and 1 for text;
  2 and negative values raise 5, where every value but 1 was binary; and 3 and above, which VB6's answers are
  consistent with reading as a Windows locale identifier, are refused (#881). A Nothing compare is 91, where it read as
  0, a binary compare. `Filter` converts every element first, and checks its compare only when it compares an element
  with the match: of an array with no element it gives an unallocated String array, and an empty match keeps every
  element, so `Filter(list, "", True, 2)` is the whole list where it raised 5. An object among its match, switch and
  compare is read only as its turn comes, so `Filter(d, k, "x")` is the switch's 13 where it was k's 438.
- **`Join` and `Filter` convert each element as a store into a String does**, as in VB6: a Null element is 94 and an
  array 13, and Nothing 91, where they read as `""`, the CLR type name and `""`. `Join` of an array with no element
  gives `""` without reading its delimiter; with one, a Null delimiter is 94 and a Nothing one 91, where both read as
  `""`. A Nothing match in `Filter` is 91 too.
- **The Immediate window's `? obj` no longer prints the class's name.** A class with no default member is 438, as
  `Debug.Print` of one is in VB6, and one with a default member says that reading it is not available in break mode.
- **A syntax error when a program starts, or a compile error while it runs, is reported like a run-time error.**
  Each was a message box nothing recorded, so a program that never started was reported to automation as having run
  and ended. Both now appear in the run-time error dialog and are kept for `get_last_runtime_error`, and a syntax
  error at the start makes `run_project` answer that nothing is running. That answer now starts its own paragraph
  rather than running on from the error's last word.
- **Every start that never becomes a run is reported as one.** A project with no startup form, and a Sub Main project
  with no usable `Main` or with two, were a message box, or a run that ended, and `run_project` answered success.
  Each is now a failed start, in VB6's words, recorded for `get_last_runtime_error`.
- **A syntax error names the module it is in.** One in a `.bas` or `.cls` was blamed on the startup form.
- **A declaration HexIDE cannot load is described the same way whichever startup object the project has.** The form
  path gave the raw exception after the form's name.
- **Run To Cursor from design mode no longer leaves its target behind when the start fails.** After fixing the error,
  a plain F5 paused at that line as though at a breakpoint.
- **A message box shows the buttons and icon it was asked for.** `MsgBox "Sure?", vbYesNo + vbQuestion` showed
  OK alone with no icon, because every in-box constant is a Long and the style was read only as an Integer, so a
  Yes branch could never run. The answer is a Long, as in VB6. A style VB6 does not accept, such as 6 or 12, is a
  plain OK box, as in VB6.
- **Closing a message box answers as VB6's does.** It answered OK whatever the buttons, so closing an OK/Cancel
  box took the program's OK branch. A box with a Cancel button now answers Cancel. A Yes/No or Abort/Retry/Ignore
  box can no longer be closed without an answer, which is Windows' own rule for those two.
- **`CInt`, `CLng` and the other numeric conversions no longer read an object or an array as 0.** `Nothing`
  raises 91, an array 13 and an object 438, as in VB6, and so do `RGB`, `QBColor` and a message box's style. A
  control, which VB6 reads through its default member, stops the run with a message saying default members are not
  implemented yet (#790), and so does a class with a default member (#174). An omitted `Optional` argument converts to
  448, as in VB6, where it read as 0.
- **A message box stays answerable when its form hides.** `Me.Visible = False` while a box was open made the box
  disappear and answer Cancel, or stopped the code waiting on a Yes/No box, and a box opened from a form already
  hidden ended the run as Internal error 51. The box now waits for its answer, shown on its own.
- **Restart with a message box open no longer runs the old program on.** The box closed, and the code waiting on it
  carried on beside the new run with a Cancel nobody pressed, or stopped the new run. It now ends with its run.
- **A name in a different case is the same name, as in VB6.** `abc = 5` after `Dim Abc` used to make a new
  variable and leave `Abc` as it was, and `foo = 7` inside `Function Foo` returned 0. Variables, constants,
  parameters, a Function's return value, controls, properties and `Debug.Print` now match in any case, and so do
  property names in a `.frm`, a form's own included, and the fields of its Font block.
- **A `Const` inside a procedure no longer overwrites a module variable or `Const` of the same name.** It is the
  procedure's own, as in VB6, and the module's value is unchanged after the call.
- **A Label's background colour set by a program now shows.** `Label1.BackColor = vbRed` read back red but left
  the label as it was; now it paints, for a label in a control array too. A program can make a transparent Label
  opaque with `BackStyle = 1`. A transparent Label ignores a `BackColor` set while it is transparent, as VB6 does.
- **Every control's colour reads back as VB6's number**, `255` rather than `&H000000FF&`, so `If Text1.BackColor
  = vbRed` is True and `Debug.Print` shows what VB6 shows. A Label's `ForeColor` and a TextBox's colours from the
  form read back at all, where they used to read as black; a TextBox now keeps the colours a form gives it. A
  ListBox's and ComboBox's text colour defaults to the window text colour, as in VB6.
- **A form keeps a property value HexIDE cannot read yet**, such as a palette colour, and saves it back as it was
  written. It used to be dropped from the file on the next save.
- **A running program writes and reads dates and numbers in the user's regional settings**, as VB6 does. The IDE
  ran every program in the invariant culture, so `CStr(#1/2/2003#)` showed `01/02/2003` on a UK machine, where VB6
  shows `02/01/2003`, and `CDate("1/2/2003")` was 2 January rather than 1 February. `CStr`, `Format`, the month and
  day names, `CDate`, `CDbl`, `IsDate`, `IsNumeric` and a two-digit year in `DateSerial` now follow the user's
  separators, date order and names, so text a program wrote reads back as the same value. Two things stay VB6's own
  whatever the locale: a date is Gregorian, so a Thai, Persian or Saudi machine still counts 2003 as 2003, and a minus
  sign is a plain `-`. A date the locale's order cannot make is read the other way round, as in VB6: `CDate("1/13/2003")`
  is 13 January on a UK machine. The IDE's own language changes none of this. The numbers written in the source
  itself are a separate matter, still read in the machine's culture in a made EXE (#781).
- **`s = Text1` no longer stores a .NET type name.** In VB6 a control where a value is wanted stands for
  its default property, so `s = Text1` reads its text. HexIDE does not implement that yet, and the assignment
  stored `HexIDE.Runtime.BuiltinControls.VBTextBox` in the String, or the control itself in a Variant, without a
  word. It now stops the program and says so, until default properties land (#790). `Set`, `For Each` and
  passing a control to a procedure are unaffected.
- **Numbers and dates turn into the text VB6 makes of them.** `CStr`, `&`, a String variable, `MsgBox` and
  `Format` with no mask wrote .NET's text: `CSng(1) / 3` came out as `0.33333334` and `0.1 + 0.2` as
  `0.30000000000000004`, where VB6 writes `0.3333333` and `0.3`. A date came out with a midnight time attached.
  Now a Single has seven significant digits and a Double fifteen, with E notation where VB6 uses it, and a date
  leaves out a midnight time or the zero date. A time written on its own, `#10:30:00 AM#`, is now that time on
  VB6's zero date rather than on today's date.

- **`End` ends the program.** It used to stop the program with an error saying HexIDE did not implement it, so a
  `Sub Main` that finished with `End` showed that error. Now the program stops at `End`, as in VB6: nothing after
  it runs, its forms close without their unload events, and no `Class_Terminate` runs.

- **A program that reaches something HexIDE cannot run yet now says so.** A statement, function or control
  method the interpreter does not implement used to stop the program without a word: a click handler ended
  part way while the program carried on, and a `Sub Main` project opened nothing at all. The run now ends,
  and the runtime-error dialog says HexIDE does not implement it, naming it in most cases; a few still carry no
  name (#181). An error handler does not swallow it, because carrying on past a statement that never ran would
  give a wrong answer. An error that ends `Sub Main` now appears in the same dialog as a form's.

- **`Label1.Caption = 5` sets the caption to "5".** A value of another type assigned to a control property is
  converted to the property's type, as VB6 does, instead of stopping the program. That covers a number into a
  caption or text, a string into a position, `Timer.Interval`, a scroll bar's `Value` or a list's `ListIndex`, a
  number into a check box's `Value`, a string into a colour, and a colour copied from one control to another. A
  value VB6 refuses raises VB6's own error, which an error handler can trap: Type mismatch for text that is not a
  number, Invalid use of Null, Overflow, and Invalid property value for one outside the property's range
  (`Check1.Value = 3`, `HScroll1.Value = 150`, `List1.ListIndex = 3` on a three-item list). `Tag` takes `Empty`
  as `""`, and reads `""` before anything is put in it. A date into a caption is written in the user's date
  format, as `CStr` writes it. Where HexIDE cannot give VB6's answer yet, the program stops and says so rather than
  carry on with a wrong one: a palette colour; a scroll bar whose `Min` is above its `Max`; a control on the right of any
  property, `Tag` and `Enabled` included.
- **`Command1.Enabled = Empty` disables the button**, as `CBool(Empty)` is False. It raised Type mismatch.
  `CDbl("NaN")`, `CDbl("Infinity")` and `CBool("NaN")` raise Type mismatch, and `CDbl("1E+400")` raises
  Overflow; .NET's spellings of those used to get through as numbers, so `Enabled = "NaN"` enabled the button.
  `CBool("&H10")` is True, as in VB6; it raised Type mismatch.

- **A language server that orders changes by version no longer ignores the first edit to a file.** That edit repeated the version the file was opened at, so such a server kept analysing the text
  from before it until the next edit.
- **Bookmarks and breakpoints are no longer lost on the way to their file.** A bookmark set just before
  closing the project, or HexIDE, was not saved, and nor was one set in a project of a group while another
  project changed or closed. A save that failed, because another program held the file, is now made again
  when the project closes.
- **The per-project file they live in (`<project>.user.hexproj`) keeps what it does not know.** Anything a
  newer HexIDE wrote into it is kept, even when both had the project open at once. It goes with the project
  when the project is saved somewhere else, is not created by a save with nothing to record, and is left
  untouched when it cannot be read rather than overwritten.
- **Saving a VB6 source file no longer changes text you did not edit.** A file whose accented characters
  happened to read as valid UTF-8 was decoded that way and written back changed, so one ASCII edit could alter
  an unrelated comment or string. A file now keeps the encoding it was read in, a UTF-8 byte-order mark
  included. A file an earlier HexIDE saved as UTF-8 still opens as UTF-8. A file the project only carries, such
  as a README, is not covered yet: its editor still rewrites any byte that is not valid UTF-8 (#745).
- **On Windows a source file is read in the machine's own code page, as VB6 reads it.** A file without a
  byte-order mark was shown as Latin-1, so a Western file's curly quotes and euro sign appeared as control
  characters, and a Japanese file on a Japanese machine as mojibake. A file that code page cannot read still
  opens, read byte for byte, and HexIDE now says which files it read that way. Each saves back unchanged. On
  macOS and Linux, which have no such code page, a file is read as Windows-1252, so curly quotes show there too.
- **An edit a file's code page cannot hold is refused, and nothing is written.** Such an edit used to be written
  anyway, in bytes VB6 on that machine reads differently. A character beyond Latin-1 turned the whole file into
  UTF-8, and one within it became its Latin-1 byte. Now a warning names the file, its code page, the
  character, and the line it is in, which matters when the character is in a property or a file name rather than
  the code just typed. A save of several files checks every one before it writes the first, so one such character
  leaves them all as they were. That covers Save Project, Save All, the save offered when a project closes, Make
  Project Group, and writing a new project to its folder. Make refuses before it asks where to put the program. A
  new form or module whose name the code page cannot hold is refused before anything is created.
- **Dismissing a file picker part-way through a save leaves every file as it was.** Saving a project with two
  new files asked where to put each in turn and wrote as it went, so cancelling the second left the first
  written. Every picker an operation needs is now answered before anything is written, Make Project Group's
  included.
- **A form or module that fails to load keeps its line in the project file.** The next Save Project removed the
  line from the `.vbp`, so the file dropped out of the project in VB6 too.
- **A save HexIDE refuses no longer leaves an automation tool waiting.** `set_file_content`,
  `set_control_property` and `add_control` waited for someone to close a dialog. They now reply at once with
  the refusal, and the change stays in the IDE.

## [0.2.0] — 2026-09-24

Most of this release is about not losing work. A save now gives back the file it read, and a form HexIDE
cannot give back is refused rather than quietly damaged. Running real code gets a long way further too,
and a language server that goes wrong can now be seen going wrong.

### Added

- **Frames and PictureBoxes hold controls**, in the designer and at run time, positioned relative to their
  container as in VB6. Forms that nest controls can now be opened, edited and saved.
- **The Image control.**
- **More of the language runs:** `Implements` and interface-typed variables; code without
  `Option Explicit`, with variables created on first use; the `App` object; `IsMissing`, with VB6's rules
  for omitted `Optional` arguments; line numbers as `GoTo` targets; and some 700 built-in constants
  (`vbRed`, `vbKeyReturn` and the rest) with their correct values and types, qualified names included.
- **`Sub Main` as the startup object**, so a code-only project with no forms runs. Project Properties
  offers it.
- **Files a project carries but does not compile** (a README, a data file) are kept byte for byte on save,
  appear in the Project Explorer and open in the code editor. **Project → Add File** now works, and
  double-clicking the project node opens a live overview of its type, startup object and members.
- **Attach any language server by editing `lsp-servers.json`**, with no rebuild, over stdio, WebSocket or a
  named pipe. A document goes to every server that claims its language, including files the project only
  carries. [How to configure one](docs/language-servers.md).
- **A Protocol Inspector** (Tools menu) showing the whole conversation with every language server on one
  timeline, with failures marked and every message viewable, and a connection list saying which servers are
  attached and how far a failed one got. A conversation can be exported with paths and names
  pseudonymised, so it is safe to attach to a bug report.
- **More from language servers:** Go to Declaration (Ctrl+F12), workspace-wide symbol search in the Object
  Browser, code actions, folding for carried files, and servers that only report errors when asked. A
  diagnostic now shows its rule code, its documentation link and which server raised it, and a
  server's messages about itself reach the status bar and the log.
- **Command-line options:** `--help` (also `/?` and `-h`); `--user-data-dir`, which keeps a session's
  settings, layout and server configuration apart from your own; and `--capture-lsp`, which records
  server conversations from the first handshake. [All of them](docs/command-line.md).

### Changed

- **Forms are written exactly the way VB6 writes them.** Of the 22 form and control files VB6 ships in its
  own `Template` folder, 21 now survive open-then-save byte for byte. Project files, standard modules and
  class modules round-trip too.
- **A form HexIDE cannot reproduce opens read-only**, with a banner saying so, instead of being saved
  lossily. That covers a form opened without its `.frx` and binary content HexIDE does not model. The one
  VB6 template still refused, `Web Browser.frm`, keeps its pictures on an ActiveX ImageList. Save As is
  refused before the file picker opens, and a form's `.frm` and `.frx` are written together or not at all.
- **Calling a built-in function HexIDE does not implement yet raises an error**, instead of silently
  returning nothing.
- **The Properties window names enum values the way VB6 does** (`0 - None`), and Enter commits a value.
- **Add-ins can ship their own versions of shared libraries**, instead of getting whichever version the IDE
  happened to load first.

### Fixed

#### Losing work

- **Undo in a freshly opened code window could empty it**, and the next save wrote the module with no
  code. Undo now stops at the code as it was loaded.
- **A UserControl's code could go missing** when it was opened from both the designer and the Project
  Explorer.
- **Saving a form rewrote its size and position**, so it reopened in VB6 with the wrong frame and scale.
- **Make EXE and saving a project to a folder left modules, classes and UserControls behind**, with the
  project file pointing outside the package.
- **Unsaved projects shared one scratch folder**, so a new project could overwrite, or inherit, files from
  an earlier session.
- **A save dropped** menus' submenu structure, controls' membership of their Frame, fonts, `Tag`,
  ListBox and ComboBox `List` data and other `.frx`-backed properties.
- **An untouched project reported itself modified**, because its `.vbp` did not round-trip.

#### Running code

- **In a single-line `If`, statements after a colon ran even when the condition was false.**
- **Starting from a form did not load the project's modules and classes**, so a call into a `.bas`, or
  `New` on a class, failed under F5.
- **Variant arithmetic raised Overflow** where VB6 widens to `Long` or `Double`. Declared `Integer` and
  `Long` still overflow where VB6's do, and arithmetic no longer produces Infinity or NaN.
- **Runaway recursion crashed the IDE.** It now raises error 28, *Out of stack space*.
- **A skipped argument (`Foo 1, , 3`) shifted the ones after it.**
- **A form's menu bar was missing at run time**, `MsgBox` ignored its title, and `OptionButton.Value`
  raised error 461 when set from code.
- Member chains such as `a.b.c.d` stopped after the first dot, doubled quotes in string literals stayed
  doubled, `Select Case` needed identical types, and a variable declared `As` a type did not coerce what it
  was given.
- Colon-separated statements, `Rem` in every form VB6 allows, a label on the same line as a statement, and
  line continuations aligned with tabs were misparsed, some badly enough that the module would not load.

#### Debugging

- **F5 silently did nothing when the startup form could not be built**, and the IDE believed a program was
  running. It now says why.
- End on the runtime-error dialog ends the run, and the dialog offers only the buttons that work. An
  intermittent crash when stepping from a breakpoint is gone.
- Breakpoints and bookmarks follow a document through a rename, a first save and Save As, and two
  projects in a group can each have a `Module1` without their marks colliding.

#### Language servers

- **A new, unsaved project had no language server**, so there were no error underlines, go-to-definition or
  rename until it was saved.
- **A third-party server could silently switch off every language feature**, by describing its features
  with option objects rather than true/false, or by never answering at startup. One failed request no
  longer kills a connection.
- Diagnostics from servers that normalise paths were discarded or shown twice, underlines vanished when a
  code window moved dock or after Make EXE with the real VB6 compiler, and the first module opened never
  folded.
- Servers are shut down as the protocol specifies, so they exit cleanly instead of reporting a crash.
- A pipe name too long for the platform, typically 43 characters on macOS, is refused when the configuration
  is read, with the reason, instead of failing later with an unrelated socket error.

#### Everything else

- Screen readers announce real names for designer controls, New Project templates, Properties rows,
  toolbar buttons, tool windows and Project Explorer nodes, instead of internal type names, and read
  captions without the access-key underscore. Nineteen Standard toolbar buttons gained their tooltips.
- A crash writes its stack trace to the IDE log on every platform, and the log rolls over at its size
  limit instead of stopping.
- Renaming a control to a name VB6 does not accept is refused, instead of producing a `.frm` VB6 cannot
  load.
- On Linux, settings, add-in consent and revocations are stored in your home config folder, not wherever
  HexIDE was launched from.
- Locking the controls on a never-saved form no longer opens Save As.

## [0.1.0] — 2026-08-16

The first versioned build. Before this, nothing in the tree carried a version at all, so a shipped
binary could not be identified.

### Added

- **A cross-platform Visual Basic 6 workbench** — form designer, toolbox, project explorer, properties
  window, menu editor and code editor, running natively on Windows, macOS and Linux.
- **Native VB6 file format support.** Reads and writes `.vbp`, `.vbg`, `.frm`, `.cls`, `.bas`, `.ctl`,
  `.pag` and their binary companions. Content HexIDE does not recognise is carried through verbatim, so
  a project opened here still opens in VB6. Saves are atomic, so a crash cannot truncate your work.
- **A VB6 interpreter** for running code in the IDE — classes, `Property Get`/`Let`/`Set`, `WithEvents`,
  user-defined types, `Enum`, object arrays, error handling, and around eighty intrinsics including the
  full `Format` mask language. Its behaviour is checked against the real 1998 compiler rather than
  against assumptions about it. It is a demonstrator with a deliberate ceiling — see the Scope page.
- **Language tooling** over an out-of-process LSP server: syntax diagnostics, document symbols, and
  keyword and declared-name completion.
- **Thirty languages**, four of them right to left, covering the whole interface.
- **A signed add-in system** with offline verification, per-add-in load contexts and a first-load
  consent gate, plus a bundled AI Chat add-in (bring your own key).
- **A bridge to the real VB6 compiler on Windows**, so Run and Make produce genuine native executables
  when VB6 is installed.

### Fixed

- **Every VB6 option button crashed the IDE on render.** `Classic.Avalonia.Theme 11.3.0.3` is compiled
  against Avalonia 11 and called a `StreamGeometryContext.ArcTo` overload that Avalonia 12 replaced, so
  drawing a radio bevel threw on the render thread and took the process down — in the designer and at
  runtime alike, with nothing in the log.
- **Every VB6 check box rendered ticked**, whatever its value, because the tick's visibility rules
  targeted a part name the template does not use.
- **Dark themes made the code editor unreadable.** Syntax colours were hardcoded for a light background,
  leaving keywords at roughly 1.1:1 contrast on both shipped dark packs.
- **Release builds contained the wrong platforms.** Publishing without a runtime identifier bundled
  native payloads for every platform SkiaSharp ships, so the Windows archive carried Linux binaries and
  the download was six times larger than it needed to be.

[Unreleased]: https://github.com/hexide-io/HexIDE/compare/v0.5.0...HEAD
[0.5.0]: https://github.com/hexide-io/HexIDE/compare/v0.4.0...v0.5.0
[0.4.0]: https://github.com/hexide-io/HexIDE/compare/v0.3.0...v0.4.0
[0.3.0]: https://github.com/hexide-io/HexIDE/compare/v0.2.0...v0.3.0
[0.2.0]: https://github.com/hexide-io/HexIDE/compare/v0.1.0...v0.2.0
[0.1.0]: https://github.com/hexide-io/HexIDE/releases/tag/v0.1.0
