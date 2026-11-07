# Documentation drift audit — #2900

Source baseline: `upstream/main` = `939c75d9` (2026-11-02), identical to this branch's HEAD at audit start.
Only this report is changed. No audited document, product source, test, oracle page or archived change is edited.

## Summary

This first audit slice records **36 findings in 13 documents**. Counts describe checked contradictions, not an estimate of all drift. Repeated occurrences of the same obsolete claim in one document are grouped into one row; the same claim in another document is counted separately.

| Document | Findings |
|---|---:|
| `README.md` | 5 |
| `CONTRIBUTING.md` | 2 |
| `CLAUDE.md` | 6 |
| `docs/MISSING_FEATURES.md` | 5 |
| `docs/MISSING_LANGUAGE.md` | 1 |
| `docs/language-servers.md` | 2 |
| `docs/lsp-server-features.md` | 3 |
| `docs/interpreter-gaps.md` | 6 |
| `docs/debugger-vb6-divergences.md` | 2 |
| `docs/parse-depth-probes.md` | 1 |
| `docs/TEST_PROJECTS.md` | 1 |
| `openspec/specs/language-server/spec.md` | 1 |
| `openspec/specs/theme-packs/spec.md` | 1 |

| Kind | Findings |
|---|---:|
| Wrong feature description | 3 |
| Feature since built | 19 |
| Feature listed as done but missing | 3 |
| Wrong command | 2 |
| Stale count | 5 |
| Wrong path | 2 |
| Renamed or removed thing | 2 |

## Method and bounds

Read the issue brief in full (it contains no later supervisor comment). Read root instructions, then compared the selected current claims below to tracked source, markup, project files and existing tests. Used `rg`, numbered source reads, counts of explicit initializer entries, and a relative Markdown-link target scan. All evidence locations are repository-relative `file:line` at the baseline. “High” means a direct contradictory branch, registration, binding or count; it is not a claim of live verification or of VB6 fidelity.

The requested audit universe is 98 files / 27,630 lines: README, CONTRIBUTING, CLAUDE, 39 top-level docs, and 56 live capability specs. The oracle redirect is counted in that inventory but excluded from inspection, as are all oracle pages. The local-link scan and referenced-C#-filename scan covered the remaining universe. Behaviour tracing focused on the documents with findings below and the prioritized language/debugger/MCP catalogues. This is a coherent first slice, **not an exhaustive semantic audit of all 98 files**. Remaining work is listed below so an unreviewed claim cannot be mistaken for one confirmed by code.

Historical measurements are not drift just because code improved afterward. In particular, the dated matrices in `lsp-parity-matrix.md`, serialization audit histories, performance samples and pre-fix parse-depth results were not counted. Intended requirements can expose code defects: spec rows below identify that distinction and do not recommend weakening a valid requirement to match a bug.

Reproducible count checks: count `new LanguageInfo(` only inside `LanguageManifest.Packs` (39); tracked `IDE/**/*.csproj` (22); `Kw("` / `Builtin("` entries in `VbKeywords.All` (87/42); distinct dictionary initializer keys in `VbSignatures._table` (148). Source presence alone was not used to conclude that an entire feature or every conversion works.

## Findings by document

### README.md

| Document and line | Brief claim | Code and evidence | Kind | Confidence |
|---|---|---|---|---|
| `README.md:14` | “producing a real `.exe` currently shells out” | File → Make exports the standalone interpreter runner; Make with VB6 is a separate command. The same stale claim recurs in the “It is not a compiler” paragraph. `IDE/HexIDE/Projects/ProjectService.cs:1179`; `IDE/HexIDE/Projects/ProjectService.cs:1231` | Wrong feature description | High |
| `README.md:82` | “There is no autosave and no crash recovery.” | Crash recovery starts with the desktop app and captures unsaved work every five seconds. This does not establish autosave to project files. `IDE/HexIDE/App.axaml.cs:91`; `IDE/HexIDE/Recovery/RecoveryService.cs:54`; `IDE/HexIDE/Recovery/RecoveryService.cs:89` | Feature since built | High |
| `README.md:124` | “undeclared variables flagged as warnings” | Bundled-server undeclared-variable diagnostics are compiled out by a false constant. `LspServer/HexIDE.VbLspServer/VbDiagnosticsProvider.cs:20` | Feature listed as done but missing | High |
| `README.md:192` | “cd IDE       && dotnet test HexIDE.Runtime.Tests/” | The pasted build/run block already entered IDE; this line attempts IDE/IDE. Subsequent cd IDE and cd LspServer lines also assume the repository root without returning to it. Checked against tracked project locations; commands were not executed. `IDE/HexIDE.Runtime.Tests/HexIDE.Runtime.Tests.csproj:1`; `LspServer/HexIDE.VbLspServer.Tests/HexIDE.VbLspServer.Tests.csproj:1` | Wrong command | High |
| `README.md:266` | “and completion (docs + icons), inline rename, signature help, more themes, and editor context” | Signature help and an editor context menu already have implemented view handlers/markup; Dark and Abyss are available themes. This roadmap sentence presents the whole set as future polish without distinguishing the implemented pieces. `IDE/HexIDE/Forms/Views/CodeEditorView.axaml.cs:2333`; `IDE/HexIDE/Forms/Views/CodeEditorView.axaml:170`; `IDE/HexIDE/Themes/ThemeService.cs:19` | Feature since built | High |

