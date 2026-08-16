# Adversarial bug-hunt backlog (2026-08-09)

**37 adversarially-verified defects** surfaced by a 64-agent whole-codebase bug-hunt (launch-hardening sweep).
Every finding was **independently repro-confirmed by a second-pass refuter** — an agent whose job was to *disprove*
the claim; only findings that survived refutation appear here. None of these are caused by the debugger v2·P5 work;
they are pre-existing.

**Severity tally:** 7 HIGH (6 original + 1 found during fixing) · 19 MEDIUM · 12 LOW. Listed high → low below (each block is self-labelled).

**Progress (2026-08-09):** all **6 original HIGH fixed** — the For…Next trio (`63b4277`), control-property coercion
(`f8bb796`), `Format()` overflow (`cc18bc6`), and the LSP symbol-visitor depth guard. One **new HIGH** surfaced
while fixing the last (the ANTLR parser has no depth limit); it is now **also fixed** (2026-08-10, `ParseDepthGuard`
at every parse site) — so **all 7 HIGH are fixed**. **5 of the 19 MEDIUM fixed** —
the error-fidelity cluster (inverted array bounds, `UBound`/`LBound` bad dimension, `CDate` overflow → trappable
Err 9/6; `VBColor.TryParse` short-value crash), the `Dim`-in-loop re-initialisation bug, `Call obj.Method(args)`
dispatch, the `Set` slot-vs-release ordering, and the **designer batch** (duplicate control names, multi-select
Delete, undo z-order scramble, and the `LanguageChanged` leak in both the designer and code-editor VMs), and the
**LSP batch** (ElseIf double-indent, Go-To-Definition on a Property, Rename corrupting strings/comments), and the
final scattered three (add-in verification crashing startup, `CheckBox.Value` stale + Click-on-uncheck, stdio LSP
transport unguarded start). **ALL 19 MEDIUM FIXED.**

**Progress (2026-08-10):** the **LOW tier is triaged and its genuine crashes cleared** (user directive: *"the genuine
crashes only, skip the intentional walls"*). **8 of 12 LOW fixed** — the **serialization group** (`.vbp` lone-quote
Substring underflow, `.frx` length integer-overflow, `.frm`/`.ctl` non-numeric font-metric crash — `87f7c46`), the
**interpreter error-fidelity group** (undimensioned/wrong-dim array access → trappable Err 9, `DateAdd` overflow →
Err 5, `TimeSerial` overflow → Err 6 — oracle-verified *distinct* codes, long fractional Format mask → zero-padded
not crashed), and the **interop/MCP group** (`FromObject` long/Date/etc. property reads, `AddItem` arity → Err 449,
`set_control_property` OverflowException). **4 LOW intentionally SKIPPED** as out-of-scope: the ExecutionState
per-call slot leak (resource-lifetime design task, not a crash), the unbounded-recursion → Error-28 *wall*
(deliberate parked feature), and the Find/Replace state-persistence *wrong-logic* item (non-crash). **The full
adversarial backlog is now cleared to the genuine-crash line.**

**Update (2026-08-10, later):** the one remaining open HIGH — the ANTLR parser's missing depth limit — is now also
**fixed** (`ParseDepthGuard` at every parse site, threshold derived from measured parser behaviour + oracle-checked
against vb6.exe). **No open crash items remain** across HIGH/MEDIUM/LOW; the only untouched items are the 3
by-design skips (recursion wall, slot-leak lifetime task, Find/Replace wrong-logic).

## How to read this list

- **HIGH** — a user hits a crash, a hang, or plainly wrong output on common input. The **For…Next trio** (the first
  three) are launch-priority: `For i = 1 To 50000`, `For i = 0 To 10 Step 3`, and an empty loop body are about as
  ordinary as VB6 gets, and one hard-freezes the IDE.
- **MEDIUM** — real defects on less-common paths, or fidelity gaps where the interpreter throws a raw, *untrappable*
  .NET exception (`OverflowException`, `ArgumentOutOfRangeException`) where VB6 raises a **trappable** error (usually
  Error 9 / Error 6) that `On Error` should catch. These are natural fodder for the oracle-driven interpreter pass.
- **LOW** — malformed-file crashes (`.vbp`/`.frx`/`.frm`), edge intrinsics, and a few items that are the interpreter's
  **deliberate boundary**, not bugs (e.g. unbounded VB6 recursion → a process crash *should* instead be the faithful
  Error 28 "Out of stack space"; see [[artificial-vs-real-limits]] — the fix is the error, not a cap).

**Fidelity note:** before pinning any expected result for a fix here, verify against real `vb6.exe`
([`docs/vb6-fidelity-oracle.md`](vb6-fidelity-oracle.md)) — the oracle overturns assumptions.

---

### HIGH — `IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:471`  ·  infinite-loop
**For…Next uses `i != toInt` instead of `<=`/`>=`, hanging when Step overshoots the bound**

