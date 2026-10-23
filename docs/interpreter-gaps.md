# HexIDE VB6 Interpreter — Gap Catalogue

> Generated 2026-08-03 by a multi-agent audit (73 agents: 14 interpreter-code-slice discoverers + 2 doc-wall
> miners → consolidate/dedup → **one verifier per claim** → synthesis). Scope: the in-process tree-walking
> interpreter only (`IDE/HexIDE.Runtime/Interpreter/`). Fidelity method: `docs/vb6-fidelity-oracle.md`. This file
> is the documented home for the interpreter's gaps — it discharges the spec's "every wall is documented" rule.
> The **positive counterpart** — every VB6 construct, function, operator, keyword and constant with its current
> support level — is [`MISSING_LANGUAGE.md`](MISSING_LANGUAGE.md). This file owns *why* something is missing and
> what kind of limit it is; that one owns *what runs*. Do not restate this file's classifications there.
> The interpreter is **approximation-only** by design; the *Walled off* items are the permanent line where a
> real language engine or compiler backend takes over, not oversights.

**Corrections applied after the audit (things the audit got slightly wrong):**
- **Clipboard text is now partial (#1156):** Clear, SetText, GetText and GetFormat(vbCFText) use the host clipboard.
  Image/data methods, non-text format queries and RTF/Link text are deferred and explicitly refused. VB6 preserves
  other formats on SetText; the Avalonia host refuses SetText when non-text formats exist until it can preserve
  them, rather than silently discarding them. Clear followed by SetText is supported.
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
walled. **Walled off** are principled, by-design permanent boundaries (the RDC/TB line) — each one *relates two
symbols without executing them*, which is binding, which is the AST. **Platform limits** are real and hard but
are NOT that boundary — COM/OLE is in scope and Windows-gated, per CLAUDE.md. **Partial** work for a real subset but fail on specific
paths. **Other: 0.**

Counts — Missed: **0** (was 15; cleared 2026-08-10) · Deferred: 26 **+ 9 reclassified** · Walled: **5** (was 13) ·
Platform: 4 · Partial: 12 · Other: 0.

> **The Walled count fell from 13 to 5 on 2026-09-02**, not because anything was implemented but because the
> section was audited entry by entry against the code and the documents it cited. Nine entries were
> deferrals or platform limits wearing a wall's label, and roughly nineteen of its thirty citations pointed
> past end-of-file or at sections that exist in no file. See the note at the head of that section.

## Missed — CLEARED (2026-08-10 doc-debt sweep)

> **All 15 formerly-undocumented throws are resolved** — 5 implemented, 9 reclassified as Deferred, 2 as Walled — so
> none is an *undocumented* throw and the spec's "every wall is documented" rule holds. Disposition (the original
> per-item findings are retained below for detail):
> - **Implemented:** **Erase** (dynamic → free / then `UBound`·index → Err 9; fixed scalar → bounds kept, elements
>   reset; oracle-pinned) · **Mid statement** `Mid(s,i[,n])=repl` (in-place, length never grows, `start` out of range
>   → Err 5; oracle-pinned) · **DoEvents** (no-op → Integer 0; since #766 it runs what is waiting, and still returns 0) · **Beep** (really implemented as of
>   2026-09-02 — `Console.Beep()`, platform-guarded and fail-safe; the earlier cross-platform justification for
>   the no-op was measured and found wrong) · **Option Compare / Option Private
>   Module** (now tolerated — the module loads; `Option Compare Text` accepted-but-always-Binary is the residual
>   divergence).
> - **→ Deferred:** Load/Unload · Deftype · `#If`/`#Const` (now a **clean compile error**, not a raw crash) · chained
>   index groups (`Split(s)(0)`) · AddressOf · string-vs-number comparison (`5 < "10"`) · **Declare … Lib** (no longer
>   fails the module load — the declaration is skipped, so the module runs; an actual call raises the clean "Sub or
>   Function not defined") · selection/system/financial intrinsics (IIf/Choose/Switch are
>   cheap future adds; the financials/system set is a larger deferral).
> - **→ Walled:** AppActivate / SendKeys (OS automation) · array-element/call-result lead in a member chain (rides the
>   multi-dot / member-chain wall).

_Original per-item findings (retained for detail; dispositions above supersede the "Undocumented" notes):_

