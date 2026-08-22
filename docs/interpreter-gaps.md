# HexIDE VB6 Interpreter — Gap Catalogue

> Generated 2026-08-03 by a multi-agent audit (73 agents: 14 interpreter-code-slice discoverers + 2 doc-wall
> miners → consolidate/dedup → **one verifier per claim** → synthesis). Scope: the in-process tree-walking
> interpreter only (`IDE/HexIDE.Runtime/Interpreter/`). Fidelity method: `docs/vb6-fidelity-oracle.md`. This file
> is the documented home for the interpreter's gaps — it discharges the spec's "every wall is documented" rule.
> The interpreter is **approximation-only** by design; the *Walled off* items are the permanent line where a
> real language engine or compiler backend takes over, not oversights.

**Corrections applied after the audit (things the audit got slightly wrong):**
- **Numeric line labels** (classic `10  Print "x"` / `GoTo 10`) do **NOT** work — the audit missed this. The
  grammar's `lineLabel` is `ambiguousIdentifier COLON`, so a bare line number never parses as a label and
  `GoTo <number>` raises "Label not defined". Alphanumeric `Name:` labels + `On Error GoTo`/`Resume` *do* work.
  → belongs under **Missed**. (Related: `Erl` is absent — see the Err-completeness Partial.)
- **`Global`** (`Global x As Integer`) **works** and is correctly *not* a gap — `GLOBAL` is in the grammar's
  `visibility` rule, so it hoists like `Public` (subject to the same cross-module unqualified-variable-read gap in
  *Partial*). Noted only because it is an ancient construct one might expect to be missing.

---
# HexIDE VB6 Interpreter — Categorised Gap Report

55 verified interpreter gaps, every one a real divergence from VB6/VBA. **Missed** *(originally 15 — now **0**, cleared
in the 2026-08-10 doc-debt sweep)* were silent, undocumented omissions — statements/operators/intrinsics that threw
`NotImplementedException` (or misrouted) with no wall/deferral note. The spec's "every wall is documented" rule now
holds. **Deferred** are acknowledged backlog items on the post-launch fidelity roadmap — intended eventually, not
walled. **Walled off** are principled, by-design permanent boundaries (the RDC/TB line) — mostly the object-model /
default-member / multi-dot surface that requires a bound AST. **Partial** work for a real subset but fail on specific
paths. **Other: 0.**

Counts — Missed: **0** (was 15; cleared 2026-08-10 — 5 implemented, 9 → Deferred, 2 → Walled) · Deferred: 26 · Walled: 13 · Partial: 12 · Other: 0.

## Missed — CLEARED (2026-08-10 doc-debt sweep)

> **All 15 formerly-undocumented throws are resolved** — 5 implemented, 9 reclassified as Deferred, 2 as Walled — so
> none is an *undocumented* throw and the spec's "every wall is documented" rule holds. Disposition (the original
> per-item findings are retained below for detail):
> - **Implemented:** **Erase** (dynamic → free / then `UBound`·index → Err 9; fixed scalar → bounds kept, elements
>   reset; oracle-pinned) · **Mid statement** `Mid(s,i[,n])=repl` (in-place, length never grows, `start` out of range
>   → Err 5; oracle-pinned) · **DoEvents** (no-op → Integer 0) · **Beep** (no-op) · **Option Compare / Option Private
>   Module** (now tolerated — the module loads; `Option Compare Text` accepted-but-always-Binary is the residual
>   divergence).
> - **→ Deferred:** Load/Unload · Deftype · `#If`/`#Const` (now a **clean compile error**, not a raw crash) · chained
>   index groups (`Split(s)(0)`) · AddressOf · string-vs-number comparison (`5 < "10"`) · **Declare … Lib** (no longer
>   fails the module load — the declaration is skipped, so the module runs; an actual call raises the clean "Sub or
>   Function not defined") · Static locals · selection/system/financial intrinsics (IIf/Choose/Switch/RGB/QBColor are
>   cheap future adds; the financials/system set is a larger deferral).
> - **→ Walled:** AppActivate / SendKeys (OS automation) · array-element/call-result lead in a member chain (rides the
>   multi-dot / member-chain wall).