- *Repro:* Run `For i = 0 To 10 Step 3` / `Debug.Print i` / `Next`. VB6 prints 0,3,6,9 and stops (12>10). HexIDE: i becomes 0,3,6,9,12,15,… and never equals 10, so the loop never terminates — the running program (and the IDE UI thread) freezes indefinitely.
- *Subsystem:* interpreter-core
- **✅ FIXED** (2026-08-09) — `<=`/`>=` termination; oracle-pinned; regression-guarded in `ForLoopFixTests`.

### HIGH — `IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:473`  ·  null-reference
**For…Next / Do loops NRE on an empty loop body (block is optional in the grammar but not null-guarded)**

- *Repro:* Run an empty delay loop: `For i = 1 To 5` immediately followed by `Next` (no statements between). `context.block()` is null → `await Visit(block)` throws NullReferenceException. Same for `Do While x < 10` / `Loop` with an empty body. VB6 executes these fine.
- *Subsystem:* interpreter-core
- **✅ FIXED** (2026-08-09) — null-guarded in ForNext + all three Do loops (matching ForEach). Empty-For regression-guarded; empty-Do is a degenerate infinite loop (untestable in isolation, guard verified by inspection).

### HIGH — `IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:458`  ·  correctness
**For…Next rejects Long bounds — `For i = 1 To 50000` throws a spurious type-mismatch**

- *Repro:* Run `For i = 1 To 50000` / `Debug.Print i` / `Next`. The literal 50000 is a Long, so TryUnpack<int>(to) fails and the loop throws a runtime 'from/to/step is not an integer' error before executing. VB6 runs the loop normally. Also breaks `For i = 1 To n` where n is a Long variable.
- *Subsystem:* interpreter-core
- **✅ FIXED** (2026-08-09) — bounds read through the double numeric rung; counter typed by magnitude (oracle: 50001→Long). *Remaining gap:* fractional counters/steps (`Step 0.5`) still throw (documented) — a follow-up, not a hang.

### HIGH — `IDE/HexIDE.Runtime/Interpreter/VB6BuiltIns.Format.cs:393`  ·  unhandled-exception
**Format() of any Double outside decimal range throws an uncatchable OverflowException**

- *Repro:* Debug.Print Format(1E30)  -> OverflowException (decimal cast), program crashes; not trappable even with `On Error Resume Next`. Equally: Format(1E300, "Scientific") and Format(1.5E30, "0.00").
- *Subsystem:* builtins-format
- **✅ FIXED** (2026-08-09) — `OutOfDecimalRangeNumeric` guards all four `NumToDecimal` call paths; out-of-range/non-finite values render in scientific notation (oracle-pinned for General/Scientific in `vb6-fidelity-oracle.md`). Regression-guarded in `FormatNumericOverflowTests`. *Divergence:* a fixed mask (`0.00`) of such a value is approximated as scientific vs VB6's full expansion (documented).

### HIGH — `IDE/HexIDE.Runtime/AvaloniaInterop/AvaloniaInteroperability.cs:71`  ·  type-coercion
**Assigning an Integer to a double-typed control property (Left/Top/Width/Height) throws and is silently swallowed**

- *Repro:* A form has Command1. Code runs `Command1.Left = 100` (or `Command1.Width = 3000`, `Me.Height = 4000`, or `Command1.Top = X` where X is a Single). The RHS is a boxed int/long/float; SetUntyped throws 'Invalid value type for setting Left, got System.Int32 expected System.Double'; the exception is logged and swallowed; the control does not move or resize and the user sees no error. Runtime control positioning/resizing is effectively non-functional for the ubiquitous integer-literal case.
- *Subsystem:* form-runtime-controls
- **✅ FIXED** (2026-08-09) — `SetUntyped` re-boxes across numeric CLR types before its exact `is TProperty` check. Regression-guarded in `ControlPropertyCoercionTests` (Integer/Long/Single → Double property).

### HIGH — `LspServer/HexIDE.VbLspServer/VbSymbolProvider.cs:33`  ·  unhandled-crash
**SymbolVisitor lacks the mandated depth guard and runs unbudgeted on the shared worker thread**

- *Repro:* Same degenerate-tree input as the DeclarationCollectorVisitor case: parse succeeds, DocumentStore calls VbSymbolProvider.GetSymbols(tree) on the worker thread, the unguarded recursive traversal overflows the stack -> StackOverflowException -> LSP server process terminates -> document-symbol/definition/procedure-dropdown and all diagnostics stop working until the server is restarted.
- *Subsystem:* lsp-server
- **✅ FIXED** (2026-08-09) — added the mandated `Visit(IParseTree)` depth guard (MaxDepth=500, verbatim from `VbScopeAnalyzer`). Benign-on-real-code covered by `ComponentPortTests.Symbols_extracted_for_each_declaration_kind`. *Superseded concern:* see the new parser-depth finding below — the ANTLR parser overflows on deep nesting **before** the visitor, so this guard is defense-in-depth.