- **Beep / AppActivate / SendKeys** — all three visitors throw `NotImplementedException` (StatementExecutor.cs:101-114, 911-914); real VB6 statements (system tone / activate another app / send keystrokes). Undocumented.
- **Load / Unload** — a form's `Load`, and `Unload` of any form, are implemented (#973, #822): a run is one program of every form, a form's name makes its default instance on first use, and a use of its control or built-in member loads it, as VB6 does. `Load` and `Unload` of a control-array element are as before. `Show vbModal` and a `Sub Main` that shows forms followed in slice 2a.2. What is still to come is #822's `New Form2` and `Forms`.
- **DoEvents** — wholly absent from the interpreter (0 hits); VB6 intrinsic that yields to the message queue and returns the open-form count. Undocumented.
- **Erase** — `VisitEraseStmt` throws (StatementExecutor.cs:253-255); VB6 reinitialises fixed arrays / frees dynamic-array storage. Undocumented.
- **Mid statement** (`Mid(s,i,n) = ...`) — `VisitMidStmt` throws (StatementExecutor.cs:675); VB6 replaces chars in place without lengthening. Every doc reference is to the Mid *function*. Undocumented.
- **Deftype** (DefInt/DefLng/DefStr A-Z…) — grammar parses it but StatementExecutor.cs:164-167 throws and PrePass has no handler; VB6 declaration-section default-typing by first letter (runtime, so not CST-walled). Undocumented.
- **Conditional compilation** (`#If/#ElseIf/#Else/#End If`, `#Const`) — PrePass.cs:202 throws so any module with `#If` fails to load wholesale; VB6 compiles only the selected branch. Undocumented (ROADMAP `#Const` row is serialization-only; LSP `#If` is folding-only).
- **Array-element / call-result lead in a member chain** (`arr(i).Name`, `GetObj(x).Foo`) — ExpressionExecutor.cs:406-411 accepts only a bare-variable lead, so a call/array lead silently misroutes into the leading-dot `With` branch (wrong object / Error 91); write path throws (StatementExecutor.cs:527-529). Valid core VB6. Undocumented (only the UDT-array sub-case is walled).
- **Chained index groups** (`Split(s)(0)`, `Array(1,2)(1)`, `f(1)(2)`, [#1476](https://github.com/hexide-io/HexIDE/issues/1476)) — ExpressionExecutor.cs:481-482 / StatementExecutor.cs:612 throw for >1 argsCall group; VB6 allows direct indexing of an array-valued call result. Undocumented.
- **AddressOf** — parses but throws (ExpressionExecutor.cs:816); VB6 passes a procedure address as a Declare-API callback. Undocumented (natural home is a Declare/Win32 wall).
- ~~**String relational comparison** (`"a" < "b"`, `<=`, `>`, `>=`)~~ — **FIXED 2026-08-03**: the operators now
  route two-string operands through the ordinal (`Option Compare Binary`) `TryCompareTo`, oracle-pinned.
  (Audit nit corrected: the cause was that `TryUnpack<TT>` has *no `string` branch*, not a numeric coincidence.)
  **Residual gap (new):** string-vs-*number* comparison (`5 < "10"`) throws Type Mismatch where VB6 coerces the
  numeric string and compares numerically — `GetTwoValuesSameTypes` rejects any type mismatch. Still **Missed**.
- **Declare Sub/Function … Lib** — PrePass.cs:161-162 throws so any module with a `Declare` fails to load; VB6 foundational for Win32 API. Undocumented (absent from the boundary map — violates the spec's every-wall-documented rule).
- **Option Compare [Binary|Text] / Option Private Module** — PrePass.cs:46-50 throws for both (module fails to load); VB6 sets default string-comparison method / hides module members. `Option Compare Text` is runtime semantics so not CST-walled. Undocumented.
- **Static local variables / Static procedure modifier** — **done (#1491).** A Static's slot is kept per declaration, for the run in a standard module and per instance in a class or form, and a `Static` procedure makes its Dim locals Static (oracle, "Static"). Collected at procedure entry like any local; nothing relates one declaration to another.
- **Selection/system/financial intrinsics** (IIf, Choose, Switch, Partition, Environ, Command, Shell, CallByName, Error(), and the 13 financials Pmt/FV/PV/NPV/IRR/Rate/NPer/IPmt/PPmt/MIRR/DDB/SLN/SYD) — all unregistered in every `VB6BuiltIns` partial (throw Sub-or-Function-not-defined, even via `VBA.`/`VB.` qualifiers); genuine VBA Interaction/Information/Financial members. RGB and QBColor have since been added, measured (#826).

## Deferred

- **Sequential/Random file I/O** (Open, Close, Print#, Write#, Input#, Line Input#, Get, Put, Seek, Lock, Unlock, Reset, Width#) — every visitor throws, no handle machinery; VB6 full channel-based file family. Documented post-launch: MISSING_FEATURES.md:504, ROADMAP.md:77,154.
- **File/directory management** (Kill, Name…As, FileCopy, MkDir, RmDir, SetAttr) — all six throw; VB6 delete/rename/copy/dir/attr with trappable errors (53, 75/76). Covered family-level by the File I/O deferral, though these six are never named individually.
- **File-related functions** (FreeFile, EOF, LOF, Loc, Seek fn, FileLen, Dir/Dir$, CurDir, GetAttr, FileDateTime, FileAttr, Input$, Spc, Tab) — absent from the builtin registry; companions to the deferred channel statements. Documented family-level (interpreter-core "fidelity roadmap", MISSING_FEATURES.md:504).
- **Registry settings** (SaveSetting/DeleteSetting statements + GetSetting/GetAllSettings functions) — statements throw (StatementExecutor.cs:161/817), read fns absent; VB6 persists under HKCU\Software\VB and VBA Program Settings. Documented "Missing" at MISSING_FEATURES.md:509.
- **End** — FIXED (#775): it ends the program where it stands. This row used to say VB6's `End` fires `Class_Terminate`; measured on vb6.exe, it fires none, and no form raises QueryUnload, Unload or Terminate either (`vb6-fidelity-oracle.md`, *End*). **`Stop` is implemented** (breaks into the debugger in the IDE; raises `StopExecutionSignal` headless) — this row said otherwise until 2026-09-02.
- **GoSub/Return, On expr GoTo/GoSub** — all four throw; VB6 legacy in-procedure computed branching. Deferred pending linearized CFG/bytecode: interpreter-core "fidelity roadmap" line 524.
- **A label sharing its line with a construct TERMINATOR** (`Cont: Next i`, `Fin: End If`, `Alt: Else`) — the construct parses and the module loads, but the name is not a jump target, so a `GoTo` to it raises "Sub or Function not defined". Measured legal in VB6 (corpus `line-labels/label-immediately-before-a-loop-terminator`, `separator-vs-label/label-before-end-if`, `label-before-else`). **This is the tree-walking limit, not the label-registration defect fixed in the line-head work**: a label sharing its line with a STATEMENT now registers correctly, but `Next` is not a statement in the body block — it is the tail of the `forNextStmt` rule — so jumping to it means resuming in the middle of a statement the walk is already inside, which is the same wall as GoSub/Return. Same for `End If` and `Else`. A label ALONE on its line before a terminator works, because there it is a statement of its own. Note the shape of the eventual fix is known and cheap once a position exists: a trailing line head labels the position one past the block's last statement, which the pc loop already treats as "this block is done" — for a `For` body that is exactly the continue semantics.
- **Date / Time statements** (set system clock) — both throw (StatementExecutor.cs:156, 1010); VB6 sets host clock. Documented "Out of Phase 2" (interpreter-core:336, Time not named).
- **Attribute statement in a procedure body** — FIXED (#832): an `Attribute` line inside a procedure, or after a module-level declaration, is metadata and runs as nothing, as VB6 runs it, and the debugger treats it as a blank line: a step never stops on one, at any nesting, and Set Next Statement onto one is refused. Measured on vb6.exe for VB_UserMemId, VB_Description, VB_ProcData, VB_HelpID, VB_MemberFlags, VB_VarDescription, VB_VarHelpID and VB_VarMemberFlags. Honouring VB_UserMemId (a default member) is a separate question, and for a class HexIDE parsed it is collection, not a wall: #174.
- **SavePicture statement** — throws (StatementExecutor.cs:810-812); VB6 runtime-library statement writing a Picture to a bitmap file. Deferred family-level (graphics/VB-objects; sibling LoadPicture slated for advanced Phase 1) but not named individually.
- **Type-hint suffixes & $-typed string twins** (`Left$`, `Mid$`, `Format$`, `x%`, `s$`) — tracked by [#742](https://github.com/hexide-io/HexIDE/issues/742). Most typeHint sites throw: an unqualified read (`Debug.Print s$` -> "Type hint is not supported", the same for `% & ! # @`), `Dim n$` ("DIM type hints not implemented") and `Next i%` ("TypeHints not implemented"). **Not every one, and the exceptions are worse than a throw.** An assignment target keeps the character in the name it looks up, so without `Option Explicit`, `Dim s As String : s$ = "x"` creates a second variable called `s$` and leaves `s` empty, with no error, for all six characters; under `Option Explicit` the assignment raises "Variable not defined (s$)" instead, which VB6 does not. A `For i% = 1 To 2` counter does the same to a declared `i`, and is silent under `Option Explicit` too, because `VisitForNextStmt` allocates its counter without that check ([#739](https://github.com/hexide-io/HexIDE/issues/739), silently wrong: VB6 sends both to the declared variable. The oracle's 2026-10-11 entry "A type character on a name the program also declares" measured the assignment for each of the six characters, module and local, with and without `Option Explicit`, and the `%` counter in a local, with and without). A module-qualified read drops the character unchecked, so `Module1.VF$("x")` runs where VB6 refuses to compile it (#742). And a `Function` declared with one (`Function GetName$()`) does not parse, so its whole module never loads: the grammar's `functionStmt` has no `typeHint?`, though `propertyGetStmt` has one ([#740](https://github.com/hexide-io/HexIDE/issues/740)). VB6 core syntax, $-intrinsics return String not Variant. A call written with a `$` now runs (#1071): `Trim$(s)` and the other 22 library `$` forms answer as their plain form with a String, a Null raising 94, and a user `Function Trim` declared `As String` answers `Trim$` (oracle, "The library's `$` forms"). A `$` the oracle has not measured on that name stops the run naming #742. What is deferred pending type-hint dispatch is the reads, and a `$` call the oracle has not measured (#742); #739 is a wrong value and #740 a grammar fix, and neither waits on it. The deferral is recorded in: `MISSING_FEATURES.md`, the "VB6 built-in functions" row ("the `$`-typed twins (`Format$/Left$/Mid$/…`) need the type-hint dispatch"); `vb6-fidelity-oracle.md`, closing the Format engine tables ("`Format$` still awaits the `$`-type-hint dispatch"); and the header comment of `VB6BuiltIns.Strings.cs` ("The `$`-typed twins (Left$, Mid$, …) need type-hint dispatch and are deferred").
- **Call-site ByRef keyword & ParamArray** — ParamArray unimplemented (real VB6, documented pending at MISSING_FEATURES.md:490); the call-site `ByRef` keyword throw is a proleap-grammar artifact (not legal VB6). ByRef semantics themselves are fully implemented via slot aliasing.
- **Like operator** (`s Like "a*b"`) — unconditional throw (ExpressionExecutor.cs:872); VB6 pattern match under Option Compare. Deferred at the interpreter deferral list above.
- **Named arguments** (`:=`) — **Done for procedures, methods and intrinsics (#1465):** a named argument binds to the parameter of its name, in a call to the program's own Sub, Function or class method and to the 57 intrinsics VB6 takes names for, and named arguments are evaluated in parameter order, not the order written (measured; oracle, "Named arguments"). VB6's compile errors ("Named argument not found", "Named argument already specified", and the rest) are raised as the statement runs. **Still refused:** a control's, a form's or an object's method, whose parameter names HexIDE has no table of ([#1474](https://github.com/hexide-io/HexIDE/issues/1474)), and an intrinsic signature help has no entry for ([#1475](https://github.com/hexide-io/HexIDE/issues/1475)). Neither is a wall: both are tables to measure.
- **Const semantics** (type hints, read-only enforcement, cross-module ordering, a procedure's Consts) — measured (#873): within a module, and within a procedure, a Const may read only what is declared above it, at every scope probed; across modules the order is free, but the modules' Consts must not form a cycle ("Circular dependencies between modules", checked per module). VB6 also accepts type characters and rejects reassignment. **Done in #873:** a module-level Const is checked against those rules as it runs, and a Const's `As` type converts its value as a store does, at both levels. **Still deferred:** another module's Const read before that module has run, in VB6's on-demand order ([#893](https://github.com/hexide-io/HexIDE/issues/893)); a procedure's Consts, which are declared at its entry and checked against nothing ([#894](https://github.com/hexide-io/HexIDE/issues/894)); type characters; and reassignment. Also interpreter-core Phase 4 (416-421,445), interpreter-advanced E5 (413).
- **Missing string/format intrinsics** (StrConv, StrComp, LenB/AscB/ChrB/MidB/LeftB/RightB/InStrB byte family, AscW/ChrW, FormatNumber, FormatCurrency, FormatPercent, FormatDateTime) — unregistered; ordinary VBA intrinsics, no principled wall. Deferred as remaining "everyday surface" work: MISSING_FEATURES.md:488, interpreter-core Phase 3.
- **Collection object, For Each over non-array enumerables, CreateObject/GetObject** — **Collection and For Each over a Collection are done ([#1471](https://github.com/hexide-io/HexIDE/issues/1471)),** measured on vb6.exe as native and p-code (oracle, "Collection"). `Dim c As New Collection` works since [#435](https://github.com/hexide-io/HexIDE/issues/435). **Still to come for it:** an array `As Collection` ([#1482](https://github.com/hexide-io/HexIDE/issues/1482)), and keys compared as `vbTextCompare` compares rather than ordinally ignoring case ([#1479](https://github.com/hexide-io/HexIDE/issues/1479)). None of the three is a wall. CreateObject/GetObject is a documented COM wall. interpreter-advanced Phase 6 (357-377,435); COM wall at spec:398, README:22,133.
- **Resume granularity (fault nested in a construct)** — nested-granular Resume is a stated 5b limitation (needs CFG/bytecode). interpreter-core Subset 5b. **Narrowed by #839:** under `On Error Resume Next` a fault in a block statement's condition or header now resumes into the block, as VB6 does, because the trap sits where the condition is evaluated and never needs to jump. What remains is a handler's `Resume` or `Resume Next` for a fault nested in a construct, which must get back inside the block. For `Resume Next`, VB6 measurably gives the same answers as Resume Next there (the If's branch, the raising Case's body, the loop's body), and HexIDE resumes after the whole top-level statement, since getting back inside the block from the handler is exactly the jump a tree-walk cannot make.
- **Call depth before Error 28** — unbounded recursion now raises VB6's trappable Error 28 instead of killing the process (#80, E6 closed); what remains is *how deep* it gets first. HexIDE's frames are far fatter than compiled VB6's — an async state machine plus an ANTLR visitor walk per call — so on a 1 MB Windows thread stack it reaches ~**198** frames for a bare `Call A(n + 1)` sub and only **74** for a bare recursive Function, against vb6.exe's measured **258,825**. Re-measured 2026-09-02: a *real* recursive Function — `Fact(500)`, an `If`/`Else` body doing `n * Fact(n - 1)` — reaches **100**. **The Function figure is the one that matters**, because it is the shape ported code uses, and it is roughly a third of the Sub figure: a Function frame also carries the return slot, `SeedReturnValue`, and the hold-transfer machinery. 198 reads as comfortable; 74 does not, and a naive recursive sum over a 200-element list is fine in VB6 and dies here. Linux main threads get 8 MB and go several times deeper. **Divergence:** real VB6 code recursing more than a hundred deep gets Error 28 where vb6.exe would complete. Not a regression (the process was already dying at that depth) and not the same axis as `ParseDepthGuard`, which bounds *nesting* at parse time. **The lever, if depth ever becomes a priority, is frame WEIGHT** — the async state machine and the per-call visitor walk — not variable allocation: measured, ten `Dim` locals per call cost **zero** depth, because slots are heap and the limit is the C# stack. Oracle rows under *Out of stack space* in `vb6-fidelity-oracle.md`.
- ~~**Variant arithmetic does not promote on overflow**~~ — **FIXED 2026-09-02 (#122)**, oracle-pinned. A Variant has no ceiling and widens Integer -> Long -> Double; a declared type is a ceiling and overflows to Err 6. Driven by operand PROVENANCE, not by the result: a literal counts as fixed, and a Variant on either side lifts the ceiling. `Vb6Value.HasFixedType` carries it, because fixedness propagates through sub-expressions (`(a + 0) * 3` still overflows for a declared `a`) and an operand may be a call result with no slot to interrogate — the shape of `Fact = n * Fact(n - 1)`, which this was filed from. The issue's original table had `30000 * 3` widening; it raises Err 6, and building to that row would have promoted `Dim i As Integer : i = i + 1` past 32767 in silence.
- **Variant vs declared, for division by zero** — real division by zero is `Err 11` for declared operands and `Err 6` on the Variant path (the OLE `VarDiv` quirk, which maps a zero divisor to `DISP_E_OVERFLOW`); HexIDE raises 11 for both. `\` and `Mod` are `Err 11` either way and correct. Needs the same Variant-vs-declared distinction as the promotion row above — the declared half now exists (#124), so this is a small change waiting on the Variant half.
- **Declared-type coverage is Dim only** — #124 records a declared type for `Dim x As T` locals and module variables, and for array element types, which is what `TypeName`, the arithmetic ladder and coercion-on-store need. Not yet recorded, all measured rather than assumed: a **ByVal parameter** *arrives* correctly typed (`BindParameters` narrows it) but its slot carries no declared type, so re-assigning it inside the body drops back to the value's own subtype — `ByVal x As Long` reads `Long` on entry and `Integer` after `x = 3`. A **ByRef** parameter is fine, inheriting its caller's slot type, which VB6 guarantees matches. A **Function's return name** behaves like the ByVal case — `TypeName(G)` inside the body is `Integer` after `G = 3`, though the value the CALLER receives is right because `RunProcedure` narrows on the way out. `Static` is untouched. Each is the same one-line change at its own allocation site.

- ~~**No implicit variable declaration; `Option Explicit` collected but never enforced**~~ — **FIXED
  2026-09-02 (#171)**, oracle-pinned. `x = 5` on an undeclared name raised Err 424, so HexIDE behaved as
  though `Option Explicit` were permanently on — while `PrePass` recorded `RequireVariableDefinitions` and
  nothing read it. Both halves were backwards. An undeclared name is now a procedure-local Variant, Empty,
  fresh per call, created on write and reported as Empty on read; `Option Explicit` rejects it, at runtime
  rather than compile time (a divergence — VB6 refuses to compile, so an undeclared name on a never-executed
  path still fails there).
- ~~**A `Dim` allocated when its statement executed, not at procedure entry**~~ — **FIXED 2026-09-02**. VB6
  treats `Dim` as a declaration, so the local exists for the whole invocation. Three consequences, all now
  correct: reading a local above its `Dim` gives the declared type's zero rather than resolving to a
  module-level *procedure* of the same name; a `Dim` in an untaken branch still declares (VB6 has no block
  scope); and scope-exit release is in real declaration order rather than execution order. Collected by
  `ProcedureInfo.DeclaredLocals` — a list of declaration sites, no relationship between them, which is the
  pre-pass boundary in `CLAUDE.md`. Measured: ten locals per call cost **zero** recursion depth, so eager
  allocation needs no lazy scheme.

## Walled off (by design)

> **Audited 2026-09-02.** Every entry in this section was re-checked against the code it describes and the
> documents it cited. **Nine of the eleven former entries were not walls** — they were deferrals, or
> platform limits, filed here because a walled *diagnostic* inside them was taken to justify walling the
> whole feature. They have moved to **Deferred** and to **Platform limits** below.
>
> The citations were worse than the classifications: of roughly thirty, **two were exact**, three pointed at
> the wrong line, and about nineteen pointed past end-of-file or named sections that exist nowhere —
> `interpreter-advanced/spec.md` is 164 lines and was cited at 173, 180, 216, 219, 283, 287, 385, 389, 391,
> 393 and 394-397. `"Phase-2 Walls"`, `"Phase-3 Walls"` and `"Phase-4 Walls"` appear in **no file but this
> one**. The August OpenSpec migration rewrote the documents and the line numbers were never updated, so the
> section's whole justification apparatus referred to a filing system that no longer exists.
>
> The definition in this file's preamble was never the problem. Applied honestly, it evicts most of what was
> here.

What remains are boundaries in the real sense: each **relates two symbols without executing them**, which is
binding, which is the AST. Note the shape they share — in every case the *construct* is buildable and only
the *compile-time diagnostic* is walled. `interpreter-core:40-42` already prescribes the in-bounds
translation: raise VB6's error number at run time instead of before it.

- **Exposing a `Private Type` through a `Public` signature** — VB6 rejects at compile time a `Public`
  procedure whose parameter or return type is a `Private Type`. Deciding it means binding a signature to a
  declaration in another module without running either. The clearest true wall here.
- **`Property Let`/`Set` agreeing with `Property Get`** — VB6 checks the accessors' parameter lists match.
  Relates two declarations to each other; needs neither to execute.
- **Interface conformance** (`Implements IFoo`) — **RESOLVED 2026-09-02 (#186); the reasoning below is why.**
  Deciding it *before the program runs* needs binding. **Checking it at all does not.** Both member tables are
  already collected by `PrePass`, so comparing them when a class is first instantiated is ordinary execution,
  and memoising per class makes it once-per-class. VB6's own COM substrate asks this question at runtime via
  `QueryInterface`; the compile-time check is a convenience over it. So what was ever walled is the *timing*,
  and `interpreter-core:40-42` prescribes the translation — which is exactly what shipped: HexIDE raises VB6's
  own message, verbatim, at first instantiation instead of refusing to build. A class never instantiated is
  never checked, which is the accepted error-on-a-path-never-taken divergence.
- **`LSet` between UDTs containing strings, objects or Variants** — VB6 refuses at compile time. The
  layout-compatibility judgement is static; `LSet` itself is a deferral.
- **Default members of third-party COM types** — see Platform limits; the marker lives in a type library, not
  in any source HexIDE parsed. (For a `.cls` HexIDE *did* parse, collecting it is permitted — #174.)

## Platform limits (COM / OS / type libraries)

Real, hard, and **not** the language-analysis boundary. `CLAUDE.md` is explicit that COM/OLE is *"not
excluded — it is in scope but Windows-gated"*, so filing these as CST-vs-AST walls misdescribed them and
made them look permanent in a way they are not.

- **`CreateObject` / `GetObject`, library-qualified `New`** (`New Excel.Application`) — needs COM activation.
- **Default members and bang access on a COM receiver** (`rs!Field` on an ADO Recordset) — the default member
  is declared in a type library. The same syntax over a HexIDE-parsed class is a deferral (#174, #173).
- **COM-visible class instancing** (`VB_Creatable`, `VB_Exposed`, `VB_GlobalNameSpace`) — meaningful only to
  external COM clients. `VB_PredeclaredId` is **not** in this group: it is a marker in the class's own
  header, so collecting it is permitted, and it is how `Form1.Show` works without `New`.
- **OS automation** — `AppActivate`, `SendKeys`.

### Reclassified out of this section (2026-09-02)

Now **Deferred**, with the real work named. None needs a bound AST; each is unimplemented, awkward or costly.

- **Enum members with non-literal values** — the entire restriction is one `long.TryParse` on text
  (`PrePass.cs:189-190`), holding the CST node it declines to evaluate. Microsoft's own canonical `Enum`
  example has eight members and every one is `&H…&`, so every one throws today. Colour and bit-flag enums
  are ordinary VB6. `ClassifyRadixLiteral` (`ExpressionExecutor.cs:389`) already parses `&H`/`&O`,
  oracle-pinned.
- ~~**Multi-level qualified member access**~~ — **READS FIXED 2026-09-02 (#173)**. A module qualifier is simply the first step of an ordinary chain; measured to four levels (`Module1.p.In1.Z`). Folded through the same `ResolveMemberChain` as unqualified chains — they were only ever different in the KIND of the first step. **Chained assignment (`a.b.c = 5`) is still refused**: the prefix may resolve to a UDT, which is a value type, so whether the write reaches its owner is an aliasing question with its own tests to write.
- **Parameterized `Property Get`/`Let`/`Set`** — the method branch resolves arguments at
  `ExpressionExecutor.cs:503`; the property branch detects them and throws eight lines later at `:510-511`,
  then passes `[]`. Same receiver, same `RunProcedure`. A Collection now exists (#1471), but its `Item` is a member of an
  in-box object that takes its index as an argument, not a parameterised `Property Get` of the program's own class,
  so it carves nothing out of this row.
- **Fixed-length strings** (`Dim s As String * n`) — the width is a CST child of the declaration; the
  behaviour is store-time coercion on a declared type, the mechanism `CoerceOnStore` already runs for
  Integer/Long/Date. The UDT-field leg needs a new coercion site: `VbUdt.TrySet` writes the field bag with
  no coercion at all.
- **Arrays OF a UDT, and array fields inside a UDT** — two independent slices. `PrePass.cs:282-283` says
  "is *not yet* supported"; the arrays-of-UDT leg has no throw at all, just a `!isArray` guard that routes
  past the lookup the scalar case performs.
- **`LSet` / `RSet`** (string-justify form) — read, measure, pad, write back; the shape already running for
  `Mid`. The UDT form waits on a shared byte-layout model, tracked with `Get`/`Put`.
- **Bang access** (`obj!key`) — **done for a Collection (#1471):** `c!key` is `c("key")`, `c![a key]` brackets a key
  with spaces, `!key` works inside `With c`, and `c!key = 1` is 424 (all measured). On any other object it is still
  refused, since it is a call of that object's default member with the key as a String, and only the Collection's is
  modelled. Not a boundary.
- **Private `Type`/`Enum` module scoping** — HexIDE already ships this algorithm for procedures
  (`TryResolveProcedure` + `IsPrivate`); Types and Enums simply never read `context.visibility()`. Mostly a
  **correctness** issue: two modules each declaring `Type Point` differently collapse last-writer-wins in
  silence, where the procedure path raises "Ambiguous name detected".
- **Object-model class features** — `With New`, `Friend`, and same-project qualified `New` are runtime lookups, not
  analysis. **`As New` is done (#435):** the object is made where the variable is first read, through the same request
  and retry that make a form's default instance (#822), with an array `As New` (#1482) and a Type's field (#1488) still
  to come. (`Friend` treated as Public is arguably not
  a gap at all in a single-project interpreter — there are no external clients to hide from.)

### Found while auditing — bugs, not classification

- **`a(1) = b` for a UDT array** calls `SetValue` with no `CopyIfValueType`, unlike every other UDT value
  boundary — a silent wrong answer waiting for arrays-of-UDT to land.
- **`t.arr(3) = 5` discards the index**, so it would overwrite the whole array slot; the `MemberHasArgs`
  guard is applied in the Object branch and not the `VbUdt` branch.
- **Several boundary throws carry no message at all** — against `interpreter-advanced:70-72`, which requires a
  boundary construct to *"fail clearly"*. The dialog then shows .NET's default "The method or operation is not
  implemented." in place of the construct's name.
  - ~~**Nothing in `IDE/` catches `NotImplementedException`**~~ — **FIXED (#754).** A form's event procedures
    and `Sub Main` used to log it and nothing else, so the program stopped part way without a word. The run
    now ends and the runtime-error dialog says what HexIDE does not implement. It stays untrappable on purpose:
    an `On Error` handler that saw it could only carry on past a statement that never ran.

## Partial

- ~~**ChDir / ChDrive**~~ — **FIXED 2026-08-03**, oracle-pinned. (Audit nit corrected: they never applied the
  side-effect — the string-unpack always failed — so both *always* threw. Now: ChDir coerces the arg to a path,
  mapping any failure to Path Not Found (76); ChDrive uses the first char as the drive letter — empty = no-op,
  a non-letter → Invalid Procedure Call (5), an unavailable drive → Device Unavailable (68).)
- ~~**Bitwise/logical And/Or/Xor/Not on Long or floating operands**~~ — **FIXED 2026-09-02 (#166)**, oracle-pinned. `TryUnpack<int>` had no Long/Single/Double rung, so any `&H…&` flag mask raised TypeMismatch. Replaced with a measured result-type ladder (`VbBitwise`): each operand reduces to 32 bits on its own terms rather than being coerced to a common type first, floats round half-to-even, and the result reports at the wider width floored at Integer — except that two Bytes stay **Byte** and two Booleans stay **Boolean**. `Not` keeps its operand's own width, so `Not CByte(5)` is **250**, an eight-bit complement, not -6. `Empty` is an Integer zero. **`Null` is measured but deliberately not implemented** — `Null Or 5` returns 5, which no per-bit three-valued rule predicts, so it is left to the Null-propagation row below rather than guessed. Table in `vb6-fidelity-oracle.md`.
- ~~**Cross-module unqualified access to Public/Global variables & Consts**~~ — **FIXED 2026-10-14 ([#880](https://github.com/hexide-io/HexIDE/issues/880))**, oracle-pinned: each module sees the other standard modules' Public variables and Consts bare, behind its own names, and a name two modules export is "Ambiguous name detected" where it is used. Private ones stay invisible bare. Qualified, a store (#1006) and an element read (#965) are still gaps. What it said before: qualified `Module1.x` works; unqualified cross-module *variable* reads throw `VBVariableNotDefinedException` (the fallback resolves only cross-module Public procedures/intrinsics, not other modules' variable envs; each env isolated at BasicInterpreter.cs:227). VB6: Public standard-module vars are project-global. **Over-claimed** — interpreter-advanced Phase 1 + ROADMAP:166-168 record this precedence chain COMPLETE, but only procedures were implemented.
- **Module/class member visibility enforcement (Private)** — Private IS enforced for module procedures (qualified + unqualified dispatch); the gaps are module-level *variable* visibility (PrePass.cs:74-76 "over-permissive") and class *instance* members (`obj.PrivateMethod`, ExpressionExecutor.cs:428-443). Both gaps documented as deferred divergences (interpreter-advanced Phase-4 + Phase-2, ROADMAP Phase 3). (Candidate overstated the total gap.)
- **ReDim Preserve / non-simple targets / ReDim-as-declaration** — plain ReDim of a declared var works (`VisitRedimStmt`), but Preserve throws and non-simple/undeclared targets throw. VB6: Preserve keeps data (last dim only), and ReDim can declare. Undocumented.
  - **FIXED (#1459): ReDim Preserve** keeps the elements in range and changes only the last dimension's upper bound, with every
    refusal VB6 makes measured and matched; the oracle's "ReDim Preserve" has the rows. Non-simple targets and ReDim as a
    declaration are still open.
  - **Fixed arrays done (#873):** `Dim a(N)` marks its slot fixed (`ExecutionState.MarkFixedArray`). ReDim of one named directly is VB6's compile error "Array already dimensioned", raised as the ReDim runs (the run-time translation interpreter-core:40-42 prescribes, so it was never walled). Through an array parameter or an untyped one it is VB6's run-time 10, and through a `v As Variant` given an array too, since #970 makes it share the caller's slot.
- **Array-to-array assignment copy & typed/element ByRef passing** — whole-array ByRef (untyped) aliases correctly and For Each works, but `b=a` aliases instead of copying, and `Foo a(1)` copies instead of ByRef. Only the element-ByRef leg is documented (interpreter-advanced Phase-2, interpreter-core "Out of Phase 1").
  - **FIXED (#970): copy on assignment.** A Let, an element Let, a ByVal argument and a For Each variable copy the array, a ByRef `As Variant` shares it, the copy holds the objects it shares, and a fixed array given an array is VB6's compile error, or 13 through a parameter. All measured; the oracle's "Arrays copy wherever VB6 stores one" has the rows.
  - **`Foo(a() As Long)` shares its caller's array since #873 (#849),** where it dropped the `()` and copied.
  - **FIXED (#1455): `Foo a(1)` passes the element by reference,** as VB6 does, so `Swap a(i), a(j)` swaps. The subscripts are
    evaluated at the call, and the array is locked until it returns: ReDim, ReDim Preserve, Erase and a store into the array's
    variable raise 10. All measured; the oracle's "An array element passed ByRef" has the rows.
  - **FIXED 2026-08-03: `Set arr(i) = obj`** (object into an array element — e.g. a fleet of objects) now works, with the store as a **counted** reference (AddRef the element so an object held only by the array stays alive after the local that created it drops; the old occupant is released). Surfaced by the Battleship challenge (see the object-model milestone in ROADMAP) and cross-validated against `vb6.exe`. A local object array's elements are released at `End Sub` since #970, as VB6 does (measured). Residual divergence: `Erase` and a `ReDim` without `Preserve` don't release the objects in the array they discard, and a module-level array's objects wait for program end (like module globals), so those terminate late or not at all, never early.
- **Array error fidelity** (LBound/UBound/index on undimensioned array, Array() Option Base, Join/Filter multi-dim) — element index-out-of-range correctly maps to Error 9, and Array()/Option Base is a documented deferral; but Join/Filter silently use dim 1 on a 2-D array instead of Error 5. Residual escape undocumented (only Array()/Option Base deferral documented: oracle, ROADMAP).
  - **FIXED (MED batch + LOW crash pass, 2026-08): bad-dimension/undimensioned LBound/UBound** now maps to Error 9 (was a raw `ArgumentOutOfRangeException`), and **dimension-count mismatch / access of an undimensioned dynamic array** (`a(0)`, `Set a(0)=obj`) now raises a trappable **Error 9** (`VBArray.GetValue`/`SetValue` were throwing an uncatchable `VBCompileErrorException "Dimension doesn't match"`). Oracle-pinned (`vb6.exe`: undim set/get = ERR9); regression in `ErrorFidelityTests`.
- **Local assignment coercion, IsMissing, Overflow (Err 6)** — coercion-on-store works at function-return and ByVal-param sites, and at Let assignment too: `ModuleExecutionContext.TryUpdateVariable` coerces every write to a declared slot (noticed while updating #774; this entry had said Let was deferred); Err 6 raised on most overflow paths but CCur/CDec huge-double (VbNumeric.cs:213) leaks a raw .NET exception (undocumented); IsMissing absent (unshipped spec stretch). Let-coercion documented at MISSING_FEATURES.md; residual conversion exception leak undocumented.
  - **FIXED (MED batch + LOW crash pass, 2026-08): CDate numeric-out-of-range → Err 6; DateAdd overflow → Err 5; TimeSerial overflow → Err 6.** These leaked uncatchable .NET exceptions (`ArgumentException`/`ArgumentOutOfRangeException`); all three are now trappable and oracle-pinned (`vb6.exe`: DateAdd = ERR5, TimeSerial = ERR6 — verified distinct). **Residual approximation gap:** VB6 coerces `DateSerial`/`TimeSerial`/date-part args to `Integer`, so it errors at ~±32768; the interpreter coerces wider and only overflows the DateTime range far later (e.g. TimeSerial errors at ~1e8 hours, not ~32768) — the crash is gone but the out-of-Integer-range *rejection point* differs. Approximation-only, not scheduled.
- **Null & numeric-String Variant propagation in relational/logical operators** — Null propagation implemented for arithmetic, `=`, `<>`, `^`, `&`, Not, Eqv, unary minus, but `< > <= >=` and And/Or/Xor still throw TypeMismatch on Null (via `GetTwoValuesSameTypes`) despite the spec pinning Null-through-comparison. (Typed-String+number TypeMismatch is actually faithful — only Variant numeric strings coerce.) Relational/And-Or-Xor Null gap undocumented (interpreter-core Phase 2 + oracle pin the intended rule).
- **MsgBox / InputBox extended arguments** — core prompt/icon/button/result-mapping and InputBox title/default work. **MsgBox Title is fixed (#131, 2026-09-01)**: it used to be hardcoded `""`, so every message box was captionless however it was called; both intrinsics now pass `null` for an omitted title and the supplied string otherwise, keeping VB6's distinction between omitted (takes the application name) and explicitly empty (stays empty). The omitted-case default is now `App.Title` (#136, 2026-09-01); the literal "HexIDE" survives only as a last resort for a program with no project behind it. Default-button/modality/help bits + InputBox xpos/ypos/helpfile/context remain ignored (VB6BuiltIns.cs). (Cancel-vs-empty-OK is NOT a gap — VB6 also returns `""` on Cancel.) **Mis-documented** — MISSING_FEATURES.md marks it "Done".
- ~~**An omitted positional argument is dropped**~~ — **fixed (#135, 2026-09-01)**. `MsgBox "x", , "T"` used to lose the blank and shift everything after it one place left, so the title bound to Buttons; proven with `MsgBox "hello", , vbCritical`, which set the *icon*. Never a MsgBox defect — the argument list is built generically, so any call using VB6's blank-argument form mis-bound, silently. Cause: the grammar's `argCall` is optional, so a blank produced **no parse node**, and `context.argCall()` returned only what was written. Argument slots are now rebuilt from the separators (slots = commas + 1) and a blank holds `Vb6Value.Missing` — its own type, not `Empty`, so the binder can tell "not supplied" from "supplied as Empty" and an `Optional` parameter still takes its declared default.
  - ~~**`IsMissing` is still not implemented**~~ — **implemented (2026-09-01)**, on top of `Vb6Value.Missing`. True for exactly one case: an `Optional` with neither a declared type nor a default that the caller left out (or left blank mid-list). An `Optional` with either is *never* missing — measured, and both are guesses that would have shipped wrong. An omitted argument now reports as VB6 does: `TypeName` `"Error"`, `VarType` 10 (`vbError`), `IsEmpty`/`IsNull` False, `IsError` **True**. Oracle-pinned; see *IsMissing, and the shape of an omitted argument* in [`vb6-fidelity-oracle.md`](vb6-fidelity-oracle.md).
    - **`CVErr` is still absent**, so `Missing` is the only `vbError` value the model holds; when `CVErr` lands, `IsError` covers it too.
    - **Using a missing value raises nothing yet.** VB6 gives Error 13 (Type mismatch) on e.g. concatenating one — measured, not implemented.
- **Err object completeness** — error handling is Done and tested (incl. Error 20 for bare Resume, contra the candidate), but Raise's 4th/5th args are dropped, and Erl/HelpFile/HelpContext/LastDllError, auto-clear on Exit/Resume, and correct Resume-after-trapped-fault are absent. VB6: Err has all those members, Raise takes 5 args, auto-resets after Exit/Resume Next. Built scope documented (MISSING_FEATURES.md:497, interpreter-core Phase 5); missing members undocumented.
- **Runtime forms/controls surface** — a real subset works (~13 controls, Load/Resize/Click/Change/Timer, AddItem/RemoveItem/Clear, ListCount and List(i)), but other control methods throw, event args are never bound (beyond a control array's `Index`), the runtime-menu loop is commented out, and Screen/Printer/Clipboard are unseeded (App landed in #136). Most gaps catalogued (MISSING_FEATURES.md + interpreter-advanced E1); event args / Controls collection / runtime menus undocumented.
  - ~~**Control arrays collapsed to one variable**~~ — **DONE 2026-08-10 (both phases).** Same-`Name` controls with distinct `Index` load as a `ControlArrayGroup` (bound to the shared name as a `CSharpProxyObject`): `Command1(i)` indexes it (missing element → **Err 340**, oracle-pinned), `.Count`/`.LBound`/`.UBound` work, a shared `Command1_Click(Index As Integer)` receives the fired element's `Index`, and Locals expands the group to its elements. **Phase 2 — runtime `Load`/`Unload`:** `Load Command1(i)` clones the lowest-index element into the live canvas forced `Visible=False` (oracle: loaded elements start hidden), wires events, stamps `Index`; `Unload` reverses. Distinct oracle-pinned errors: `Load` an existing index → **360**, `Unload` a design-time element → **362**, `Unload`/index a missing element → **340**. Live-verified + unit-tested (14 `ControlArrayTests`). **Residual divergences (minor, unscheduled):** `Load`/`Unload` are modelled only for control-array *elements*, not for whole *forms* (`Load Form2` → `NotImplementedException`); and `Load` clones the template's *design-time* properties (re-instantiates the `.frm` component), not any runtime-mutated state of the source element — the common Load-then-set-properties path is unaffected. The `Controls` collection + container-child nesting remain separate gaps.
  - **Assigning to a control property converts** to the property's type, with VB6's range checks (#768, #774). Refused as
    not implemented rather than answered wrong: a palette colour (#786); `Value` on a scroll bar whose Min exceeds its Max, which Avalonia cannot hold
    (#787); and a control or an array on the right, where VB6 reads a control's default property (#790). A class
    instance on the right raises 438, or is refused naming #174, and Err stands for its Number (#840). Still
    different: `Tag` holds what it is given until #211; a hex String's width and
    trailing `&` (#788). Positions are whole twips, in the container's ScaleMode, since #933 (#791). A colour read in code is the Long, since #789. Detail
    and measurements: `vb6-fidelity-oracle.md`, "HexIDE, after #774".
  - ~~**A running form's `Me.Tag` leaked the runtime's internal show-completion object**~~ — **FIXED 2026-08-10.** `VBLoader.RunForm` used to park its show-completion `TaskCompletionSource` in the form window's Avalonia `Control.Tag`, which backs the VB6 `Tag` property, so `Me.Tag` (and the Locals property surface, D7) read back `"System.Threading.Tasks.TaskCompletionSource"`. The handler now captures the `tcs` in its closure (recovering the window from `sender`, since the `out` window can't be captured) instead of round-tripping through `Tag`. Surfaced by the P8/D7 Locals property surface; live-verified (run → stop → re-run cycle intact, `Me.Tag` now reads the interpreter's Tag default). **Residual, since fixed (#774):** an unset `Tag` read back `Null`, not VB6's `""`, for forms and child controls alike. A running program now reads `""`; the slot itself is still null, so the debugger's Locals window shows it as such.

- **Parse nesting depth** — the recursive-descent parser (both grammar copies) is bounded by a `ParseDepthGuard` at a rule-depth of **300**; input nested deeper (hundreds of nested parens or blocks) is rejected as a compile error ("nesting too deep") instead of overflowing the C# stack. **Divergence:** vb6.exe's parser is effectively unbounded here (it compiles 4096 nested parens — oracle-verified); HexIDE cannot match that on a fixed stack, so it rejects absurd nesting cleanly rather than crash. No real VB6 code approaches the limit (a deliberately deep real procedure peaks near rule-depth ~50), so this only affects degenerate/malicious input. Documented + regression-tested (`ParserDepthGuardTests`, `ParseHardeningTests`); the threshold derivation is in the guard's XML doc.

## Other

_(none)_