_Original per-item findings (retained for detail; dispositions above supersede the "Undocumented" notes):_

- **Beep / AppActivate / SendKeys** — all three visitors throw `NotImplementedException` (StatementExecutor.cs:101-114, 911-914); real VB6 statements (system tone / activate another app / send keystrokes). Undocumented.
- **Load / Unload** — both throw; VB6 places/removes a form or control-array element in memory (`Unload Me` is the canonical self-close). Undocumented — nearest wall (Forms-as-classes) covers only second-form instantiation, and the advanced spec Phase 1 actually lists them as expected-to-resolve.
- **DoEvents** — wholly absent from the interpreter (0 hits); VB6 intrinsic that yields to the message queue and returns the open-form count. Undocumented.
- **Erase** — `VisitEraseStmt` throws (StatementExecutor.cs:253-255); VB6 reinitialises fixed arrays / frees dynamic-array storage. Undocumented.
- **Mid statement** (`Mid(s,i,n) = ...`) — `VisitMidStmt` throws (StatementExecutor.cs:675); VB6 replaces chars in place without lengthening. Every doc reference is to the Mid *function*. Undocumented.
- **Deftype** (DefInt/DefLng/DefStr A-Z…) — grammar parses it but StatementExecutor.cs:164-167 throws and PrePass has no handler; VB6 declaration-section default-typing by first letter (runtime, so not CST-walled). Undocumented.
- **Conditional compilation** (`#If/#ElseIf/#Else/#End If`, `#Const`) — PrePass.cs:202 throws so any module with `#If` fails to load wholesale; VB6 compiles only the selected branch. Undocumented (ROADMAP `#Const` row is serialization-only; LSP `#If` is folding-only).
- **Array-element / call-result lead in a member chain** (`arr(i).Name`, `GetObj(x).Foo`) — ExpressionExecutor.cs:406-411 accepts only a bare-variable lead, so a call/array lead silently misroutes into the leading-dot `With` branch (wrong object / Error 91); write path throws (StatementExecutor.cs:527-529). Valid core VB6. Undocumented (only the UDT-array sub-case is walled).
- **Chained index groups** (`Split(s)(0)`, `Array(1,2)(1)`, `f(1)(2)`) — ExpressionExecutor.cs:481-482 / StatementExecutor.cs:612 throw for >1 argsCall group; VB6 allows direct indexing of an array-valued call result. Undocumented.
- **AddressOf** — parses but throws (ExpressionExecutor.cs:816); VB6 passes a procedure address as a Declare-API callback. Undocumented (natural home is a Declare/Win32 wall).
- ~~**String relational comparison** (`"a" < "b"`, `<=`, `>`, `>=`)~~ — **FIXED 2026-08-03**: the operators now
  route two-string operands through the ordinal (`Option Compare Binary`) `TryCompareTo`, oracle-pinned.
  (Audit nit corrected: the cause was that `TryUnpack<TT>` has *no `string` branch*, not a numeric coincidence.)
  **Residual gap (new):** string-vs-*number* comparison (`5 < "10"`) throws Type Mismatch where VB6 coerces the
  numeric string and compares numerically — `GetTwoValuesSameTypes` rejects any type mismatch. Still **Missed**.