### HIGH — `LspServer/HexIDE.VbLspServer/VbSymbolProvider.cs:14`  ·  unhandled-crash *(NEW — found 2026-08-09 while fixing the above)*
**The ANTLR parser has no depth limit — deeply-nested input stack-overflows `valueStmt` recursion (uncatchable), killing the LSP server**

- *Repro:* Feed ~600 nested parentheses in an expression (`r = ((((…1…))))`, a small string well under the 400k size guard and fast enough to dodge the wall-clock backstop). `VisualBasic6Parser.valueStmt` recurses per nesting level and overflows the C# stack — a StackOverflowException is UNCATCHABLE, so neither the wall-clock backstop (it runs the parse on a background thread, but a StackOverflow crashes the whole process regardless of thread) nor the new visitor guard prevents it. Affects both `VbDiagnosticsProvider` and `VbSymbolProvider`.
- *Subsystem:* lsp-server
- *Notes:* far beyond VB6's published nesting maximums (degenerate/malicious input, not real code). Hard to fix cleanly — ANTLR generates recursive-descent; a depth limit needs a parse-listener/interceptor or a pre-parse nesting-depth scan that rejects pathological input before parsing. Low priority (degenerate input) but a genuine remotely-triggerable crash if the LSP ever serves untrusted content.
- **✅ FIXED** (2026-08-10) — a `ParseDepthGuard : IParseTreeListener` aborts the parse at a rule-depth of **300** with a *catchable* exception, applied at every parse site (interpreter: `BasicInterpreter.Parse`/`ParseValueStmt`, `SyntaxChecker`; LSP: `VbDiagnosticsProvider.ParseSource` both SLL+LL stages, `VbSymbolProvider`). Interpreter surfaces it as a `VBCompileErrorException`; the LSP as a "nesting too deep; diagnostics paused" info diagnostic (tree → null). **Threshold derivation** (measured, not guessed): each nesting level ≈ 1 rule frame, a deliberately deep *real* procedure peaks at rule-depth ~50, the stack overflows near ~600 — so 300 sits ~6× above real code and ~2× under the overflow. The listener was proven to fire cleanly at 301 (no crash) on inputs up to **4096** parens under both SLL and LL prediction, so the ATN-predictor-overflow concern doesn't apply. **Divergence (documented):** vb6.exe itself compiles 4096-deep nesting (its parser is effectively unbounded); a recursive-descent parser can't match that on a fixed stack, so HexIDE rejects absurd nesting with a clean error rather than crash — recorded in `docs/interpreter-gaps.md` + `docs/vb6-fidelity-oracle.md`. Regressions: `ParserDepthGuardTests` (interpreter, parens + blocks) and `ParseHardeningTests` (LSP, parens + blocks + symbols).

### MEDIUM — `IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:1211`  ·  wrong-logic
**Dim inside a loop re-initializes the variable every iteration (silent wrong results)**

- *Repro:* `For i = 1 To 3` / `Dim total As Integer` / `total = total + i` / `Debug.Print total` / `Next`. VB6 prints 1, 3, 6 (total persists). HexIDE prints 1, 2, 3 because `Dim total` resets total to 0 each iteration.
- *Subsystem:* interpreter-core
- **✅ FIXED** (2026-08-09) — a per-activation `declaredLocals` set makes a re-executed `Dim` a no-op (allocates once, keeps its value); the first `Dim` still rebinds to a fresh slot so it shadows a module var. Oracle-pinned (1,3,6); regression + shadowing guards in `DimSemanticsTests`.

### MEDIUM — `IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:396`  ·  correctness
**`Call obj.Method(args)` on a user class instance throws "Unknown method" instead of dispatching**

- *Repro:* With a class instance `myObj` exposing `Public Sub DoThing(n)`, the statement `Call myObj.DoThing(5)` throws a runtime 'Unknown method DoThing' error, while `myObj.DoThing 5` runs correctly.
- *Subsystem:* interpreter-core
- **✅ FIXED** (2026-08-09) — the explicit-`Call` member handler (`VisitECS_MemberProcedureCall`) now has the same `ValueType.Object` dispatch branch the bare-call handler already had. Regression-guarded in `ClassMethodTests`.

### MEDIUM — `IDE/HexIDE.Runtime/Interpreter/Vb6Value.cs:326`  ·  unhandled-exception
**Inverted/negative array bounds throw raw OverflowException instead of VB6 error 9**

- *Repro:* User runs `Dim a(-2)` (bounds (0,-2) -> size -1) or `Dim a(2 To 0)` (size -1), or `ReDim a(hi To lo)` where the evaluated hi < lo-1. VBArray.CreateArray executes `new object[-1]` -> unhandled System.OverflowException propagates out of Execute(), crashing the run rather than surfacing trappable error 9.
- *Subsystem:* value-model-refcount
- **✅ FIXED** (2026-08-09) — `CreateArray` guards a negative element count → trappable `SubscriptOutOfRange` (Err 9, oracle-pinned). Empty (size 0) still valid. Regression-guarded in `ErrorFidelityTests`.