### CONTRIBUTING.md

| Document and line | Brief claim | Code and evidence | Kind | Confidence |
|---|---|---|---|---|
| `CONTRIBUTING.md:38` | “cd IDE && dotnet run” | In the single pasted setup block the preceding command already entered IDE, so this attempts IDE/IDE. The Tests block has the same cumulative-directory problem after its first line. `IDE/HexIDE.Desktop/HexIDE.Desktop.csproj:1`; `LspServer/HexIDE.VbLspServer.Tests/HexIDE.VbLspServer.Tests.csproj:1` | Wrong command | High |
| `CONTRIBUTING.md:132` | “HexIDE itself makes no network” | Configured revocation refresh performs HTTP GETs; the add-in registry invokes it when RevocationListUrl is set. The default being unset does not support “no network calls of any kind.” `IDE/HexIDE/Addins/RevocationStore.cs:54`; `IDE/HexIDE/Addins/AddinRegistry.cs:82` | Wrong feature description | High |

### CLAUDE.md

| Document and line | Brief claim | Code and evidence | Kind | Confidence |
|---|---|---|---|---|
| `CLAUDE.md:210` | “ships 27 language packs” | LanguageManifest.Packs declares 39 neutral language entries. Counted the new LanguageInfo entries in that initializer, excluding Regions. `IDE/HexIDE/Localization/LanguagePack.cs:51` | Stale count | High |
| `CLAUDE.md:569` | “IDE application (14 projects)” | There are 22 tracked IDE/**/*.csproj files. This includes helper tools and test hosts as well as application libraries; no current reading of “IDE/ projects” explains the stated 14. `IDE/HexIDE.Dap.Parity/HexIDE.Dap.Parity.csproj:1`; `IDE/HexIDE.LspGuard/HexIDE.LspGuard.csproj:1` | Stale count | High |
| `CLAUDE.md:578` | “Package versions are centralized in `IDE/Directory.Build.props`” | NuGet versions are in root Directory.Packages.props. IDE/Directory.Build.props provides target frameworks, app version and global usings, not the cited Avalonia/Dock/Serilog package pins. `Directory.Packages.props:15`; `Directory.Packages.props:42`; `IDE/Directory.Build.props:4` | Wrong path | High |
| `CLAUDE.md:595` | “Diagnostics = collecting ANTLR error listener + `Option Explicit`” | The architecture paragraph includes Option Explicit analysis as part of the diagnostics flow, but it is compiled out. The later boundary section correctly calls it default-off. `LspServer/HexIDE.VbLspServer/VbDiagnosticsProvider.cs:20` | Feature listed as done but missing | High |
| `CLAUDE.md:850` | “**Server handler** in `LspServer/HexIDE.VbLspServer/LspServer.cs`” | The recipe targets a removed file and switch dispatcher; its next step also names the removed BuildInitializeResult. Current handlers are registered in LspServerHost, implementations in LspRequestHandlers, and capabilities in VbServerCapabilities. `LspServer/HexIDE.VbLspServer/LspServerHost.cs:131`; `LspServer/HexIDE.VbLspServer/LspRequestHandlers.cs:14`; `LspServer/HexIDE.VbLspServer/LspServerHost.cs:42` | Renamed or removed thing | High |
| `CLAUDE.md:886` | “vi, zh-Hans, zh-Hant` — **29**” | The supported full-pack list is stale as well as its count: the manifest has 39 entries including en and now includes ca, gl, bg, hr, sk, th, et, sr-Latn, ro, hu, sl, lt, lv and eu; names such as eo/la in this prose are absent from the manifest. Do not use the prose list to decide which packs to translate. `IDE/HexIDE/Localization/LanguagePack.cs:51` | Stale count | High |

### docs/MISSING_FEATURES.md

| Document and line | Brief claim | Code and evidence | Kind | Confidence |
|---|---|---|---|---|
| `docs/MISSING_FEATURES.md:376` | “Tooltips on hover \| Missing” | Toolbox ToggleButtons bind ToolTip.Tip to the component Name. This proves a tooltip is wired, not its live rendering. `IDE/HexIDE/VisualDesigner/Views/ToolBoxToolView.axaml:25` | Feature since built | High |
| `docs/MISSING_FEATURES.md:240` | “signature help across 89 VB6 built-ins” | The signature table contains 148 distinct function keys, served by VbSignatures.Get. Counted dictionary-key initializer lines, not parameter labels or overloads. `LspServer/HexIDE.VbLspServer/VbSignatures.cs:21`; `LspServer/HexIDE.VbLspServer/VbSignatures.cs:353` | Stale count | High |
| `docs/MISSING_FEATURES.md:546` | “Clipboard object \| Missing” | Clear, GetText, SetText and GetFormat(text) have implemented async dispatch; data/non-text members explicitly refuse. `IDE/HexIDE.Runtime/Interpreter/VbClipboard.cs:27`; `IDE/HexIDE.Runtime/Interpreter/VbClipboard.cs:45`; `IDE/HexIDE.Runtime/Interpreter/BasicInterpreter.cs:145` | Feature since built | High |
| `docs/MISSING_FEATURES.md:547` | “Collection object \| Missing” | New Collection and New VBA.Collection construct the implemented VbCollection; it supports item storage and enumeration rather than being missing. `IDE/HexIDE.Runtime/Interpreter/ExpressionExecutor.cs:2465`; `IDE/HexIDE.Runtime/Interpreter/VbCollection.cs:21` | Feature since built | High |
| `docs/MISSING_FEATURES.md:437` | “[TRUST.md](TRUST.md)” | The relative link resolves to docs/TRUST.md, which does not exist. The tracked file is root TRUST.md. `TRUST.md:1` | Wrong path | High |

### docs/MISSING_LANGUAGE.md

| Document and line | Brief claim | Code and evidence | Kind | Confidence |
|---|---|---|---|---|
| `docs/MISSING_LANGUAGE.md:1375` | “LoadPicture retains its existing unimplemented status.” | LoadPicture is registered and its measured single-file BMP/ICO subset is implemented. The earlier LoadPicture row in this same inventory correctly says Partial. This does not imply every control accepts its result. `IDE/HexIDE.Runtime/Interpreter/VB6BuiltIns.cs:190`; `IDE/HexIDE.Runtime/Interpreter/PanelPicture.cs:37` | Feature since built | High |

### docs/language-servers.md

| Document and line | Brief claim | Code and evidence | Kind | Confidence |
|---|---|---|---|---|
| `docs/language-servers.md:131` | “`.cls` the project merely carries goes to every entry” | Carried .cls content is classified on open. A VB6 claimant receives it only when its first nonblank line is VERSION 1.0 CLASS; a LaTeX class no longer reaches the bundled VB6 server just by extension. `IDE/HexIDE.Lsp/LspClientRegistry.cs:698`; `IDE/HexIDE.Lsp/LspClientRegistry.cs:727` | Feature since built | High |
| `docs/language-servers.md:431` | “Workspace-folder changes before imports are tracked in #2364. File-operation routing under #2248 is not” | The registry updates workspace folders before ImportAsync; file-operation routing exists and first saves invoke DidCreateFilesAsync. The ending sentence still describes both landed changes as absent/pending. `IDE/HexIDE.Lsp/LspClientRegistry.cs:958`; `IDE/HexIDE.Lsp/LspClientRegistry.cs:337`; `IDE/HexIDE/Projects/ProjectService.cs:686` | Feature since built | High |

### docs/lsp-server-features.md

| Document and line | Brief claim | Code and evidence | Kind | Confidence |
|---|---|---|---|---|
| `docs/lsp-server-features.md:18` | “The interpreter's expression-walk overflow remains.” | ExpressionExecutor.Visit now checks available execution stack and raises the runtime OutOfStackSpace error; ExpressionDepthGuardTests cover long operator chains. This finding concerns the documented unguarded execution overflow, not a guarantee against every possible stack failure. `IDE/HexIDE.Runtime/Interpreter/ExpressionExecutor.cs:244`; `IDE/HexIDE.Runtime.Tests/ExpressionDepthGuardTests.cs:5` | Feature since built | High |
| `docs/lsp-server-features.md:121` | “The client shows no message for one yet, on hover or anywhere else” | The editor collects diagnostic marker messages at the hovered offset and sets/opens a ToolTip. Rendering still needs a live check. `IDE/HexIDE/Forms/Views/CodeEditorView.axaml.cs:1447`; `IDE/HexIDE/Forms/Views/CodeEditorView.axaml.cs:1462` | Feature since built | High |
| `docs/lsp-server-features.md:325` | “89 VB6 built-ins are covered” | The current signature table has 148 distinct keys. The static completion counts of 87 keywords and 42 built-ins on this page still match VbKeywords and are not findings. `LspServer/HexIDE.VbLspServer/VbSignatures.cs:21`; `LspServer/HexIDE.VbLspServer/VbSignatures.cs:353` | Stale count | High |

### docs/interpreter-gaps.md

| Document and line | Brief claim | Code and evidence | Kind | Confidence |
|---|---|---|---|---|
| `docs/interpreter-gaps.md:23` | “**Numeric line labels** … do **NOT** work” | Numeric labels now parse through lineHead/lineNumber and register as pending jump targets. NumericLineLabelTests cover the runtime path. `IDE/HexIDE.Runtime/Interpreter/Grammar/VB6.g4:186`; `IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:4964`; `IDE/HexIDE.Runtime.Tests/NumericLineLabelTests.cs:22` | Feature since built | High |
| `docs/interpreter-gaps.md:100` | “IIf/Choose/Switch are cheap future adds” | IIf, Choose and Switch are registered implementations; the later “all unregistered” sentence at line 131 is also stale for IIf/Choose and Command (though that paragraph already acknowledges Switch was added). NPV/IRR/MIRR/DDB are not claimed fixed here. `IDE/HexIDE.Runtime/Interpreter/VB6BuiltIns.Choice.cs:42`; `IDE/HexIDE.Runtime/Interpreter/VB6BuiltIns.Choice.cs:55`; `IDE/HexIDE.Runtime/Interpreter/VB6BuiltIns.cs:194`; `IDE/HexIDE.Runtime/Interpreter/VB6BuiltIns.cs:198` | Feature since built | High |
| `docs/interpreter-gaps.md:128` | “A string function given no compare compares as Binary” | Omitted compares are now passed from the current module to the built-ins; InStr/InStrRev/Replace/Split/Filter use that value. The earlier corrections list repeats the stale #1572 residual. `IDE/HexIDE.Runtime/Interpreter/ExpressionExecutor.cs:50`; `IDE/HexIDE.Runtime/Interpreter/VB6BuiltIns.Strings.cs:287`; `IDE/HexIDE.Runtime/Interpreter/VB6BuiltIns.Array.cs:70` | Feature since built | High |
| `docs/interpreter-gaps.md:136` | “Lock, Unlock and Width # throw.” | The three duplicate file-gap bullets contradict each other and source: Lock/Unlock dispatch to file locking; Reset closes all; Width dispatches to the measured file-width operation. Retain their explicit unmeasured subsets, not any whole-member “throws” claim. `IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:2812`; `IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:3808`; `IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:3133`; `IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:4452` | Feature since built | High |
| `docs/interpreter-gaps.md:156` | “keys compared as `vbTextCompare` compares rather than ordinally ignoring case” | Collection.KeyComparer is already VbTextCompare.Comparer, and backs the key dictionary. This finding concerns the remaining key-comparison claim, not every edge of Collection arrays. `IDE/HexIDE.Runtime/Interpreter/VbCollection.cs:25`; `IDE/HexIDE.Runtime/Interpreter/VbCollection.cs:30` | Feature since built | High |
| `docs/interpreter-gaps.md:338` | “`Load`/`Unload` are modelled only for control-array *elements*, not for whole *forms*” | The residual control-array paragraph still says Load Form2 is unimplemented. VisitLoadStmt handles a form instance through interpreter.Forms.Load; it refuses only when the host has no forms service. The earlier Load/Unload paragraph already describes the landed implementation. `IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:2763`; `IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:2773` | Feature since built | High |

### docs/debugger-vb6-divergences.md

| Document and line | Brief claim | Code and evidence | Kind | Confidence |
|---|---|---|---|---|
| `docs/debugger-vb6-divergences.md:186` | “locals appear **only once their `Dim` statement has executed**” | Procedure entry calls HoistDeclaredLocals before the body or declaration breakpoint. That method allocates every collected local, including ones in untaken branches. D9's lazy-allocation explanation is obsolete. `IDE/HexIDE.Runtime/Interpreter/BasicInterpreter.cs:1499`; `IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:4308` | Feature since built | High |
| `docs/debugger-vb6-divergences.md:190` | “object- or UDT-typed array labels its header as `Variant()`” | DebugInspector.ArrayType formats the element type and bounds, and ElementOf recovers the declared class. LocalsTypeTests exercise class/object arrays. The later D30 section already describes the new type column; D9 still gives the previous behaviour. `IDE/HexIDE.Runtime/Debugging/DebugInspector.cs:334`; `IDE/HexIDE.Runtime/Debugging/DebugInspector.cs:342`; `IDE/HexIDE.Runtime.Tests/LocalsTypeTests.cs:90` | Feature since built | High |

### docs/parse-depth-probes.md

| Document and line | Brief claim | Code and evidence | Kind | Confidence |
|---|---|---|---|---|
| `docs/parse-depth-probes.md:121` | “the interpreter execution overflow remains outstanding.” | The dated pre-fix matrix is historical evidence and should stay. This current trailing status is stale: the expression visitor now guards execution stack and ExpressionDepthGuardTests check catchable failure. `IDE/HexIDE.Runtime/Interpreter/ExpressionExecutor.cs:244`; `IDE/HexIDE.Runtime.Tests/ExpressionDepthGuardTests.cs:5` | Feature since built | High |

### docs/TEST_PROJECTS.md

| Document and line | Brief claim | Code and evidence | Kind | Confidence |
|---|---|---|---|---|
| `docs/TEST_PROJECTS.md:9` | “see `RoundTripTests.cs`” | No tracked source file has this name. The corpus runner is SerializationCorpusTests, alongside SerializationRoundTripTests for inline regression cases. `IDE/HexIDE.Runtime.Tests/SerializationCorpusTests.cs:28`; `IDE/HexIDE.Runtime.Tests/SerializationRoundTripTests.cs:10` | Renamed or removed thing | High |

### openspec/specs/language-server/spec.md

| Document and line | Brief claim | Code and evidence | Kind | Confidence |
|---|---|---|---|---|
| `openspec/specs/language-server/spec.md:130` | “The server SHALL parse a changed document once” | The shared-analysis design is still implemented, but an unconditional one-parse limit is no longer accurate: ParseSource can retry with last conditional branches, and ParseApplied reparses after blanking protected regions. Later requirements on this same spec require header recovery. Reconcile the earlier wording with those deliberate recovery passes rather than deleting recovery. `LspServer/HexIDE.VbLspServer/VbDiagnosticsProvider.cs:258`; `LspServer/HexIDE.VbLspServer/VbDiagnosticsProvider.cs:288` | Wrong feature description | High |

### openspec/specs/theme-packs/spec.md

| Document and line | Brief claim | Code and evidence | Kind | Confidence |
|---|---|---|---|---|
| `openspec/specs/theme-packs/spec.md:16` | “adding one SHALL require no code change and” | Available themes are a hard-coded three-name list; Apply reads embedded avares resources. Dropping a new file beside a running build cannot make a selectable theme. This is a contract/implementation mismatch: review whether the requirement remains intended before changing its text. `IDE/HexIDE/Themes/ThemeService.cs:19`; `IDE/HexIDE/Themes/ThemeService.cs:47`; `IDE/HexIDE/HexIDE.csproj:54` | Feature listed as done but missing | High |

## Checks with no finding in the examined subset

- `docs/command-line.md`: compared its options with `ServerOptions.Options`, the Debug guards, positional-extension recognition and precedence. The documented ten named options and four Debug-only flags match. This was source inspection, not a GUI launch or a terminal-colour check.
- `docs/lsp-client.md`: traced the carried-class gate, file-operation notifications, workspace-folder-before-import sequence and current completion/signature paths. Its descriptions of the first three agree with source; the corresponding obsolete descriptions found in `language-servers.md` are listed above. The protocol table's 93-message completeness and every entry's depth were not independently re-proven.
- `docs/debugger-vb6-divergences.md`: the Locals allocation and array-type paths produced the two findings above. Other current/resolved headings were read but not fully traced. The VB6 side of every comparison remains measurement-owned; no live check was performed.
- `docs/mcp-server-gaps.md`: relative numbered-entry links resolve, and it points to the current per-file open/closed layout. Historical connection drops and the prose's first-contact usefulness cannot be re-measured by reading the server.
- The static completion initializer contains the documented 87 keywords / 42 built-ins. The 148 signature keys are a separate table and do not imply 148 runtime implementations.
- Local Markdown-link file targets exist throughout the scanned scope except the `docs/MISSING_FEATURES.md` TRUST link recorded above. Heading anchors were not validated. Removed C# filenames in the explicitly historical parity matrix were excluded; the current corpus-harness pointer is recorded above. The removed test filename cited in the grammar-fixes document occurs in its dated investigation history and is not counted as a current code claim.

## Not checkable from code

These are excluded from finding counts, rather than treated as true or false:

- Actual rendering, pointer behaviour, tooltip placement and readability: README's screenshot, designer/theme claims, diagnostic hover and toolbox tips. A live IDE check should hover a malformed-code squiggle and a toolbox item, open the editor context menu and signature help, and switch Classic/Dark/Abyss while a designer is open. Source establishes wiring only.
- The measured VB6 half of the debugger, serialization and language catalogues; template-corpus byte equality on the measuring machine; native versus p-code results. No new VB6 measurements were taken and no oracle document was inspected or changed.
- MCP client reconnection, session schema caching and whether first-contact descriptions help a fresh caller. These need the actual client/server session, which this run was instructed not to use.
- Published release packaging, macOS signing/quarantine, operating-system/CI image observations, installed prerequisites, GitHub labels and externally enforced branch protection. Workflow code can show intent, not the state of those services or machines.
- Historic performance and financial precision measurements. Source can establish the implemented algorithm, not reproduce the recorded timing or real-VB6 output without the original probe environment.

## Remaining

This report is ready as a starting list for the docs session. The larger audit remains open:

1. Trace every remaining behavioural row in `MISSING_FEATURES.md`, `MISSING_LANGUAGE.md`, `interpreter-gaps.md`, the debugger catalogue and both LSP documents; a summary or count guard is not a semantic proof. In particular, runtime conversions, control-specific members, protocol capability depth and error ordering were not exhaustively checked here.
2. Audit the other top-level docs beyond local link/file references and historical/current framing, including the full save-writer inventory, DAP, profiles, localization, installer and serialization contracts.
3. Trace the remaining 54 live capability specs requirement by requirement, distinguishing an obsolete design record from a valid intended requirement whose implementation is defective. The two spec findings above need that review before a documentation edit.
4. Perform the live/external checks above with the supervisor's environment. No measured VB6 fact should be inferred from HexIDE's own output.

No changelog fragment is added: this run's explicit docs-only scope says to change only the named report, and the issue says to edit no other document. No OpenSpec change/delta is created, so there is no change-id to validate or archive, no requirement/scenario edit and no empty delta to delete. `MISSING_LANGUAGE.md` and its counts are unchanged. No tests are added and the new-test teeth count is zero (not applicable to a report-only change).

## Validation results

- `HEXIDE_FOREIGN_LSP_DOWNLOAD=0 pwsh scripts/test.ps1 IDE/HexIDE.Tests/ -- --filter-class "*DesignRecordTests" --filter-class "*ChangelogFragmentTests"`: **9 passed, 0 failed, 0 skipped**; the script's filtered-run floor passed. `DOTNET_CLI_HOME` was directed to this clone's ignored `artifacts/dotnet-home` because the sandbox cannot write the normal first-run sentinel. No skipped test is cited as coverage. These existing guards validate design-record/changelog structure, not the truth of this report's findings; no new tests or before/after teeth are claimed.
- OpenSpec CLI **1.14.3** works here. There is no change-id for this report-only task, so `validate <change-id> --type change --strict` is inapplicable. The broader `openspec validate --specs --strict` returned **15 passed, 41 failed (56 specs), exit 1**, reporting existing requirement-length warnings over 500 characters. Live specs were not changed to silence these warnings.
- A separate text check of all **700 live requirements** found **0** missing a SHALL/MUST keyword in the body or a `#### Scenario:`. This is a structural check only.
- `git diff --check` passed. No tracked file other than the new report is changed. Final `bash scripts/check-tree-hygiene.sh`: **passed, 10 nonblocking warnings** (identity scan skipped because HEXIDE_IDENTITY_PATTERN is unset). An intermediate scan caught an unnecessary historical third-party filename quotation in this report; it was removed before the passing rerun.