- **Declare Sub/Function … Lib** — PrePass.cs:161-162 throws so any module with a `Declare` fails to load; VB6 foundational for Win32 API. Undocumented (absent from the boundary map — violates the spec's every-wall-documented rule).
- **Option Compare [Binary|Text] / Option Private Module** — PrePass.cs:46-50 throws for both (module fails to load); VB6 sets default string-comparison method / hides module members. `Option Compare Text` is runtime semantics so not CST-walled. Undocumented.
- **Static local variables / Static procedure modifier** — Static locals throw (StatementExecutor.cs:1109); the Static proc-header keyword is parsed but never read. VB6 persists locals across calls. Runtime machinery, not CST-walled. Undocumented.
- **Selection/system/financial intrinsics** (IIf, Choose, Switch, Partition, Environ, Command, Shell, CallByName, RGB, QBColor, Error(), and the 13 financials Pmt/FV/PV/NPV/IRR/Rate/NPer/IPmt/PPmt/MIRR/DDB/SLN/SYD) — all unregistered in every `VB6BuiltIns` partial (throw Sub-or-Function-not-defined, even via `VBA.`/`VB.` qualifiers); genuine VBA Interaction/Information/Financial members. Undocumented (RGB/QBColor only incidentally named in interpreter-advanced spec:93).

## Deferred

- **Sequential/Random file I/O** (Open, Close, Print#, Write#, Input#, Line Input#, Get, Put, Seek, Lock, Unlock, Reset, Width#) — every visitor throws, no handle machinery; VB6 full channel-based file family. Documented post-launch: MISSING_FEATURES.md:504, ROADMAP.md:77,154.
- **File/directory management** (Kill, Name…As, FileCopy, MkDir, RmDir, SetAttr) — all six throw; VB6 delete/rename/copy/dir/attr with trappable errors (53, 75/76). Covered family-level by the File I/O deferral, though these six are never named individually.
- **File-related functions** (FreeFile, EOF, LOF, Loc, Seek fn, FileLen, Dir/Dir$, CurDir, GetAttr, FileDateTime, FileAttr, Input$, Spc, Tab) — absent from the builtin registry; companions to the deferred channel statements. Documented family-level (interpreter-core "fidelity roadmap", MISSING_FEATURES.md:504).
- **Registry settings** (SaveSetting/DeleteSetting statements + GetSetting/GetAllSettings functions) — statements throw (StatementExecutor.cs:161/817), read fns absent; VB6 persists under HKCU\Software\VB and VBA Program Settings. Documented "Missing" at MISSING_FEATURES.md:509.
- **Stop / End** — both unhandled (StatementExecutor.cs:248-250, 1003-1005); VB6 End terminates (firing Class_Terminate), Stop breaks into the debugger. `End`/program-end hook deferred in interpreter-advanced Phase 4; Stop rides the same hook.
- **GoSub/Return, On expr GoTo/GoSub** — all four throw; VB6 legacy in-procedure computed branching. Deferred pending linearized CFG/bytecode: interpreter-core "fidelity roadmap" line 524.
- **Date / Time statements** (set system clock) — both throw (StatementExecutor.cs:156, 1010); VB6 sets host clock. Documented "Out of Phase 2" (interpreter-core:336, Time not named).
- **Attribute statement in a procedure body** — `VisitAttributeStmt` throws instead of no-op-skipping, and `ModuleFileFormat` strips only the top-of-file header, so member-level attributes in real VB6-authored .cls/.frm crash execution; VB6 ignores Attribute lines at runtime. Documented as a "pre-existing wart, ignore for launch" (lsp-parity-matrix.md). Honoring VB_UserMemId (default members) is a separate permanent wall.
- **SavePicture statement** — throws (StatementExecutor.cs:810-812); VB6 runtime-library statement writing a Picture to a bitmap file. Deferred family-level (graphics/VB-objects; sibling LoadPicture slated for advanced Phase 1) but not named individually.
- **Type-hint suffixes & $-typed string twins** (`Left$`, `Mid$`, `Format$`, `x%`, `s$`) — every typeHint site throws; VB6 core syntax, $-intrinsics return String not Variant. Deferred pending type-hint dispatch: MISSING_FEATURES.md:488, oracle:365, VB6BuiltIns.Strings.cs:9-10.
- **Call-site ByRef keyword & ParamArray** — ParamArray unimplemented (real VB6, documented pending at MISSING_FEATURES.md:490); the call-site `ByRef` keyword throw is a proleap-grammar artifact (not legal VB6). ByRef semantics themselves are fully implemented via slot aliasing.
- **Like operator** (`s Like "a*b"`) — unconditional throw (ExpressionExecutor.cs:872); VB6 pattern match under Option Compare. Deferred at the interpreter deferral list above.
- **Named arguments** (`:=`) — `VisitVsAssign` throws (ExpressionExecutor.cs:870), no arg-binding special-case; VB6 order-free named args (unknown → Err 448). Deferred as last/optional slice: MISSING_FEATURES.md:490, interpreter-core Phase 1.
- **Const semantics** (type hints, As-type coercion, read-only enforcement, cross-module ordering) — VB6 accepts type chars + As-type, evaluates order-independently, rejects reassignment. Deferred: interpreter-core Phase 4 (416-421,445), interpreter-advanced E5 (413).
- **Missing string/format intrinsics** (StrConv, StrComp, LenB/AscB/ChrB/MidB/LeftB/RightB/InStrB byte family, AscW/ChrW, FormatNumber, FormatCurrency, FormatPercent, FormatDateTime) — unregistered; ordinary VBA intrinsics, no principled wall. Deferred as remaining "everyday surface" work: MISSING_FEATURES.md:488, interpreter-core Phase 3.
- **Collection object, For Each over non-array enumerables, CreateObject/GetObject** — Collection + For-Each-over-Collection are fully-designed unimplemented Phase 6 (droppable = deferred); CreateObject/GetObject is a documented COM wall. interpreter-advanced Phase 6 (357-377,435); COM wall at spec:398, README:22,133.
- **Resume granularity (fault nested in a construct)** — nested-granular Resume is a stated 5b limitation (needs CFG/bytecode). interpreter-core Subset 5b.
- **Call depth before Error 28** — unbounded recursion now raises VB6's trappable Error 28 instead of killing the process (#80, E6 closed); what remains is *how deep* it gets first. HexIDE's frames are far fatter than compiled VB6's — an async state machine plus an ANTLR visitor walk per call — so on a 1 MB Windows thread stack it reaches ~**202** frames for a bare `Call A(n + 1)` sub and **60–80** for a recursive Function, against vb6.exe's measured **258,825**. Linux main threads get 8 MB and go several times deeper. **Divergence:** real VB6 code recursing more than a hundred deep gets Error 28 where vb6.exe would complete. Not a regression (the process was already dying at that depth) and not the same axis as `ParseDepthGuard`, which bounds *nesting* at parse time. Oracle rows under *Out of stack space* in `vb6-fidelity-oracle.md`.
- **Variant arithmetic does not promote on overflow** — VB6 widens a Variant result that outgrows its type (Integer → Long → Double, so `30000 * 3` is `90000` as a Long and an untyped `Fact(13)` is a Double); HexIDE raises `Err 6` instead. Only *declared* types have a ceiling that overflows. Untyped variables are what most VB6 code uses, so this is the common path. Oracle rows under *Variant arithmetic promotes on overflow*.

## Walled off (by design)

- **LSet / RSet** — LSet-between-UDTs is an explicit Phase-2 wall, and the string-justify forms exist mainly to pad fixed-length strings (also walled). interpreter-advanced Phase-2 Walls (bare string form not named verbatim).
- **Bang / dictionary-call access** (`obj!key`) — rides on default parameterized members (VB_UserMemId=0), rejected in the boundary map; all paths throw by design. interpreter-advanced Walls (216-217,389), ROADMAP (283-287).
- **Multi-dot object member chains** (`obj.a.b`) — single-dot by design; VB6 composes the dot operator freely. Boundary map "not a backlog": interpreter-advanced:107,391; ROADMAP:199,285.
- **Multi-level qualified member access** (`Module1.Something.Field`) — single-dot only (UDT field-chain from a local root the sole lifted exception); code comment calls it "the deferred general member-chain wall" (ExpressionExecutor.cs:522-525). interpreter-advanced:107,391; interpreter-core:428/446; ROADMAP:285.
- **Default-property dereference / VB_UserMemId=0** (`s = txtBox`, `If obj = 5`) — permanent wall; only Collection gets a hard-coded default member. interpreter-advanced Phase-4 Walls (216) + boundary map (389); Collection.Item exception (368-374).
- **Parameterized Property Get/Let/Set** (`obj.Item(1)`, `obj.Item(i)=x`) — principled wall, Collection.Item the only exception; VB6 allows arglist on Property statements. interpreter-advanced Walls (~216)/boundary (~389), ROADMAP (~284).
- **Fixed-length strings** (`Dim s As String * n`, incl. UDT fields) — throws deliberately at all three sites; VB6 space-pads/truncates 1–65,526-char fixed strings. interpreter-advanced Phase-2 Walls (138) + boundary map (393); ROADMAP:184,285.
- **Array fields inside a UDT, and arrays OF a UDT** (`Dim a(5) As Employee`) — VB6 allows both with per-element value semantics. Boundary map "not a backlog": interpreter-advanced Phase-2 Walls (138) + 393. (Nit: arrays-of-UDT leg fails silently at first element access rather than a clean Dim-time wall error.)
- **Enum members with non-literal values** (hex, expressions, refs to prior members) — PrePass.cs:177-181 handles only decimal literals + auto-increment, else throws; VB6 accepts any constant Long expression. interpreter-advanced Phase-2 Walls.
- **Private Type / Private Enum module scoping** — PrePass.cs:20-22 ignores visibility; all Types/Enums aggregate into one program-wide table (BasicInterpreter.cs:235-241); VB6 restricts a Private Type to its module. interpreter-advanced:140, ROADMAP:184-185.
- **Object-model class features** (As New / With New auto-instantiation, Implements/interfaces, Friend, instancing modes, Set type-enforcement, New with qualified class name) — VB6 supports all; the demonstrator ends here (needs bound AST). interpreter-advanced Phase-3 Walls (173-174,180) + boundary map (385,391,394-397); With-New at 219,283; Friend-as-Public at ProcedureModel.cs:18.

## Partial

- ~~**ChDir / ChDrive**~~ — **FIXED 2026-08-03**, oracle-pinned. (Audit nit corrected: they never applied the
  side-effect — the string-unpack always failed — so both *always* threw. Now: ChDir coerces the arg to a path,
  mapping any failure to Path Not Found (76); ChDrive uses the first char as the drive letter — empty = no-op,
  a non-letter → Invalid Procedure Call (5), an unavailable drive → Device Unavailable (68).)
- **Bitwise/logical And/Or/Xor/Not on Long or floating operands** — work for Integer/Byte/Boolean/parseable-String, but `TryUnpack<int>` has no Long/Single/Double rung so Long or floating operands (any `&H…&` flag mask) hit TypeMismatch. VB6 gives 32-bit bitwise results, rounding floats to Long first. Undocumented.
- **Cross-module unqualified access to Public/Global variables & Consts** — qualified `Module1.x` works; unqualified cross-module *variable* reads throw `VBVariableNotDefinedException` (the fallback resolves only cross-module Public procedures/intrinsics, not other modules' variable envs; each env isolated at BasicInterpreter.cs:227). VB6: Public standard-module vars are project-global. **Over-claimed** — interpreter-advanced Phase 1 + ROADMAP:166-168 record this precedence chain COMPLETE, but only procedures were implemented.
- **Module/class member visibility enforcement (Private)** — Private IS enforced for module procedures (qualified + unqualified dispatch); the gaps are module-level *variable* visibility (PrePass.cs:74-76 "over-permissive") and class *instance* members (`obj.PrivateMethod`, ExpressionExecutor.cs:428-443). Both gaps documented as deferred divergences (interpreter-advanced Phase-4 + Phase-2, ROADMAP Phase 3). (Candidate overstated the total gap.)
- **ReDim Preserve / non-simple targets / ReDim-as-declaration / fixed-vs-dynamic distinction** — plain ReDim of a declared var works (StatementExecutor.cs:1023-1051), but Preserve throws, non-simple/undeclared targets throw, and VBArray has no fixed/dynamic flag. VB6: Preserve keeps data (last dim only), ReDim can declare, fixed arrays raise a compile error. Undocumented (the fixed-array compile-error sub-item alone would be CST-walled).
- **Array-to-array assignment copy & typed/element ByRef passing** — whole-array ByRef (untyped) aliases correctly and For Each works, but `b=a` aliases instead of copying, `Foo(a() As Long)` drops the `()`, and `Foo a(1)` copies instead of ByRef. Only the element-ByRef leg is documented (interpreter-advanced Phase-2, interpreter-core "Out of Phase 1"); copy-on-assign and typed-param `()` loss undocumented.
  - **FIXED 2026-08-03: `Set arr(i) = obj`** (object into an array element — e.g. a fleet of objects) now works, with the store as a **counted** reference (AddRef the element so an object held only by the array stays alive after the local that created it drops; the old occupant is released). Surfaced by the Battleship challenge (see the object-model milestone in ROADMAP) and cross-validated against `vb6.exe`. Residual divergence: array *elements* aren't scope-released, so a local object array leaks its elements at `End Sub` (like module globals — VB6 would terminate them).
- **Array error fidelity** (LBound/UBound/index on undimensioned array, Array() Option Base, Join/Filter multi-dim) — element index-out-of-range correctly maps to Error 9, and Array()/Option Base is a documented deferral; but Join/Filter silently use dim 1 on a 2-D array instead of Error 5. Residual escape undocumented (only Array()/Option Base deferral documented: oracle, ROADMAP).
  - **FIXED (MED batch + LOW crash pass, 2026-08): bad-dimension/undimensioned LBound/UBound** now maps to Error 9 (was a raw `ArgumentOutOfRangeException`), and **dimension-count mismatch / access of an undimensioned dynamic array** (`a(0)`, `Set a(0)=obj`) now raises a trappable **Error 9** (`VBArray.GetValue`/`SetValue` were throwing an uncatchable `VBCompileErrorException "Dimension doesn't match"`). Oracle-pinned (`vb6.exe`: undim set/get = ERR9); regression in `ErrorFidelityTests`.
- **Local assignment coercion, IsMissing, Overflow (Err 6)** — coercion-on-store works at function-return and ByVal-param sites but not at Let assignment (documented deferred); Err 6 raised on most overflow paths but CCur/CDec huge-double (VbNumeric.cs:213) leaks a raw .NET exception (undocumented); IsMissing absent (unshipped spec stretch). Let-coercion documented at MISSING_FEATURES.md; residual conversion exception leak undocumented.
  - **FIXED (MED batch + LOW crash pass, 2026-08): CDate numeric-out-of-range → Err 6; DateAdd overflow → Err 5; TimeSerial overflow → Err 6.** These leaked uncatchable .NET exceptions (`ArgumentException`/`ArgumentOutOfRangeException`); all three are now trappable and oracle-pinned (`vb6.exe`: DateAdd = ERR5, TimeSerial = ERR6 — verified distinct). **Residual approximation gap:** VB6 coerces `DateSerial`/`TimeSerial`/date-part args to `Integer`, so it errors at ~±32768; the interpreter coerces wider and only overflows the DateTime range far later (e.g. TimeSerial errors at ~1e8 hours, not ~32768) — the crash is gone but the out-of-Integer-range *rejection point* differs. Approximation-only, not scheduled.
- **Null & numeric-String Variant propagation in relational/logical operators** — Null propagation implemented for arithmetic, `=`, `<>`, `^`, `&`, Not, Eqv, unary minus, but `< > <= >=` and And/Or/Xor still throw TypeMismatch on Null (via `GetTwoValuesSameTypes`) despite the spec pinning Null-through-comparison. (Typed-String+number TypeMismatch is actually faithful — only Variant numeric strings coerce.) Relational/And-Or-Xor Null gap undocumented (interpreter-core Phase 2 + oracle pin the intended rule).
- **MsgBox / InputBox extended arguments** — core prompt/icon/button/result-mapping and InputBox title/default work, but MsgBox Title is hardcoded `""` and default-button/modality/help bits + InputBox xpos/ypos/helpfile/context are ignored (VB6BuiltIns.cs:788-843). (Cancel-vs-empty-OK is NOT a gap — VB6 also returns `""` on Cancel.) **Mis-documented** — MISSING_FEATURES.md marks it "Done".
- **Err object completeness** — error handling is Done and tested (incl. Error 20 for bare Resume, contra the candidate), but Raise's 4th/5th args are dropped, and Erl/HelpFile/HelpContext/LastDllError, auto-clear on Exit/Resume, and correct Resume-after-trapped-fault are absent. VB6: Err has all those members, Raise takes 5 args, auto-resets after Exit/Resume Next. Built scope documented (MISSING_FEATURES.md:497, interpreter-core Phase 5); missing members undocumented.
- **Runtime forms/controls surface** — a real subset works (~13 controls, Load/Resize/Click/Change/Timer, AddItem/Clear), but other control methods throw, event args are never bound (beyond a control array's `Index`), the runtime-menu loop is commented out, and App/Screen/Printer/Clipboard are unseeded. Most gaps catalogued (MISSING_FEATURES.md + interpreter-advanced E1); event args / Controls collection / runtime menus undocumented.
  - ~~**Control arrays collapsed to one variable**~~ — **DONE 2026-08-10 (both phases).** Same-`Name` controls with distinct `Index` load as a `ControlArrayGroup` (bound to the shared name as a `CSharpProxyObject`): `Command1(i)` indexes it (missing element → **Err 340**, oracle-pinned), `.Count`/`.LBound`/`.UBound` work, a shared `Command1_Click(Index As Integer)` receives the fired element's `Index`, and Locals expands the group to its elements. **Phase 2 — runtime `Load`/`Unload`:** `Load Command1(i)` clones the lowest-index element into the live canvas forced `Visible=False` (oracle: loaded elements start hidden), wires events, stamps `Index`; `Unload` reverses. Distinct oracle-pinned errors: `Load` an existing index → **360**, `Unload` a design-time element → **362**, `Unload`/index a missing element → **340**. Live-verified + unit-tested (14 `ControlArrayTests`). **Residual divergences (minor, unscheduled):** `Load`/`Unload` are modelled only for control-array *elements*, not for whole *forms* (`Load Form2` → `NotImplementedException`); and `Load` clones the template's *design-time* properties (re-instantiates the `.frm` component), not any runtime-mutated state of the source element — the common Load-then-set-properties path is unaffected. The `Controls` collection + container-child nesting remain separate gaps.
  - ~~**A running form's `Me.Tag` leaked the runtime's internal show-completion object**~~ — **FIXED 2026-08-10.** `VBLoader.RunForm` used to park its show-completion `TaskCompletionSource` in the form window's Avalonia `Control.Tag`, which backs the VB6 `Tag` property, so `Me.Tag` (and the Locals property surface, D7) read back `"System.Threading.Tasks.TaskCompletionSource"`. The handler now captures the `tcs` in its closure (recovering the window from `sender`, since the `out` window can't be captured) instead of round-tripping through `Tag`. Surfaced by the P8/D7 Locals property surface; live-verified (run → stop → re-run cycle intact, `Me.Tag` now reads the interpreter's Tag default). **Residual (separate, minor):** the interpreter's `Tag` default is `Null`, not VB6's `""` — a default-value approximation shared by forms + child controls, not scheduled.

- **Parse nesting depth** — the recursive-descent parser (both grammar copies) is bounded by a `ParseDepthGuard` at a rule-depth of **300**; input nested deeper (hundreds of nested parens or blocks) is rejected as a compile error ("nesting too deep") instead of overflowing the C# stack. **Divergence:** vb6.exe's parser is effectively unbounded here (it compiles 4096 nested parens — oracle-verified); HexIDE cannot match that on a fixed stack, so it rejects absurd nesting cleanly rather than crash. No real VB6 code approaches the limit (a deliberately deep real procedure peaks near rule-depth ~50), so this only affects degenerate/malicious input. Documented + regression-tested (`ParserDepthGuardTests`, `ParseHardeningTests`); the threshold derivation is in the guard's XML doc.

## Other

_(none)_