### MEDIUM — `IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:1034`  ·  wrong-logic
**Set updates the slot AFTER releasing the old reference, so Class_Terminate observes/clobbers the stale slot**

- *Repro:* `Dim g As CThing` ; `Set g = New CThing` (g->A) ; `Set g = New CThing` (should give g->B then terminate A). On the second Set, ReleaseRef(A) fires A.Class_Terminate while g still points at A: a `Class_Terminate` that reads `g Is Nothing` sees False and reads the dying A instead of B (wrong result). If `Class_Terminate` does `Set g = SomethingElse`, that object is AddRef'd, stored, then immediately clobbered by the outer `TryUpdateVariable(g, B)` at line 1034 -> the SomethingElse object leaks (never terminated) and the reassignment is lost.
- *Subsystem:* value-model-refcount
- **✅ FIXED** (2026-08-10) — all three Set paths (variable / object-field / array-element) now write the slot BEFORE `ReleaseRef` of the old occupant (oracle-verified `0;N;` — a terminate sees the new value). *Caveat:* defense-in-depth in the current interpreter — a `Class_Terminate` can't yet read an outer-scope slot (cross-module global from a class is unsupported), so the scenario isn't reachable to unit-test; the ordering is now correct. Existing `Reassign_NewInitializesBeforeOldTerminates` confirms no timing regression.

### MEDIUM — `IDE/HexIDE.Runtime/Interpreter/VB6BuiltIns.Conversion.cs:70`  ·  unhandled-exception
**CDate() of an out-of-range numeric throws a raw ArgumentException (uncatchable), unlike AsDate which wraps it**

- *Repro:* CDate(1000000000)  -> ArgumentException from FromOADate (1e9 exceeds the max OA date ~2958466); program crashes, not trappable by On Error. Same for CDate(-1E9).
- *Subsystem:* builtins-format
- **✅ FIXED** (2026-08-09) — CDate wraps FromOADate → trappable `Overflow` (Err 6, oracle-verified — NB the oracle says **6**, not AsDate's Err 5). Regression-guarded in `ErrorFidelityTests`.

### MEDIUM — `IDE/HexIDE.Runtime/Interpreter/VB6BuiltIns.Array.cs:29`  ·  index-error
**UBound/LBound with a dimension < 1 or > rank throws ArgumentOutOfRangeException instead of VB error 9**

- *Repro:* Dim a(1 To 5): x = UBound(a, 3)  -> ArgumentOutOfRangeException (bounds[2] on a rank-1 array). Also UBound(a, 0) -> bounds[-1] throws. Neither is catchable by On Error.
- *Subsystem:* builtins-format
- **✅ FIXED** (2026-08-09) — `Bound` validates `1 <= dimension <= Rank` → trappable `SubscriptOutOfRange` (Err 9, oracle-verified). Regression-guarded in `ErrorFidelityTests`.

### MEDIUM — `IDE/HexIDE.Core/BuiltinTypes/VBColor.cs:44`  ·  malformed-input-crash
**VBColor.TryParse throws ArgumentOutOfRangeException on short "&H…" values, aborting the whole form load**

- *Repro:* A user opens a VB6 project whose Form1.frm contains a property value beginning with a short hex literal whose first byte is 0x00 or 0x80, e.g. `MaskColor = &H80&` or a hand-edited/third-party `Foo = &H00`. ParseValue calls VBColor.TryParse which throws ArgumentOutOfRangeException; the entire form fails to load and silently disappears from the project (only a generic parse error is logged), instead of the single value being preserved verbatim.
- *Subsystem:* serialization
- **✅ FIXED** (2026-08-09) — `TryParse` length-guards before the fixed slices → returns false (never throws) for short input. Regression-guarded in `VBColorTests`.

### MEDIUM — `IDE/HexIDE.Runtime/BuiltinControls/VBCheckBox.cs:28`  ·  wrong-logic
**CheckBox.Value is not updated on user interaction (stale reads) and Click is suppressed when unchecking**

- *Repro:* User ticks Check1 at runtime, then code (or the same Click handler) evaluates `If Check1.Value = 1 Then ...` — it reads the stale value and takes the wrong branch. Also: clicking an already-checked box to uncheck it fires no Click event at all, so uncheck logic never runs.
- *Subsystem:* form-runtime-controls
- **✅ FIXED** (2026-08-10) — `OnClick` now mirrors the new IsChecked into `Value` (closing the one-way ValueProperty→IsChecked sync) and fires Click on EVERY click, not only when it becomes checked. Regression-guarded in `VBCheckBoxTests` (Value syncs on check AND uncheck).

### MEDIUM — `IDE/HexIDE.Lsp/Transports/StdioProcessLspTransport.cs:107`  ·  unhandled-exception
**Unguarded _process.Start() escapes as an unobserved exception and leaves IsAlive throwing on every subsequent access**

- *Repro:* On a Linux/macOS build where the LSP apphost is shipped without +x and the sibling .dll is absent (so the dll fallback is skipped and the apphost is launched directly), or on Windows where the found server exe is AV-quarantined: Process.Start throws Win32Exception (EACCES/'permission denied'). StartAsync's task faults unobserved (LSP silently dead), and `_process` stays set. The user then opens any form/module in the code editor; CodeEditorViewModel.Initialize evaluates `lspClient.IsRunning`, which calls the throwing IsAlive getter → InvalidOperationException on the UI thread → the editor-open operation (or the IDE) crashes, and it recurs on every editor open.
- *Subsystem:* lsp-client
- **✅ FIXED** (2026-08-10) — `_process.Start()` is guarded; on failure it logs, tears down the never-started Process (so `IsAlive` returns false, never throws), and returns a null handler — LSP features degrade off instead of crashing every editor open. (No unit test — HexIDE.Lsp has no test project; the guard is a small, obviously-correct safety net.)

### MEDIUM — `LspServer/HexIDE.VbLspServer/VbFormatter.cs:74`  ·  wrong-logic
**'ElseIf ... Then' is counted as both a mid-block and an opener, double-indenting its body**

- *Repro:* Format the well-indented input:\\nIf a Then\\n    x = 1\\nElseIf b Then\\n    y = 2\\nEnd If\\nOutput becomes:\\nIf a Then\\n    x = 1\\nElseIf b Then\\n        y = 2\\n    End If\\n— the ElseIf body is indented 8 spaces instead of 4 and 'End If' is indented to 4 instead of 0. Each additional ElseIf adds a further level.
- *Subsystem:* lsp-server
- **✅ FIXED** (2026-08-10) — `RxOpenerIf` no longer matches `ElseIf` (dropped the `(else)?`); ElseIf is handled solely by the mid-block path (dedent then re-indent), so its body no longer gets a second indent level. Regression-guarded in `VbFormatterTests.Format_HandlesElseIf`.

### MEDIUM — `LspServer/HexIDE.VbLspServer/LspRequestHandlers.cs:187`  ·  wrong-logic
**Go-To-Definition never resolves a Property because symbol names carry a display suffix**

- *Repro:* Module contains 'Public Property Get Total() As Long'. User invokes Go-To-Definition on a 'Total' reference (or on the declaration itself). Definition() compares 'Total (Get)' to 'Total' -> no match -> returns null -> navigation silently does nothing, even though the property is a known symbol.
- *Subsystem:* lsp-server
- **✅ FIXED** (2026-08-10) — Definition matches (and sizes the range) on `BaseSymbolName` — the bare identifier before the " (Get)"/"(Let)"/"(Set)" display suffix. Regression-guarded in `WireContractTests.Definition_resolves_a_Property_despite_its_display_suffix`.

### MEDIUM — `LspServer/HexIDE.VbLspServer/LspRequestHandlers.cs:248`  ·  wrong-logic
**Rename rewrites whole-word matches inside string literals and comments, corrupting source**

- *Repro:* Module has: Text = "Enter Text here"  ' set the Text\\nUser renames the variable 'Text' to 'Caption'. FindAllOccurrences matches the code token AND the whole word 'Text' inside the string literal and the comment, producing: Caption = "Enter Caption here"  ' set the Caption — the string literal shown to the end user and the comment are silently altered.
- *Subsystem:* lsp-server
- **✅ FIXED** (2026-08-10) — `FindAllOccurrences` now skips any match inside a string literal or a ' comment (per-line quote/comment scan, `""` treated as an escaped quote). Fixes rename AND document-highlight. Regression-guarded in `WireContractTests.Rename_skips_string_literals_and_comments`.

### MEDIUM — `IDE/HexIDE/VisualDesigner/ViewModels/FormEditViewModel.cs:95`  ·  resource-leak
**FormEditViewModel leaks for the app lifetime via un-disposed LanguageChanged subscription**

- *Repro:* Open a Form (or UserControl) designer tab and close it, then repeat many times during a session (common when browsing forms). Each closed designer's FormEditViewModel plus its whole component/undo graph stays reachable from ILocalizationService.LanguageChanged and is never garbage-collected, so memory grows without bound across the session.
- *Subsystem:* visual-designer
- **✅ FIXED** (2026-08-10) — the `LanguageChanged` handler is now stored and unsubscribed on Dispose (tab close) via `AutoDispose(new ActionDisposable(...))`, like every other subscription in the VM. (Same fix applied to `CodeEditorViewModel:132` — the identical leak.)

### MEDIUM — `IDE/HexIDE/VisualDesigner/ViewModels/FormEditViewModel.cs:207`  ·  correctness
**SpawnControlAt can create two controls with the same Name (duplicate identifiers)**

- *Repro:* On a blank form: add a CommandButton (named Command0), add another (Command1); Components.Count is now 2. Delete the first (Command0); Components.Count is back to 1. Add another CommandButton -> its name is computed as Command1, colliding with the existing Command1. The form now has two controls both named 'Command1'.
- *Subsystem:* visual-designer
- **✅ FIXED** (2026-08-10) — names use the LOWEST unused index (scan existing names), never a stale `Components.Count`; a freed name is reused, no collision. Live-confirmed sequential naming; delete-then-add path is code-reviewed (MCP can't drive designer delete — see mcp-server-gaps #5).

### MEDIUM — `IDE/HexIDE/VisualDesigner/ViewModels/FormEditViewModel.cs:532`  ·  correctness
**Delete key / context-menu Delete removes only the primary of a multi-selection**

- *Repro:* Rubber-band-select three buttons on the form and press Delete. Only the primary button is removed; the other two remain on the form. A subsequent single Undo restores only that one control, diverging from VB6 (which deletes all selected controls as one undoable action).
- *Subsystem:* visual-designer
- **✅ FIXED** (2026-08-10) — DeleteSelected now deletes the whole selection as one undoable action, mirroring the proven CutSelectedControls path.

### MEDIUM — `IDE/HexIDE/VisualDesigner/Commands/RemoveControlsCommand.cs:25`  ·  undo-corruption
**Undo of a multi-control Cut/Delete can silently scramble control z-order**

- *Repro:* Form control z-order is [X, B, Y, A, Z] (B at index 1, A at index 3). Ctrl-click A then B (selection order A,B, so entries are [(A,3),(B,1)]), Cut them, then Undo. Undo inserts A at index 3 into [X,Y,Z] -> [X,Y,Z,A], then B at index 1 -> [X,B,Y,Z,A]. A and Z are now swapped in paint/tab order versus the original [X,B,Y,A,Z], with no error shown, and the scrambled order also persists to AllComponents/serialization.
- *Subsystem:* visual-designer
- **✅ FIXED** (2026-08-10) — RemoveControlsCommand.Undo re-inserts in ASCENDING original-index order (per list), so each stored index is valid against the progressively-restored list — original z-order preserved.

### MEDIUM — `IDE/HexIDE.Core/Addins/PackageVerification.cs:148`  ·  unhandled-exception
**Unhandled I/O exceptions during package verification abort all add-in loading and can crash IDE startup**

- *Repro:* On Windows first launch right after an installer/updater writes the add-in package, real-time antivirus holds a read/scan lock on addins/<pkg>/publisher.sig (or intermediate.sig). AddinRegistry.LoadAll -> LoadPackage -> Verify -> TryReadText -> File.ReadAllText throws IOException (sharing violation). Nothing catches it, so it unwinds through DesktopStartupHook and OnFrameworkInitializationCompleted -> the IDE fails to finish startup / hard-crashes. Equivalently, one add-in package containing a subdirectory the process cannot enumerate, or a legitimately deep nested-dependency path exceeding MAX_PATH, makes EnumerateFiles throw and prevents every other add-in from loading.
- *Subsystem:* addin-trust
- **✅ FIXED** (2026-08-10) — two layers: (1) `PackageVerification` I/O helpers (`TryReadText`/`TryReadBytes`, the completeness `EnumerateFiles`) catch IO/access/security exceptions → Untrusted, never throw; (2) `AddinRegistry.LoadAll` guards each package's load, so a broken one is logged + skipped and can never abort the other add-ins nor unwind through IDE startup. Regression-guarded in `PackageVerificationTests` (a locked signature file → Untrusted, Windows-only).

### MEDIUM — `IDE/HexIDE/Forms/ViewModels/CodeEditorViewModel.cs:132`  ·  event-subscription-leak
**CodeEditorViewModel leaks a LanguageChanged subscription on every tab close**

- *Repro:* User opens Form1 code, edits, closes the tab; repeats for many modules/forms across a session. Each close disposes the tab VM but the singleton LocalizationService.LanguageChanged retains a lambda referencing it, so none are collected — the working set grows unbounded (each retains a full document text buffer). Then the user switches IDE language once in Options → Language: LanguageChanged fires ComputeTitle() on all N leaked dead VMs, doing pointless work proportional to every editor ever opened.
- *Subsystem:* localization-theming
- **✅ FIXED** (2026-08-10) — same fix as the FormEditViewModel leak: the `LanguageChanged` handler is stored and unsubscribed on Dispose via `AutoDispose(new ActionDisposable(...))`.

### MEDIUM — `IDE/HexIDE/VisualDesigner/ViewModels/FormEditViewModel.cs:95`  ·  event-subscription-leak
**FormEditViewModel leaks a LanguageChanged subscription on every designer close**

- *Repro:* User opens a form in the designer, tweaks controls, closes it; repeats for several forms. Each closed designer VM (with its full component-instance VM graph and undo history) is retained forever via LocalizationService.LanguageChanged, so memory climbs across the session and a single Options language switch replays ComputeTitle() across every designer ever opened.
- **✅ FIXED** (2026-08-10) — duplicate of the `FormEditViewModel:95` leak above; fixed by the same `AutoDispose` unsubscribe.
- *Subsystem:* localization-theming

### LOW — `IDE/HexIDE.Runtime/Interpreter/ExecutionState.cs:11`  ·  memory-leak
**ExecutionState slots are never reclaimed — unbounded memory growth across calls and loop iterations**

- *Repro:* `For i = 1 To 1000000` / `Call Work(i)` / `Next`, where Work has a couple of locals, permanently allocates several million ExecutionState slots (one set per call, never reclaimed), steadily consuming memory until OOM on a large enough run.
- *Subsystem:* interpreter-core
- **⏭️ SKIPPED** (2026-08-10, user directive "genuine crashes only, skip the intentional walls") — not a crash but a resource-lifetime issue; reclaiming per-call slots is a real design task (activation lifetime), not a defensive guard. Parked, not fixed.

### LOW — `IDE/HexIDE.Runtime/Interpreter/BasicInterpreter.cs:412`  ·  stack-overflow
**Unbounded VB6 recursion overflows the C# stack and crashes the whole process**

- *Repro:* `Sub A()` / `Call A()` / `End Sub` then `Call A` — infinite recursion drives the C# stack to StackOverflowException, hard-crashing the HexIDE process instead of raising VB6 runtime error 28.
- *Subsystem:* interpreter-core
- **⏭️ SKIPPED** (2026-08-10, user directive) — this is the parked Error-28 "Out of stack space" wall, tracked in memory `artificial-vs-real-limits` as *legitimate-but-PARKED*. A recursion-depth guard is a deliberate future feature, not a defensive crash-guard; not in the genuine-crash scope.

### LOW — `IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:1093`  ·  unhandled-exception
**Wrong-dimension / undimensioned array access throws untrappable VBCompileErrorException instead of trappable error 9**

- *Repro:* `Dim a() As Ship` then `On Error Resume Next : Set a(0) = New Ship` (undimensioned array, bounds.Count=0 vs index.Count=1): GetValue at StatementExecutor line 1093 throws VBCompileErrorException, which is not caught by the IndexOutOfRangeException handler at line 1098 nor by the ResumeNext trap, so the program crashes instead of continuing past a trappable error 9. Same for a read like `Dim a(5) : x = a(1,2)`.
- *Subsystem:* value-model-refcount
- **✅ FIXED** (2026-08-10) — `VBArray.GetValue`/`SetValue` now raise a trappable `VBRunTimeException(SubscriptOutOfRange)` (Err 9) on a dimension-count mismatch instead of an uncatchable `VBCompileErrorException`. Oracle-pinned (`vb6.exe`: undim set/get = ERR9); regression in `ErrorFidelityTests.UndimensionedArrayAccess_RaisesSubscriptOutOfRange`.

### LOW — `IDE/HexIDE.Runtime/Interpreter/VB6BuiltIns.Format.cs:340`  ·  unhandled-exception
**Numeric Format with a long fractional mask throws an uncatchable overflow/argument exception**

- *Repro:* Format(0.5, "0.00000000000000000000")  (20 fractional zeros) -> 0.5 * 1e20 = 5e19 overflows long at line 340; crash, not catchable. Format(1.5, "0." & String(30, "0")) -> ArgumentOutOfRangeException at Math.Round (line 332).
- *Subsystem:* builtins-format
- **✅ FIXED** (2026-08-10) — the fractional rounding/scaling is capped at a long-safe width (18) and the mask is zero-padded beyond that; VB6 does NOT error on a wide fixed mask — it zero-pads past the value's precision (oracle: `Format(0.5, 20 zeros)` = `0.5` + 19 zeros). Regression in `FormatNumericOverflowTests.LongFractionalMask_ZeroPadsBeyondPrecision_NoCrash`.

### LOW — `IDE/HexIDE.Runtime/Interpreter/VB6BuiltIns.DateTime.cs:88`  ·  unhandled-exception
**DateAdd/TimeSerial that overflow DateTime throw an uncatchable ArgumentOutOfRangeException**

- *Repro:* DateAdd("yyyy", 100000, Now)  -> AddYears pushes past year 9999 -> ArgumentOutOfRangeException, program crashes uncatchably. Same for TimeSerial(9999999, 0, 0).
- *Subsystem:* builtins-format
- **✅ FIXED** (2026-08-10) — both wrap the .NET date arithmetic; the oracle showed the two error codes DIFFER (`vb6.exe`: DateAdd overflow = **Err 5** Invalid procedure call, TimeSerial overflow = **Err 6** Overflow), now pinned. Regression in `ErrorFidelityTests.DateAddOverflow_RaisesInvalidProcedureCall` / `TimeSerialOverflow_RaisesOverflow`. NB VB6 rejects out-of-`Integer` args far earlier (~±32768) via arg coercion — a documented approximation gap (`interpreter-gaps.md`), not the crash.

### LOW — `IDE/HexIDE.Runtime/Serialization/ProjectDeserializer.cs:62`  ·  malformed-input-crash
**.vbp value of a single double-quote makes Substring(1, length-2) underflow and abort project open**

- *Repro:* Opening a corrupted or truncated .vbp containing a line like `HelpFile="` (an unterminated quote, value trims to a lone `"`) throws ArgumentOutOfRangeException; the project fails to load entirely with only a log entry, leaving the IDE with no open project.
- *Subsystem:* serialization
- **✅ FIXED** (2026-08-10) — the quote-strip requires length ≥ 2, so a lone `"` is left as-is instead of `Substring(1, -1)` throwing. Regression-guarded in `SerializationRoundTripTests`.

### LOW — `IDE/HexIDE.Runtime/Serialization/FrxDeserializer.cs:29`  ·  integer-overflow
**FrxDeserializer.Read bounds check can integer-overflow, allowing an unbounded blob allocation from a crafted .frx**

- *Repro:* Opening a project with a hand-crafted Form1.frx whose second blob declares length 0x7FFFFFFF at a non-zero offset makes Read attempt `new byte[2147483647]` (~2 GB) before the caller's try/catch swallows the failure — a per-load memory spike / DoS on malformed input.
- *Subsystem:* serialization
- **✅ FIXED** (2026-08-10) — the bound uses `length > content.Length - pos` (subtraction) instead of `pos + length > content.Length`, so a near-int.MaxValue length can't overflow past the check. Regression-guarded in `SerializationRoundTripTests`.

### LOW — `IDE/HexIDE.Runtime/Serialization/FormDeserializer.cs:239`  ·  malformed-input-crash
**Unguarded Convert.ToInt32 on font metrics (Size/Weight/Italic) throws on non-numeric BeginProperty Font values**

- *Repro:* A .frm/.ctl whose control has `BeginProperty Font … Weight = Bold … EndProperty` (a non-numeric metric): in the IDE the whole form silently fails to load; running `HexIDE.Standalone --check project.vbp` over the same project terminates with an unhandled FormatException instead of reporting the form as FAIL.
- *Subsystem:* serialization
- **✅ FIXED** (2026-08-10) — a `MetricOr(v, fallback)` helper catches Format/InvalidCast/Overflow and falls back to the VB6 default (Size 8, Weight 400, Italic 0), so a malformed metric no longer throws. (Covered by build + review — hand-crafting a malformed-font .ctl fixture was disproportionate.)

### LOW — `IDE/HexIDE.Runtime/AvaloniaInterop/AvaloniaInteroperability.cs:216`  ·  unhandled-exception
**Reading a control property whose stored value is a long (e.g. Tag) throws NotImplementedException**

- *Repro:* `Command1.Tag = 100000` (stored as boxed long) followed by `x = Command1.Tag` -> FromObject(long) throws NotImplementedException -> swallowed/logged -> the read yields nothing and subsequent logic misbehaves.
- *Subsystem:* form-runtime-controls
- **✅ FIXED** (2026-08-10) — `FromObject` now maps `long`/`short`/`byte`/`decimal`/`DateTime` and passes a `Vb6Value` through; any still-unmapped type renders as a String rather than throwing `NotImplementedException` at a control-property read. Build-verified (the control-property read path needs a live form runtime the pure-interpreter suite doesn't host).

### LOW — `IDE/HexIDE.Runtime/AvaloniaInterop/AvaloniaMethodsInteroperability.cs:16`  ·  index-out-of-range
**AddItem indexes args[0] without an arity check**

- *Repro:* `List1.AddItem` written with no argument reaches Call with args.Count == 0 -> `args[0]` throws IndexOutOfRangeException -> logged/swallowed instead of raising a VB6 'Argument not optional' error.
- *Subsystem:* form-runtime-controls
- **✅ FIXED** (2026-08-10) — an arity guard raises a trappable `Err 449` (Argument not optional) when AddItem is called with no argument, instead of `IndexOutOfRangeException`. Build-verified (control-method path needs a live form runtime).

### LOW — `IDE/HexIDE/IDE/FindReplaceService.cs:50`  ·  wrong-logic
**Find/Replace search state is discarded on every reopen despite the reuse design**

- *Repro:* User opens Find, types a search term and toggles Match Case/Whole Word, closes the dialog, then reopens Find. The search box and all options are reset to defaults instead of persisting, contrary to the service's stated contract (and VB6 behaviour).
- *Subsystem:* ide-services-di
- **⏭️ SKIPPED** (2026-08-10, user directive) — a wrong-logic behavioural gap, not a crash; out of the genuine-crash scope. A candidate for a later Find/Replace polish pass.

### LOW — `IDE/HexIDE.Desktop/Server/HexIdeTools.cs:289`  ·  error-handling
**set_control_property faults on integer overflow (only FormatException caught) and parses numbers with current culture**

- *Repro:* MCP client calls set_control_property on an int-typed property with value "9999999999" (or "2147483648"). int.Parse throws OverflowException, which the FormatException handler does not catch; the tool call faults with an unhandled exception rather than returning the clean 'Cannot parse' error result.
- *Subsystem:* mcp-server
- **✅ FIXED** (2026-08-10) — the catch is widened to `FormatException or OverflowException`, so an out-of-range numeric literal returns the clean "Cannot parse" result instead of faulting the tool handler. (Current-culture parsing is a separate non-crash nicety, not in the genuine-crash scope.)

